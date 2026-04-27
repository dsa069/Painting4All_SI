using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

public class CanvasResizeController : MonoBehaviour
{
    [SerializeField] private float triggerThreshold = 0.15f;
    [SerializeField] private float raycastMaxDistance = 100f;

    [Header("Scale Limits")]
    [SerializeField] private float minScaleX = 0.2f;
    [SerializeField] private float maxScaleX = 6f;
    [SerializeField] private float minScaleY = 0.2f;
    [SerializeField] private float maxScaleY = 6f;

    [Header("Resize Smoothing")]
    [SerializeField] private float smoothSpeed = 18f;

    [Header("Handle Collider")]
    [SerializeField] private Vector2 handleSizeLocal = new Vector2(10f, 10f);
    [SerializeField] private float handleDepthLocal = 10f;

    private Transform leftControllerTransform;
    private Transform rightControllerTransform;

    private bool triggerPressedLeft;
    private bool triggerPressedRight;
    private bool lastTriggerPressedLeft;
    private bool lastTriggerPressedRight;

    private bool isResizing;
    private CanvasGripManager.ActiveHand resizeHand;
    private CanvasResizeHandleKind activeHandleKind;

    private Vector3 startScale;
    private float startProjectedDistance;
    private Vector3 handleDirectionLocal;
    private Vector3 pivotLocal;

    private Vector3 targetScale;

    private Seleccionar_Lienzo thisCanvas;

    private void Awake()
    {
        thisCanvas = GetComponent<Seleccionar_Lienzo>();
        targetScale = transform.localScale;
        AutoDetectControllers();
        EnsureHandleColliders();
    }

    private void LateUpdate()
    {
        UpdateTriggerState();

        if (!TryGetGrippedHand(out CanvasGripManager.ActiveHand grippedHand))
        {
            isResizing = false;
            return;
        }

        resizeHand = CanvasGripManager.Instance.GetOppositeHand(grippedHand);

        bool isTriggerDown = GetTriggerDown(resizeHand);
        bool isTriggerHeld = GetTriggerHeld(resizeHand);

        if (!isResizing && isTriggerDown)
        {
            TryBeginResize();
        }

        if (isResizing)
        {
            if (!isTriggerHeld)
            {
                isResizing = false;
            }
            else
            {
                UpdateResize();
            }
        }

        lastTriggerPressedLeft = triggerPressedLeft;
        lastTriggerPressedRight = triggerPressedRight;
    }

    private void AutoDetectControllers()
    {
        Transform[] allObjects = FindObjectsOfType<Transform>();

        foreach (Transform t in allObjects)
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
    }

    private void UpdateTriggerState()
    {
        triggerPressedLeft = false;
        triggerPressedRight = false;

        foreach (InputDevice device in InputSystem.devices)
        {
            if (!(device is XRController xrController))
            {
                continue;
            }

            AxisControl triggerControl = xrController.TryGetChildControl<AxisControl>("trigger");
            bool isPressed = triggerControl != null && triggerControl.ReadValue() >= triggerThreshold;

            if (IsLeftHandDevice(xrController))
            {
                triggerPressedLeft = isPressed;
            }
            else
            {
                triggerPressedRight = isPressed;
            }
        }
    }

    private bool IsLeftHandDevice(XRController device)
    {
        if (device.name.Contains("Left") || device.name.Contains("left"))
        {
            return true;
        }

        if (device.name.Contains("Right") || device.name.Contains("right"))
        {
            return false;
        }

        if (device.path.Contains("lefthand"))
        {
            return true;
        }

        if (device.path.Contains("righthand"))
        {
            return false;
        }

        int xrControllerIndex = -1;
        int xrControllerCount = 0;

        foreach (InputDevice dev in InputSystem.devices)
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

    private bool TryGetGrippedHand(out CanvasGripManager.ActiveHand hand)
    {
        hand = CanvasGripManager.ActiveHand.Left;

        if (thisCanvas == null)
        {
            return false;
        }

        CanvasGripManager.ActiveHand? maybe = CanvasGripManager.Instance.GetHandGrippingCanvas(thisCanvas);
        if (!maybe.HasValue)
        {
            return false;
        }

        hand = maybe.Value;
        return true;
    }

    private bool GetTriggerHeld(CanvasGripManager.ActiveHand hand)
    {
        return hand == CanvasGripManager.ActiveHand.Left ? triggerPressedLeft : triggerPressedRight;
    }

    private bool GetTriggerDown(CanvasGripManager.ActiveHand hand)
    {
        if (hand == CanvasGripManager.ActiveHand.Left)
        {
            return triggerPressedLeft && !lastTriggerPressedLeft;
        }

        return triggerPressedRight && !lastTriggerPressedRight;
    }

    private Transform GetController(CanvasGripManager.ActiveHand hand)
    {
        return hand == CanvasGripManager.ActiveHand.Left ? leftControllerTransform : rightControllerTransform;
    }

    private void TryBeginResize()
    {
        Transform controller = GetController(resizeHand);
        if (controller == null)
        {
            return;
        }

        Ray ray = new Ray(controller.position, controller.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, raycastMaxDistance, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            CanvasResizeHandleMarker marker = hits[i].collider.GetComponent<CanvasResizeHandleMarker>();
            if (marker == null || marker.Owner != this)
            {
                continue;
            }

            BeginResize(marker.Kind, controller.position);
            return;
        }
    }

    private void BeginResize(CanvasResizeHandleKind kind, Vector3 controllerWorldPos)
    {
        Vector2 half = GetCanvasHalfSizeLocal();

        activeHandleKind = kind;
        startScale = transform.localScale;

        handleDirectionLocal = GetHandleDirection(kind).normalized;
        pivotLocal = GetPivotLocal(kind, half);

        Vector3 startControllerLocal = transform.InverseTransformPoint(controllerWorldPos);
        startProjectedDistance = Mathf.Max(0.0001f, Vector3.Dot(startControllerLocal - pivotLocal, handleDirectionLocal));

        targetScale = transform.localScale;
        isResizing = true;

        string handleFamily = IsCorner(kind) ? "esquina" : "borde";
        Debug.Log($"[CanvasResize] Agarre detectado en {handleFamily}: {kind}");
    }

    private bool IsCorner(CanvasResizeHandleKind kind)
    {
        switch (kind)
        {
            case CanvasResizeHandleKind.CornerTopLeft:
            case CanvasResizeHandleKind.CornerTopRight:
            case CanvasResizeHandleKind.CornerBottomLeft:
            case CanvasResizeHandleKind.CornerBottomRight:
                return true;
            default:
                return false;
        }
    }

    private void UpdateResize()
    {
        Transform controller = GetController(resizeHand);
        if (controller == null)
        {
            isResizing = false;
            return;
        }

        Vector3 controllerLocal = transform.InverseTransformPoint(controller.position);
        float currentProjected = Vector3.Dot(controllerLocal - pivotLocal, handleDirectionLocal);
        float ratio = currentProjected / startProjectedDistance;
        ratio = Mathf.Clamp(ratio, 0.1f, 10f);

        Vector3 desired = startScale;

        switch (activeHandleKind)
        {
            case CanvasResizeHandleKind.EdgeLeft:
            case CanvasResizeHandleKind.EdgeRight:
                desired.x = Mathf.Clamp(startScale.x * ratio, minScaleX, maxScaleX);
                desired.y = transform.localScale.y;
                break;

            case CanvasResizeHandleKind.EdgeTop:
            case CanvasResizeHandleKind.EdgeBottom:
                desired.y = Mathf.Clamp(startScale.y * ratio, minScaleY, maxScaleY);
                desired.x = transform.localScale.x;
                break;

            default:
                desired.x = Mathf.Clamp(startScale.x * ratio, minScaleX, maxScaleX);
                desired.y = Mathf.Clamp(startScale.y * ratio, minScaleY, maxScaleY);
                break;
        }

        desired.z = transform.localScale.z;
        targetScale = desired;

        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        Vector3 nextScale = Vector3.Lerp(transform.localScale, targetScale, t);

        Vector3 pivotBefore = transform.TransformPoint(pivotLocal);
        transform.localScale = nextScale;
        Vector3 pivotAfter = transform.TransformPoint(pivotLocal);
        transform.position += pivotBefore - pivotAfter;
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

    private Vector3 GetHandleDirection(CanvasResizeHandleKind kind)
    {
        switch (kind)
        {
            case CanvasResizeHandleKind.CornerTopLeft: return new Vector3(-1f, 1f, 0f);
            case CanvasResizeHandleKind.CornerTopRight: return new Vector3(1f, 1f, 0f);
            case CanvasResizeHandleKind.CornerBottomLeft: return new Vector3(-1f, -1f, 0f);
            case CanvasResizeHandleKind.CornerBottomRight: return new Vector3(1f, -1f, 0f);
            case CanvasResizeHandleKind.EdgeTop: return Vector3.up;
            case CanvasResizeHandleKind.EdgeBottom: return Vector3.down;
            case CanvasResizeHandleKind.EdgeLeft: return Vector3.left;
            case CanvasResizeHandleKind.EdgeRight: return Vector3.right;
            default: return Vector3.one;
        }
    }

    private Vector3 GetPivotLocal(CanvasResizeHandleKind kind, Vector2 half)
    {
        switch (kind)
        {
            case CanvasResizeHandleKind.CornerTopLeft: return new Vector3(half.x, -half.y, 0f);
            case CanvasResizeHandleKind.CornerTopRight: return new Vector3(-half.x, -half.y, 0f);
            case CanvasResizeHandleKind.CornerBottomLeft: return new Vector3(half.x, half.y, 0f);
            case CanvasResizeHandleKind.CornerBottomRight: return new Vector3(-half.x, half.y, 0f);
            case CanvasResizeHandleKind.EdgeTop: return new Vector3(0f, -half.y, 0f);
            case CanvasResizeHandleKind.EdgeBottom: return new Vector3(0f, half.y, 0f);
            case CanvasResizeHandleKind.EdgeLeft: return new Vector3(half.x, 0f, 0f);
            case CanvasResizeHandleKind.EdgeRight: return new Vector3(-half.x, 0f, 0f);
            default: return Vector3.zero;
        }
    }

    private void EnsureHandleColliders()
    {
        Transform existingRoot = transform.Find("ResizeHandles");
        if (existingRoot != null)
        {
            for (int i = existingRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(existingRoot.GetChild(i).gameObject);
            }

            CreateHandleChildren(existingRoot);
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

        BoxCollider col = h.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(handleSizeLocal.x, handleSizeLocal.y, handleDepthLocal);

        CanvasResizeHandleMarker marker = h.AddComponent<CanvasResizeHandleMarker>();
        marker.Initialize(this, kind);
    }
}

public enum CanvasResizeHandleKind
{
    CornerTopLeft,
    CornerTopRight,
    CornerBottomLeft,
    CornerBottomRight,
    EdgeTop,
    EdgeBottom,
    EdgeLeft,
    EdgeRight
}

public class CanvasResizeHandleMarker : MonoBehaviour
{
    public CanvasResizeController Owner { get; private set; }
    public CanvasResizeHandleKind Kind { get; private set; }

    public void Initialize(CanvasResizeController owner, CanvasResizeHandleKind kind)
    {
        Owner = owner;
        Kind = kind;
    }
}
