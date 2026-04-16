using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Controls;
#endif
#endif

public class Paint : MonoBehaviour
{
    [Header("Pointer")]
    public Transform rayOrigin;
    public KeyCode paintKey = KeyCode.JoystickButton0;
    
    private Transform rightControllerTransform;
    private Transform leftControllerTransform;

    [Header("Brush")]
    public int brushSize = 16;
    public Color brushColor = Color.red;
    public bool previewEnabled = true;

    Renderer rend;
    SpriteRenderer spriteRenderer;
    Image uiImage;

    Texture2D runtimeTex;
    Texture2D previewTex;
    Texture2D sourceTex;

    int lastDrawPx = -1, lastDrawPy = -1;

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
    }

    void Update()
    {
        if (runtimeTex == null) return;

        Transform activePointer = rightControllerTransform ?? leftControllerTransform ?? rayOrigin;
        if (activePointer == null) return;

        Ray ray = new Ray(activePointer.position, activePointer.forward);

        if (!GetRayHit(ray, out Vector2 uv))
        {
            ApplyRuntimeTextureToMaterial();
            lastDrawPx = -1;
            lastDrawPy = -1;
            return;
        }

        int px = Mathf.FloorToInt(uv.x * runtimeTex.width);
        int py = Mathf.FloorToInt(uv.y * runtimeTex.height);

        if (previewEnabled)
        {
            UpdatePreview(px, py);
        }
        else
        {
            ApplyRuntimeTextureToMaterial();
        }

        if (IsPainting())
        {
            if (lastDrawPx >= 0 && lastDrawPy >= 0 && (px != lastDrawPx || py != lastDrawPy))
            {
                DrawStroke(runtimeTex, lastDrawPx, lastDrawPy, px, py, brushSize, brushColor);
            }
            else
            {
                DrawCircle(runtimeTex, px, py, brushSize, brushColor);
            }
            
            runtimeTex.Apply();
            lastDrawPx = px;
            lastDrawPy = py;

            if (previewEnabled)
            {
                UpdatePreview(px, py);
            }
        }
        else
        {
            lastDrawPx = -1;
            lastDrawPy = -1;
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
                    
                    if (uvCandidate.x >= 0f && uvCandidate.x <= 1f && uvCandidate.y >= 0f && uvCandidate.y <= 1f)
                    {
                        uv = uvCandidate;
                        return true;
                    }
                }
            }
        }

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            uv = hit.textureCoord;
            if (uv == Vector2.zero)
                uv = ComputeUVFromHit(hit);
            return true;
        }

        return false;
    }

    bool IsPainting()
    {
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

        DrawCircle(previewTex, px, py, brushSize, Color.white);
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

    void DrawCircle(Texture2D tex, int cx, int cy, int radius, Color col)
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
                    Color outc = Color.Lerp(src, col, col.a);
                    tex.SetPixel(x, y, outc);
                }
            }
        }
    }

    void DrawStroke(Texture2D tex, int fromX, int fromY, int toX, int toY, int radius, Color col)
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
            DrawCircle(tex, x, y, radius, col);
            
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
}
