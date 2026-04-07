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

    // trigger logging helpers
    float lastTriggerValue = -1f;
    float lastTriggerLogTime = 0f;
    const float triggerLogInterval = 2.0f; // seconds (reduced frequency)

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
            // Use a known sprite shader to ensure we can swap textures at runtime
            var spriteMat = new Material(Shader.Find("Sprites/Default"));
            spriteMat.mainTexture = runtimeTex;
            spriteRenderer.material = spriteMat;
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
        Debug.Log("[PAINT] Update() ejecutándose");

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
            if (rayOrigin == null) 
            {
                Debug.LogWarning("[PAINT] rayOrigin es NULL - no se puede hacer raycast en VR");
                return;
            }
            Debug.Log("[PAINT] Usando rayOrigin: " + rayOrigin.name + " pos=" + rayOrigin.position);
            ray = new Ray(rayOrigin.position, rayOrigin.forward);
        }

        // Try sprite plane intersection first (so SpriteRenderer works without a MeshCollider)
        bool haveHit = false;
        Vector2 uv = Vector2.zero;

        if (spriteRenderer != null)
        {
            // Manual intersection with sprite plane + extra diagnostics
            Vector3 planeNormal = spriteRenderer.transform.forward;
            Vector3 planePoint = spriteRenderer.transform.position;
            float denom = Vector3.Dot(planeNormal, ray.direction);
            Debug.Log("[PAINT] Ray origin=" + ray.origin + " dir=" + ray.direction + " planeNormal=" + planeNormal + " denom=" + denom);
            if (Mathf.Abs(denom) > 1e-6f)
            {
                float t = Vector3.Dot(planeNormal, planePoint - ray.origin) / denom;
                if (t >= 0f)
                {
                    Vector3 worldPoint = ray.GetPoint(t);
                    Vector2 uvCandidate = ComputeUVForSprite(worldPoint);
                    // only accept hits inside the sprite rect (0..1 UV)
                    if (uvCandidate.x >= 0f && uvCandidate.x <= 1f && uvCandidate.y >= 0f && uvCandidate.y <= 1f)
                    {
                        uv = uvCandidate;
                        haveHit = true;
                        Debug.Log("[PAINT] ✓ Ray intersectó plano del sprite (manual) - UV: " + uv);
                    }
                    else
                    {
                        Debug.Log("[PAINT] Ray intersectó el plano pero fuera del rect del sprite - UV candidate: " + uvCandidate);
                    }
                }
                else
                {
                    Debug.Log("[PAINT] Ray intersection detrás del origen (t=" + t + ")");
                }
            }
            else
            {
                Debug.Log("[PAINT] Ray paralelo al plano del sprite (denom=" + denom + ")");
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
                    Debug.Log("[PAINT] Actualizar preview en píxel (" + px + ", " + py + ")");
                    UpdatePreview(px, py);
                    lastPx = px; lastPy = py;
                }
            }
            else
            {
                ApplyRuntimeTextureToMaterial();
            }

            bool painting = false;
            Debug.Log("[PAINT] Buscando input...");
#if ENABLE_LEGACY_INPUT_MANAGER
            Debug.Log("[PAINT] Usando LEGACY INPUT MANAGER");
            painting = Input.GetMouseButton(0) || Input.GetButton("Fire1") || Input.GetKey(paintKey);
#elif ENABLE_INPUT_SYSTEM
            Debug.Log("[PAINT] Usando INPUT SYSTEM");
            var m2 = Mouse.current;
            if (m2 != null && m2.leftButton.isPressed) 
            {
                Debug.Log("[PAINT] Mouse izquierdo detectado");
                painting = true;
            }
            var gp = Gamepad.current;
            if (!painting && gp != null && gp.buttonSouth.isPressed) 
            {
                Debug.Log("[PAINT] Gamepad button south detectado");
                painting = true;
            }

            // Check XR controllers (Input System)
            Debug.Log("[PAINT] Buscando XR controllers... Count=" + InputSystem.devices.Count);
            foreach (var dev in InputSystem.devices)
            {
                if (dev is UnityEngine.InputSystem.XR.XRController xr)
                {
                    Debug.Log("[PAINT] Encontrado XRController: " + xr.name);
                    var trigger = xr.TryGetChildControl<AxisControl>("trigger");
                    if (trigger != null)
                    {
                        float triggerValue = trigger.ReadValue();
                        // Only log frequently (every triggerLogInterval) or when value changes noticeably
                        if (Mathf.Abs(triggerValue - lastTriggerValue) > 0.01f || Time.realtimeSinceStartup - lastTriggerLogTime > triggerLogInterval)
                        {
                            lastTriggerLogTime = Time.realtimeSinceStartup;
                            Debug.Log("[PAINT][" + Time.realtimeSinceStartup.ToString("F2") + "] Trigger Value: " + triggerValue + " (threshold: 0.1)");
                            lastTriggerValue = triggerValue;
                        }
                        if (triggerValue > 0.1f)  // LOWERED to 0.1 for testing
                        {
                            Debug.Log("[PAINT] ✓ Trigger XR detectado (AxisControl) - painting=true");
                            painting = true;
                            break;
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[PAINT] Trigger AxisControl es NULL");
                    }
                    
                    // some controllers expose "trigger" as a ButtonControl
                    try
                    {
                        var triggerBtn = xr.TryGetChildControl<ButtonControl>("trigger");
                        if (triggerBtn != null && triggerBtn.isPressed)
                        {
                            Debug.Log("[PAINT] ✓ Trigger XR detectado (ButtonControl) - painting=true");
                            painting = true;
                            break;
                        }
                    }
                    catch (System.InvalidOperationException)
                    {
                        // Control type mismatch - ignore and continue
                        // This happens when trigger is AxisControl but not ButtonControl
                    }
                }
            }
#else
            Debug.Log("[PAINT] ¡NINGÚN INPUT SYSTEM DISPONIBLE!");
#endif
            if (painting)
            {
                Debug.Log("[PAINT] Dibujando círculo en píxel (" + px + ", " + py + ")");
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
        Debug.Log("[PAINT] UpdatePreview() llamado con px=" + px + ", py=" + py);
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
        // give the preview texture a name so logs are informative
        try { previewTex.name = (runtimeTex != null ? runtimeTex.name + " (preview)" : "previewTex"); } catch {}
        Debug.Log("[PAINT] Preview dibujado y aplicado a material");
        SetMaterialTexture(previewTex);

        // For SpriteRenderer, assigning the texture to the material may not display
        // the full texture if the sprite uses a sub-rect (atlas). Create a Sprite
        // from the preview texture and assign it to the SpriteRenderer as a fallback
        // so the preview is guaranteed to be visible.
        if (spriteRenderer != null && previewTex != null)
        {
            var orig = spriteRenderer.sprite;
            if (orig != null)
            {
                Rect fullRect = new Rect(0, 0, previewTex.width, previewTex.height);
                Vector2 pivotNorm = new Vector2(0.5f, 0.5f);
                try
                {
                    pivotNorm = new Vector2(orig.pivot.x / orig.rect.width, orig.pivot.y / orig.rect.height);
                }
                catch {}
                var newSprite = Sprite.Create(previewTex, fullRect, pivotNorm, orig.pixelsPerUnit);
                try { newSprite.name = orig.name + " (previewSprite)"; } catch {}
                spriteRenderer.sprite = newSprite;
                Debug.Log("[PAINT] Assigned preview Sprite to SpriteRenderer: " + newSprite.name);
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
        {
            rend.material.mainTexture = tex;
            Debug.Log("[PAINT] Set material texture on Renderer: " + tex.name);
        }
        else if (spriteRenderer != null)
        {
            spriteRenderer.material.mainTexture = tex;
            Debug.Log("[PAINT] Set material texture on SpriteRenderer: " + tex.name);
        }
        else if (uiImage != null)
        {
            uiImage.material.mainTexture = tex;
            Debug.Log("[PAINT] Set material texture on UI Image: " + tex.name);
        }
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
