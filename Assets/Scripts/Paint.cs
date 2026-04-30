using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Controls;
#endif
#endif

/// <summary>
/// Tracks painting state for one hand independently
/// </summary>
public enum ToolType { Pincel, Goma, Graffiti, Acuarela, Mano }

public struct HandPaintState
{
    public Transform controller;
    public int lastPx;          // Last paint position (for stroke continuity)
    public int lastPy;          // Last paint position (for stroke continuity)
    public int currentPx;       // Current raycast position (for preview cursor)
    public int currentPy;       // Current raycast position (for preview cursor)
    public bool isPainting;
    public ToolType currentTool;
}

public class Paint : MonoBehaviour
{
    [Header("Pointer")]
    public Transform rayOrigin;
    public KeyCode paintKey = KeyCode.JoystickButton0;
    
    private Transform rightControllerTransform;
    private Transform leftControllerTransform;
    private GestureUIController gestureController;

    [Header("Brush")]
    public int brushSize = 16;
    public Color brushColor = Color.red;
    public bool previewEnabled = true;
    
    [Header("Canvas Boundaries")]
    public float edgeMargin = 0.002f;  // Minimal margin (2-3 pixels) to prevent painting at extreme edges
    public int pixelEdgeMargin = 2;  // Pixel margin from edges - prevents painting in edge pixels

    Renderer rend;
    SpriteRenderer spriteRenderer;
    Image uiImage;

    Texture2D runtimeTex;
    Texture2D previewTex;
    Texture2D sourceTex;

    // Per-hand painting state for simultaneous dual-hand painting
    HandPaintState leftHandState;
    HandPaintState rightHandState;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        uiImage = GetComponent<Image>(); 
        rend = GetComponent<Renderer>();

        if (spriteRenderer != null)
        {
            sourceTex = spriteRenderer.sprite.texture;
            if (sourceTex == null || !sourceTex.isReadable)
                return;
            
            runtimeTex = CreateWritableTexture(sourceTex);
            var spriteMat = new Material(Shader.Find("Sprites/Default"));
            spriteMat.mainTexture = runtimeTex;
            spriteRenderer.material = spriteMat;
            CreatePreviewTexture();
        }
        else if (uiImage != null)
        {
            sourceTex = uiImage.sprite.texture;
            if (sourceTex == null || !sourceTex.isReadable)
                return;
            
            runtimeTex = CreateWritableTexture(sourceTex);
            uiImage.material = new Material(uiImage.material);
            uiImage.material.mainTexture = runtimeTex;
            CreatePreviewTexture();
        }
        else if (rend != null)
        {
            var mat = rend.material;
            sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null || !sourceTex.isReadable)
                return;
            
            runtimeTex = CreateWritableTexture(sourceTex);
            mat.mainTexture = runtimeTex;
            CreatePreviewTexture();
        }

        AutoDetectControllers();
    }

    void CreatePreviewTexture()
    {
        previewTex = new Texture2D(runtimeTex.width, runtimeTex.height, TextureFormat.RGBA32, false);
        try { previewTex.SetPixels(runtimeTex.GetPixels()); }
        catch { Graphics.CopyTexture(runtimeTex, previewTex); }
        previewTex.Apply();
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
                if ((name == "RightHandAnchor" || name == "RightControllerAnchor" || 
                     (name.Contains("Right") && (name.Contains("Controller") || name.Contains("Hand")))))
                    rightControllerTransform = t;
                
                if ((name == "LeftHandAnchor" || name == "LeftControllerAnchor" || 
                     (name.Contains("Left") && (name.Contains("Controller") || name.Contains("Hand")))))
                    leftControllerTransform = t;
            }
        }
        #endif

        // Auto-detectar GestureUIController si no está asignado
        if (gestureController == null)
        {
            gestureController = FindObjectOfType<GestureUIController>();
            if (gestureController != null)
            {
                Debug.Log("✓ GestureUIController detectado automáticamente");
            }
        }

        // Initialize per-hand painting states
        leftHandState = new HandPaintState { controller = leftControllerTransform, lastPx = -1, lastPy = -1, currentPx = -1, currentPy = -1, isPainting = false, currentTool = ToolType.Pincel };
        rightHandState = new HandPaintState { controller = rightControllerTransform, lastPx = -1, lastPy = -1, currentPx = -1, currentPy = -1, isPainting = false, currentTool = ToolType.Pincel };
    }

    /// <summary>
    /// Determines if an XR controller device corresponds to the left hand (true) or right hand (false)
    /// Uses device enumeration order: first XR controller = left, second+ = right
    /// This approach is more reliable than name-based matching for generic device names
    /// </summary>
    bool IsLeftHandDevice(UnityEngine.InputSystem.XR.XRController device)
    {
        int xrControllerIndex = -1;
        int xrControllerCount = 0;
        
        foreach (var dev in InputSystem.devices)
        {
            if (dev is UnityEngine.InputSystem.XR.XRController)
            {
                if (dev == device)
                    xrControllerIndex = xrControllerCount;
                xrControllerCount++;
            }
        }

        // First XR device (index 0) = left hand, rest = right hand
        return xrControllerIndex == 0;
    }

    void Update()
    {
        if (runtimeTex == null) return;

        // Process both hands independently
        ProcessHandPainting(ref leftHandState, false);
        ProcessHandPainting(ref rightHandState, true);

        // Apply texture once after both hands finish
        runtimeTex.Apply();

        // Update preview for both hands
        if (previewEnabled)
        {
            UpdatePreviewBothHands();
        }
        else
        {
            ApplyRuntimeTextureToMaterial();
        }
    }

    /// <summary>
    /// Processes painting for one hand independently
    /// Always performs raycast for preview cursor, draws strokes only when painting
    /// </summary>
    void ProcessHandPainting(ref HandPaintState handState, bool isRightHand)
    {
        if (handState.controller == null)
        {
            handState.currentPx = -1;
            handState.currentPy = -1;
            handState.lastPx = -1;
            handState.lastPy = -1;
            handState.isPainting = false;
            return;
        }

        Ray ray = new Ray(handState.controller.position, handState.controller.forward);

        // Always update current position from raycast (for preview cursor)
        if (!GetRayHit(ray, out Vector2 uv))
        {
            handState.currentPx = -1;
            handState.currentPy = -1;
            handState.lastPx = -1;
            handState.lastPy = -1;
            handState.isPainting = false;
            return;
        }

        int px = Mathf.FloorToInt(uv.x * runtimeTex.width);
        int py = Mathf.FloorToInt(uv.y * runtimeTex.height);

        // Validate that pixel is not in edge margin - reject if too close to edges
        if (px < pixelEdgeMargin || px >= runtimeTex.width - pixelEdgeMargin ||
            py < pixelEdgeMargin || py >= runtimeTex.height - pixelEdgeMargin)
        {
            handState.currentPx = -1;
            handState.currentPy = -1;
            handState.lastPx = -1;
            handState.lastPy = -1;
            handState.isPainting = false;
            return;
        }

        // Update current position for preview (always)
        handState.currentPx = px;
        handState.currentPy = py;

        // Check if this hand is currently painting
        bool isPaintingNow = false;
        
        if (handState.currentTool != ToolType.Mano)
        {
            isPaintingNow = isRightHand ? IsPaintingRight() : IsPaintingLeft();
        }

        if (isPaintingNow)
        {
            // Draw stroke from last position to current position
            if (handState.lastPx >= 0 && handState.lastPy >= 0 && (px != handState.lastPx || py != handState.lastPy))
            {
                DrawStroke(runtimeTex, handState.lastPx, handState.lastPy, px, py, brushSize, brushColor, handState.currentTool);
            }
            else
            {
                DrawCircle(runtimeTex, px, py, brushSize, brushColor, handState.currentTool);
            }
            
            handState.lastPx = px;
            handState.lastPy = py;
            handState.isPainting = true;
        }
        else
        {
            // Reset last position when not painting (but keep current for preview)
            handState.lastPx = -1;
            handState.lastPy = -1;
            handState.isPainting = false;
        }
    }

    bool GetRayHit(Ray ray, out Vector2 uv)
    {
        uv = Vector2.zero;

        if (spriteRenderer != null)
        {
            Vector3 planeNormal = spriteRenderer.transform.forward;
            Vector3 planePoint = spriteRenderer.transform.position;
            float denom = Vector3.Dot(planeNormal, ray.direction);
            
            if (Mathf.Abs(denom) > 1e-6f)
            {
                float t = Vector3.Dot(planeNormal, planePoint - ray.origin) / denom;
                if (t >= 0f)
                {
                    Vector3 worldPoint = ray.GetPoint(t);
                    Vector2 uvCandidate = ComputeUVForSprite(worldPoint);
                    
                    // Check if UV is within valid bounds and outside edge margin
                    float minBound = edgeMargin;
                    float maxBound = 1f - edgeMargin;
                    if (uvCandidate.x >= minBound && uvCandidate.x <= maxBound && 
                        uvCandidate.y >= minBound && uvCandidate.y <= maxBound)
                    {
                        uv = uvCandidate;
                        return true;
                    }
                }
            }
        }

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // Only accept hits on this GameObject to prevent painting on other objects
            if (hit.collider.gameObject != gameObject)
                return false;
                
            uv = hit.textureCoord;
            if (uv == Vector2.zero)
                uv = ComputeUVFromHit(hit);
            
            // Check if UV is within valid bounds and outside edge margin
            float minBound = edgeMargin;
            float maxBound = 1f - edgeMargin;
            if (uv.x >= minBound && uv.x <= maxBound && uv.y >= minBound && uv.y <= maxBound)
            {
                return true;
            }
        }

        return false;
    }

    bool IsPaintingRight()
    {
        // Verificar si T1 (gesto pinza índice-pulgar) está activo en mano derecha
        if (gestureController != null && gestureController.IsT1ActiveRight)
            return true;

        #if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(0) || Input.GetButton("Fire1") || Input.GetKey(paintKey))
            return true;
        #endif

        #if ENABLE_INPUT_SYSTEM
        var gp = Gamepad.current;
        if (gp != null && gp.buttonSouth.isPressed) 
            return true;

        foreach (var dev in InputSystem.devices)
        {
            if (dev is UnityEngine.InputSystem.XR.XRController xr)
            {
                // Use device index matching instead of unreliable name filtering
                if (IsLeftHandDevice(xr))
                    continue; // This is left hand, skip it

                var trigger = xr.TryGetChildControl<AxisControl>("trigger");
                if (trigger != null && trigger.ReadValue() > 0.1f)
                    return true;

                try
                {
                    var triggerBtn = xr.TryGetChildControl<ButtonControl>("trigger");
                    if (triggerBtn != null && triggerBtn.isPressed)
                        return true;
                }
                catch { }
                
                if (dev.name.Contains("Hand") || dev.name.Contains("hand"))
                {
                    float bestValue = 0f;
                    string[] controlNames = { "select", "trigger", "grip", "pinch" };
                    
                    foreach (var controlName in controlNames)
                    {
                        try
                        {
                            var ctrl = xr.TryGetChildControl<AxisControl>(controlName);
                            if (ctrl != null)
                                bestValue = Mathf.Max(bestValue, ctrl.ReadValue());
                        }
                        catch { }
                    }
                    
                    if (bestValue > 0.3f)
                        return true;
                }
            }
        }
        #endif

        return false;
    }

    bool IsPaintingLeft()
    {
        // Verificar si T1 (gesto pinza índice-pulgar) está activo en mano izquierda
        if (gestureController != null && gestureController.IsT1ActiveLeft)
            return true;

        #if ENABLE_INPUT_SYSTEM
        foreach (var dev in InputSystem.devices)
        {
            if (dev is UnityEngine.InputSystem.XR.XRController xr)
            {
                // Use device index matching instead of unreliable name filtering
                if (!IsLeftHandDevice(xr))
                    continue; // This is right hand, skip it

                var trigger = xr.TryGetChildControl<AxisControl>("trigger");
                if (trigger != null && trigger.ReadValue() > 0.1f)
                    return true;

                try
                {
                    var triggerBtn = xr.TryGetChildControl<ButtonControl>("trigger");
                    if (triggerBtn != null && triggerBtn.isPressed)
                        return true;
                }
                catch { }
                
                if (dev.name.Contains("Hand") || dev.name.Contains("hand"))
                {
                    float bestValue = 0f;
                    string[] controlNames = { "select", "trigger", "grip", "pinch" };
                    
                    foreach (var controlName in controlNames)
                    {
                        try
                        {
                            var ctrl = xr.TryGetChildControl<AxisControl>(controlName);
                            if (ctrl != null)
                                bestValue = Mathf.Max(bestValue, ctrl.ReadValue());
                        }
                        catch { }
                    }
                    
                    if (bestValue > 0.3f)
                        return true;
                }
            }
        }
        #endif

        return false;
    }

    // Backward compatibility wrapper - returns true if either hand is painting
    bool IsPainting()
    {
        return IsPaintingLeft() || IsPaintingRight();
    }

    /// <summary>
    /// Updates preview to show brush cursors for both hands simultaneously
    /// Shows cursor even when not painting (based on raycast position)
    /// </summary>
    void UpdatePreviewBothHands()
    {
        try
        {
            Graphics.CopyTexture(runtimeTex, previewTex);
        }
        catch
        {
            previewTex.SetPixels(runtimeTex.GetPixels());
        }

        // Draw preview circle for left hand if it has a valid raycast position
        if (leftHandState.currentPx >= 0 && leftHandState.currentPy >= 0)
        {
            DrawPreviewCursor(previewTex, leftHandState.currentPx, leftHandState.currentPy, brushSize, leftHandState.currentTool);
        }

        // Draw preview circle for right hand if it has a valid raycast position
        if (rightHandState.currentPx >= 0 && rightHandState.currentPy >= 0)
        {
            DrawPreviewCursor(previewTex, rightHandState.currentPx, rightHandState.currentPy, brushSize, rightHandState.currentTool);
        }

        previewTex.Apply();
        SetMaterialTexture(previewTex);

        if (spriteRenderer != null && previewTex != null)
        {
            var orig = spriteRenderer.sprite;
            if (orig != null)
            {
                Rect fullRect = new Rect(0, 0, previewTex.width, previewTex.height);
                Vector2 pivotNorm = new Vector2(orig.pivot.x / orig.rect.width, orig.pivot.y / orig.rect.height);
                var newSprite = Sprite.Create(previewTex, fullRect, pivotNorm, orig.pixelsPerUnit);
                spriteRenderer.sprite = newSprite;
            }
        }
    }

    // Legacy method for backward compatibility
    void UpdatePreview(int px, int py)
    {
        try
        {
            Graphics.CopyTexture(runtimeTex, previewTex);
        }
        catch
        {
            previewTex.SetPixels(runtimeTex.GetPixels());
        }

        DrawPreviewCursor(previewTex, px, py, brushSize, ToolType.Pincel);
        previewTex.Apply();
        SetMaterialTexture(previewTex);

        if (spriteRenderer != null && previewTex != null)
        {
            var orig = spriteRenderer.sprite;
            if (orig != null)
            {
                Rect fullRect = new Rect(0, 0, previewTex.width, previewTex.height);
                Vector2 pivotNorm = new Vector2(orig.pivot.x / orig.rect.width, orig.pivot.y / orig.rect.height);
                var newSprite = Sprite.Create(previewTex, fullRect, pivotNorm, orig.pixelsPerUnit);
                spriteRenderer.sprite = newSprite;
            }
        }
    }

    void ApplyRuntimeTextureToMaterial()
    {
        SetMaterialTexture(runtimeTex);
    }

    void SetMaterialTexture(Texture2D tex)
    {
        if (rend != null)
            rend.material.mainTexture = tex;
        else if (spriteRenderer != null)
            spriteRenderer.material.mainTexture = tex;
        else if (uiImage != null)
            uiImage.material.mainTexture = tex;
    }

    Vector2 ComputeUVFromHit(RaycastHit hit)
    {
        Transform t = hit.collider.transform;
        Vector3 local = t.InverseTransformPoint(hit.point);

        Vector3 size = Vector3.one;
        if (rend != null) size = rend.bounds.size;
        else if (spriteRenderer != null) size = spriteRenderer.bounds.size;
        else if (uiImage != null) size = ((RectTransform)uiImage.transform).rect.size;

        float ux = (local.x / size.x) + 0.5f;
        float uy = (local.y / size.y) + 0.5f;
        return new Vector2(ux, uy);
    }

    Vector2 ComputeUVForSprite(Vector3 worldPoint)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null) 
            return Vector2.zero;
        
        Sprite s = spriteRenderer.sprite;
        Vector3 local = spriteRenderer.transform.InverseTransformPoint(worldPoint);

        Rect rect = s.rect;
        Vector2 pivot = s.pivot;
        float ppu = s.pixelsPerUnit;

        float localX = local.x * ppu + pivot.x;
        float localY = local.y * ppu + pivot.y;

        float texX = rect.x + localX;
        float texY = rect.y + localY;

        float u = texX / s.texture.width;
        float v = texY / s.texture.height;

        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }

    Texture2D CreateWritableTexture(Texture2D source)
    {
        if (source == null) return null;
        
        int w = source.width;
        int h = source.height;
        Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        
        if (source.isReadable)
        {
            try
            {
                dst.SetPixels(source.GetPixels());
                dst.Apply();
                return dst;
            }
            catch { }
        }

        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
        var prev = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        dst.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return dst;
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
            int dx = x - cx;
            int dx2 = dx * dx;
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - cy;
                if (dx2 + dy * dy <= r2)
                {
                    Color src = tex.GetPixel(x, y);
                    Color outc = src;

                    switch (tool)
                    {
                        case ToolType.Pincel:
                            outc = Color.Lerp(src, col, col.a);
                            break;
                        
                        case ToolType.Goma:
                            // Restaura el color original en lugar de hacerlo transparente
                            outc = sourceTex != null ? sourceTex.GetPixel(x, y) : Color.clear;
                            break;
                        
                        case ToolType.Graffiti:
                            // Solo pinta el 15% de los píxeles aleatoriamente para efecto spray
                            if (Random.value < 0.15f)
                            {
                                outc = Color.Lerp(src, col, col.a);
                            }
                            break;
                        
                        case ToolType.Acuarela:
                            // Calcula qué tan lejos estamos del centro (0 es el centro, 1 es el borde)
                            float distanceRatio = Mathf.Sqrt(dx2 + dy * dy) / radius;
                            // Efecto suave: más transparente hacia los bordes
                            float watercolorAlpha = Mathf.Lerp(0.05f, 0.01f, distanceRatio);
                            Color watercolorCol = new Color(col.r, col.g, col.b, watercolorAlpha);
                            outc = Color.Lerp(src, watercolorCol, watercolorCol.a);
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

        int x = fromX;
        int y = fromY;

        while (true)
        {
            DrawCircle(tex, x, y, radius, col, tool);
            
            if (x == toX && y == toY) break;

            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y += sy;
            }
        }
    }

    void DrawPreviewCursor(Texture2D tex, int cx, int cy, int radius, ToolType tool)
    {
        int x0 = Mathf.Clamp(cx - radius, 0, tex.width - 1);
        int x1 = Mathf.Clamp(cx + radius, 0, tex.width - 1);
        int y0 = Mathf.Clamp(cy - radius, 0, tex.height - 1);
        int y1 = Mathf.Clamp(cy + radius, 0, tex.height - 1);

        int r2 = radius * radius;
        for (int x = x0; x <= x1; x++)
        {
            int dx = x - cx;
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - cy;
                float distSq = dx * dx + dy * dy;
                
                if (distSq <= r2)
                {
                    float dist = Mathf.Sqrt(distSq) / radius; // de 0 a 1
                    bool drawPixel = false;
                    Color cursorColor = Color.yellow;

                    switch (tool)
                    {
                        case ToolType.Pincel: // Círculo amarillo sólido semi-transparente
                            cursorColor.a = 0.5f;
                            drawPixel = true;
                            break;
                            
                        case ToolType.Goma: // Solo dibuja el contorno (un anillo)
                            if (dist > 0.8f) { cursorColor.a = 0.8f; drawPixel = true; }
                            break;
                            
                        case ToolType.Graffiti: // Puntos dispersos para simular el área del spray
                            if (Random.value < 0.1f) { cursorColor.a = 0.8f; drawPixel = true; }
                            break;
                            
                        case ToolType.Acuarela: // Círculo muy difuminado
                            cursorColor.a = Mathf.Lerp(0.3f, 0.0f, dist);
                            drawPixel = true;
                            break;
                            
                        case ToolType.Mano: // No hay cursor visible
                            drawPixel = false;
                            break;
                    }

                    if (drawPixel)
                    {
                        Color src = tex.GetPixel(x, y);
                        tex.SetPixel(x, y, Color.Lerp(src, cursorColor, cursorColor.a));
                    }
                }
            }
        }
    }

    public void SetTool(ToolType newTool)
    {
        // En ausencia de saber qué mano abrió el menú, se asume que
        // queremos cambiar la herramienta para ambas manos por defecto.
        leftHandState.currentTool = newTool;
        rightHandState.currentTool = newTool;
        Debug.Log($"Tool changed to: {newTool}");
    }
}
