using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

/// <summary>
/// Setup tool para crear automáticamente el prefab de partículas CanvasGrabSparkles
/// 
/// Uso:
/// - En el Editor: Menú Tools → Setup Canvas Grab Sparkles Prefab
/// - O llamar manualmente: CanvasGrabSparklesSetup.CreateSparklesPrefab()
/// 
/// Lo que hace:
/// 1. Crea la carpeta Assets/Resources/ParticleFX si no existe
/// 2. Crea un GameObject con ParticleSystem configurado para chispas pequeñas
/// 3. Guarda como prefab: Assets/Resources/ParticleFX/CanvasGrabSparkles.prefab
/// </summary>
public class CanvasGrabSparklesSetup : MonoBehaviour
{
#if UNITY_EDITOR
    private const string PREFAB_PATH = "Assets/Resources/ParticleFX/CanvasGrabSparkles.prefab";
    private const string FOLDER_PATH = "Assets/Resources/ParticleFX";

    [MenuItem("Tools/Setup Canvas Grab Sparkles Prefab")]
    public static void CreateSparklesPrefab()
    {
        Debug.Log("[Setup] Iniciando creación del prefab de chispas...");

        // Paso 1: Crear carpeta si no existe
        if (!AssetDatabase.IsValidFolder(FOLDER_PATH))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "ParticleFX");
            Debug.Log($"[Setup] Carpeta creada: {FOLDER_PATH}");
        }

        // Paso 2: Crear GameObject base
        GameObject sparklesGO = new GameObject("CanvasGrabSparkles");
        
        // Paso 3: Agregar ParticleSystem
        ParticleSystem ps = sparklesGO.AddComponent<ParticleSystem>();
        
        // Paso 4: Configurar el ParticleSystem para chispas pequeñas
        ConfigureParticleSystem(ps);
        
        // Paso 5: Agregar Renderer (requerido para que sea visible)
            ParticleSystemRenderer renderer = sparklesGO.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // Intentar cargar un material ya existente creado para el prefab
                string matPath = "Assets/Resources/ParticleFX/CanvasGrabSparkles_Mat.mat";
                Material particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                if (particleMaterial == null)
                {
                    // Buscar un shader URP/Particles o fallback
                    Shader found = Shader.Find("Universal Render Pipeline/Particles/Lit")
                                   ?? Shader.Find("Universal Render Pipeline/Lit")
                                   ?? Shader.Find("Particles/Standard Unlit");

                    if (found == null)
                    {
                        // Último recurso: usar Standard shader para evitar material missing
                        found = Shader.Find("Standard");
                    }

                    particleMaterial = new Material(found ?? Shader.Find("Standard"));

                    // Guardar el material en Assets para que el prefab lo referencie correctamente
                    AssetDatabase.CreateAsset(particleMaterial, matPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                // Asignar como sharedMaterial para que el prefab almacene la referencia al asset
                renderer.sharedMaterial = particleMaterial;
            }

        // Paso 6: Guardar como prefab
        string prefabPath = PREFAB_PATH;
        
        // Si el prefab ya existe, sobreescribir
        if (File.Exists(prefabPath))
        {
            Debug.Log($"[Setup] Prefab existente será sobreescrito: {prefabPath}");
        }
        
        PrefabUtility.SaveAsPrefabAsset(sparklesGO, prefabPath);
        
        // Paso 7: Limpiar el GameObject temporal
        DestroyImmediate(sparklesGO);
        
        // Paso 8: Refresh de assets
        AssetDatabase.Refresh();
        
        Debug.Log($"[Setup] ✓ Prefab creado exitosamente: {prefabPath}");
        EditorUtility.DisplayDialog("Success", "Sparkles prefab creado en:\n" + prefabPath, "OK");
    }

    /// <summary>
    /// Configura el ParticleSystem con parámetros para chispas GRANDES y visibles 3D
    /// Emisión continua mientras se sostiene el lienzo, sin duración límite
    /// </summary>
    private static void ConfigureParticleSystem(ParticleSystem ps)
    {
        // === MAIN MODULE ===
        ParticleSystem.MainModule mainModule = ps.main;
        mainModule.duration = 10f;                                     // Duración larga (se controla manualmente)
        mainModule.loop = true;                                        // Loop para emisión continua
        mainModule.prewarm = false;
        mainModule.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);      // Vida más larga: 1-2s
        mainModule.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);       // Velocidad más rápida: 0.8-2 m/s
        mainModule.startSize = new ParticleSystem.MinMaxCurve(0.005f, 0.01f);     // GRANDE: 0.25-0.5 m (5x más grande)
        mainModule.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.2f, 1f)); // Dorado más saturado

        // === EMISSION MODULE ===
        ParticleSystem.EmissionModule emissionModule = ps.emission;
        emissionModule.enabled = true;
        emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(20f);      // 20 partículas/seg (notorio pero no abrumador)

        // === SHAPE MODULE ===
        ParticleSystem.ShapeModule shapeModule = ps.shape;
        shapeModule.enabled = true;
        shapeModule.shapeType = ParticleSystemShapeType.Sphere;
        shapeModule.radius = 0.15f;  // Radio más grande para dispersión 3D

        // === VELOCITY OVER LIFETIME ===
        ParticleSystem.VelocityOverLifetimeModule velocityModule = ps.velocityOverLifetime;
        velocityModule.enabled = true;
        velocityModule.x = new ParticleSystem.MinMaxCurve(-1f, 1f);    // Mayor dispersión lateral
        velocityModule.y = new ParticleSystem.MinMaxCurve(0.5f, 1.5f); // Movimiento hacia arriba y lateral
        velocityModule.z = new ParticleSystem.MinMaxCurve(-1f, 1f);

        // === SIZE OVER LIFETIME ===
        ParticleSystem.SizeOverLifetimeModule sizeModule = ps.sizeOverLifetime;
        sizeModule.enabled = true;
        AnimationCurve sizeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0.3f);  // Decae pero se mantiene visible
        sizeModule.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // === ALPHA OVER LIFETIME (via Color) ===
        ParticleSystem.ColorOverLifetimeModule colorModule = ps.colorOverLifetime;
        colorModule.enabled = true;
        Gradient alphaGradient = new Gradient();
        alphaGradient.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(new Color(1f, 0.8f, 0.2f, 1f), 0f),   // Dorado
                new GradientColorKey(new Color(1f, 0.5f, 0f, 1f), 1f)      // Naranja al final
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),        // Opaco al inicio
                new GradientAlphaKey(0.8f, 0.3f),    // Mayormente opaco durante vida
                new GradientAlphaKey(0f, 1f)         // Fade out al final
            }
        );
        colorModule.color = new ParticleSystem.MinMaxGradient(alphaGradient);

        // === RENDERER ===
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
        }

        Debug.Log("[Setup] ParticleSystem configurado para chispas GRANDES y notoria en 3D.");
    }
#endif
}
