using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using System;

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
    [SerializeField] private float triggerPressThreshold = 0.1f;
    [SerializeField] private float triggerReleaseThreshold = 0.05f;

    [Header("Detección de Handler")]
    [SerializeField] private bool useCanvasSurfaceFallback = true;
    [Range(0.05f, 0.45f)]
    [SerializeField] private float edgeZonePercent = 0.15f;

    [Header("Estabilidad de Resize")]
    [SerializeField] private float resizeMovementDeadZone = 0.0025f;

    // Estado Interno
    private bool isResizing = false;
    private CanvasResizeHandleKind currentHandleKind;
    private Vector3 resizeStartScale;
    private Vector3 resizeStartLocalHitPoint;
    private Seleccionar_Lienzo selectionScript;
    private bool warnedMissingControllerAnchors;
    private Collider canvasCollider;
    private SpriteRenderer canvasSpriteRenderer;
    private int lastNoHandleLogFrame = -999;
    private bool isResizeTriggerLatched;
    private CanvasGripManager.ActiveHand? latchedTriggerHand;

    private void Awake()
    {
        selectionScript = GetComponent<Seleccionar_Lienzo>();
        canvasCollider = GetComponent<Collider>();
        canvasSpriteRenderer = GetComponent<SpriteRenderer>();

        if (handleLayerMask.value == 0)
        {
            Debug.LogWarning($"[CanvasDynamicResizer] handleLayerMask no está configurada en '{gameObject.name}'. No se podrá detectar ningún handler.");
        }
        
        // 🛡️ SISTEMA DE SEGURIDAD SENIOR: 
        int handleLayerValue = handleLayerMask.value;
        bool isSingleLayerMask = handleLayerValue > 0 && (handleLayerValue & (handleLayerValue - 1)) == 0;
        if (isSingleLayerMask)
        {
            int handleLayerIndex = (int)Mathf.Log(handleLayerValue, 2);
            if (gameObject.layer == handleLayerIndex && handleLayerIndex > 0)
            {
                Debug.LogWarning($"[CanvasResizer] Cambiando layer del lienzo '{gameObject.name}' a Default para evitar bloqueos de Raycast.");
                gameObject.layer = LayerMask.NameToLayer("Default");
            }
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
            if (isResizing) StopResizing("se perdió el grip de la mano A");
            return;
        }

        // 2. Definir cuál es la Mano B (la que va a estirar)
        CanvasGripManager.ActiveHand handB = (gripHand == CanvasGripManager.ActiveHand.Left) 
            ? CanvasGripManager.ActiveHand.Right 
            : CanvasGripManager.ActiveHand.Left;

        if (latchedTriggerHand != handB)
        {
            latchedTriggerHand = handB;
            isResizeTriggerLatched = false;
        }

        // 3. Detectar Gatillo Frontal (Index Trigger) en Mano B usando OpenXR / Nuevo Input System
        bool triggerPressed = UpdateResizeTriggerLatch(handB);

        if (triggerPressed && !isResizing)
        {
            TryStartResizing(handB);
        }
        else if (!triggerPressed && isResizing)
        {
            StopResizing("se soltó el trigger de la mano B");
        }

        if (isResizing)
        {
            ApplyResize(handB);
        }
    }

    private void TryStartResizing(CanvasGripManager.ActiveHand hand)
    {
        Transform controllerT = GetControllerTransform(hand);
        if (controllerT == null)
        {
            Debug.LogWarning($"[CanvasDynamicResizer] No se puede iniciar resize: falta anchor del controlador para mano {hand}.");
            return;
        }

        Debug.Log($"[CanvasDynamicResizer] Intento de resize con mano B {hand} desde '{controllerT.name}'.");

        Ray ray = new Ray(controllerT.position, controllerT.forward);
        
        // Usamos RaycastAll para atravesar interferencias
        RaycastHit[] hits = Physics.RaycastAll(ray, 10f, handleLayerMask, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        
        foreach (var hit in hits)
        {
            CanvasResizeHandleMarker handle = hit.collider.GetComponent<CanvasResizeHandleMarker>();
            if (handle == null)
            {
                handle = hit.collider.GetComponentInParent<CanvasResizeHandleMarker>();
            }

            if (handle != null)
            {
                Debug.Log($"[CanvasDynamicResizer] ✓ Handler detectado: '{hit.collider.name}' | Kind: {handle.Kind} | Distancia: {hit.distance:F3}");
                BeginResizing(handle.Kind, transform.InverseTransformPoint(hit.point));
                Debug.Log($"[CanvasDynamicResizer] Iniciando resize en: {currentHandleKind}");
                return; // Borde encontrado, salimos
            }
        }

        if (useCanvasSurfaceFallback && TryResolveHandleFromCanvasSurface(ray, out CanvasResizeHandleKind fallbackKind, out float fallbackDistance))
        {
            Debug.Log($"[CanvasDynamicResizer] ✓ Handler detectado por fallback de superficie | Kind: {fallbackKind} | Distancia: {fallbackDistance:F3}");
            if (TryGetCanvasLocalHit(ray, out Vector3 localHitPoint, out _))
            {
                BeginResizing(fallbackKind, localHitPoint);
                Debug.Log($"[CanvasDynamicResizer] Iniciando resize en: {currentHandleKind}");
            }
            return;
        }

        if (Time.frameCount - lastNoHandleLogFrame > 30)
        {
            Debug.Log($"[CanvasDynamicResizer] Trigger activo en mano {hand}, pero no se detectó ningún handler (ni por capa ni por fallback de superficie).");
            lastNoHandleLogFrame = Time.frameCount;
        }
    }

    private void ApplyResize(CanvasGripManager.ActiveHand hand)
    {
        Transform controllerT = GetControllerTransform(hand);
        if (controllerT == null)
        {
            StopResizing("se perdió referencia del controlador durante resize");
            return;
        }

        Ray ray = new Ray(controllerT.position, controllerT.forward);
        if (!TryGetCanvasLocalHit(ray, out Vector3 currentHitLocalPoint, out _))
        {
            StopResizing("se perdió el punto de impacto sobre el lienzo");
            return;
        }

        Vector3 delta = currentHitLocalPoint - resizeStartLocalHitPoint;

        if (Mathf.Abs(delta.x) < resizeMovementDeadZone)
        {
            delta.x = 0f;
        }

        if (Mathf.Abs(delta.y) < resizeMovementDeadZone)
        {
            delta.y = 0f;
        }

        Vector3 newScale = resizeStartScale;

        // Lógica de expansión
        switch (currentHandleKind)
        {
            case CanvasResizeHandleKind.EdgeRight:
                newScale.x = resizeStartScale.x + (delta.x * resizeSensitivity);
                break;
            case CanvasResizeHandleKind.EdgeLeft:
                newScale.x = resizeStartScale.x - (delta.x * resizeSensitivity);
                break;
            case CanvasResizeHandleKind.EdgeTop:
                newScale.y = resizeStartScale.y + (delta.y * resizeSensitivity);
                break;
            case CanvasResizeHandleKind.EdgeBottom:
                newScale.y = resizeStartScale.y - (delta.y * resizeSensitivity);
                break;
            case CanvasResizeHandleKind.CornerTopRight:
                newScale.x = resizeStartScale.x + (delta.x * resizeSensitivity);
                newScale.y = resizeStartScale.y + (delta.y * resizeSensitivity);
                break;
            case CanvasResizeHandleKind.CornerTopLeft:
                newScale.x = resizeStartScale.x - (delta.x * resizeSensitivity);
                newScale.y = resizeStartScale.y + (delta.y * resizeSensitivity);
                break;
            case CanvasResizeHandleKind.CornerBottomRight:
                newScale.x = resizeStartScale.x + (delta.x * resizeSensitivity);
                newScale.y = resizeStartScale.y - (delta.y * resizeSensitivity);
                break;
            case CanvasResizeHandleKind.CornerBottomLeft:
                newScale.x = resizeStartScale.x - (delta.x * resizeSensitivity);
                newScale.y = resizeStartScale.y - (delta.y * resizeSensitivity);
                break;
        }

        // Aplicar límites
        newScale.x = Mathf.Clamp(newScale.x, minScale.x, maxScale.x);
        newScale.y = Mathf.Clamp(newScale.y, minScale.y, maxScale.y);
        newScale.z = resizeStartScale.z; // El grosor Z no cambia

        transform.localScale = newScale;
    }

    private void BeginResizing(CanvasResizeHandleKind handleKind, Vector3 localHitPoint)
    {
        isResizing = true;
        currentHandleKind = handleKind;
        resizeStartScale = transform.localScale;
        resizeStartLocalHitPoint = localHitPoint;
    }

    private void StopResizing(string reason = null)
    {
        if (isResizing)
        {
            if (string.IsNullOrEmpty(reason))
            {
                Debug.Log("[CanvasDynamicResizer] Resize finalizado.");
            }
            else
            {
                Debug.Log($"[CanvasDynamicResizer] Resize finalizado: {reason}.");
            }
        }

        isResizing = false;
        isResizeTriggerLatched = false;
    }

    // --- MÉTODOS DE AYUDA ---

    private Transform GetControllerTransform(CanvasGripManager.ActiveHand hand)
    {
        if (leftControllerTransform == null || rightControllerTransform == null)
        {
            leftControllerTransform = GameObject.Find("LeftControllerAnchor")?.transform;
            rightControllerTransform = GameObject.Find("RightControllerAnchor")?.transform;

            if ((leftControllerTransform == null || rightControllerTransform == null) && !warnedMissingControllerAnchors)
            {
                Debug.LogWarning("[CanvasDynamicResizer] No se encontraron LeftControllerAnchor/RightControllerAnchor. Asigna referencias en Inspector.");
                warnedMissingControllerAnchors = true;
            }
        }
        return (hand == CanvasGripManager.ActiveHand.Left) ? leftControllerTransform : rightControllerTransform;
    }

    // Lee gatillo frontal de mano específica usando la misma estrategia robusta que Paint.
    private bool UpdateResizeTriggerLatch(CanvasGripManager.ActiveHand hand)
    {
        if (!TryGetIndexTriggerValue(hand, out float triggerValue))
        {
            isResizeTriggerLatched = false;
            return false;
        }

        if (isResizeTriggerLatched)
        {
            if (triggerValue <= triggerReleaseThreshold)
            {
                isResizeTriggerLatched = false;
            }
        }
        else if (triggerValue >= triggerPressThreshold)
        {
            isResizeTriggerLatched = true;
        }

        return isResizeTriggerLatched;
    }

    private bool TryGetIndexTriggerValue(CanvasGripManager.ActiveHand hand, out float triggerValue)
    {
        triggerValue = 0f;
        bool foundDevice = false;

        foreach (var device in InputSystem.devices)
        {
            if (device is XRController controller)
            {
                bool isLeft = IsLeftHandDevice(controller);
                
                if ((hand == CanvasGripManager.ActiveHand.Left && isLeft) || 
                    (hand == CanvasGripManager.ActiveHand.Right && !isLeft))
                {
                    var triggerControl = controller.TryGetChildControl<AxisControl>("trigger");
                    if (triggerControl != null)
                    {
                        triggerValue = Mathf.Max(triggerValue, triggerControl.ReadValue());
                        foundDevice = true;
                    }

                    try
                    {
                        var triggerButton = controller.TryGetChildControl<ButtonControl>("trigger");
                        if (triggerButton != null)
                        {
                            foundDevice = true;
                            if (triggerButton.isPressed)
                            {
                                triggerValue = 1f;
                            }
                        }
                    }
                    catch
                    {
                        // Algunos dispositivos pueden no exponer trigger como botón.
                    }
                }
            }
        }

        return foundDevice;
    }

    private bool IsLeftHandDevice(XRController device)
    {
        int xrControllerIndex = -1;
        int xrControllerCount = 0;

        foreach (var dev in InputSystem.devices)
        {
            if (dev is XRController)
            {
                if (dev == device)
                {
                    xrControllerIndex = xrControllerCount;
                }

                xrControllerCount++;
            }
        }

        return xrControllerIndex == 0;
    }

    private bool TryResolveHandleFromCanvasSurface(Ray ray, out CanvasResizeHandleKind kind, out float distance)
    {
        kind = CanvasResizeHandleKind.EdgeRight;
        distance = 0f;

        if (!TryGetCanvasLocalHit(ray, out Vector3 localHitPoint, out float rayDistance))
        {
            return false;
        }

        if (!TryGetCanvasLocalBounds(out float minX, out float maxX, out float minY, out float maxY))
        {
            return false;
        }

        float width = Mathf.Max(maxX - minX, 0.0001f);
        float height = Mathf.Max(maxY - minY, 0.0001f);
        float marginX = width * edgeZonePercent;
        float marginY = height * edgeZonePercent;

        bool nearLeft = localHitPoint.x <= (minX + marginX);
        bool nearRight = localHitPoint.x >= (maxX - marginX);
        bool nearBottom = localHitPoint.y <= (minY + marginY);
        bool nearTop = localHitPoint.y >= (maxY - marginY);

        if (nearTop && nearLeft)
        {
            kind = CanvasResizeHandleKind.CornerTopLeft;
            distance = rayDistance;
            return true;
        }

        if (nearTop && nearRight)
        {
            kind = CanvasResizeHandleKind.CornerTopRight;
            distance = rayDistance;
            return true;
        }

        if (nearBottom && nearLeft)
        {
            kind = CanvasResizeHandleKind.CornerBottomLeft;
            distance = rayDistance;
            return true;
        }

        if (nearBottom && nearRight)
        {
            kind = CanvasResizeHandleKind.CornerBottomRight;
            distance = rayDistance;
            return true;
        }

        if (nearTop)
        {
            kind = CanvasResizeHandleKind.EdgeTop;
            distance = rayDistance;
            return true;
        }

        if (nearBottom)
        {
            kind = CanvasResizeHandleKind.EdgeBottom;
            distance = rayDistance;
            return true;
        }

        if (nearLeft)
        {
            kind = CanvasResizeHandleKind.EdgeLeft;
            distance = rayDistance;
            return true;
        }

        if (nearRight)
        {
            kind = CanvasResizeHandleKind.EdgeRight;
            distance = rayDistance;
            return true;
        }

        return false;
    }

    private bool TryGetCanvasLocalHit(Ray ray, out Vector3 localHitPoint, out float distance)
    {
        localHitPoint = Vector3.zero;
        distance = 0f;

        if (canvasCollider != null)
        {
            if (canvasCollider.Raycast(ray, out RaycastHit hit, 10f))
            {
                localHitPoint = transform.InverseTransformPoint(hit.point);
                distance = hit.distance;
                return true;
            }
        }

        if (canvasSpriteRenderer != null)
        {
            Vector3 planeNormal = canvasSpriteRenderer.transform.forward;
            Vector3 planePoint = canvasSpriteRenderer.transform.position;
            float denom = Vector3.Dot(planeNormal, ray.direction);

            if (Mathf.Abs(denom) > 1e-6f)
            {
                float t = Vector3.Dot(planeNormal, planePoint - ray.origin) / denom;
                if (t >= 0f)
                {
                    Vector3 worldPoint = ray.GetPoint(t);
                    localHitPoint = canvasSpriteRenderer.transform.InverseTransformPoint(worldPoint);
                    distance = t;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryGetCanvasLocalBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = maxX = minY = maxY = 0f;

        if (canvasSpriteRenderer != null && canvasSpriteRenderer.sprite != null)
        {
            Rect spriteRect = canvasSpriteRenderer.sprite.rect;
            Vector2 spritePivot = canvasSpriteRenderer.sprite.pivot;
            float ppu = canvasSpriteRenderer.sprite.pixelsPerUnit;

            float spriteWidth = spriteRect.width / ppu;
            float spriteHeight = spriteRect.height / ppu;
            float pivotX = spritePivot.x / ppu;
            float pivotY = spritePivot.y / ppu;

            minX = -pivotX;
            maxX = spriteWidth - pivotX;
            minY = -pivotY;
            maxY = spriteHeight - pivotY;
            return true;
        }

        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 half = box.size * 0.5f;
            Vector3 c = box.center;
            minX = c.x - half.x;
            maxX = c.x + half.x;
            minY = c.y - half.y;
            maxY = c.y + half.y;
            return true;
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