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
                // Crear o actualizar un material unlit para que el color no dependa de la luz
                string matPath = "Assets/Resources/ParticleFX/CanvasGrabSparkles_Mat.mat";
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                               ?? Shader.Find("Particles/Standard Unlit")
                               ?? Shader.Find("Universal Render Pipeline/Unlit")
                               ?? Shader.Find("Standard");

                Material particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                if (particleMaterial == null)
                {
                    particleMaterial = new Material(shader);
                    AssetDatabase.CreateAsset(particleMaterial, matPath);
                }
                else if (particleMaterial.shader != shader)
                {
                    particleMaterial.shader = shader;
                    EditorUtility.SetDirty(particleMaterial);
                }

                particleMaterial.color = new Color(1f, 0.8f, 0.2f, 1f);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

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
        mainModule.startLifetime = 1.5f;                               // Vida fija para todas las partículas
        mainModule.startSpeed = 0f;                                    // Sin variación de movimiento inicial
        mainModule.startSize = 0.008f;                                 // Tamaño fijo
        mainModule.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.2f, 1f)); // Dorado fijo

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
        velocityModule.enabled = false;

        // === SIZE OVER LIFETIME ===
        ParticleSystem.SizeOverLifetimeModule sizeModule = ps.sizeOverLifetime;
        sizeModule.enabled = false;

        // === ALPHA OVER LIFETIME (via Color) ===
        // Desactivar para mantener color constante (evita efecto multicolor)
        ParticleSystem.ColorOverLifetimeModule colorModule = ps.colorOverLifetime;
        colorModule.enabled = false;

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
