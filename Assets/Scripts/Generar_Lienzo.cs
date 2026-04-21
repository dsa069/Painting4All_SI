using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CanvasSpawner - Sistema de instanciación dinámica de lienzos de pintura para Meta Quest 3
/// 
/// Permite al usuario generar nuevos lienzos de pintura presionando botones del controlador:
/// - Botón Y (Izquierdo): OVRInput.Button.One, OVRInput.Controller.LTouch
/// - Botón B (Derecho): OVRInput.Button.Two, OVRInput.Controller.RTouch
/// 
/// Características:
/// - Instancia lienzos desde un Prefab o clona el lienzo existente
/// - Posiciona automáticamente a 1-1.5m frente al usuario
/// - Orienta el lienzo hacia la cámara para pintar inmediatamente
/// - Permite múltiples instancias simultáneamente
/// - Auto-detecta la cámara principal
/// 
/// Uso:
/// 1. Crear un GameObject vacío llamado "CanvasSpawner"
/// 2. Asignar este script
/// 3. (Opcional) Crear un Prefab en Assets/Resources/Prefabs/Lienzo.prefab
/// 4. En el Inspector, configurar:
///    - Canvas Prefab (si existe) O dejar en blanco para usar lienzo existente
///    - Spawn Distance (1-1.5m, default 1.25m)
/// </summary>
public class Generar_Lienzo : MonoBehaviour
{
    [Header("Prefab Configuration")]
    [SerializeField]
    private GameObject canvasPrefab;

    [Header("Spawn Settings")]
    [SerializeField]
    private float spawnDistance = 1.25f;
    [SerializeField]
    private float forwardOffset = 0.1f; // Offset para evitar clipping con la cámara

    private Camera mainCamera;
    private GameObject templateCanvas; // Referencia al lienzo existente en la escena (si no hay Prefab)
    private bool hasPrefab = false;

    // Gesture detection
    private GestureUIController gestureController;
    private bool wasB1ActiveLastFrame = false; // Para detectar transición de gesto (GetDown-like behavior)
    private float lastB1SpawnTime = -1f; // Cooldown entre spawns por gesto
    [SerializeField]
    private float b1SpawnCooldown = 2f; // Tiempo mínimo entre spawns por gesto B1 (en segundos)

    private void Start()
    {
        // Auto-detectar cámara principal
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("❌ No se encontró Camera.main. Asegúrate de que la cámara tenga la etiqueta 'MainCamera'.");
            enabled = false;
            return;
        }

        // Intentar cargar Prefab desde Resources
        if (canvasPrefab == null)
        {
            canvasPrefab = Resources.Load<GameObject>("Prefabs/Lienzo");
            if (canvasPrefab != null)
            {
                hasPrefab = true;
                Debug.Log("✓ Prefab encontrado: Assets/Resources/Prefabs/Lienzo.prefab");
            }
        }
        else
        {
            hasPrefab = true;
            Debug.Log("✓ Prefab asignado en Inspector");
        }

        // Si no hay Prefab, buscar lienzo existente en la escena
        if (!hasPrefab)
        {
            templateCanvas = FindObjectOfType<Paint>()?.gameObject;
            if (templateCanvas != null)
            {
                Debug.Log("✓ Lienzo existente detectado. Se usará como plantilla para clonación.");
            }
            else
            {
                Debug.LogError("❌ No se encontró Prefab ni lienzo existente en la escena. " +
                    "Por favor, crea un Prefab en Assets/Resources/Prefabs/Lienzo.prefab");
                enabled = false;
                return;
            }
        }

        // Auto-detectar GestureUIController
        gestureController = FindObjectOfType<GestureUIController>();
        if (gestureController != null)
        {
            Debug.Log("✓ GestureUIController detectado. Se puede generar lienzo con gesto B1.");
        }
        else
        {
            Debug.LogWarning("⚠ No se encontró GestureUIController. Solo funcionará generación por botones.");
        }

        Debug.Log("✓ CanvasSpawner inicializado correctamente.");
    }

    private void Update()
    {
        // Detectar entrada de botones OVR - Solo Botón Y (izquierdo) y Botón B (derecho)
        // Button.Two en mando izquierdo = Y | Button.Two en mando derecho = B
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
        {
            Debug.Log("🎨 Botón Y (izquierdo) presionado → Generando lienzo...");
            SpawnCanvas();
        }

        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            Debug.Log("🎨 Botón B (derecho) presionado → Generando lienzo...");
            SpawnCanvas();
        }

        // Detectar gesto B1 (pinza anular-pulgar) para generar lienzo
        // Solo spawna cuando B1 PASA a estar activo (transición de false a true) Y ha pasado el cooldown
        // Esto previene múltiples spawns en gestos rápidos
        bool isB1ActiveNow = gestureController != null && gestureController.IsB1Active();
        if (isB1ActiveNow && !wasB1ActiveLastFrame)
        {
            float timeSinceLastB1Spawn = Time.time - lastB1SpawnTime;
            if (timeSinceLastB1Spawn >= b1SpawnCooldown)
            {
                Debug.Log("🎨 Gesto B1 detectado → Generando lienzo...");
                SpawnCanvas();
                lastB1SpawnTime = Time.time;
            }
        }
        wasB1ActiveLastFrame = isB1ActiveNow;

        // Debug: Permitir spawn con teclas en Editor (para testing sin Quest)
        #if UNITY_EDITOR
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.yKey.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame))
        {
            Debug.Log("🎨 Tecla Y/7 presionada → Generando lienzo (Editor Mode)...");
            SpawnCanvas();
        }
        #endif
    }

    private void SpawnCanvas()
    {
        GameObject newCanvas = null;

        if (hasPrefab && canvasPrefab != null)
        {
            // Instanciar desde Prefab
            newCanvas = Instantiate(canvasPrefab);
        }
        else if (templateCanvas != null)
        {
            // Clonar lienzo existente
            newCanvas = Instantiate(templateCanvas);
        }
        else
        {
            Debug.LogError("❌ No se pudo crear el lienzo. Verifica la configuración.");
            return;
        }

        // Calcular posición: 1.25m frente a la cámara
        Vector3 cameraPosition = mainCamera.transform.position;
        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 spawnPosition = cameraPosition + (cameraForward * spawnDistance) + (cameraForward * forwardOffset);

        // Aplicar posición y rotación
        newCanvas.transform.position = spawnPosition;

        // Orientar el lienzo hacia la cámara (LookAt)
        // El lienzo mira hacia atrás por defecto, así que apuntamos hacia la cámara
        newCanvas.transform.LookAt(cameraPosition);

        // Opcional: Nombrar el lienzo con timestamp para facilitar depuración
        newCanvas.name = $"Lienzo_{System.DateTime.Now:HH-mm-ss}";

        Debug.Log($"✓ Lienzo instanciado en posición: {spawnPosition}");
    }
}
