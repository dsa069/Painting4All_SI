using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// CanvasSpawner - Sistema dinámico de lienzos para Meta Quest 3
/// 
/// Lógica actualizada (Creación y Borrado Condicional):
/// - Acción normal (Botón Y/B o Gesto B1): Genera un lienzo.
/// - Acción cruzada: Si la Mano A sujeta un lienzo (Grip o T2) y la Mano B ejecuta 
///   la acción, el lienzo sujetado por la Mano A se DESTRUYE en lugar de crear uno nuevo.
/// </summary>
public class Generar_Lienzo : MonoBehaviour
{
    [Header("Prefab Configuration")]
    [SerializeField]
    private GameObject canvasPrefab;

    [Header("Spawn Settings")]
    [SerializeField]
    private float spawnDistance = 3f;
    [SerializeField]
    private float forwardOffset = 0.1f;

    private Camera mainCamera;
    private GameObject templateCanvas;
    private bool hasPrefab = false;

    // Gesture detection
    private GestureUIController gestureController;
    
    // Estados independientes por mano para evitar spam de generación/borrado
    private bool wasB1LeftLastFrame = false;
    private bool wasB1RightLastFrame = false;
    private float lastB1ActionTimeLeft = -1f;
    private float lastB1ActionTimeRight = -1f;
    
    [SerializeField]
    private float b1ActionCooldown = 1.5f; // Cooldown unificado para acciones B1

    private void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("❌ No se encontró Camera.main.");
            enabled = false;
            return;
        }

        if (canvasPrefab == null)
        {
            canvasPrefab = Resources.Load<GameObject>("Prefabs/Lienzo");
            if (canvasPrefab != null) hasPrefab = true;
        }
        else
        {
            hasPrefab = true;
        }

        if (!hasPrefab)
        {
            templateCanvas = FindObjectOfType<Paint>()?.gameObject;
            if (templateCanvas == null)
            {
                Debug.LogError("❌ No se encontró Prefab ni lienzo en escena.");
                enabled = false;
                return;
            }
        }

        gestureController = FindObjectOfType<GestureUIController>();
        Debug.Log("✓ CanvasSpawner (Creación/Borrado) inicializado con Nuevo Input System.");
    }

    private void Update()
    {
        bool actionLeftTriggered = false;
        bool actionRightTriggered = false;

        // 1. Detección de Botones (Físicos) - Usando el Nuevo Input System de Unity
        foreach (var device in InputSystem.devices)
        {
            if (device is XRController xrController)
            {
                // En Meta Quest, el "secondaryButton" corresponde a la Y (Izquierda) y la B (Derecha)
                var secondaryButton = xrController.TryGetChildControl<ButtonControl>("secondaryButton");
                
                if (secondaryButton != null && secondaryButton.wasPressedThisFrame)
                {
                    if (IsLeftHandDevice(xrController))
                        actionLeftTriggered = true;
                    else
                        actionRightTriggered = true;
                }
            }
        }

        // 2. Detección de Gestos B1 (Tracking) - Separado por manos
        if (gestureController != null)
        {
            // Evaluación Mano Izquierda
            bool isB1LeftNow = gestureController.IsB1ActiveLeft;
            if (isB1LeftNow && !wasB1LeftLastFrame && Time.time - lastB1ActionTimeLeft >= b1ActionCooldown)
            {
                actionLeftTriggered = true;
                lastB1ActionTimeLeft = Time.time;
            }
            wasB1LeftLastFrame = isB1LeftNow;

            // Evaluación Mano Derecha
            bool isB1RightNow = gestureController.IsB1ActiveRight;
            if (isB1RightNow && !wasB1RightLastFrame && Time.time - lastB1ActionTimeRight >= b1ActionCooldown)
            {
                actionRightTriggered = true;
                lastB1ActionTimeRight = Time.time;
            }
            wasB1RightLastFrame = isB1RightNow;
        }

        // 3. Procesamiento de Lógica Condicional (Detección Cruzada)
        
        // Si la acción provino de la mano IZQUIERDA (Botón Y o Gesto B1)
        if (actionLeftTriggered)
        {
            // Validamos si la mano DERECHA está sujetando un lienzo
            if (CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Right))
            {
                DeleteGrippedCanvas(CanvasGripManager.ActiveHand.Right);
            }
            else
            {
                Debug.Log("🎨 Acción Izquierda → Generando lienzo...");
                SpawnCanvas();
            }
        }

        // Si la acción provino de la mano DERECHA (Botón B o Gesto B1)
        if (actionRightTriggered)
        {
            // Validamos si la mano IZQUIERDA está sujetando un lienzo
            if (CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Left))
            {
                DeleteGrippedCanvas(CanvasGripManager.ActiveHand.Left);
            }
            else
            {
                Debug.Log("🎨 Acción Derecha → Generando lienzo...");
                SpawnCanvas();
            }
        }

        // 4. Debugging en Editor
        #if UNITY_EDITOR
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.yKey.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame))
        {
            if (CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Right))
                DeleteGrippedCanvas(CanvasGripManager.ActiveHand.Right);
            else if (CanvasGripManager.Instance.IsHandAlreadyGripping(CanvasGripManager.ActiveHand.Left))
                DeleteGrippedCanvas(CanvasGripManager.ActiveHand.Left);
            else
                SpawnCanvas();
        }
        #endif
    }

    /// <summary>
    /// Determina si un XRController corresponde a la mano izquierda
    /// (Misma lógica probada que en Seleccionar_Lienzo.cs)
    /// </summary>
    private bool IsLeftHandDevice(XRController device)
    {
        if (device.name.Contains("Left") || device.name.Contains("left"))
            return true;
        if (device.name.Contains("Right") || device.name.Contains("right"))
            return false;
            
        if (device.path.Contains("lefthand"))
            return true;
        if (device.path.Contains("righthand"))
            return false;

        // Fallback por enumeración
        int xrControllerIndex = -1;
        int xrControllerCount = 0;
        
        foreach (var dev in InputSystem.devices)
        {
            if (dev is XRController)
            {
                if (dev == device)
                    xrControllerIndex = xrControllerCount;
                xrControllerCount++;
            }
        }
        return xrControllerIndex == 0;
    }

    /// <summary>
    /// Se encarga de destruir el lienzo de forma segura, eliminando las referencias en el Manager
    /// antes de destruir el GameObject para evitar NullReferenceExceptions.
    /// </summary>
    private void DeleteGrippedCanvas(CanvasGripManager.ActiveHand grippingHand)
    {
        Seleccionar_Lienzo canvasToDelete = CanvasGripManager.Instance.GetGrippedCanvas(grippingHand);
        
        if (canvasToDelete != null)
        {
            Debug.Log($"🗑️ Borrando lienzo sostenido por la mano {grippingHand}...");
            CanvasGripManager.Instance.UnregisterGrip(grippingHand);
            Destroy(canvasToDelete.gameObject);
        }
    }

    private void SpawnCanvas()
    {
        GameObject newCanvas = null;

        if (hasPrefab && canvasPrefab != null)
        {
            newCanvas = Instantiate(canvasPrefab);
        }
        else if (templateCanvas != null)
        {
            newCanvas = Instantiate(templateCanvas);
        }
        else
        {
            Debug.LogError("❌ No se pudo crear el lienzo.");
            return;
        }

        Vector3 cameraPosition = mainCamera.transform.position;
        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 spawnPosition = cameraPosition + (cameraForward * spawnDistance) + (cameraForward * forwardOffset);

        newCanvas.transform.position = spawnPosition;
        newCanvas.transform.LookAt(cameraPosition);
        newCanvas.transform.Rotate(0f, 180f, 0f);
        newCanvas.name = $"Lienzo_{System.DateTime.Now:HH-mm-ss}";
    }
}