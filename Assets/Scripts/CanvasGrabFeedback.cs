using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Reproduce efectos de partículas visuales cuando se agarra un lienzo
/// 
/// Sistema:
/// - Se suscribe a eventos OnGripStarted y OnGripEnded del CanvasGripManager
/// - Cuando un lienzo es agarrado: reproduce partículas continuamente en el controlador + en el lienzo
/// - Partículas permanecen activas TODO el tiempo que se sostiene el lienzo
/// - Cuando un lienzo es soltado: detiene ambos efectos inmediatamente
/// 
/// Características:
/// - Mantiene referencias a instancias de partículas activas por (mano, lienzo)
/// - Auto-detecta referencias a controladores
/// - Efecto GRANDE y notorio, visible desde cualquier ángulo (3D)
/// </summary>
public class CanvasGrabFeedback : MonoBehaviour
{
    [SerializeField] private float particleDuration = -1f;  // -1 = indefinido (se controla manualmente)
    
    private OVRCameraRig cameraRig;
    private Transform leftControllerTransform;
    private Transform rightControllerTransform;
    
    // Mantener referencias a partículas activas: Key = "Hand_CanvasInstanceId", Value = ParticleSystem
    private Dictionary<string, ParticleSystem> activeControllerParticles = new Dictionary<string, ParticleSystem>();
    private Dictionary<string, ParticleSystem> activeCanvasParticles = new Dictionary<string, ParticleSystem>();

    private static CanvasGrabFeedback instance;

    public static CanvasGrabFeedback Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<CanvasGrabFeedback>();
                
                if (instance == null)
                {
                    GameObject feedbackGO = new GameObject("CanvasGrabFeedback");
                    instance = feedbackGO.AddComponent<CanvasGrabFeedback>();
                    DontDestroyOnLoad(feedbackGO);
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[CanvasGrabFeedback] Ya existe una instancia. Destruyendo duplicada.");
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        
        AutoDetectControllers();
    }

    private void OnEnable()
    {
        // Suscribirse a eventos del CanvasGripManager
        CanvasGripManager.OnGripStarted += OnGripStarted;
        CanvasGripManager.OnGripEnded += OnGripEnded;
        
        Debug.Log("[CanvasGrabFeedback] ✓ Suscrito a eventos de grip del CanvasGripManager.");
    }

    private void OnDisable()
    {
        // Desuscribirse para evitar memory leaks
        CanvasGripManager.OnGripStarted -= OnGripStarted;
        CanvasGripManager.OnGripEnded -= OnGripEnded;
        
        Debug.Log("[CanvasGrabFeedback] ✗ Desuscrito de eventos de grip del CanvasGripManager.");
    }

    /// <summary>
    /// Auto-detecta las referencias a los mandos en la escena
    /// </summary>
    private void AutoDetectControllers()
    {
        cameraRig = FindObjectOfType<OVRCameraRig>();
        
        if (cameraRig != null)
        {
            leftControllerTransform = cameraRig.leftHandAnchor;
            rightControllerTransform = cameraRig.rightHandAnchor;
        }
        
        if (leftControllerTransform == null || rightControllerTransform == null)
        {
            var allObjects = FindObjectsOfType<Transform>();
            
            foreach (var t in allObjects)
            {
                if (t.name == "LeftControllerAnchor" || t.name == "LeftHand" || t.name == "LeftHandAnchor")
                    leftControllerTransform = t;
                
                if (t.name == "RightControllerAnchor" || t.name == "RightHand" || t.name == "RightHandAnchor")
                    rightControllerTransform = t;
            }
        }
        
        if (leftControllerTransform == null)
            Debug.LogWarning("[CanvasGrabFeedback] No se encontró mando izquierdo");
        
        if (rightControllerTransform == null)
            Debug.LogWarning("[CanvasGrabFeedback] No se encontró mando derecho");
    }

    /// <summary>
    /// Callback cuando se inicia un grip
    /// Dispara partículas en el controlador y en el lienzo
    /// Las partículas permanecen activas TODO el tiempo que se sostiene
    /// </summary>
    private void OnGripStarted(CanvasGripManager.ActiveHand hand, Seleccionar_Lienzo canvas)
    {
        if (canvas == null)
        {
            Debug.LogWarning("[CanvasGrabFeedback] OnGripStarted: canvas es null");
            return;
        }

        string key = GetGripKey(hand, canvas);
        
        Debug.Log($"[CanvasGrabFeedback] 🎆 Grip iniciado: Mano {hand}, Lienzo {canvas.gameObject.name}");
        
        // Obtener transform del controlador
        Transform controllerTransform = hand == CanvasGripManager.ActiveHand.Left 
            ? leftControllerTransform 
            : rightControllerTransform;
        
        if (controllerTransform == null)
        {
            Debug.LogWarning($"[CanvasGrabFeedback] No se pudo obtener transform del controlador para mano {hand}");
            return;
        }
        
        // Reproducir partículas EN EL CONTROLADOR (duración indefinida)
        ParticleSystem controllerParticles = ParticleEffectManager.Instance.PlaySparklesAt(
            controllerTransform.position, 
            controllerTransform,
            particleDuration  // -1 = indefinido
        );
        
        if (controllerParticles != null)
        {
            activeControllerParticles[key] = controllerParticles;
            Debug.Log($"[CanvasGrabFeedback] ✨ Partículas INICIADAS EN CONTROLADOR {hand} (duración: indefinida)");
        }
        
        // Reproducir partículas EN EL LIENZO (centro)
        ParticleSystem canvasParticles = ParticleEffectManager.Instance.PlaySparklesAt(
            canvas.transform.position,
            canvas.transform,
            particleDuration  // -1 = indefinido
        );
        
        if (canvasParticles != null)
        {
            string canvasKey = key + "_canvas";
            activeCanvasParticles[canvasKey] = canvasParticles;
            Debug.Log($"[CanvasGrabFeedback] ✨ Partículas INICIADAS EN LIENZO: {canvas.gameObject.name} (duración: indefinida)");
        }
    }

    /// <summary>
    /// Callback cuando finaliza un grip
    /// Detiene ambos efectos de partículas inmediatamente
    /// </summary>
    private void OnGripEnded(CanvasGripManager.ActiveHand hand, Seleccionar_Lienzo canvas)
    {
        if (canvas == null)
        {
            Debug.LogWarning("[CanvasGrabFeedback] OnGripEnded: canvas es null");
            return;
        }

        string key = GetGripKey(hand, canvas);
        
        Debug.Log($"[CanvasGrabFeedback] 🎆 Grip finalizado: Mano {hand}, Lienzo {canvas.gameObject.name}");
        
        // Detener partículas del controlador
        if (activeControllerParticles.ContainsKey(key))
        {
            ParticleEffectManager.Instance.StopSparkles(activeControllerParticles[key]);
            activeControllerParticles.Remove(key);
            Debug.Log($"[CanvasGrabFeedback] ⏹️ Partículas detenidas EN CONTROLADOR {hand}");
        }
        
        // Detener partículas del lienzo
        string canvasKey = key + "_canvas";
        if (activeCanvasParticles.ContainsKey(canvasKey))
        {
            ParticleEffectManager.Instance.StopSparkles(activeCanvasParticles[canvasKey]);
            activeCanvasParticles.Remove(canvasKey);
            Debug.Log($"[CanvasGrabFeedback] ⏹️ Partículas detenidas EN LIENZO: {canvas.gameObject.name}");
        }
    }

    /// <summary>
    /// Genera una clave única para identificar un grip (mano + lienzo)
    /// </summary>
    private string GetGripKey(CanvasGripManager.ActiveHand hand, Seleccionar_Lienzo canvas)
    {
        return $"{hand}_{canvas.GetInstanceID()}";
    }

    #if UNITY_EDITOR
    public int GetActiveParticleCount()
    {
        return activeControllerParticles.Count + activeCanvasParticles.Count;
    }
    #endif
}
