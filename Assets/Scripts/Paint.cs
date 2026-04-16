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
    
    // Auto-detected VR controller transforms (prioritize these over rayOrigin)
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

    // trigger logging helpers
    float lastTriggerValue = -1f;
    float lastTriggerLogTime = 0f;
    const float triggerLogInterval = 2.0f; // seconds (reduced frequency)

    int lastPx = -1, lastPy = -1;
    int lastDrawPx = -1, lastDrawPy = -1;  // Track last position where we actually painted

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
        
        // Auto-detect XR controller transforms for VR painting (prioritize controllers over rayOrigin)
        // Search scene hierarchy for Right and Left controller GameObjects
        #if ENABLE_INPUT_SYSTEM
        try
        {
            Debug.Log("[PAINT] Buscando Hand/Controller Anchors XR en la jerarquía de la escena...");
            
            // Search scene for controller GameObjects by exact name or pattern
            var allObjects = FindObjectsOfType<Transform>();
            foreach (var t in allObjects)
            {
                string name = t.name;
                // Look for: RightHandAnchor, RightControllerAnchor, or similar
                if ((name == "RightHandAnchor" || name == "RightControllerAnchor" || 
                     (name.Contains("Right") && (name.Contains("Controller") || name.Contains("Hand") || name.Contains("Anchor")))) &&
                    !name.Contains("Detached"))
                {
                    rightControllerTransform = t;
                    Debug.Log("[PAINT] ✓ RIGHT Hand/Controller Anchor encontrado: " + t.name);
                }
                else if ((name == "LeftHandAnchor" || name == "LeftControllerAnchor" || 
                         (name.Contains("Left") && (name.Contains("Controller") || name.Contains("Hand") || name.Contains("Anchor")))) &&
                        !name.Contains("Detached"))
                {
                    leftControllerTransform = t;
                    Debug.Log("[PAINT] ✓ LEFT Hand/Controller Anchor encontrado: " + t.name);
                }
            }
            
            if (rightControllerTransform != null)
                Debug.Log("[PAINT] ✅ Usando RIGHT Anchor: " + rightControllerTransform.name + " [pos=" + rightControllerTransform.position + "]");
            if (leftControllerTransform != null)
                Debug.Log("[PAINT] ✅ Usando LEFT Anchor: " + leftControllerTransform.name + " [pos=" + leftControllerTransform.position + "]");
            if (rightControllerTransform == null && leftControllerTransform == null)
                Debug.LogWarning("[PAINT] ⚠️  No se encontraron Hand/Controller Anchors. Fallback a rayOrigin (si está asignado)");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[PAINT] Error en auto-detección de Hand/Controller Anchors: " + ex.Message);
        }
        #endif
    }

    void Update()
    {
        if (runtimeTex == null) return;
        Debug.Log("[PAINT] Update() ejecutándose");

        Ray ray;
        // PRIORIDAD: RIGHT controller > LEFT controller > rayOrigin
        Transform activePointer = null;
        string pointerSource = "";
        if (rightControllerTransform != null)
        {
            activePointer = rightControllerTransform;
            pointerSource = "RIGHT Controller (mando)";
        }
        else if (leftControllerTransform != null)
        {
            activePointer = leftControllerTransform;
            pointerSource = "LEFT Controller (mando)";
        }
        else if (rayOrigin != null)
        {
            activePointer = rayOrigin;
            pointerSource = "rayOrigin (asignado manualmente)";
        }
        else
        {
            pointerSource = "NINGUNO (activePointer es NULL)";
        }

        Debug.Log($"[PAINT] [DEBUG] activePointer seleccionado: {pointerSource} {(activePointer != null ? "[" + activePointer.name + "]" : "[NULL]")}");

        // if (useMouseForEditor && Application.isEditor)
        // {
        //     if (Camera.main == null) return;
        //     Vector2 mousePos;
        // #if ENABLE_LEGACY_INPUT_MANAGER
        //     mousePos = Input.mousePosition;
        // #elif ENABLE_INPUT_SYSTEM
        //     var m = Mouse.current;
        //     if (m == null) return;
        //     mousePos = m.position.ReadValue();
        // #else
        //     // No supported input backend available
        //     return;
        // #endif
        //     ray = Camera.main.ScreenPointToRay(mousePos);
        //     Debug.Log("[PAINT] Raycast desde Mouse/Editor (cámara)");
        // }
        //else
        //{
            if (activePointer == null)
            {
                Debug.LogWarning("[PAINT] [ERROR] activePointer es NULL - no se puede hacer raycast en VR. Revisa la detección de controladores y el campo rayOrigin.");
                return;
            }
            Debug.Log($"[PAINT] Raycast desde: {activePointer.name} pos={activePointer.position} (fuente: {pointerSource})");
            
            // Usar la orientación del controlador: forward direction
            // Este es el vector que apunta en la dirección que está mirando/apuntando el controlador
            Vector3 rayDirection = activePointer.forward;
            ray = new Ray(activePointer.position, rayDirection);
            Debug.Log($"[PAINT] Ray direction (forward del controlador): {rayDirection}");
        //}

        // Try sprite plane intersection first (so SpriteRenderer works without a MeshCollider)
        bool haveHit = false;
        Vector2 uv = Vector2.zero;

        if (spriteRenderer != null)
        {
            // Intersección manual con el plano del sprite
            Vector3 planeNormal = spriteRenderer.transform.forward;
            Vector3 planePoint = spriteRenderer.transform.position;
            float denom = Vector3.Dot(planeNormal, ray.direction);
            Debug.Log($"[PAINT] Ray origin={ray.origin} dir={ray.direction} planeNormal={planeNormal} denom={denom}");
            if (Mathf.Abs(denom) > 1e-6f)
            {
                float t = Vector3.Dot(planeNormal, planePoint - ray.origin) / denom;
                Debug.Log($"[PAINT] [DIAGNÓSTICO] t={t:F6}, denom={denom:F6}, numerador={Vector3.Dot(planeNormal, planePoint - ray.origin):F6}");
                if (t >= 0f)
                {
                    Vector3 worldPoint = ray.GetPoint(t);
                    Debug.Log($"[PAINT] ✓ Intersección VÁLIDA en worldPoint={worldPoint}");
                    Vector2 uvCandidate = ComputeUVForSprite(worldPoint);
                    Debug.Log($"[PAINT] UV candidate (antes de clamp): {uvCandidate}");
                    // Solo aceptar hits dentro del rectángulo del sprite (0..1 UV)
                    if (uvCandidate.x >= 0f && uvCandidate.x <= 1f && uvCandidate.y >= 0f && uvCandidate.y <= 1f)
                    {
                        uv = uvCandidate;
                        haveHit = true;
                        Debug.Log($"[PAINT] ✓ Ray intersectó plano del sprite (manual) - UV: {uv}");
                    }
                    else
                    {
                        Debug.Log($"[PAINT] Ray intersectó el plano pero fuera del rect del sprite - UV candidate: {uvCandidate}");
                    }
                }
                else
                {
                    Debug.Log($"[PAINT] Ray intersection detrás del origen (t={t})");
                }
            }
            else
            {
                Debug.Log($"[PAINT] Ray paralelo al plano del sprite (denom={denom})");
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
            // var m2 = Mouse.current;
            // if (m2 != null && m2.leftButton.isPressed) 
            // {
            //     Debug.Log("[PAINT] Mouse izquierdo detectado");
            //     painting = true;
            // }
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

            // Check HAND TRACKING (OpenXR Hand Devices) for painting with hand gestures
            Debug.Log("[PAINT] Verificando hand tracking... Total devices: " + InputSystem.devices.Count);
            foreach (var dev in InputSystem.devices)
            {
                Debug.Log("[PAINT] Device detectado: " + dev.name + " | Type: " + dev.GetType().Name);
                
                // Check if this is a hand device (by name or by being XRController)
                bool isHand = dev.name.Contains("Hand") || dev.name.Contains("hand");
                
                if (dev is UnityEngine.InputSystem.XR.XRController xrDev)
                {
                    Debug.Log("[PAINT]   → Es XRController. Name='" + xrDev.name + "' | description.product='" + xrDev.description.product + "'");
                    
                    // In Oculus/Meta, hand tracking shows as XRController with specific names
                    if (isHand)
                    {
                        Debug.Log("[PAINT]   ✓ Detectada MANO: " + xrDev.name);
                        
                        // Try multiple possible control names for pinch/select
                        float bestValue = 0f;
                        string[] controlNames = { "select", "trigger", "grip", "pinch" };
                        
                        foreach (var controlName in controlNames)
                        {
                            try
                            {
                                var ctrl = xrDev.TryGetChildControl<AxisControl>(controlName);
                                if (ctrl != null)
                                {
                                    float val = ctrl.ReadValue();
                                    Debug.Log("[PAINT]     → Control '" + controlName + "' = " + val.ToString("F3"));
                                    bestValue = Mathf.Max(bestValue, val);
                                }
                            }
                            catch { }
                        }
                        
                        // PINCH threshold: typically 0.5+ for "fully pinched"
                        // Lower it to 0.3 to detect even light pinches
                        if (bestValue > 0.3f)
                        {
                            Debug.Log("[PAINT] ✓ PINCH detectado en MANO (value=" + bestValue.ToString("F3") + ") - painting=true");
                            painting = true;
                            break;
                        }
                    }
                }
            }
#else
            Debug.Log("[PAINT] ¡NINGÚN INPUT SYSTEM DISPONIBLE!");
#endif
            if (painting)
            {
                Debug.Log("[PAINT] Dibujando en píxel (" + px + ", " + py + ")");
                
                // Si es la primera pintura o si se movió, dibujar línea continua
                if (lastDrawPx >= 0 && lastDrawPy >= 0 && (px != lastDrawPx || py != lastDrawPy))
                {
                    // Dibujar línea de círculos desde la última posición
                    DrawStroke(runtimeTex, lastDrawPx, lastDrawPy, px, py, brushSize, brushColor);
                }
                else
                {
                    // Primera pintura o mismo píxel
                    DrawCircle(runtimeTex, px, py, brushSize, brushColor);
                }
                
                runtimeTex.Apply();
                lastDrawPx = px;
                lastDrawPy = py;
                
                // keep preview in sync
                if (previewEnabled)
                {
                    UpdatePreview(px, py);
                }
            }
            else
            {
                // Cuando sueltes el botón, resetea la última posición de pintura
                lastDrawPx = -1;
                lastDrawPy = -1;
                ApplyRuntimeTextureToMaterial();
                lastPx = lastPy = -1;
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

        // Use bright WHITE color for preview circle so it's clearly visible before painting
        Color previewColor = Color.white;
        DrawCircle(previewTex, px, py, brushSize, previewColor);
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

        // Convertir el punto mundial a espacio local del sprite
        Vector3 local = spriteRenderer.transform.InverseTransformPoint(worldPoint);
        Debug.Log($"[PAINT][UV] local={local}");

        // El rectángulo del sprite en píxeles dentro de la textura
        Rect rect = s.rect;
        Vector2 pivot = s.pivot;
        float ppu = s.pixelsPerUnit;

        // Coordenadas locales en unidades del sprite (centro en 0,0)
        // El área visible del sprite va de (-rect.width/2, -rect.height/2) a (+rect.width/2, +rect.height/2) en unidades/ppu
        float localX = local.x * ppu + pivot.x;
        float localY = local.y * ppu + pivot.y;

        // Coordenadas en píxeles dentro de la textura
        float texX = rect.x + localX;
        float texY = rect.y + localY;

        // Normalizar a UV (0..1) respecto a la textura completa
        float u = texX / s.texture.width;
        float v = texY / s.texture.height;

        Debug.Log($"[PAINT][UV] texX={texX}, texY={texY}, u={u}, v={v}");

        // Clamp para evitar saltos fuera de rango
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

    void DrawStroke(Texture2D tex, int fromX, int fromY, int toX, int toY, int radius, Color col)
    {
        // Bresenham's line algorithm para interpolar suavemente entre dos puntos
        int dx = Mathf.Abs(toX - fromX);
        int dy = Mathf.Abs(toY - fromY);
        int sx = fromX < toX ? 1 : -1;
        int sy = fromY < toY ? 1 : -1;
        int err = dx - dy;

        int x = fromX;
        int y = fromY;

        while (true)
        {
            // Dibujar círculo en esta posición
            DrawCircle(tex, x, y, radius, col);
            
            // Si llegamos al destino, terminar
            if (x == toX && y == toY) break;

            // Calcular siguiente posición
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
