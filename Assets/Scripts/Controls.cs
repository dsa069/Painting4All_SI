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
/// CONFIGURACIÓN EN INSPECTOR (¡Automática!):
/// NO NECESITAS ASIGNAR NADA EN EL INSPECTOR. El script auto-detecta automáticamente:
/// - Las manos (IHand) buscando en la escena
/// - Crea el Canvas 3D dinámicamente
/// - Crea el Text flotante automáticamente
/// 
/// ¡Solo crea un GameObject vacío, agrega el script y listo!
/// </summary>
public class GestureUIController : MonoBehaviour
{
    [Header("Hand References (Auto-detected if empty)")]
    [SerializeField] 
    private MonoBehaviour leftHandReference;
    
    [SerializeField] 
    private MonoBehaviour rightHandReference;

    [Header("Canvas Configuration")]
    [SerializeField] 
    private float canvasHeightOffset = 0.08f;
    
    [SerializeField] 
    private float canvasScale = 0.08f;

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
    private GameObject textContainer;
    private TextMeshPro gestureTextMesh3D;

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
    /// Si no están asignadas, las auto-detecta buscando en la escena
    /// </summary>
    private void InitializeHandReferences()
    {
        // Intentar obtener IHand desde referencias asignadas
        if (leftHandReference != null)
        {
            leftHand = leftHandReference as IHand;
        }
        
        if (rightHandReference != null)
        {
            rightHand = rightHandReference as IHand;
        }

        // Auto-detectar si no se asignaron
        if (leftHand == null || rightHand == null)
        {
            Debug.Log("Auto-detectando manos en la escena...");
            AutoDetectHands();
        }

        if (leftHand == null || rightHand == null)
        {
            Debug.LogError("ERROR: No se pudieron encontrar referencias a manos IHand. " +
                "Verifica que LeftHandAnchor y RightHandAnchor existan en la escena con componentes IHand.");
        }
        else
        {
            Debug.Log("✓ Manos detectadas correctamente.");
        }
    }

    /// <summary>
    /// Auto-detecta manos buscando componentes IHand en la escena
    /// </summary>
    private void AutoDetectHands()
    {
        // Buscar TODOS los componentes IHand en la escena
        var allMonoBehaviours = FindObjectsOfType<MonoBehaviour>();
        
        foreach (var mb in allMonoBehaviours)
        {
            IHand hand = mb as IHand;
            if (hand != null)
            {
                string name = mb.name.ToLower();
                string parentName = mb.transform.parent != null ? mb.transform.parent.name.ToLower() : "";
                
                // Buscar "Left" en el nombre del objeto o del padre
                if ((name.Contains("left") || parentName.Contains("left")) && leftHand == null)
                {
                    leftHand = hand;
                    Debug.Log($"✓ Left Hand detectada en: {mb.name}");
                }
                
                // Buscar "Right" en el nombre del objeto o del padre
                if ((name.Contains("right") || parentName.Contains("right")) && rightHand == null)
                {
                    rightHand = hand;
                    Debug.Log($"✓ Right Hand detectada en: {mb.name}");
                }
                
                // Si ambas están detectadas, salir del loop
                if (leftHand != null && rightHand != null)
                    break;
            }
        }
    }

    /// <summary>
    /// Crea un TextMesh 3D para mostrar el texto flotante
    /// </summary>
    private void InitializeGestureCanvas()
    {
        // Crear GameObject para contener el TextMesh 3D
        textContainer = new GameObject("GestureTextMesh");
        gestureTextMesh3D = textContainer.AddComponent<TextMeshPro>();
        
        // Configurar el TextMesh 3D
        gestureTextMesh3D.text = "";
        gestureTextMesh3D.fontSize = 36;
        gestureTextMesh3D.alignment = TextAlignmentOptions.Center;
        gestureTextMesh3D.color = Color.white;
        
        // Escalar el objeto para que sea pequeño en el mundo
        textContainer.transform.localScale = new Vector3(canvasScale, canvasScale, canvasScale);
        
        if (gestureTextMesh3D == null)
        {
            Debug.LogError("Error creando TextMeshPro 3D. Verifica que TMPro está instalado.");
        }

        // Inicialmente desactivado
        textContainer.SetActive(false);
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
    /// Muestra el TextMesh 3D con el texto del gesto, posicionado sobre la mano
    /// </summary>
    private void ShowGesture(string gestureName, IHand hand)
    {
        if (textContainer == null || gestureTextMesh3D == null)
            return;

        // Obtener el texto a mostrar
        if (!gestureToTextMap.TryGetValue(gestureName, out string displayText))
        {
            displayText = gestureName;
        }

        gestureTextMesh3D.text = displayText;

        // Posicionar texto sobre la mano (HandWristRoot + offset)
        if (hand.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose))
        {
            Vector3 displayPosition = wristPose.position;
            displayPosition.y += canvasHeightOffset;

            textContainer.transform.position = displayPosition;

            // Hacer que el texto siempre mire a la cámara (billboard)
            textContainer.transform.LookAt(Camera.main.transform);
            textContainer.transform.Rotate(0, 180, 0);
        }

        // Activar si no está activo
        if (!textContainer.activeSelf)
        {
            textContainer.SetActive(true);
        }
    }

    /// <summary>
    /// Oculta el TextMesh 3D de gesto
    /// </summary>
    private void HideGestureCanvas()
    {
        if (textContainer != null && textContainer.activeSelf)
        {
            textContainer.SetActive(false);
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
