using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
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
    
    [Header("=== GRIP DETECTION SETTINGS ===")]
    [SerializeField] private float gripPressThreshold = 0.5f;      // Valor analógico para considerar presionado
    [SerializeField] private float gripReleaseThreshold = 0.1f;    // Valor analógico para considerar soltado (histéresis)
    
    [Header("=== RAYCAST SETTINGS ===")]
    [SerializeField] private float raycastMaxDistance = 100f;      // Distancia máxima del raycast
    private Collider canvasCollider;
    
    [Header("=== MOVEMENT SMOOTHING ===")]
    [SerializeField] private float positionSpeed = 15f;            // Velocidad de movimiento en m/s (frame-rate independiente)
    [SerializeField] private float rotationSpeed = 720f;           // Velocidad de rotación en grados/s
    [SerializeField] private float canvasDistanceFromController = 4f; // Distancia deseada del lienzo adelante del mando
    [SerializeField] private bool useSmoothing = true;             // Habilitar/deshabilitar suavizado
    
    [Header("=== MANO DOMINANTE (opcional) ===")]
    [SerializeField] private bool prioritizeLastPressedHand = true; // Si ambas presionan, última gana
    
    // === ESTADO DE GRIP (por mano) ===
    private bool gripPressedLeft = false;
    private bool gripPressedRight = false;
    private bool lastGripPressedLeft = false;
    private bool lastGripPressedRight = false;
    
    // === OFFSETS RELATIVOS (cacheados al presionar Grip) ===
    // Removidos: ahora usamos distancia fija adelante del mando
    
    // === MANO ACTUALMENTE ACTIVA ===
    private enum ActiveHand { None, Left, Right }
    private ActiveHand currentActiveHand = ActiveHand.None;
    private bool justEngaged = false; // Flag para evitar salto en primer frame
    
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
        
        // Obtener collider del lienzo
        canvasCollider = GetComponent<Collider>();
        if (canvasCollider == null)
        {
            Debug.LogError($"[Seleccionar_Lienzo] El objeto '{gameObject.name}' no tiene Collider. Agrega un Box Collider o Mesh Collider.", gameObject);
        }
        
        // Inicializar posición/rotación objetivo
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    void Update()
    {
        // Actualizar estado de grip en ambos mandos
        UpdateGripState();
        
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
    /// Usa el orden de enumeración: primer device XR = izquierda, siguientes = derecha
    /// </summary>
    private bool IsLeftHandDevice(XRController device)
    {
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
    /// Detecta transiciones de Grip (presionado → soltado o soltado → presionado)
    /// Engancha el lienzo cuando se presiona Grip sobre él
    /// Lo desenganche cuando se suelta
    /// </summary>
    private void ProcessGripTransitions()
    {
        // TRANSICIÓN IZQUIERDA: soltado → presionado
        if (gripPressedLeft && !lastGripPressedLeft)
        {
            if (RaycastFromController(leftControllerTransform))
            {
                // Raycast éxitoso: enganchar lienzo con mano izquierda
                EngageCanvas(ActiveHand.Left, leftControllerTransform);
            }
        }
        
        // TRANSICIÓN DERECHA: soltado → presionado
        if (gripPressedRight && !lastGripPressedRight)
        {
            if (RaycastFromController(rightControllerTransform))
            {
                // Raycast éxitoso: enganchar lienzo con mano derecha
                EngageCanvas(ActiveHand.Right, rightControllerTransform);
            }
        }
        
        // LIBERACIÓN IZQUIERDA: presionado → soltado
        if (!gripPressedLeft && lastGripPressedLeft && currentActiveHand == ActiveHand.Left)
        {
            DisengageCanvas();
        }
        
        // LIBERACIÓN DERECHA: presionado → soltado
        if (!gripPressedRight && lastGripPressedRight && currentActiveHand == ActiveHand.Right)
        {
            DisengageCanvas();
        }
        
        // Guardar estado anterior
        lastGripPressedLeft = gripPressedLeft;
        lastGripPressedRight = gripPressedRight;
    }

    /// <summary>
    /// Realiza un raycast desde el mando hacia el lienzo
    /// Devuelve true si el raycast acierta el lienzo
    /// </summary>
    private bool RaycastFromController(Transform controller)
    {
        if (controller == null || canvasCollider == null)
            return false;
        
        Ray ray = new Ray(controller.position, controller.forward);
        
        // Debug visual (opcional: comentar si causa lag)
        // Debug.DrawRay(ray.origin, ray.direction * raycastMaxDistance, Color.cyan, 0.016f);
        
        if (Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance))
        {
            // Verificar si el raycast golpeó este lienzo específicamente
            if (hit.collider == canvasCollider || hit.collider.gameObject == gameObject)
            {
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Engancha el lienzo a un mando específico
    /// El lienzo se moverá a una distancia fija adelante del mando en la dirección forward
    /// </summary>
    private void EngageCanvas(ActiveHand hand, Transform controller)
    {
        currentActiveHand = hand;
        justEngaged = true; // Flag para evitar movimiento en el primer frame
        
        // Guardar rotaciones iniciales para mantener orientación relativa
        initialCanvasRotation = transform.rotation;
        initialControllerRotation = controller.rotation;
        
        // Detectar si se agarra de atrás: si el ángulo entre forward del mando y forward del lienzo es > 90 grados
        float dotProduct = Vector3.Dot(controller.forward, transform.forward);
        isGrabbedFromBehind = dotProduct < 0; // Si dot product es negativo, están mirando en direcciones opuestas
        
        #if UNITY_EDITOR
        Debug.Log($"[Seleccionar_Lienzo] ✓ Lienzo enganchado a mano {hand}, isGrabbedFromBehind: {isGrabbedFromBehind}");
        #endif
    }

    /// <summary>
    /// Desengancia el lienzo del mando
    /// El lienzo se queda fijo en su posición actual
    /// </summary>
    private void DisengageCanvas()
    {
        currentActiveHand = ActiveHand.None;
        justEngaged = false;
        
        #if UNITY_EDITOR
        Debug.Log("[Seleccionar_Lienzo] ✓ Lienzo desenganchado");
        #endif
    }

    /// <summary>
    /// Actualiza la posición y rotación del lienzo en tiempo real
    /// El lienzo se mueve hacia un punto adelante del mando (en la dirección que apunta)
    /// a una distancia fija configurable (canvasDistanceFromController)
    /// </summary>
    private void UpdateCanvasMovement()
    {
        Transform activeController = null;
        
        // Obtener mando activo
        if (currentActiveHand == ActiveHand.Left && leftControllerTransform != null)
        {
            activeController = leftControllerTransform;
        }
        else if (currentActiveHand == ActiveHand.Right && rightControllerTransform != null)
        {
            activeController = rightControllerTransform;
        }
        
        if (activeController == null)
            return;
        
        // Calcular posición objetivo: punto adelante del mando en dirección forward
        Vector3 newPosition = activeController.position + activeController.forward * canvasDistanceFromController;
        
        // Rotación objetivo: mantener la rotación relativa inicial
        // Calcular la diferencia de rotación desde que fue agarrado
        Quaternion rotationDelta = activeController.rotation * Quaternion.Inverse(initialControllerRotation);
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
            Debug.Log($"[Seleccionar_Lienzo] Controller: {activeController.position} | Forward: {activeController.forward} | NewPos: {newPosition} | TargetPos: {targetPosition} | ActualPos: {transform.position}");
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
