using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Oculus.Interaction.Input;
using System.Collections.Generic;

/// <summary>
/// GestureUIController - Sistema de detección de gestos de manos con visualización de texto flotante
/// 
/// Detecta 7 gestos específicos en ambas manos (Meta XR SDK v85.0):
/// - Pinza Thumb + Index → "T1"
/// - Pinza Thumb + Middle → "T2"
/// - Pinza Thumb + Ring → "B1"
/// - Pinza Thumb + Pinky → "B2"
/// - Puño Cerrado (Fist) → "J"
/// - Apuntar (Pointing) → "PI"
/// - Sin gesto → Canvas desaparece
/// 
/// CONFIGURACIÓN EN INSPECTOR (Solo 2 pasos):
/// 1. Asigna en "Left Hand Reference" el GameObject que contiene IHand de mano izquierda
///    (típicamente: LeftHandAnchor que tenga componente IHand)
/// 2. Asigna en "Right Hand Reference" el GameObject que contiene IHand de mano derecha
///    (típicamente: RightHandAnchor que tenga componente IHand)
/// 
/// ¡Eso es todo! El script crea automáticamente el Canvas 3D y el Text en runtime.
/// </summary>
public class GestureUIController : MonoBehaviour
{
    [Header("Hand References (Meta XR)")]
    [SerializeField] 
    private MonoBehaviour leftHandReference;
    
    [SerializeField] 
    private MonoBehaviour rightHandReference;

    [Header("Canvas Configuration")]
    [SerializeField] 
    private float canvasHeightOffset = 0.15f;
    
    [SerializeField] 
    private float canvasScale = 0.05f;

    [Header("Gesture Settings")]
    [SerializeField] 
    private float fistClosureThreshold = 0.7f;
    
    [SerializeField] 
    private float pointingFingerRelaxThreshold = 0.5f;

    [Header("Gesture Text Mappings")]
    [SerializeField]
    private List<GestureMapping> gestureMappings = new List<GestureMapping>
    {
        new GestureMapping { gestureName = "IndexThumb", displayText = "T1" },
        new GestureMapping { gestureName = "MiddleThumb", displayText = "T2" },
        new GestureMapping { gestureName = "RingThumb", displayText = "B1" },
        new GestureMapping { gestureName = "PinkyThumb", displayText = "B2" },
        new GestureMapping { gestureName = "Fist", displayText = "J" },
        new GestureMapping { gestureName = "Pointing", displayText = "PI" }
    };

    // Caché de interfaces IHand
    private IHand leftHand;
    private IHand rightHand;

    // Canvas y texto flotante
    private Canvas runtimeGestureCanvas;
    private TextMeshProUGUI gestureTextMesh;
    private RectTransform canvasRectTransform;

    // Estado actual
    private string currentGesture = null;

    private Dictionary<string, string> gestureToTextMap = new Dictionary<string, string>();

    void Start()
    {
        InitializeHandReferences();
        InitializeGestureCanvas();
        BuildGestureMap();
    }

    void Update()
    {
        if (!ValidateHandTracking())
        {
            HideGestureCanvas();
            return;
        }

        // Detectar gestos en ambas manos (prioritizar la mano con gesto activo)
        string rightGesture = DetectActiveGesture(rightHand);
        string leftGesture = DetectActiveGesture(leftHand);

        // Mostrar el gesto detectado (si hay)
        if (!string.IsNullOrEmpty(rightGesture))
        {
            ShowGesture(rightGesture, rightHand);
            currentGesture = rightGesture;
        }
        else if (!string.IsNullOrEmpty(leftGesture))
        {
            ShowGesture(leftGesture, leftHand);
            currentGesture = leftGesture;
        }
        else
        {
            HideGestureCanvas();
            currentGesture = null;
        }
    }

    /// <summary>
    /// Inicializa las referencias a las manos desde MonoBehaviour (que deben implementar IHand)
    /// </summary>
    private void InitializeHandReferences()
    {
        if (leftHandReference != null)
        {
            leftHand = leftHandReference as IHand;
            if (leftHand == null)
            {
                Debug.LogWarning("Left Hand Reference no implementa IHand. Buscando automáticamente...");
                leftHand = FindHandByName("Left");
            }
        }
        else
        {
            Debug.LogWarning("Left Hand Reference no asignado. Auto-detectando...");
            leftHand = FindHandByName("Left");
        }

        if (rightHandReference != null)
        {
            rightHand = rightHandReference as IHand;
            if (rightHand == null)
            {
                Debug.LogWarning("Right Hand Reference no implementa IHand. Buscando automáticamente...");
                rightHand = FindHandByName("Right");
            }
        }
        else
        {
            Debug.LogWarning("Right Hand Reference no asignado. Auto-detectando...");
            rightHand = FindHandByName("Right");
        }

        if (leftHand == null || rightHand == null)
        {
            Debug.LogError("No se pudieron encontrar referencias a manos. Asigna Left/Right Hand References en el Inspector.");
        }
    }

    /// <summary>
    /// Auto-detecta manos buscando por nombre (fallback si no hay referencias asignadas)
    /// </summary>
    private IHand FindHandByName(string handSide)
    {
        var allMonoBehaviours = FindObjectsOfType<MonoBehaviour>();
        foreach (var mb in allMonoBehaviours)
        {
            if (mb is IHand hand)
            {
                string name = mb.name.ToLower();
                if ((handSide == "Left" && name.Contains("left")) || 
                    (handSide == "Right" && name.Contains("right")))
                {
                    return hand;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Crea dinámicamente el Canvas 3D para mostrar el texto flotante
    /// </summary>
    private void InitializeGestureCanvas()
    {
        // Crear Canvas 3D dinámicamente
        GameObject canvasGO = new GameObject("GestureTextCanvas");
        runtimeGestureCanvas = canvasGO.AddComponent<Canvas>();
        runtimeGestureCanvas.renderMode = RenderMode.WorldSpace;

        canvasRectTransform = canvasGO.GetComponent<RectTransform>();
        canvasRectTransform.sizeDelta = new Vector2(100, 100);
        canvasRectTransform.localScale = new Vector3(canvasScale, canvasScale, canvasScale);

        // Crear TextMeshPro Text
        GameObject textGO = new GameObject("GestureText");
        textGO.transform.SetParent(canvasGO.transform);
        textGO.transform.localPosition = Vector3.zero;

        gestureTextMesh = textGO.AddComponent<TextMeshProUGUI>();
        gestureTextMesh.text = "";
        gestureTextMesh.alignment = TextAlignmentOptions.Center;
        gestureTextMesh.fontSize = 72;

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(100, 100);

        if (gestureTextMesh == null)
        {
            Debug.LogError("Error creando TextMeshProUGUI. Verifica que TMPro está instalado.");
        }

        // Inicialmente desactivado
        runtimeGestureCanvas.gameObject.SetActive(false);
    }

    /// <summary>
    /// Construye el diccionario de mapeo gesto → texto
    /// </summary>
    private void BuildGestureMap()
    {
        gestureToTextMap.Clear();
        foreach (var mapping in gestureMappings)
        {
            gestureToTextMap[mapping.gestureName] = mapping.displayText;
        }
    }

    /// <summary>
    /// Valida que el tracking de manos sea válido
    /// </summary>
    private bool ValidateHandTracking()
    {
        return leftHand != null && rightHand != null &&
               leftHand.IsTrackedDataValid && rightHand.IsTrackedDataValid;
    }

    /// <summary>
    /// Detecta cuál es el gesto activo en una mano específica
    /// Retorna el nombre del gesto o null si no hay ninguno
    /// </summary>
    private string DetectActiveGesture(IHand hand)
    {
        if (hand == null || !hand.IsTrackedDataValid)
            return null;

        // Orden de detección: pinzas específicas primero, luego puño, luego pointing
        if (hand.GetFingerIsPinching(HandFinger.Index))
            return "IndexThumb";
        
        if (hand.GetFingerIsPinching(HandFinger.Middle))
            return "MiddleThumb";
        
        if (hand.GetFingerIsPinching(HandFinger.Ring))
            return "RingThumb";
        
        if (hand.GetFingerIsPinching(HandFinger.Pinky))
            return "PinkyThumb";

        if (IsFistClosed(hand))
            return "Fist";

        if (IsPointing(hand))
            return "Pointing";

        return null;
    }

    /// <summary>
    /// Detecta si todos los dedos están pinzando (puño cerrado)
    /// </summary>
    private bool IsFistClosed(IHand hand)
    {
        if (hand == null)
            return false;

        float indexStrength = hand.GetFingerPinchStrength(HandFinger.Index);
        float middleStrength = hand.GetFingerPinchStrength(HandFinger.Middle);
        float ringStrength = hand.GetFingerPinchStrength(HandFinger.Ring);
        float pinkyStrength = hand.GetFingerPinchStrength(HandFinger.Pinky);

        return indexStrength > fistClosureThreshold &&
               middleStrength > fistClosureThreshold &&
               ringStrength > fistClosureThreshold &&
               pinkyStrength > fistClosureThreshold;
    }

    /// <summary>
    /// Detecta si el índice está extendido (pointing gesture)
    /// </summary>
    private bool IsPointing(IHand hand)
    {
        if (hand == null)
            return false;

        // El índice debe estar extendido (baja fuerza de pinch)
        // y otros dedos relajados
        float indexPinch = hand.GetFingerPinchStrength(HandFinger.Index);
        float middlePinch = hand.GetFingerPinchStrength(HandFinger.Middle);
        float ringPinch = hand.GetFingerPinchStrength(HandFinger.Ring);
        float pinkyPinch = hand.GetFingerPinchStrength(HandFinger.Pinky);

        return indexPinch < pointingFingerRelaxThreshold &&
               middlePinch < pointingFingerRelaxThreshold &&
               ringPinch < pointingFingerRelaxThreshold &&
               pinkyPinch < pointingFingerRelaxThreshold &&
               hand.GetPointerPose(out _);  // Verifica que el pointer pose es válido
    }

    /// <summary>
    /// Muestra el Canvas con el texto del gesto, posicionado sobre la mano
    /// </summary>
    private void ShowGesture(string gestureName, IHand hand)
    {
        if (runtimeGestureCanvas == null || gestureTextMesh == null)
            return;

        // Obtener el texto a mostrar
        if (!gestureToTextMap.TryGetValue(gestureName, out string displayText))
        {
            displayText = gestureName;
        }

        gestureTextMesh.text = displayText;

        // Posicionar canvas sobre la mano (HandWristRoot + offset)
        if (hand.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose))
        {
            Vector3 displayPosition = wristPose.position;
            displayPosition.y += canvasHeightOffset;

            canvasRectTransform.position = displayPosition;

            // Billboard: hacer que el Canvas siempre mire a la cámara
            canvasRectTransform.LookAt(Camera.main.transform);
            canvasRectTransform.Rotate(0, 180, 0);  // Invertir para que esté de frente
        }

        // Activar canvas si no está activo
        if (!runtimeGestureCanvas.gameObject.activeSelf)
        {
            runtimeGestureCanvas.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Oculta el Canvas de gesto
    /// </summary>
    private void HideGestureCanvas()
    {
        if (runtimeGestureCanvas != null && runtimeGestureCanvas.gameObject.activeSelf)
        {
            runtimeGestureCanvas.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Clase serializable para mapear gestos a textos
    /// </summary>
    [System.Serializable]
    public class GestureMapping
    {
        public string gestureName;
        public string displayText;
    }
}
