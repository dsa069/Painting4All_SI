using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using Oculus.Interaction.Input;
using System.Linq;

/// <summary>
/// Canvas 3D Movement Controller for Meta Quest 3
/// 
/// Permite mover un lienzo (canvas) en el espacio 3D usando los mandos de Meta Quest 3.
/// 
/// LÓGICA:
/// 1. El usuario apunta al lienzo con un mando (Raycast desde el mando)
/// 2. Al presionar y mantener el Grip Button (botón lateral), el lienzo se engancha al mando
/// 3. Mientras se mantiene presionado, el lienzo sigue la posición del mando en tiempo real
/// 4. El lienzo mantiene su rotación original (no rota con el mando)
/// 5. Al soltar el Grip Button, el lienzo se fija en su posición actual
/// 
/// CARACTERÍSTICAS:
/// - Ambas manos (izquierda y derecha) funcionan de forma independiente
/// - Sistema de raycast para detectar si el mando apunta al lienzo
/// - Suavizado de movimiento con Lerp para evitar jitter
/// - Rotación fija: el lienzo siempre muestra el lado que estás agarrando (sin voltearse)
/// 
/// SETUP:
/// 1. Agrega este script al lienzo (objeto con SpriteRenderer o Mesh)
/// 2. Asegúrate de que el lienzo tiene un Collider (Box Collider, Mesh Collider, etc.)
/// 3. Asegúrate de que existen GameObjects con nombres: "LeftControllerAnchor" y "RightControllerAnchor"
///    (estos son creados automáticamente por Meta XR SDK)
/// 4. ¡Listo! No necesitas asignar nada en el Inspector
/// </summary>
public class Seleccionar_Lienzo : MonoBehaviour
{
    [Header("=== REFERENCIAS DE MANDOS (Auto-detectadas) ===")]
    private Transform leftControllerTransform;
    private Transform rightControllerTransform;
    private GestureUIController gestureController;
    private IHand leftHandTracking;
    private IHand rightHandTracking;
    
    [Header("=== GRIP DETECTION SETTINGS ===")]
    [SerializeField] private float gripPressThreshold = 0.5f;      // Valor analógico para considerar presionado
    
    [Header("=== RAYCAST SETTINGS ===")]
    [SerializeField] private float raycastMaxDistance = 100f;      // Distancia máxima del raycast
    private Collider canvasCollider;
    
    [Header("=== MOVEMENT SMOOTHING ===")]
    [SerializeField] private float positionSpeed = 15f;            // Velocidad de movimiento en m/s (frame-rate independiente)
    [SerializeField] private float rotationSpeed = 720f;           // Velocidad de rotación en grados/s
    [SerializeField] private float canvasDistanceFromController = 4f; // Distancia deseada del lienzo adelante del mando
    [SerializeField] private bool useSmoothing = true;             // Habilitar/deshabilitar suavizado
    
    // === ESTADO DE GRIP (por mano) ===
    private bool gripPressedLeft = false;
    private bool gripPressedRight = false;
    private bool lastGripPressedLeft = false;
    private bool lastGripPressedRight = false;

    // === ESTADO DE T2 GESTURE (por mano) ===
    private bool t2PressedLeft = false;
    private bool t2PressedRight = false;
    private bool lastT2PressedLeft = false;
    private bool lastT2PressedRight = false;
    
    // === OFFSETS RELATIVOS (cacheados al presionar Grip) ===
    // Removidos: ahora usamos distancia fija adelante del mando
    
    // === MANO ACTUALMENTE ACTIVA ===
    private enum ActiveHand { None, Left, Right }
    private ActiveHand currentActiveHand = ActiveHand.None;
    private bool justEngaged = false; // Flag para evitar salto en primer frame
    private bool currentHandIsTracking = false; // Flag para distinguir si es control por tracking o Grip button
    
    // === POSICIÓN/ROTACIÓN OBJETIVO SUAVIZADA ===
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Quaternion initialCanvasRotation; // Rotación inicial del lienzo al agarrarlo
    private Quaternion initialControllerRotation; // Rotación inicial del controlador al agarrarlo
    private bool isGrabbedFromBehind = false; // Flag para detectar si se agarra de atrás

    void Awake()
    {
        // Auto-detectar mandos
        AutoDetectControllers();
        
        // Auto-detectar gesture controller
        AutoDetectGestureController();
        
        // Obtener collider del lienzo
        canvasCollider = GetComponent<Collider>();
        if (canvasCollider == null)
        {
            Debug.LogError($"[Seleccionar_Lienzo] El objeto '{gameObject.name}' no tiene Collider. Agrega un Box Collider o Mesh Collider.", gameObject);
        }

        if (GetComponent<CanvasResizeController>() == null)
        {
            gameObject.AddComponent<CanvasResizeController>();
        }
        
        // Inicializar posición/rotación objetivo
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    void Update()
    {
        // Actualizar estado de grip en ambos mandos
        UpdateGripState();
        
        // Actualizar estado de T2 gesture en ambas manos
        UpdateT2State();
        
        // Procesar enganches y desengaches
        ProcessGripTransitions();
        
        // Actualizar movimiento si está enganchado
        if (currentActiveHand != ActiveHand.None)
        {
            UpdateCanvasMovement();
        }
    }

    /// <summary>
    /// Auto-detecta las referencias a los mandos en la escena
    /// Busca GameObjects con nombres "LeftControllerAnchor", "RightControllerAnchor", etc.
    /// </summary>
    private void AutoDetectControllers()
    {
        var allObjects = FindObjectsOfType<Transform>();
        
        foreach (var t in allObjects)
        {
            if (t.name == "LeftControllerAnchor" || t.name == "LeftHand" || t.name == "LeftHandAnchor")
            {
                leftControllerTransform = t;
            }
            
            if (t.name == "RightControllerAnchor" || t.name == "RightHand" || t.name == "RightHandAnchor")
            {
                rightControllerTransform = t;
            }
        }
        
        if (leftControllerTransform == null)
            Debug.LogWarning("[Seleccionar_Lienzo] No se encontró mando izquierdo (LeftControllerAnchor)");
        
        if (rightControllerTransform == null)
            Debug.LogWarning("[Seleccionar_Lienzo] No se encontró mando derecho (RightControllerAnchor)");
    }

    /// <summary>
    /// Auto-detecta el controlador de gestos (Controls.cs / GestureUIController)
    /// También obtiene referencias a las manos para tracking
    /// </summary>
    private void AutoDetectGestureController()
    {
        gestureController = FindObjectOfType<GestureUIController>();
        
        if (gestureController == null)
        {
            Debug.LogWarning("[Seleccionar_Lienzo] ⚠ No se encontró GestureUIController (Controls.cs). Hand tracking para T2 no funcionará.");
            return;
        }
        
        // Obtener referencias a las manos desde el gesture controller
        leftHandTracking = gestureController.GetLeftHand();
        rightHandTracking = gestureController.GetRightHand();
        
        if (leftHandTracking == null || rightHandTracking == null)
        {
            Debug.LogWarning("[Seleccionar_Lienzo] ⚠ No se pudieron obtener referencias de manos. Hand tracking para T2 no funcionará.");
        }
        else
        {
            Debug.Log("[Seleccionar_Lienzo] ✓ Hand tracking para T2 gesture iniciado correctamente.");
        }
    }

    /// <summary>
    /// Lee el estado actual del Grip Button en ambas manos
    /// Utiliza el Input System moderno (InputSystem.devices)
    /// </summary>
    private void UpdateGripState()
    {
        gripPressedLeft = false;
        gripPressedRight = false;
        
        // Iterar sobre todos los dispositivos XR
        foreach (var device in InputSystem.devices)
        {
            if (device is XRController xrController)
            {
                // Intentar obtener el control "grip" (analógico)
                var gripControl = xrController.TryGetChildControl<AxisControl>("grip");
                if (gripControl == null)
                    continue;
                
                float gripValue = gripControl.ReadValue();
                bool isGripPressed = gripValue >= gripPressThreshold;
                
                // Determinar si es mano izquierda o derecha
                bool isLeftHand = IsLeftHandDevice(xrController);
                
                #if UNITY_EDITOR
                if (isGripPressed && Time.frameCount % 30 == 0) // Debug cada 30 frames si está presionado
                {
                    Debug.Log($"[Seleccionar_Lienzo] Grip detectado - Device: {xrController.name} | Path: {xrController.path} | IsLeft: {isLeftHand} | GripValue: {gripValue}");
                }
                #endif
                
                if (isLeftHand)
                {
                    gripPressedLeft = isGripPressed;
                }
                else
                {
                    gripPressedRight = isGripPressed;
                }
            }
        }
    }
    
    /// <summary>
    /// Determina si un XRController corresponde a la mano izquierda
    /// Usa las características del device: si tiene InputDeviceCharacteristics.Left, es mano izquierda
    /// </summary>
    private bool IsLeftHandDevice(XRController device)
    {
        // Intentar obtener características del device
        if (device is XRController xrCtrl)
        {
            // Método 1: Usar el nombre del device (es lo más confiable en Meta Quest)
            if (device.name.Contains("Left") || device.name.Contains("left"))
                return true;
            if (device.name.Contains("Right") || device.name.Contains("right"))
                return false;
            
            // Método 2: Si el método 1 no funciona, buscar en el path del device
            if (device.path.Contains("lefthand"))
                return true;
            if (device.path.Contains("righthand"))
                return false;
        }
        
        // Fallback: por defecto, si no puede determinar, asumir que es por orden de enumeración
        int xrControllerIndex = -1;
        int xrControllerCount = 0;
        
        foreach (var dev in InputSystem.devices)
        {
            if (dev is XRController)
            {
                if (dev == device)
                    xrControllerIndex = xrControllerCount;
                xrControllerCount++;
            }
        }
        
        // Primer device XR = mano izquierda
        return xrControllerIndex == 0;
    }

    /// <summary>
    /// Lee el estado actual del gesto T2 (Thumb + Middle pinch) en ambas manos
    /// Detecta si el gesture controller está disponible y si T2 está activo
    /// </summary>
    private void UpdateT2State()
    {
        t2PressedLeft = false;
        t2PressedRight = false;
        
        // Si el gesture controller no está disponible, no hacer nada
        if (gestureController == null)
            return;
        
        // Detectar T2 en mano derecha
        t2PressedRight = gestureController.IsT2ActiveRight;
        
        // Detectar T2 en mano izquierda
        t2PressedLeft = gestureController.IsT2ActiveLeft;
    }

    /// <summary>
    /// Detecta transiciones de Grip Button y T2 Gesture (presionado → soltado o soltado → presionado)
    /// Engancha el lienzo cuando se presiona Grip o T2 sobre él
    /// Lo desenganche cuando se suelta
    /// </summary>
    private void ProcessGripTransitions()
    {
        // ========== TRANSICIONES GRIP BUTTON ==========
        
        // TRANSICIÓN IZQUIERDA GRIP: soltado → presionado
        if (gripPressedLeft && !lastGripPressedLeft)
        {
            if (RaycastFromController(leftControllerTransform))
            {
                // Validar que la mano izquierda no esté ya agarrando otro lienzo
                if (!CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Left))
                {
                    // Raycast éxitoso: enganchar lienzo con mano izquierda (por Grip)
                    EngageCanvas(ActiveHand.Left, leftControllerTransform, false); // false = no es tracking
                }
            }
        }
        
        // TRANSICIÓN DERECHA GRIP: soltado → presionado
        if (gripPressedRight && !lastGripPressedRight)
        {
            if (RaycastFromController(rightControllerTransform))
            {
                // Validar que la mano derecha no esté ya agarrando otro lienzo
                if (!CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Right))
                {
                    // Raycast éxitoso: enganchar lienzo con mano derecha (por Grip)
                    EngageCanvas(ActiveHand.Right, rightControllerTransform, false); // false = no es tracking
                }
            }
        }
        
        // ========== TRANSICIONES T2 GESTURE ==========
        
        // TRANSICIÓN IZQUIERDA T2: soltado → presionado (REQUIERE raycast desde muñeca)
        if (t2PressedLeft && !lastT2PressedLeft)
        {
            // T2 presionado: validar que estés apuntando a ESTE lienzo específicamente
            if (leftHandTracking != null && RaycastFromHandWrist(leftHandTracking))
            {
                // Validar que la mano izquierda no esté ya agarrando otro lienzo
                if (!CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Left))
                {
                    EngageCanvas(ActiveHand.Left, null, true); // true = es tracking
                }
            }
        }
        
        // TRANSICIÓN DERECHA T2: soltado → presionado (REQUIERE raycast desde muñeca)
        if (t2PressedRight && !lastT2PressedRight)
        {
            // T2 presionado: validar que estés apuntando a ESTE lienzo específicamente
            if (rightHandTracking != null && RaycastFromHandWrist(rightHandTracking))
            {
                // Validar que la mano derecha no esté ya agarrando otro lienzo
                if (!CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Right))
                {
                    EngageCanvas(ActiveHand.Right, null, true); // true = es tracking
                }
            }
        }
        
        // ========== LIBERACIONES ==========
        
        // LIBERACIÓN IZQUIERDA: presionado → soltado (tanto Grip como T2)
        if ((!gripPressedLeft && lastGripPressedLeft || !t2PressedLeft && lastT2PressedLeft) && currentActiveHand == ActiveHand.Left)
        {
            DisengageCanvas();
        }
        
        // LIBERACIÓN DERECHA: presionado → soltado (tanto Grip como T2)
        if ((!gripPressedRight && lastGripPressedRight || !t2PressedRight && lastT2PressedRight) && currentActiveHand == ActiveHand.Right)
        {
            DisengageCanvas();
        }
        
        // Guardar estado anterior para GRIP
        lastGripPressedLeft = gripPressedLeft;
        lastGripPressedRight = gripPressedRight;
        
        // Guardar estado anterior para T2
        lastT2PressedLeft = t2PressedLeft;
        lastT2PressedRight = t2PressedRight;
    }

    /// <summary>
    /// Convierte ActiveHand (enum local) a CanvasGripManager.ActiveHand para comunicación con el manager
    /// </summary>
    private CanvasGripManager.ActiveHand ConvertToManagerActiveHand(ActiveHand hand)
    {
        return hand == ActiveHand.Left ? CanvasGripManager.ActiveHand.Left : CanvasGripManager.ActiveHand.Right;
    }

    /// <summary>
    /// Realiza un raycast desde el mando hacia el lienzo
    /// Usa la misma lógica que Paint.cs: plane intersection para SpriteRenderer, luego Physics.Raycast
    /// Devuelve true si el raycast acierta este lienzo específico
    /// </summary>
    private bool RaycastFromController(Transform controller)
    {
        if (controller == null)
            return false;
        
        Ray ray = new Ray(controller.position, controller.forward);
        return IsRayHittingCanvas(ray);
    }

    /// <summary>
    /// Realiza un raycast desde la muñeca de la mano hacia el lienzo
    /// Usa la misma lógica que Paint.cs: plane intersection para SpriteRenderer, luego Physics.Raycast
    /// Devuelve true si el raycast acierta este lienzo específico
    /// Usado para validar T2 gesture - solo engancha si apuntas específicamente al lienzo
    /// </summary>
    private bool RaycastFromHandWrist(IHand hand)
    {
        if (hand == null || !hand.IsTrackedDataValid)
            return false;
        
        // Obtener pose de la muñeca
        if (!hand.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose))
            return false;
        
        Ray ray = new Ray(wristPose.position, wristPose.rotation * Vector3.forward);
        return IsRayHittingCanvas(ray);

    }

    /// <summary>
    /// Valida si un ray golpea este lienzo específico
    /// Usa la misma lógica que Paint.cs para consistencia:
    /// 1. Plane intersection para SpriteRenderer (más preciso)
    /// 2. Physics.Raycast como fallback para otros renderers
    /// </summary>
    private bool IsRayHittingCanvas(Ray ray)
    {
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        
        // MÉTODO 1: Plane intersection para SpriteRenderer (igual que Paint.cs)
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Vector3 planeNormal = spriteRenderer.transform.forward;
            Vector3 planePoint = spriteRenderer.transform.position;
            float denom = Vector3.Dot(planeNormal, ray.direction);
            
            // Si el denom es muy pequeño, el ray es casi paralelo al plano
            if (Mathf.Abs(denom) > 1e-6f)
            {
                float t = Vector3.Dot(planeNormal, planePoint - ray.origin) / denom;
                
                // Solo si el punto de intersección está adelante del ray origin
                if (t >= 0f)
                {
                    Vector3 worldPoint = ray.GetPoint(t);
                    
                    // Convertir a coordenadas locales del sprite
                    Vector3 localPoint = spriteRenderer.transform.InverseTransformPoint(worldPoint);
                    Rect spriteRect = spriteRenderer.sprite.rect;
                    Vector2 spritePivot = spriteRenderer.sprite.pivot;
                    float ppu = spriteRenderer.sprite.pixelsPerUnit;
                    
                    // Calcular si está dentro de los bounds del sprite
                    float spriteWidth = spriteRect.width / ppu;
                    float spriteHeight = spriteRect.height / ppu;
                    float pivotX = spritePivot.x / ppu;
                    float pivotY = spritePivot.y / ppu;
                    
                    float minX = -pivotX;
                    float maxX = spriteWidth - pivotX;
                    float minY = -pivotY;
                    float maxY = spriteHeight - pivotY;
                    
                    if (localPoint.x >= minX && localPoint.x <= maxX &&
                        localPoint.y >= minY && localPoint.y <= maxY)
                    {
                        return true;
                    }
                }
            }
        }
        
        // MÉTODO 2: Physics.Raycast como fallback para otros tipos de renderers
        if (canvasCollider != null)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance))
            {
                // Verificar que el hit sea específicamente ESTE gameobject
                if (hit.collider.gameObject == gameObject)
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Engancha el lienzo a un mando específico
    /// El lienzo se moverá a una distancia fija adelante del mando/mano en la dirección forward
    /// isTracking: true si es por T2 gesture (hand tracking), false si es por Grip button
    /// </summary>
    private void EngageCanvas(ActiveHand hand, Transform controller, bool isTracking)
    {
        // Registrar en el manager global que esta mano está agarrando este lienzo
        CanvasGripManager.ActiveHand managerHand = ConvertToManagerActiveHand(hand);
        if (!CanvasGripManager.Instance.RegisterGrip(managerHand, this))
        {
            // Si no se pudo registrar (mano ya agarrando otro lienzo), cancelar enganche
            return;
        }
        
        currentActiveHand = hand;
        currentHandIsTracking = isTracking; // Guardar si es tracking o Grip
        justEngaged = true; // Flag para evitar movimiento en el primer frame
        
        // Guardar rotaciones iniciales para mantener orientación relativa
        initialCanvasRotation = transform.rotation;
        
        if (!isTracking && controller != null)
        {
            // Para Grip: usar rotación del controller
            initialControllerRotation = controller.rotation;
            
            // Detectar si se agarra de atrás: si el ángulo entre forward del mando y forward del lienzo es > 90 grados
            float dotProduct = Vector3.Dot(controller.forward, transform.forward);
            isGrabbedFromBehind = dotProduct < 0;
        }
        else if (isTracking && hand == ActiveHand.Left && leftHandTracking != null)
        {
            // Para T2 mano izquierda: usar rotación de la mano
            if (leftHandTracking.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose))
            {
                initialControllerRotation = wristPose.rotation;
                float dotProduct = Vector3.Dot(wristPose.rotation * Vector3.forward, transform.forward);
                isGrabbedFromBehind = dotProduct < 0;
            }
        }
        else if (isTracking && hand == ActiveHand.Right && rightHandTracking != null)
        {
            // Para T2 mano derecha: usar rotación de la mano
            if (rightHandTracking.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose))
            {
                initialControllerRotation = wristPose.rotation;
                float dotProduct = Vector3.Dot(wristPose.rotation * Vector3.forward, transform.forward);
                isGrabbedFromBehind = dotProduct < 0;
            }
        }
        
        #if UNITY_EDITOR
        Debug.Log($"[Seleccionar_Lienzo] ✓ Lienzo enganchado a mano {hand} ({(isTracking ? "T2 Tracking" : "Grip Button")}), isGrabbedFromBehind: {isGrabbedFromBehind}");
        #endif
    }

    /// <summary>
    /// Desengancia el lienzo del mando
    /// El lienzo se queda fijo en su posición actual
    /// </summary>
    private void DisengageCanvas()
    {
        // Desregistrar del manager global
        if (currentActiveHand != ActiveHand.None)
        {
            CanvasGripManager.ActiveHand managerHand = ConvertToManagerActiveHand(currentActiveHand);
            CanvasGripManager.Instance.UnregisterGrip(managerHand);
        }
        
        currentActiveHand = ActiveHand.None;
        currentHandIsTracking = false;
        justEngaged = false;
        
        #if UNITY_EDITOR
        Debug.Log("[Seleccionar_Lienzo] ✓ Lienzo desenganchado");
        #endif
    }

    /// <summary>
    /// Obtiene la posición y rotación actuales de la mano activa
    /// Devuelve true si se obtuvieron exitosamente, false si hay error
    /// Retorna position y rotation por out parameters
    /// </summary>
    private bool GetActiveHandTransform(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        
        if (currentActiveHand == ActiveHand.None)
            return false;
        
        if (currentHandIsTracking)
        {
            // Usar hand tracking (T2 gesture)
            IHand activeHand = (currentActiveHand == ActiveHand.Left) ? leftHandTracking : rightHandTracking;
            
            if (activeHand == null || !activeHand.IsTrackedDataValid)
                return false;
            
            // Obtener pose de la muñeca
            if (activeHand.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose))
            {
                position = wristPose.position;
                rotation = wristPose.rotation;
                return true;
            }
        }
        else
        {
            // Usar controller (Grip button)
            Transform controller = (currentActiveHand == ActiveHand.Left) ? leftControllerTransform : rightControllerTransform;
            
            if (controller == null)
                return false;
            
            position = controller.position;
            rotation = controller.rotation;
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Actualiza la posición y rotación del lienzo en tiempo real
    /// El lienzo se mueve hacia un punto adelante de la mano/mando (en la dirección que apunta)
    /// a una distancia fija configurable (canvasDistanceFromController)
    /// Funciona tanto con Grip button como con T2 hand tracking
    /// </summary>
    private void UpdateCanvasMovement()
    {
        // Obtener posición y rotación de la mano/mando activo
        if (!GetActiveHandTransform(out Vector3 handPosition, out Quaternion handRotation))
            return;
        
        // Calcular posición objetivo: punto adelante de la mano en dirección forward
        Vector3 newPosition = handPosition + (handRotation * Vector3.forward) * canvasDistanceFromController;
        
        // Rotación objetivo: mantener la rotación relativa inicial
        // Calcular la diferencia de rotación desde que fue agarrado
        Quaternion rotationDelta = handRotation * Quaternion.Inverse(initialControllerRotation);
        Quaternion newRotation = rotationDelta * initialCanvasRotation;
        
        // EN EL PRIMER FRAME DESPUÉS DE ENGANCHAR: no mover el lienzo, solo inicializar targets
        if (justEngaged)
        {
            targetPosition = transform.position;
            targetRotation = transform.rotation;
            justEngaged = false; // Desactivar flag para frames posteriores
            return; // NO mover el lienzo en este frame
        }
        
        // EN FRAMES POSTERIORES: interpolar a velocidad constante (frame-rate independiente)
        if (useSmoothing)
        {
            // POSICIÓN: Mover a velocidad constante basada en tiempo delta
            float distanceToMove = Vector3.Distance(targetPosition, newPosition);
            if (distanceToMove > 0.001f) // Evitar división por cero
            {
                float maxMovement = positionSpeed * Time.deltaTime;
                float interpolationAmount = Mathf.Min(1f, maxMovement / distanceToMove);
                targetPosition = Vector3.Lerp(targetPosition, newPosition, interpolationAmount);
            }
            else
            {
                targetPosition = newPosition;
            }
            
            // ROTACIÓN: Rotar a velocidad constante basada en tiempo delta
            float angleDifference = Quaternion.Angle(targetRotation, newRotation);
            if (angleDifference > 0.01f) // Evitar cálculos innecesarios
            {
                float maxRotation = rotationSpeed * Time.deltaTime;
                float rotationInterpolation = Mathf.Min(1f, maxRotation / angleDifference);
                targetRotation = Quaternion.Lerp(targetRotation, newRotation, rotationInterpolation);
            }
            else
            {
                targetRotation = newRotation;
            }
        }
        else
        {
            // Sin suavizado: asignar directamente
            targetPosition = newPosition;
            targetRotation = newRotation;
        }
        
        // Debug: Ver qué está pasando con la posición
        #if UNITY_EDITOR
        if (Time.frameCount % 30 == 0) // Log cada 30 frames para no saturar console
        {
            string source = currentHandIsTracking ? "Hand Tracking (T2)" : "Controller (Grip)";
            Debug.Log($"[Seleccionar_Lienzo] [{source}] Pos: {handPosition} | NewPos: {newPosition} | TargetPos: {targetPosition} | ActualPos: {transform.position}");
        }
        #endif
        
        // Aplicar transformación al lienzo
        transform.position = targetPosition;
        transform.rotation = targetRotation;
    }

    #if UNITY_EDITOR
    
    /// <summary>
    /// Visualización en el editor para debugging
    /// Dibuja líneas desde los mandos hacia el lienzo
    /// </summary>
    void OnDrawGizmosSelected()
    {
        // Dibujar raycast de mano izquierda
        if (leftControllerTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(leftControllerTransform.position, leftControllerTransform.forward * 5f);
        }
        
        // Dibujar raycast de mano derecha
        if (rightControllerTransform != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawRay(rightControllerTransform.position, rightControllerTransform.forward * 5f);
        }
        
        // Dibujar esfera en lienzo
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.05f);
    }
    
    #endif
}
