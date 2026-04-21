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
/// 3. Mientras se mantiene presionado, el lienzo sigue la posición Y rotación del mando en tiempo real
/// 4. Al soltar el Grip Button, el lienzo se fija en su posición actual
/// 
/// CARACTERÍSTICAS:
/// - Ambas manos (izquierda y derecha) funcionan de forma independiente
/// - Sistema de raycast para detectar si el mando apunta al lienzo
/// - Suavizado de movimiento con Lerp para evitar jitter
/// - Sincronización de rotación con el mando
/// - Calibración automática de posición/rotación relativa al presionar Grip
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
    [SerializeField] private float positionSmoothingAlpha = 0.2f;  // 0.2 = suavizado moderado (menor = más suave)
    [SerializeField] private float rotationSmoothingAlpha = 0.15f; // 0.15 = suavizado más agresivo para rotación
    [SerializeField] private bool useSmoothing = true;             // Habilitar/deshabilitar suavizado
    
    [Header("=== MANO DOMINANTE (opcional) ===")]
    [SerializeField] private bool prioritizeLastPressedHand = true; // Si ambas presionan, última gana
    
    // === ESTADO DE GRIP (por mano) ===
    private bool gripPressedLeft = false;
    private bool gripPressedRight = false;
    private bool lastGripPressedLeft = false;
    private bool lastGripPressedRight = false;
    
    // === OFFSETS RELATIVOS (cacheados al presionar Grip) ===
    private Vector3 leftGripPositionOffset;
    private Quaternion leftGripRotationOffset;
    private Vector3 rightGripPositionOffset;
    private Quaternion rightGripRotationOffset;
    
    // === MANO ACTUALMENTE ACTIVA ===
    private enum ActiveHand { None, Left, Right }
    private ActiveHand currentActiveHand = ActiveHand.None;
    
    // === POSICIÓN/ROTACIÓN OBJETIVO SUAVIZADA ===
    private Vector3 targetPosition;
    private Quaternion targetRotation;

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
    /// Calcula y cachea los offsets relativos de posición y rotación
    /// </summary>
    private void EngageCanvas(ActiveHand hand, Transform controller)
    {
        currentActiveHand = hand;
        
        // Calcular offset relativo = diferencia entre posición del lienzo y del mando
        Vector3 positionOffset = transform.position - controller.position;
        
        // Calcular offset de rotación = rotación relativa
        Quaternion rotationOffset = transform.rotation * Quaternion.Inverse(controller.rotation);
        
        // Guardar offsets según la mano
        if (hand == ActiveHand.Left)
        {
            leftGripPositionOffset = positionOffset;
            leftGripRotationOffset = rotationOffset;
        }
        else if (hand == ActiveHand.Right)
        {
            rightGripPositionOffset = positionOffset;
            rightGripRotationOffset = rotationOffset;
        }
        
        // Inicializar posición/rotación objetivo
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        
        #if UNITY_EDITOR
        Debug.Log($"[Seleccionar_Lienzo] ✓ Lienzo enganchado a mano {hand}");
        #endif
    }

    /// <summary>
    /// Desengancia el lienzo del mando
    /// El lienzo se queda fijo en su posición actual
    /// </summary>
    private void DisengageCanvas()
    {
        currentActiveHand = ActiveHand.None;
        
        #if UNITY_EDITOR
        Debug.Log("[Seleccionar_Lienzo] ✓ Lienzo desenganchado");
        #endif
    }

    /// <summary>
    /// Actualiza la posición y rotación del lienzo en tiempo real
    /// Sigue el mando manteniendo el offset relativo calculado al presionar Grip
    /// Aplica suavizado con Lerp para evitar jitter
    /// </summary>
    private void UpdateCanvasMovement()
    {
        Transform activeController = null;
        Vector3 positionOffset = Vector3.zero;
        Quaternion rotationOffset = Quaternion.identity;
        
        // Obtener offset según la mano activa
        if (currentActiveHand == ActiveHand.Left && leftControllerTransform != null)
        {
            activeController = leftControllerTransform;
            positionOffset = leftGripPositionOffset;
            rotationOffset = leftGripRotationOffset;
        }
        else if (currentActiveHand == ActiveHand.Right && rightControllerTransform != null)
        {
            activeController = rightControllerTransform;
            positionOffset = rightGripPositionOffset;
            rotationOffset = rightGripRotationOffset;
        }
        
        if (activeController == null)
            return;
        
        // Calcular nueva posición y rotación basada en posición del mando
        Vector3 newPosition = activeController.position + positionOffset;
        Quaternion newRotation = activeController.rotation * rotationOffset;
        
        // Aplicar suavizado si está habilitado
        if (useSmoothing)
        {
            targetPosition = Vector3.Lerp(targetPosition, newPosition, positionSmoothingAlpha);
            targetRotation = Quaternion.Lerp(targetRotation, newRotation, rotationSmoothingAlpha);
        }
        else
        {
            targetPosition = newPosition;
            targetRotation = newRotation;
        }
        
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
