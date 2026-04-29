using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
#endif

public class CanvasDynamicResizer : MonoBehaviour
{
    [Header("Configuración")]
    public float resizeSensitivity = 1.0f;
    public Vector3 minScale = new Vector3(0.5f, 0.5f, 0.5f);
    public Vector3 maxScale = new Vector3(3.0f, 3.0f, 3.0f);

    [Header("Detección de bordes")]
    [SerializeField] private float raycastMaxDistance = 100f;
    [SerializeField] private Transform leftControllerTransform;
    [SerializeField] private Transform rightControllerTransform;
    [SerializeField] private Vector2 handleSizeLocal = new Vector2(2f, 2f);
    [SerializeField] private float handleDepthLocal = 2f;
    [SerializeField] private bool logEdgeHits = false;
    [SerializeField] private bool autoCreateHandles = true;
    [SerializeField] private string handleLayerName = "ResizeHandle";
    [SerializeField] private bool onlyRaycastHandles = true;

    [Header("Raycast Settings")]
    // Asigna esto en el Inspector a la capa "ResizeHandles"
    [SerializeField] private LayerMask handleLayerMask;

    [Header("Estado Interno (Solo lectura)")]
    public bool isHeldByHandA = false;
    public bool isResizing = false;

    // Referencias para el cálculo
    private Vector3 initialHandLocalPosition;
    private Vector3 initialCanvasScale;
    private OVRInput.Controller handAController;
    private OVRInput.Controller handBController;

    private const float IndexPressThreshold = 0.2f;
    private const float IndexReleaseThreshold = 0.1f;
    private bool handBIndexHeld;

    // Tipo de borde interactuado
    private enum ResizeAxis { None, Horizontal, Vertical, Proportional }
    private ResizeAxis currentResizeAxis = ResizeAxis.None;

    private Seleccionar_Lienzo selectionScript;


    private bool triedAutoDetectControllers;

    private void Awake()
    {
        selectionScript = GetComponent<Seleccionar_Lienzo>();
        TryAutoDetectControllers();
        EnsureHandleColliders();
        ApplyHandleSizes();
    }

    private void OnValidate()
    {
        EnsureHandleColliders();
        ApplyHandleSizes();
    }

    void Update()
    {
        HandleHoldLogic();

        // RESTRICCIÓN: Solo si está sujeto por la Mano A, evaluamos la Mano B
        if (isHeldByHandA)
        {
            HandleResizeLogic();
        }
        else
        {
            // Si no está sujeto, nos aseguramos de cancelar cualquier redimensión activa
            if (isResizing) StopResizing();
        }
    }

    private void HandleHoldLogic()
    {
        CanvasGripManager.ActiveHand? grippingHand = GetHandGrippingThisCanvas();
        if (grippingHand.HasValue)
        {
            isHeldByHandA = true;
            handAController = grippingHand.Value == CanvasGripManager.ActiveHand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            handBController = grippingHand.Value == CanvasGripManager.ActiveHand.Left ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            return;
        }

        if (CanvasGripManager.Instance != null && selectionScript != null)
        {
            isHeldByHandA = false;
            return;
        }

        bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool rightGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);

        if (!isHeldByHandA && (leftGrip || rightGrip))
        {
            isHeldByHandA = true;
            handAController = leftGrip ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            handBController = leftGrip ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        }
        else if (isHeldByHandA && !OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, handAController))
        {
            isHeldByHandA = false;
        }
    }

    private void HandleResizeLogic()
    {
        bool handBIndex = GetHandBIndexPressed();

        if (handBIndex && !isResizing)
        {
            // AQUÍ: Lógica de detección del borde (Raycast o Collider overlap)
            // Supongamos que tienes un método que te devuelve qué borde estás tocando
            GameObject touchedEdge = CheckIfHandBIsTouchingEdge(); 

            if (touchedEdge != null)
            {
                StartResizing(touchedEdge);
            }
        }
        else if (!handBIndex && isResizing)
        {
            StopResizing();
        }

        if (isResizing)
        {
            ApplyResize();
        }
    }

    private void StartResizing(GameObject edge)
    {
        CanvasResizeHandleMarker marker = edge.GetComponent<CanvasResizeHandleMarker>();
        if (marker == null)
        {
            currentResizeAxis = ResizeAxis.None;
            return;
        }

        if (!TryGetHandBLocalPosition(out initialHandLocalPosition))
        {
            currentResizeAxis = ResizeAxis.None;
            return;
        }

        isResizing = true;
        initialCanvasScale = transform.localScale;

        switch (marker.Kind)
        {
            case CanvasResizeHandleKind.EdgeLeft:
            case CanvasResizeHandleKind.EdgeRight:
                currentResizeAxis = ResizeAxis.Horizontal;
                break;
            case CanvasResizeHandleKind.EdgeTop:
            case CanvasResizeHandleKind.EdgeBottom:
                currentResizeAxis = ResizeAxis.Vertical;
                break;
            default:
                currentResizeAxis = ResizeAxis.Proportional;
                break;
        }
    }

    private void StopResizing()
    {
        isResizing = false;
        currentResizeAxis = ResizeAxis.None;
    }

    private void ApplyResize()
    {
        if (!TryGetHandBLocalPosition(out Vector3 currentHandLocalPos))
        {
            StopResizing();
            return;
        }

        Vector3 localMovement = currentHandLocalPos - initialHandLocalPosition;

        Vector3 newScale = initialCanvasScale;

        // 3. Aplicar la matemática según el eje
        switch (currentResizeAxis)
        {
            case ResizeAxis.Horizontal:
                newScale.x += localMovement.x * resizeSensitivity;
                break;
            case ResizeAxis.Vertical:
                newScale.y += localMovement.y * resizeSensitivity;
                break;
            case ResizeAxis.Proportional:
                // Usamos la magnitud o el promedio del movimiento para escalar uniformemente
                float uniformScale = (localMovement.x + localMovement.y) * resizeSensitivity;
                newScale += new Vector3(uniformScale, uniformScale, uniformScale);
                break;
        }

        // 4. Aplicar límites de seguridad (Clamp) para que no se invierta o crezca infinitamente
        newScale.x = Mathf.Clamp(newScale.x, minScale.x, maxScale.x);
        newScale.y = Mathf.Clamp(newScale.y, minScale.y, maxScale.y);
        newScale.z = Mathf.Clamp(newScale.z, minScale.z, maxScale.z);

        // 5. Asignar la nueva escala
        transform.localScale = newScale;
    }

    // Método simulado: Sustituye esto por tu sistema actual de detección (Triggers o Raycast)
    private GameObject CheckIfHandBIsTouchingEdge()
    {
        Transform controller = GetControllerTransform(handBController);
        if (controller == null)
        {
            TryAutoDetectControllers();
            controller = GetControllerTransform(handBController);
            if (controller == null)
            {
                return null;
            }
        }

        Ray ray = new Ray(controller.position, controller.forward);
        int mask = handleLayerMask.value != 0 ? handleLayerMask.value : GetHandleLayerMask();
        RaycastHit[] hits = Physics.RaycastAll(ray, raycastMaxDistance, mask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            return null;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            CanvasResizeHandleMarker marker = hits[i].collider.GetComponentInParent<CanvasResizeHandleMarker>();
            if (marker != null)
            {
                if (logEdgeHits)
                {
                    Debug.Log($"[CanvasDynamicResizer] Hit handle {marker.Kind} with {hits[i].collider.name}", hits[i].collider);
                }
                return marker.gameObject;
            }
        }

        if (logEdgeHits)
        {
            string hitList = string.Empty;
            int maxLogHits = Mathf.Min(4, hits.Length);
            for (int i = 0; i < maxLogHits; i++)
            {
                Collider col = hits[i].collider;
                hitList += $"{col.name} (layer {LayerMask.LayerToName(col.gameObject.layer)}), ";
            }
            Debug.Log($"[CanvasDynamicResizer] Raycast hit, but no CanvasResizeHandleMarker found. First hits: {hitList}");
        }

        return null;
    }

    private void ApplyHandleSizes()
    {
        CanvasResizeHandleMarker[] markers = GetComponentsInChildren<CanvasResizeHandleMarker>(true);
        for (int i = 0; i < markers.Length; i++)
        {
            BoxCollider box = markers[i].GetComponent<BoxCollider>();
            if (box == null)
            {
                continue;
            }

            box.isTrigger = true;
            box.size = new Vector3(handleSizeLocal.x, handleSizeLocal.y, handleDepthLocal);
            ApplyHandleLayer(markers[i].gameObject);
        }
    }

    private void EnsureHandleColliders()
    {
        if (!autoCreateHandles)
        {
            return;
        }

        Transform existingRoot = transform.Find("ResizeHandles");
        if (existingRoot != null)
        {
            if (existingRoot.childCount == 0)
            {
                CreateHandleChildren(existingRoot);
            }

            return;
        }

        GameObject rootObj = new GameObject("ResizeHandles");
        rootObj.transform.SetParent(transform, false);
        CreateHandleChildren(rootObj.transform);
    }

    private void CreateHandleChildren(Transform root)
    {
        Vector2 half = GetCanvasHalfSizeLocal();

        CreateHandle(root, "CornerTopLeft", new Vector3(-half.x, half.y, 0f), CanvasResizeHandleKind.CornerTopLeft);
        CreateHandle(root, "CornerTopRight", new Vector3(half.x, half.y, 0f), CanvasResizeHandleKind.CornerTopRight);
        CreateHandle(root, "CornerBottomLeft", new Vector3(-half.x, -half.y, 0f), CanvasResizeHandleKind.CornerBottomLeft);
        CreateHandle(root, "CornerBottomRight", new Vector3(half.x, -half.y, 0f), CanvasResizeHandleKind.CornerBottomRight);

        CreateHandle(root, "EdgeTop", new Vector3(0f, half.y, 0f), CanvasResizeHandleKind.EdgeTop);
        CreateHandle(root, "EdgeBottom", new Vector3(0f, -half.y, 0f), CanvasResizeHandleKind.EdgeBottom);
        CreateHandle(root, "EdgeLeft", new Vector3(-half.x, 0f, 0f), CanvasResizeHandleKind.EdgeLeft);
        CreateHandle(root, "EdgeRight", new Vector3(half.x, 0f, 0f), CanvasResizeHandleKind.EdgeRight);
    }

    private void CreateHandle(Transform root, string handleName, Vector3 localPos, CanvasResizeHandleKind kind)
    {
        GameObject h = new GameObject(handleName);
        h.transform.SetParent(root, false);
        h.transform.localPosition = localPos;
        h.transform.localRotation = Quaternion.identity;
        ApplyHandleLayer(h);

        BoxCollider col = h.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(handleSizeLocal.x, handleSizeLocal.y, handleDepthLocal);

        CanvasResizeHandleMarker marker = h.AddComponent<CanvasResizeHandleMarker>();
        marker.Initialize(kind);
    }

    private int GetHandleLayerMask()
    {
        if (!onlyRaycastHandles)
        {
            return ~0;
        }

        int layer = LayerMask.NameToLayer(handleLayerName);
        if (layer < 0)
        {
            return ~0;
        }

        return 1 << layer;
    }

    private void ApplyHandleLayer(GameObject handle)
    {
        int layer = LayerMask.NameToLayer(handleLayerName);
        if (layer >= 0)
        {
            handle.layer = layer;
        }
    }

    private Vector2 GetCanvasHalfSizeLocal()
    {
        BoxCollider box = GetComponent<Collider>() as BoxCollider;
        if (box != null)
        {
            Vector3 half = box.size * 0.5f;
            return new Vector2(Mathf.Abs(half.x), Mathf.Abs(half.y));
        }

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            float ppu = sr.sprite.pixelsPerUnit;
            Rect rect = sr.sprite.rect;
            return new Vector2(rect.width / ppu * 0.5f, rect.height / ppu * 0.5f);
        }

        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            Vector3 localExtents = transform.InverseTransformVector(r.bounds.extents);
            return new Vector2(Mathf.Abs(localExtents.x), Mathf.Abs(localExtents.y));
        }

        return new Vector2(0.5f, 0.5f);
    }

    private Transform GetControllerTransform(OVRInput.Controller controller)
    {
        if ((controller & OVRInput.Controller.LTouch) != 0 || (controller & OVRInput.Controller.LHand) != 0)
        {
            return leftControllerTransform;
        }

        if ((controller & OVRInput.Controller.RTouch) != 0 || (controller & OVRInput.Controller.RHand) != 0)
        {
            return rightControllerTransform;
        }

        return null;
    }

    private void TryAutoDetectControllers()
    {
        if (triedAutoDetectControllers)
        {
            return;
        }

        triedAutoDetectControllers = true;
        if (leftControllerTransform != null && rightControllerTransform != null)
        {
            return;
        }

        Transform[] allObjects = FindObjectsOfType<Transform>();
        foreach (Transform t in allObjects)
        {
            string name = t.name;
            if (name.Contains("Detached"))
            {
                continue;
            }

            if (leftControllerTransform == null && (name == "LeftControllerAnchor" || name == "LeftHand" || name == "LeftHandAnchor" ||
                (name.Contains("Left") && (name.Contains("Controller") || name.Contains("Hand")))))
            {
                leftControllerTransform = t;
            }

            if (rightControllerTransform == null && (name == "RightControllerAnchor" || name == "RightHand" || name == "RightHandAnchor" ||
                (name.Contains("Right") && (name.Contains("Controller") || name.Contains("Hand")))))
            {
                rightControllerTransform = t;
            }

            if (leftControllerTransform != null && rightControllerTransform != null)
            {
                return;
            }
        }
    }

    private bool TryGetHandBWorldPosition(out Vector3 worldPosition)
    {
        Transform controller = GetControllerTransform(handBController);
        if (controller == null)
        {
            TryAutoDetectControllers();
            controller = GetControllerTransform(handBController);
        }

        if (controller == null)
        {
            worldPosition = Vector3.zero;
            return false;
        }

        worldPosition = controller.position;
        return true;
    }

    private bool TryGetHandBLocalPosition(out Vector3 localPosition)
    {
        if (!TryGetHandBWorldPosition(out Vector3 worldPosition))
        {
            localPosition = Vector3.zero;
            return false;
        }

        localPosition = transform.InverseTransformPoint(worldPosition);
        return true;
    }

    private bool GetHandBIndexPressed()
    {
        bool isLeftHand = IsLeftController(handBController);
        if (!TryGetIndexTriggerValueFromInputSystem(isLeftHand, out float value))
        {
            value = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, handBController);
        }

        if (!handBIndexHeld && value >= IndexPressThreshold)
        {
            handBIndexHeld = true;
        }
        else if (handBIndexHeld && value <= IndexReleaseThreshold)
        {
            handBIndexHeld = false;
        }

        return handBIndexHeld;
    }

    private bool IsLeftController(OVRInput.Controller controller)
    {
        return (controller & OVRInput.Controller.LTouch) != 0 || (controller & OVRInput.Controller.LHand) != 0;
    }

    private bool TryGetIndexTriggerValueFromInputSystem(bool isLeftHand, out float value)
    {
#if ENABLE_INPUT_SYSTEM
        bool foundDevice = false;
        foreach (var dev in InputSystem.devices)
        {
            if (dev is XRController xr)
            {
                bool deviceIsLeft = IsLeftHandDevice(xr);
                if (deviceIsLeft != isLeftHand)
                {
                    continue;
                }

                foundDevice = true;
                var triggerControl = xr.TryGetChildControl<InputControl>("trigger");
                if (triggerControl is AxisControl triggerAxis)
                {
                    value = triggerAxis.ReadValue();
                    return true;
                }

                if (triggerControl is ButtonControl triggerButton)
                {
                    value = triggerButton.isPressed ? 1f : 0f;
                    return true;
                }
            }
        }

        if (foundDevice)
        {
            value = 0f;
            return true;
        }
#endif

        value = 0f;
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    private bool IsLeftHandDevice(XRController device)
    {
        if (device.name.Contains("Left") || device.name.Contains("left"))
            return true;
        if (device.name.Contains("Right") || device.name.Contains("right"))
            return false;

        if (device.path.Contains("lefthand"))
            return true;
        if (device.path.Contains("righthand"))
            return false;

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

        return xrControllerIndex == 0;
    }
#endif

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