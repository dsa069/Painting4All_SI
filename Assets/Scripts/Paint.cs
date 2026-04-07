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
    public bool useMouseForEditor = true;
    public KeyCode paintKey = KeyCode.JoystickButton0;

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

    int lastPx = -1, lastPy = -1;

    void Start()
    {
        // prefer SpriteRenderer and UI Image over generic Renderer, because SpriteRenderer also returns a Renderer
        spriteRenderer = GetComponent<SpriteRenderer>();
        uiImage = GetComponent<Image>();
        rend = GetComponent<Renderer>();

        if (spriteRenderer != null)
        {
            sourceTex = spriteRenderer.sprite.texture;
            if (sourceTex == null)
            {
                Debug.LogError("Paint: SpriteRenderer has no sprite texture.");
                return;
            }
            if (!sourceTex.isReadable)
            {
                Debug.LogWarning("Paint: source sprite texture is not Read/Write enabled. Enable Read/Write in the texture import settings.");
            }
            runtimeTex = CreateWritableTexture(sourceTex);
            runtimeTex.name = sourceTex.name + " (runtime)";
            spriteRenderer.material = new Material(spriteRenderer.sharedMaterial);
            spriteRenderer.material.mainTexture = runtimeTex;
            previewTex = new Texture2D(runtimeTex.width, runtimeTex.height, TextureFormat.RGBA32, false);
            try { previewTex.SetPixels(runtimeTex.GetPixels()); }
            catch { Graphics.CopyTexture(runtimeTex, previewTex); }
            previewTex.Apply();
        }
        else if (uiImage != null)
        {
            sourceTex = uiImage.sprite.texture;
            if (sourceTex == null)
            {
                Debug.LogError("Paint: UI Image has no sprite texture.");
                return;
            }
            if (!sourceTex.isReadable)
            {
                Debug.LogWarning("Paint: source UI sprite texture is not Read/Write enabled. Enable Read/Write in the texture import settings.");
            }
            runtimeTex = CreateWritableTexture(sourceTex);
            runtimeTex.name = sourceTex.name + " (runtime)";
            uiImage.material = new Material(uiImage.material);
            uiImage.material.mainTexture = runtimeTex;
            previewTex = new Texture2D(runtimeTex.width, runtimeTex.height, TextureFormat.RGBA32, false);
            try { previewTex.SetPixels(runtimeTex.GetPixels()); }
            catch { Graphics.CopyTexture(runtimeTex, previewTex); }
            previewTex.Apply();
        }
        else if (rend != null)
        {
            var mat = rend.material; // instantiate material
            sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null)
            {
                Debug.LogError("Paint: Renderer does not have a Texture2D as mainTexture.");
                return;
            }
            if (!sourceTex.isReadable)
            {
                Debug.LogWarning("Paint: source texture is not Read/Write enabled. Enable Read/Write in the texture import settings.");
            }
            // create runtime copy
            runtimeTex = CreateWritableTexture(sourceTex);
            runtimeTex.name = sourceTex.name + " (runtime)";
            mat.mainTexture = runtimeTex;
            previewTex = new Texture2D(runtimeTex.width, runtimeTex.height, TextureFormat.RGBA32, false);
            try { previewTex.SetPixels(runtimeTex.GetPixels()); }
            catch { Graphics.CopyTexture(runtimeTex, previewTex); }
            previewTex.Apply();
        }
        else
        {
            Debug.LogError("Paint: No Renderer, SpriteRenderer or UI Image found on the GameObject.");
        }
    }

    void Update()
    {
        if (runtimeTex == null) return;

        Ray ray;
        if (useMouseForEditor && Application.isEditor)
        {
            if (Camera.main == null) return;
            Vector2 mousePos;
#if ENABLE_LEGACY_INPUT_MANAGER
            mousePos = Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return;
            mousePos = m.position.ReadValue();
#else
            // No supported input backend available
            return;
#endif
            ray = Camera.main.ScreenPointToRay(mousePos);
        }
        else
        {
            if (rayOrigin == null) return;
            ray = new Ray(rayOrigin.position, rayOrigin.forward);
        }

        // Try sprite plane intersection first (so SpriteRenderer works without a MeshCollider)
        bool haveHit = false;
        Vector2 uv = Vector2.zero;

        if (spriteRenderer != null)
        {
            Plane plane = new Plane(spriteRenderer.transform.forward, spriteRenderer.transform.position);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 worldPoint = ray.GetPoint(enter);
                uv = ComputeUVForSprite(worldPoint);
                haveHit = true;
            }
        }

        // Fallback to physics raycast for meshes (gives hit.textureCoord)
        if (!haveHit && Physics.Raycast(ray, out RaycastHit hit))
        {
            uv = hit.textureCoord;
            if (uv == Vector2.zero)
            {
                uv = ComputeUVFromHit(hit);
            }
            haveHit = true;
        }

        if (haveHit)
        {
            int px = Mathf.FloorToInt(uv.x * runtimeTex.width);
            int py = Mathf.FloorToInt(uv.y * runtimeTex.height);

            // preview
            if (previewEnabled)
            {
                if (px != lastPx || py != lastPy)
                {
                    UpdatePreview(px, py);
                    lastPx = px; lastPy = py;
                }
            }
            else
            {
                ApplyRuntimeTextureToMaterial();
            }

            bool painting = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            painting = Input.GetMouseButton(0) || Input.GetButton("Fire1") || Input.GetKey(paintKey);
#elif ENABLE_INPUT_SYSTEM
            var m2 = Mouse.current;
            if (m2 != null && m2.leftButton.isPressed) painting = true;
            var gp = Gamepad.current;
            if (!painting && gp != null && gp.buttonSouth.isPressed) painting = true;

            // Check XR controllers (Input System)
            foreach (var dev in InputSystem.devices)
            {
                if (dev is UnityEngine.InputSystem.XR.XRController xr)
                {
                    var trigger = xr.TryGetChildControl<AxisControl>("trigger");
                    if (trigger != null && trigger.ReadValue() > 0.5f)
                    {
                        painting = true;
                        break;
                    }
                    // some controllers expose "trigger" as a ButtonControl
                    var triggerBtn = xr.TryGetChildControl<ButtonControl>("trigger");
                    if (triggerBtn != null && triggerBtn.isPressed)
                    {
                        painting = true;
                        break;
                    }
                }
            }
#endif
            if (painting)
            {
                DrawCircle(runtimeTex, px, py, brushSize, brushColor);
                runtimeTex.Apply();
                // keep preview in sync
                if (previewEnabled)
                {
                    UpdatePreview(px, py);
                }
            }
        }
        else
        {
            // No hit - show runtime texture
            ApplyRuntimeTextureToMaterial();
            lastPx = lastPy = -1;
        }
    }

    void UpdatePreview(int px, int py)
    {
        // copy runtime into preview then draw
        try
        {
            Graphics.CopyTexture(runtimeTex, previewTex);
        }
        catch
        {
            // fallback to slower copy if CopyTexture can't be used
            previewTex.SetPixels(runtimeTex.GetPixels());
        }

        DrawCircle(previewTex, px, py, brushSize, brushColor);
        previewTex.Apply();
        SetMaterialTexture(previewTex);
    }

    void ApplyRuntimeTextureToMaterial()
    {
        SetMaterialTexture(runtimeTex);
    }

    void SetMaterialTexture(Texture2D tex)
    {
        if (rend != null) rend.material.mainTexture = tex;
        else if (spriteRenderer != null) spriteRenderer.material.mainTexture = tex;
        else if (uiImage != null) uiImage.material.mainTexture = tex;
    }

    Vector2 ComputeUVFromHit(RaycastHit hit)
    {
        // Map hit.point to local space and then to UV assuming the object's local X/Y map to U/V.
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
        if (spriteRenderer == null || spriteRenderer.sprite == null) return Vector2.zero;
        Sprite s = spriteRenderer.sprite;

        // Convert world point to local space of the sprite
        Vector3 local = spriteRenderer.transform.InverseTransformPoint(worldPoint);

        float ppu = s.pixelsPerUnit;
        Vector2 localPixels = new Vector2(local.x * ppu, local.y * ppu);

        Vector2 pivot = s.pivot; // in pixels
        Rect rect = s.rect; // position and size in texture pixels

        Vector2 pixelCoord = pivot + localPixels;
        Vector2 texPixel = new Vector2(rect.x + pixelCoord.x, rect.y + pixelCoord.y);

        float u = texPixel.x / s.texture.width;
        float v = texPixel.y / s.texture.height;
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
            catch
            {
                // fall through to RT copy
            }
        }

        // Fallback: render the source into a RenderTexture and read back (works even if source isn't readable)
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
}
