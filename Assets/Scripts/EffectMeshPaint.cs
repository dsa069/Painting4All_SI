using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Collections.Generic;
using System.Linq;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Controls;
#endif

public class EffectMeshPaint : MonoBehaviour
{
    private ToolType currentTool => Paint.globalCurrentTool;
    private PaintColorType currentColorType => Paint.globalColor;
    private BrushThickness currentThickness => Paint.globalThickness;

    [Header("Effect Mesh")]
    public Material paintableMaterial;
    public int textureResolution = 512;

    [Header("Pointer")]
    public Transform rayOrigin;

    [Header("Brush Config (Synced with Paint.cs)")]
    private int brushSize;
    private Color brushColor;
    public bool previewEnabled = true;

    [Header("Canvas Boundaries")]
    public int pixelEdgeMargin = 2;

    private HandPaintState leftHandState;
    private HandPaintState rightHandState;

    private Transform rightControllerTransform;
    private Transform leftControllerTransform;
    private GestureUIController gestureController;

    private Dictionary<MeshRenderer, Texture2D> runtimeTextures = new Dictionary<MeshRenderer, Texture2D>();
    private Dictionary<MeshRenderer, Texture2D> previewTextures = new Dictionary<MeshRenderer, Texture2D>();
    private HashSet<Texture2D> dirtyTextures = new HashSet<Texture2D>();

    void Start()
    {
        if (paintableMaterial == null)
        {
            Debug.LogError("[EffectMeshPaint] Asigna el material con shader 'MixedReality/SceneMeshPintable'.");
            enabled = false;
            return;
        }

        InitializeEffectMesh();
        AutoDetectControllers();

        leftHandState = new HandPaintState { lastPx = -1, lastPy = -1, currentPx = -1, currentPy = -1, isPainting = false };
        rightHandState = new HandPaintState { lastPx = -1, lastPy = -1, currentPx = -1, currentPy = -1, isPainting = false };
        
        SyncBrushSettings();
    }

    #region Setup

    void InitializeEffectMesh()
    {
        MeshRenderer[] allRenderers = Resources.FindObjectsOfTypeAll<MeshRenderer>();

        foreach (var rend in allRenderers)
        {
            bool esMallaDeMeta = rend.name.ToLower().Contains("effectmesh") || 
                                (rend.sharedMaterial != null && rend.sharedMaterial.name.Contains("RoomBoxEffects"));

            string objName = rend.name.ToLower();

            if (esMallaDeMeta)
            {
                bool esSuperficiePlana = objName.Contains("wall") || 
                                         objName.Contains("floor") || 
                                         objName.Contains("ceiling") || 
                                         objName.Contains("window") ||
                                         objName.Contains("door");

                if (!esSuperficiePlana) continue;

                if (runtimeTextures.ContainsKey(rend)) continue;

                if (rend.GetComponent<Collider>() == null)
                {
                    rend.gameObject.AddComponent<MeshCollider>();
                }

                Texture2D tex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGBA32, false);
                Color32[] whitePixels = new Color32[textureResolution * textureResolution];
                for (int i = 0; i < whitePixels.Length; i++) whitePixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(whitePixels);
                tex.Apply();
                
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;

                Material matInstance = new Material(paintableMaterial);
                matInstance.SetTexture("_MainTex", tex);
                rend.material = matInstance;

                runtimeTextures[rend] = tex;

                if (previewEnabled)
                {
                    Texture2D pTex = new Texture2D(textureResolution, textureResolution, TextureFormat.RGBA32, false);
                    pTex.filterMode = FilterMode.Bilinear;
                    pTex.SetPixels32(whitePixels);
                    pTex.Apply();
                    previewTextures[rend] = pTex;
                }
            }
        }
    }

    void AutoDetectControllers()
    {
        #if ENABLE_INPUT_SYSTEM
        var allObjects = FindObjectsOfType<Transform>();
        foreach (var t in allObjects)
        {
            string name = t.name;
            if (!name.Contains("Detached"))
            {
                if ((name == "RightHandAnchor" || name == "RightControllerAnchor" || (name.Contains("Right") && (name.Contains("Controller") || name.Contains("Hand")))))
                    rightControllerTransform = t;
                if ((name == "LeftHandAnchor" || name == "LeftControllerAnchor" || (name.Contains("Left") && (name.Contains("Controller") || name.Contains("Hand")))))
                    leftControllerTransform = t;
            }
        }
        #endif

        if (gestureController == null)
            gestureController = FindObjectOfType<GestureUIController>();
    }

    bool IsLeftHandDevice(XRController device)
    {
        return device.name.ToLower().Contains("left") || 
               device.usages.Contains(CommonUsages.LeftHand);
    }

    #endregion

    #region Sync Logic

    void SyncBrushSettings()
    {
        brushSize = (int)currentThickness;
        brushColor = ConvertTypeToColor(currentColorType);
    }

    Color ConvertTypeToColor(PaintColorType type)
    {
        Color c = Color.white;
        switch (type)
        {
            case PaintColorType.White: ColorUtility.TryParseHtmlString("#F9FFFE", out c); break;
            case PaintColorType.LightGray: ColorUtility.TryParseHtmlString("#9D9D97", out c); break;
            case PaintColorType.Gray: ColorUtility.TryParseHtmlString("#474F52", out c); break;
            case PaintColorType.Black: ColorUtility.TryParseHtmlString("#1D1D21", out c); break;
            case PaintColorType.Brown: ColorUtility.TryParseHtmlString("#835432", out c); break;
            case PaintColorType.Red: ColorUtility.TryParseHtmlString("#B02E26", out c); break;
            case PaintColorType.Orange: ColorUtility.TryParseHtmlString("#F9801D", out c); break;
            case PaintColorType.Yellow: ColorUtility.TryParseHtmlString("#FED83D", out c); break;
            case PaintColorType.Lime: ColorUtility.TryParseHtmlString("#80C71F", out c); break;
            case PaintColorType.Green: ColorUtility.TryParseHtmlString("#5E7C16", out c); break;
            case PaintColorType.Cyan: ColorUtility.TryParseHtmlString("#169C9C", out c); break;
            case PaintColorType.LightBlue: ColorUtility.TryParseHtmlString("#3AB3DA", out c); break;
            case PaintColorType.Blue: ColorUtility.TryParseHtmlString("#3C44AA", out c); break;
            case PaintColorType.Purple: ColorUtility.TryParseHtmlString("#8932B8", out c); break;
            case PaintColorType.Magenta: ColorUtility.TryParseHtmlString("#C74EBD", out c); break;
            case PaintColorType.Pink: ColorUtility.TryParseHtmlString("#F38BAA", out c); break;
        }
        return c;
    }

    #endregion

    #region Update Loop

    private float retryTimer = 0f;

    void Update()
    {
        SyncBrushSettings();

        if (runtimeTextures.Count == 0)
        {
            retryTimer += Time.deltaTime;
            if (retryTimer >= 1.0f)
            {
                InitializeEffectMesh();
                retryTimer = 0f;
            }
            return;
        }

        dirtyTextures.Clear();

        ProcessHandPainting(ref leftHandState, false);
        ProcessHandPainting(ref rightHandState, true);

        foreach (var tex in dirtyTextures)
        {
            tex.Apply();
        }

        if (previewEnabled)
        {
            UpdatePreviewBothHands();
        }
    }

    void ProcessHandPainting(ref HandPaintState handState, bool isRightHand)
    {
        if (handState.controller == null)
            handState.controller = isRightHand ? rightControllerTransform : leftControllerTransform;

        if (handState.controller == null)
        {
            ResetHandState(ref handState);
            return;
        }

        Ray ray = new Ray(handState.controller.position, handState.controller.forward);
        
        if (!GetRayHit(ray, out Vector2 uv, out MeshRenderer hitRenderer))
        {
            ResetHandState(ref handState);
            return;
        }

        if (handState.lastHitRenderer != null && handState.lastHitRenderer != hitRenderer)
        {
            handState.lastPx = -1;
            handState.lastPy = -1;
        }

        Texture2D targetTex = runtimeTextures[hitRenderer];
        int px = Mathf.FloorToInt(uv.x * targetTex.width);
        int py = Mathf.FloorToInt(uv.y * targetTex.height);

        if (px < pixelEdgeMargin || px >= targetTex.width - pixelEdgeMargin ||
            py < pixelEdgeMargin || py >= targetTex.height - pixelEdgeMargin)
        {
            ResetHandState(ref handState);
            return;
        }

        handState.currentPx = px;
        handState.currentPy = py;
        handState.lastHitRenderer = hitRenderer;
        handState.currentTool = currentTool;

        bool isTriggerPressed = isRightHand ? IsPaintingRight() : IsPaintingLeft();

        if (isTriggerPressed && currentTool != ToolType.Mano)
        {
            if (handState.lastPx >= 0 && handState.lastPy >= 0 && (px != handState.lastPx || py != handState.lastPy))
            {
                DrawStroke(targetTex, handState.lastPx, handState.lastPy, px, py, brushSize, brushColor, currentTool);
            }
            else
            {
                DrawCircle(targetTex, px, py, brushSize, brushColor, currentTool);
            }

            handState.lastPx = px;
            handState.lastPy = py;
            handState.isPainting = true;
            dirtyTextures.Add(targetTex);
        }
        else
        {
            handState.lastPx = -1;
            handState.lastPy = -1;
            handState.isPainting = false;
        }
    }

    void ResetHandState(ref HandPaintState handState)
    {
        handState.currentPx = -1;
        handState.currentPy = -1;
        handState.lastPx = -1;
        handState.lastPy = -1;
        handState.isPainting = false;
        handState.lastHitRenderer = null;
    }

    bool GetRayHit(Ray ray, out Vector2 uv, out MeshRenderer hitRenderer)
    {
        uv = Vector2.zero;
        hitRenderer = null;

        if (Physics.Raycast(ray, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            hitRenderer = hit.collider.GetComponent<MeshRenderer>();
            if (hitRenderer == null || !runtimeTextures.ContainsKey(hitRenderer)) return false;

            uv = hit.textureCoord;
            
            if (uv == Vector2.zero || float.IsNaN(uv.x))
            {
                uv = ComputeUVFromHit(hit);
            }
            return true;
        }
        return false;
    }

    Vector2 ComputeUVFromHit(RaycastHit hit)
    {
        Vector3 normal = hit.normal;
        Vector3 localPos = hit.collider.transform.InverseTransformPoint(hit.point);
        Vector3 size = hit.collider.bounds.size;
        
        if (size.x < 0.01f) size.x = 1;
        if (size.y < 0.01f) size.y = 1;
        if (size.z < 0.01f) size.z = 1;

        float absX = Mathf.Abs(normal.x);
        float absY = Mathf.Abs(normal.y);
        float absZ = Mathf.Abs(normal.z);

        Vector2 finalUV;
        if (absX > absY && absX > absZ)
            finalUV = new Vector2(localPos.z / size.z, localPos.y / size.y);
        else if (absY > absX && absY > absZ)
            finalUV = new Vector2(localPos.x / size.x, localPos.z / size.z);
        else
            finalUV = new Vector2(localPos.x / size.x, localPos.y / size.y);

        return new Vector2(finalUV.x + 0.5f, finalUV.y + 0.5f);
    }

    #endregion

    #region Input (SOLUCIONADO: Validación estricta de dispositivo)

    bool IsPaintingRight()
    {
        if (CanvasGripManager.Instance != null && CanvasGripManager.Instance.IsPaintBlockedByGrip(CanvasGripManager.ActiveHand.Right))
            return false;

        if (gestureController != null && gestureController.IsT1ActiveRight)
            return true;

#if ENABLE_INPUT_SYSTEM
        var rightHand = InputSystem.GetDevice<XRController>(CommonUsages.RightHand);
        if (rightHand != null)
        {
            var trigger = rightHand.TryGetChildControl<AxisControl>("trigger");
            if (trigger != null && trigger.ReadValue() > 0.05f) return true;
        }

        foreach (var dev in InputSystem.devices)
        {
            if (dev is XRController xr && !IsLeftHandDevice(xr))
            {
                var trigger = xr.TryGetChildControl<AxisControl>("trigger");
                if (trigger != null && trigger.ReadValue() > 0.05f) return true;
            }
        }

        var gp = Gamepad.current;
        if (gp != null && gp.buttonSouth.isPressed) return true;
#endif
        return false;
    }

    bool IsPaintingLeft()
    {
        if (CanvasGripManager.Instance != null && CanvasGripManager.Instance.IsPaintBlockedByGrip(CanvasGripManager.ActiveHand.Left))
            return false;

        if (gestureController != null && gestureController.IsT1ActiveLeft)
            return true;

#if ENABLE_INPUT_SYSTEM
        var leftHand = InputSystem.GetDevice<XRController>(CommonUsages.LeftHand);
        if (leftHand != null)
        {
            var trigger = leftHand.TryGetChildControl<AxisControl>("trigger");
            if (trigger != null && trigger.ReadValue() > 0.05f) return true;
        }

        foreach (var dev in InputSystem.devices)
        {
            if (dev is XRController xr && IsLeftHandDevice(xr))
            {
                var trigger = xr.TryGetChildControl<AxisControl>("trigger");
                if (trigger != null && trigger.ReadValue() > 0.05f) return true;
            }
        }
#endif
        return false;
    }

    #endregion

    #region Preview & Drawing (Restaurado 100%)

    void UpdatePreviewBothHands()
    {
        foreach (var kvp in runtimeTextures)
        {
            MeshRenderer rend = kvp.Key;
            Texture2D runtime = kvp.Value;

            if (!previewTextures.ContainsKey(rend)) continue;
            
            bool isLeftOnThis = (leftHandState.lastHitRenderer == rend);
            bool isRightOnThis = (rightHandState.lastHitRenderer == rend);

            if (!isLeftOnThis && !isRightOnThis)
            {
                rend.material.mainTexture = runtime;
                continue;
            }

            Texture2D preview = previewTextures[rend];
            try { Graphics.CopyTexture(runtime, preview); }
            catch { preview.SetPixels(runtime.GetPixels()); }

            if (isLeftOnThis && leftHandState.currentPx >= 0)
                DrawPreviewCursor(preview, leftHandState.currentPx, leftHandState.currentPy, brushSize, currentTool);

            if (isRightOnThis && rightHandState.currentPx >= 0)
                DrawPreviewCursor(preview, rightHandState.currentPx, rightHandState.currentPy, brushSize, currentTool);

            preview.Apply();
            rend.material.mainTexture = preview;
        }
    }

    void DrawCircle(Texture2D tex, int cx, int cy, int radius, Color col, ToolType tool)
    {
        int x0 = Mathf.Clamp(cx - radius, 0, tex.width - 1);
        int x1 = Mathf.Clamp(cx + radius, 0, tex.width - 1);
        int y0 = Mathf.Clamp(cy - radius, 0, tex.height - 1);
        int y1 = Mathf.Clamp(cy + radius, 0, tex.height - 1);

        int r2 = radius * radius;
        for (int x = x0; x <= x1; x++)
        {
            int dx2 = (x - cx) * (x - cx);
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - cy;
                if (dx2 + dy * dy <= r2)
                {
                    Color src = tex.GetPixel(x, y);
                    Color outc = src;

                    switch (tool)
                    {
                        case ToolType.Pincel: outc = Color.Lerp(src, col, col.a); break;
                        case ToolType.Goma: outc = Color.white; break;
                        case ToolType.Graffiti: if (Random.value < 0.07f) outc = Color.Lerp(src, col, col.a); break;
                        case ToolType.Acuarela:
                            float dRatio = Mathf.Sqrt(dx2 + dy * dy) / radius;
                            float alpha = Mathf.Lerp(0.05f, 0.01f, dRatio);
                            outc = Color.Lerp(src, col, alpha);
                            break;
                    }
                    tex.SetPixel(x, y, outc);
                }
            }
        }
    }

    void DrawStroke(Texture2D tex, int fromX, int fromY, int toX, int toY, int radius, Color col, ToolType tool)
    {
        int dx = Mathf.Abs(toX - fromX);
        int dy = Mathf.Abs(toY - fromY);
        int sx = fromX < toX ? 1 : -1;
        int sy = fromY < toY ? 1 : -1;
        int err = dx - dy;

        int x = fromX, y = fromY;
        while (true)
        {
            DrawCircle(tex, x, y, radius, col, tool);
            if (x == toX && y == toY) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    void DrawPreviewCursor(Texture2D tex, int cx, int cy, int radius, ToolType tool)
    {
        if (tool == ToolType.Mano) return;
        int x0 = Mathf.Clamp(cx - radius, 0, tex.width - 1);
        int x1 = Mathf.Clamp(cx + radius, 0, tex.width - 1);
        int y0 = Mathf.Clamp(cy - radius, 0, tex.height - 1);
        int y1 = Mathf.Clamp(cy + radius, 0, tex.height - 1);

        int r2 = radius * radius;
        for (int x = x0; x <= x1; x++)
        {
            int dx2 = (x - cx) * (x - cx);
            for (int y = y0; y <= y1; y++)
            {
                int dy2 = (y - cy) * (y - cy);
                float distSq = dx2 + dy2;
                if (distSq > r2) continue;

                if (distSq > (radius - 1) * (radius - 1))
                {
                    Color src = tex.GetPixel(x, y);
                    tex.SetPixel(x, y, Color.Lerp(src, Color.yellow, 0.5f));
                }
            }
        }
    }

    #endregion
}