using UnityEngine;

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
    private Vector3 initialHandPosition;
    private Vector3 initialCanvasScale;
    private OVRInput.Controller handAController;
    private OVRInput.Controller handBController;

    // Tipo de borde interactuado
    private enum ResizeAxis { None, Horizontal, Vertical, Proportional }
    private ResizeAxis currentResizeAxis = ResizeAxis.None;

    // --- VARIABLES QUE FALTABAN (FIX CS0103) ---
        private CanvasResizeHandleKind currentHandleKind;
        private CanvasGripManager.ActiveHand resizingHand; 
        private Vector3 initialScale;
        private Vector3 lastHandLocalPos;
        private Seleccionar_Lienzo selectionScript;


    private bool triedAutoDetectControllers;

    private void Awake()
    {
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
        // Simplificación: Detectamos si ALGUN mando está pulsando el Grip (Gatillo lateral)
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
        // Detectar si la Mano B pulsa el gatillo frontal (Index)
        bool handBIndex = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, handBController);

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
        isResizing = true;
        initialHandPosition = OVRInput.GetLocalControllerPosition(handBController);
        initialCanvasScale = transform.localScale;

        // Determinar cómo escalar según el collider tocado (Esquina vs Borde)
        CanvasResizeHandleMarker marker = edge.GetComponent<CanvasResizeHandleMarker>();
        if (marker == null)
        {
            currentResizeAxis = ResizeAxis.None;
            return;
        }

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
        Vector3 currentHandPos = OVRInput.GetLocalControllerPosition(handBController);
        
        // 1. Calcular el vector de movimiento de la mano
        Vector3 handMovement = currentHandPos - initialHandPosition;

        // 2. Convertir el movimiento del mundo al espacio local del canvas
        // Esto es VITAL: Si el canvas está rotado, mover la mano en Z del mundo 
        // no significa hacer el canvas más grande.
        Vector3 localMovement = transform.InverseTransformDirection(handMovement);

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
        int mask = GetHandleLayerMask();
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
            if (leftControllerTransform == null && (t.name == "LeftControllerAnchor" || t.name == "LeftHand" || t.name == "LeftHandAnchor"))
            {
                leftControllerTransform = t;
            }

            if (rightControllerTransform == null && (t.name == "RightControllerAnchor" || t.name == "RightHand" || t.name == "RightHandAnchor"))
            {
                rightControllerTransform = t;
            }

            if (leftControllerTransform != null && rightControllerTransform != null)
            {
                return;
            }
        }
    }

    private void TryStartResizing(CanvasGripManager.ActiveHand hand)
{
    Transform controllerT = GetControllerTransform(hand);
    Ray ray = new Ray(controllerT.position, controllerT.forward);
    RaycastHit hit;

    // AÑADIDO: Pasamos la LayerMask al Raycast para que IGNORE el collider del lienzo
    if (Physics.Raycast(ray, out hit, 10f, handleLayerMask))
    {
        CanvasResizeHandleMarker handle = hit.collider.GetComponent<CanvasResizeHandleMarker>();
        if (handle != null)
        {
            isResizing = true;
            currentHandleKind = handle.Kind;
            resizingHand = hand;
            initialScale = transform.localScale;
            
            lastHandLocalPos = transform.InverseTransformPoint(controllerT.position);
            Debug.Log($"[CanvasResizer] Iniciando resize: {currentHandleKind}");
        }
    }
}

private Transform GetControllerTransform(CanvasGripManager.ActiveHand hand)
    {
        return (hand == CanvasGripManager.ActiveHand.Left) ? leftControllerTransform : rightControllerTransform;
    }

    private bool GetIndexTriggerDown(CanvasGripManager.ActiveHand hand)
    {
        OVRInput.Controller controller = (hand == CanvasGripManager.ActiveHand.Left) ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller);
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