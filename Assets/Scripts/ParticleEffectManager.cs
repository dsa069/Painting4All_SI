using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Gestiona un pool de Particle Systems para reutilización eficiente
/// Evita instantiate/destroy spam durante gameplay
/// 
/// Características:
/// - Pool pattern con preallocación
/// - Caché de instancias activas
/// - Control de emisión manual
/// - Soporta múltiples instancias simultáneas
/// </summary>
public class ParticleEffectManager : MonoBehaviour
{
    [SerializeField] private GameObject particleEffectPrefab;
    [SerializeField] private int poolSize = 10;
    [SerializeField] private bool preallocateOnStart = true;

    private List<ParticleSystem> particlePool = new List<ParticleSystem>();
    private HashSet<ParticleSystem> activeParticles = new HashSet<ParticleSystem>();
    private static ParticleEffectManager instance;

    public static ParticleEffectManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<ParticleEffectManager>();
                
                if (instance == null)
                {
                    GameObject managerGO = new GameObject("ParticleEffectManager");
                    instance = managerGO.AddComponent<ParticleEffectManager>();
                    
                    // Auto-cargar prefab desde Resources
                    GameObject prefab = Resources.Load<GameObject>("ParticleFX/CanvasGrabSparkles");
                    if (prefab != null)
                    {
                        instance.particleEffectPrefab = prefab;
                        instance.InitializePool();
                    }
                    else
                    {
                        Debug.LogWarning("[ParticleEffectManager] No se pudo cargar prefab desde Resources/ParticleFX/CanvasGrabSparkles");
                    }
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[ParticleEffectManager] Ya existe una instancia. Destruyendo duplicada.");
            Destroy(gameObject);
            return;
        }
        
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (preallocateOnStart && particlePool.Count == 0)
        {
            InitializePool();
        }
    }

    /// <summary>
    /// Inicializa el pool con instancias preallocadas
    /// </summary>
    private void InitializePool()
    {
        if (particleEffectPrefab == null)
        {
            Debug.LogWarning("[ParticleEffectManager] No hay prefab asignado para inicializar el pool.");
            return;
        }

        for (int i = 0; i < poolSize; i++)
        {
            GameObject instance = Instantiate(particleEffectPrefab, transform);
            instance.name = $"ParticleFX_Pooled_{i}";
            instance.SetActive(false);
            
            ParticleSystem ps = instance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                particlePool.Add(ps);
            }
            else
            {
                Debug.LogWarning($"[ParticleEffectManager] Prefab no tiene ParticleSystem: {particleEffectPrefab.name}");
                Destroy(instance);
            }
        }

        Debug.Log($"[ParticleEffectManager] Pool inicializado con {particlePool.Count} instancias.");
    }

    /// <summary>
    /// Obtiene una instancia del pool, la posiciona y la activa
    /// Si no hay disponibles en el pool, crea una nueva
    /// 
    /// Parámetros:
    /// - position: Posición mundial donde se reproduce el efecto
    /// - parent: Transform padre (opcional). Si se proporciona, el efecto se hija a este transform
    /// - duration: Duración en segundos antes de devolver al pool (opcional, default 1s). 
    ///            Si duration <= 0, la partícula se mantiene activa indefinidamente
    /// 
    /// Retorna: La instancia del ParticleSystem, o null si hay error
    /// </summary>
    public ParticleSystem PlaySparklesAt(Vector3 position, Transform parent = null, float duration = 1f)
    {
        ParticleSystem ps = GetAvailableParticle();
        
        if (ps == null)
        {
            Debug.LogWarning("[ParticleEffectManager] No hay partículas disponibles en el pool.");
            return null;
        }

        // Configurar posición y padre
        Transform psTransform = ps.transform;
        psTransform.SetParent(parent, true); // mantener posición mundial
        psTransform.position = position;
        psTransform.localRotation = Quaternion.identity;
        // Evitar heredar escalado no deseado del padre
        psTransform.localScale = Vector3.one;

        // Activar el efecto
        ps.gameObject.SetActive(true);
        ps.Play();

        activeParticles.Add(ps);

        // Programar devolución al pool solo si duration > 0
        if (duration > 0)
        {
            StopCoroutine(ReturnToPoolAfterDelay(ps, duration));
            StartCoroutine(ReturnToPoolAfterDelay(ps, duration));
        }
        else
        {
            // Duración indefinida (se controlará manualmente)
            Debug.Log("[ParticleEffectManager] Partículas activadas con duración indefinida (control manual).");
        }

        return ps;
    }

    /// <summary>
    /// Obtiene una partícula disponible del pool
    /// Si no hay disponibles, crea una nueva
    /// Limpia referencias destruidas del pool antes de buscar
    /// </summary>
    private ParticleSystem GetAvailableParticle()
    {
        // PASO 1: Limpiar del pool cualquier instancia que fue destruida (ej: canvas eliminado con sus children)
        for (int i = particlePool.Count - 1; i >= 0; i--)
        {
            ParticleSystem ps = particlePool[i];
            // Verificar si la ParticleSystem o su GameObject fue destruida
            if (ps == null || ps.gameObject == null)
            {
                Debug.LogWarning($"[ParticleEffectManager] Removiendo instancia destruida del pool (índice {i}).");
                particlePool.RemoveAt(i);
            }
        }

        // PASO 2: Buscar una inactiva en el pool limpio
        foreach (ParticleSystem ps in particlePool)
        {
            if (!ps.gameObject.activeSelf)
            {
                return ps;
            }
        }

        // PASO 3: Si no hay disponibles, crear una nueva
        if (particleEffectPrefab != null)
        {
            GameObject instance = Instantiate(particleEffectPrefab, transform);
            instance.name = $"ParticleFX_Dynamic_{particlePool.Count}";
            
            ParticleSystem ps = instance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                particlePool.Add(ps);
                Debug.Log($"[ParticleEffectManager] Pool extendido a {particlePool.Count} instancias (creación dinámica).");
                return ps;
            }
            else
            {
                Destroy(instance);
                Debug.LogWarning("[ParticleEffectManager] No se pudo crear instancia dinámica.");
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Detiene un ParticleSystem y lo devuelve al pool
    /// </summary>
    public void StopSparkles(ParticleSystem ps)
    {
        if (ps == null) return;

        ps.Stop();
        activeParticles.Remove(ps);
        ps.gameObject.SetActive(false);
    }

    /// <summary>
    /// Corrutina para devolver un ParticleSystem al pool después de un delay
    /// </summary>
    private System.Collections.IEnumerator ReturnToPoolAfterDelay(ParticleSystem ps, float delay)
    {
        yield return new WaitForSeconds(delay);
        StopSparkles(ps);
    }

    /// <summary>
    /// Detiene todos los efectos activos inmediatamente
    /// Usado para cleanup
    /// </summary>
    public void StopAllEffects()
    {
        foreach (ParticleSystem ps in activeParticles)
        {
            if (ps != null)
            {
                ps.Stop();
                ps.gameObject.SetActive(false);
            }
        }
        activeParticles.Clear();
    }

    #if UNITY_EDITOR
    public int GetActiveParticleCount() => activeParticles.Count;
    public int GetPoolSize() => particlePool.Count;
    #endif
}
