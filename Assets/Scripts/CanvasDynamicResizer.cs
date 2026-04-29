using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class CanvasDynamicResizer : MonoBehaviour
{
    [Header("Configuración de Escala")]
    public float resizeSensitivity = 1.5f;
    public Vector3 minScale = new Vector3(0.1f, 0.1f, 0.1f);
    public Vector3 maxScale = new Vector3(5.0f, 5.0f, 5.0f);

    [Header("Referencias y Capas")]
    [Tooltip("Asigna aquí la capa ResizeHandle")]
    [SerializeField] private LayerMask handleLayerMask; 
    [SerializeField] private Transform leftControllerTransform;
    [SerializeField] private Transform rightControllerTransform;

    // Estado Interno
    private bool isResizing = false;
    private CanvasResizeHandleKind currentHandleKind;
    private Vector3 initialScale;
    private Vector3 lastHandLocalPos;
    private Seleccionar_Lienzo selectionScript;

    private void Awake()
    {
        selectionScript = GetComponent<Seleccionar_Lienzo>();
        
        // 🛡️ SISTEMA DE SEGURIDAD SENIOR: 
        int handleLayerIndex = (int)Mathf.Log(handleLayerMask.value, 2);
        if (gameObject.layer == handleLayerIndex && handleLayerIndex > 0)
        {
            Debug.LogWarning($"[CanvasResizer] Cambiando layer del lienzo '{gameObject.name}' a Default para evitar bloqueos de Raycast.");
            gameObject.layer = LayerMask.NameToLayer("Default");
        }
    }

    private void Update()
    {
        HandleResizeLogic();
    }

    private void HandleResizeLogic()
    {
        // 1. Identificar si ALGUIEN sujeta el lienzo (Mano A)
        var gripHand = GetHandGrippingThisCanvas();
        if (gripHand == null)
        {
            if (isResizing) StopResizing();
            return;
        }

        // 2. Definir cuál es la Mano B (la que va a estirar)
        CanvasGripManager.ActiveHand handB = (gripHand == CanvasGripManager.ActiveHand.Left) 
            ? CanvasGripManager.ActiveHand.Right 
            : CanvasGripManager.ActiveHand.Left;

        // 3. Detectar Gatillo Frontal (Index Trigger) en Mano B usando OpenXR / Nuevo Input System
        bool triggerPressed = GetIndexTriggerDown(handB);

        if (triggerPressed && !isResizing)
        {
            TryStartResizing(handB);
        }
        else if (!triggerPressed && isResizing)
        {
            StopResizing();
        }

        if (isResizing)
        {
            ApplyResize(handB);
        }
    }

    private void TryStartResizing(CanvasGripManager.ActiveHand hand)
    {
        Transform controllerT = GetControllerTransform(hand);
        if (controllerT == null) return;

        Ray ray = new Ray(controllerT.position, controllerT.forward);
        
        // Usamos RaycastAll para atravesar interferencias
        RaycastHit[] hits = Physics.RaycastAll(ray, 10f, handleLayerMask, QueryTriggerInteraction.Collide);
        
        foreach (var hit in hits)
        {
            CanvasResizeHandleMarker handle = hit.collider.GetComponent<CanvasResizeHandleMarker>();
            if (handle != null)
            {
                isResizing = true;
                currentHandleKind = handle.Kind;
                initialScale = transform.localScale;
                
                // Calculamos posición local
                lastHandLocalPos = transform.InverseTransformPoint(controllerT.position);
                Debug.Log($"[CanvasDynamicResizer] Iniciando resize en: {currentHandleKind}");
                return; // Borde encontrado, salimos
            }
        }
    }

    private void ApplyResize(CanvasGripManager.ActiveHand hand)
    {
        Transform controllerT = GetControllerTransform(hand);
        Vector3 currentHandLocalPos = transform.InverseTransformPoint(controllerT.position);
        Vector3 delta = currentHandLocalPos - lastHandLocalPos;

        Vector3 newScale = transform.localScale;

        // Lógica de expansión
        switch (currentHandleKind)
        {
            case CanvasResizeHandleKind.EdgeRight:
                newScale.x += delta.x * resizeSensitivity;
                break;
            case CanvasResizeHandleKind.EdgeLeft:
                newScale.x -= delta.x * resizeSensitivity;
                break;
            case CanvasResizeHandleKind.EdgeTop:
                newScale.y += delta.y * resizeSensitivity;
                break;
            case CanvasResizeHandleKind.EdgeBottom:
                newScale.y -= delta.y * resizeSensitivity;
                break;
            case CanvasResizeHandleKind.CornerTopRight:
                float growTR = (delta.x + delta.y) * 0.5f;
                newScale += new Vector3(growTR, growTR, 0) * resizeSensitivity;
                break;
            case CanvasResizeHandleKind.CornerTopLeft:
                float growTL = (-delta.x + delta.y) * 0.5f;
                newScale += new Vector3(growTL, growTL, 0) * resizeSensitivity;
                break;
            case CanvasResizeHandleKind.CornerBottomRight:
                float growBR = (delta.x - delta.y) * 0.5f;
                newScale += new Vector3(growBR, growBR, 0) * resizeSensitivity;
                break;
            case CanvasResizeHandleKind.CornerBottomLeft:
                float growBL = (-delta.x - delta.y) * 0.5f;
                newScale += new Vector3(growBL, growBL, 0) * resizeSensitivity;
                break;
        }

        // Aplicar límites
        newScale.x = Mathf.Clamp(newScale.x, minScale.x, maxScale.x);
        newScale.y = Mathf.Clamp(newScale.y, minScale.y, maxScale.y);
        newScale.z = initialScale.z; // El grosor Z no cambia

        transform.localScale = newScale;
        lastHandLocalPos = currentHandLocalPos;
    }

    private void StopResizing()
    {
        if (isResizing) Debug.Log("[CanvasDynamicResizer] Resize finalizado.");
        isResizing = false;
    }

    // --- MÉTODOS DE AYUDA ---

    private Transform GetControllerTransform(CanvasGripManager.ActiveHand hand)
    {
        if (leftControllerTransform == null || rightControllerTransform == null)
        {
            leftControllerTransform = GameObject.Find("LeftControllerAnchor")?.transform;
            rightControllerTransform = GameObject.Find("RightControllerAnchor")?.transform;
        }
        return (hand == CanvasGripManager.ActiveHand.Left) ? leftControllerTransform : rightControllerTransform;
    }

    // 🔥 FIX VITAL: AHORA LEE EL GATILLO FRONTAL USANDO EL MISMO SISTEMA QUE SELECCIONAR_LIENZO
    private bool GetIndexTriggerDown(CanvasGripManager.ActiveHand hand)
    {
        foreach (var device in InputSystem.devices)
        {
            if (device is XRController controller)
            {
                // Identificamos si este mando es el Izquierdo o el Derecho
                bool isLeft = controller.name.ToLower().Contains("left") || controller.path.ToLower().Contains("lefthand");
                
                if ((hand == CanvasGripManager.ActiveHand.Left && isLeft) || 
                    (hand == CanvasGripManager.ActiveHand.Right && !isLeft))
                {
                    // Leemos el gatillo frontal ("trigger")
                    var triggerControl = controller.TryGetChildControl<AxisControl>("trigger");
                    if (triggerControl != null)
                    {
                        return triggerControl.ReadValue() > 0.5f; // Retorna true si está pulsado a más de la mitad
                    }
                }
            }
        }
        return false;
    }

    private CanvasGripManager.ActiveHand? GetHandGrippingThisCanvas()
    {
        if (CanvasGripManager.Instance == null) return null;
        
        if (CanvasGripManager.Instance.GetGrippedCanvas(CanvasGripManager.ActiveHand.Left) == selectionScript)
            return CanvasGripManager.ActiveHand.Left;
        
        if (CanvasGripManager.Instance.GetGrippedCanvas(CanvasGripManager.ActiveHand.Right) == selectionScript)
            return CanvasGripManager.ActiveHand.Right;

        return null;
    }
}