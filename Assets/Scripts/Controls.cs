using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Oculus.Interaction.Input;
using System.Collections.Generic;

/// <summary>
/// GestureUIController - Sistema de detección INDEPENDIENTE de gestos en AMBAS manos simultáneamente
/// 
/// Detecta 7 gestos específicos en AMBAS manos (Meta XR SDK v85.0) de forma SIMULTÁNEA:
/// - Pinza Thumb + Index → "T1"
/// - Pinza Thumb + Middle → "T2"
/// - Pinza Thumb + Ring → "B1"
/// - Pinza Thumb + Pinky → "B2"
/// - Puño Cerrado (Fist) → "J"
/// - Apuntar (Pointing) → "PI"
/// - Sin gesto → TextMesh correspondiente se oculta
/// 
/// IMPORTANTE: Ahora cada mano se detecta de forma INDEPENDIENTE.
/// Si la mano derecha hace un gesto y la izquierda otro diferente,
/// AMBOS se mostrarán simultáneamente, cada uno sobre su mano.
/// 
/// CONFIGURACIÓN EN INSPECTOR (¡Automática!):
/// NO NECESITAS ASIGNAR NADA EN EL INSPECTOR. El script auto-detecta automáticamente:
/// - Las manos (IHand) buscando en la escena
/// - Crea dos TextMesh 3D dinámicamente (uno por mano)
/// - Detecta gestos independientes en cada mano en simultáneo
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
    private float canvasScale = 0.01f;

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

    // TextMesh 3D para AMBAS manos (independientes)
    private GameObject leftTextContainer;
    private GameObject rightTextContainer;
    private TextMeshPro leftGestureTextMesh3D;
    private TextMeshPro rightGestureTextMesh3D;

    // Estado actual
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

        // Detectar gestos en AMBAS manos de forma independiente
        string rightGesture = DetectActiveGesture(rightHand);
        string leftGesture = DetectActiveGesture(leftHand);

        // Mostrar o ocultar gestos de forma independiente
        if (!string.IsNullOrEmpty(rightGesture))
        {
            ShowGestureForHand(rightGesture, rightHand, true);
        }
        else
        {
            HideGestureForHand(true);
        }

        if (!string.IsNullOrEmpty(leftGesture))
        {
            ShowGestureForHand(leftGesture, leftHand, false);
        }
        else
        {
            HideGestureForHand(false);
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
    /// Crea TextMesh 3D para AMBAS manos
    /// </summary>
    private void InitializeGestureCanvas()
    {
        // TextMesh para mano DERECHA
        rightTextContainer = new GameObject("GestureTextMesh_Right");
        rightGestureTextMesh3D = rightTextContainer.AddComponent<TextMeshPro>();
        rightGestureTextMesh3D.text = "";
        rightGestureTextMesh3D.fontSize = 36;
        rightGestureTextMesh3D.alignment = TextAlignmentOptions.Center;
        rightGestureTextMesh3D.color = Color.white;
        rightTextContainer.transform.localScale = new Vector3(canvasScale, canvasScale, canvasScale);
        rightTextContainer.SetActive(false);

        // TextMesh para mano IZQUIERDA
        leftTextContainer = new GameObject("GestureTextMesh_Left");
        leftGestureTextMesh3D = leftTextContainer.AddComponent<TextMeshPro>();
        leftGestureTextMesh3D.text = "";
        leftGestureTextMesh3D.fontSize = 36;
        leftGestureTextMesh3D.alignment = TextAlignmentOptions.Center;
        leftGestureTextMesh3D.color = Color.white;
        leftTextContainer.transform.localScale = new Vector3(canvasScale, canvasScale, canvasScale);
        leftTextContainer.SetActive(false);

        if (leftGestureTextMesh3D == null || rightGestureTextMesh3D == null)
        {
            Debug.LogError("Error creando TextMeshPro 3D. Verifica que TMPro está instalado.");
        }
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
    /// Muestra el TextMesh 3D de UNA MANO con el texto del gesto
    /// </summary>
    private void ShowGestureForHand(string gestureName, IHand hand, bool isRightHand)
    {
        GameObject textContainer = isRightHand ? rightTextContainer : leftTextContainer;
        TextMeshPro textMesh = isRightHand ? rightGestureTextMesh3D : leftGestureTextMesh3D;

        if (textContainer == null || textMesh == null)
            return;

        // Obtener el texto a mostrar
        if (!gestureToTextMap.TryGetValue(gestureName, out string displayText))
        {
            displayText = gestureName;
        }

        textMesh.text = displayText;

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
    /// Oculta el TextMesh 3D de UNA MANO
    /// </summary>
    private void HideGestureForHand(bool isRightHand)
    {
        GameObject textContainer = isRightHand ? rightTextContainer : leftTextContainer;
        
        if (textContainer != null && textContainer.activeSelf)
        {
            textContainer.SetActive(false);
        }
    }

    /// <summary>
    /// Oculta ambos TextMesh 3D (cuando falla el tracking)
    /// </summary>
    private void HideGestureCanvas()
    {
        if (rightTextContainer != null && rightTextContainer.activeSelf)
            rightTextContainer.SetActive(false);
        
        if (leftTextContainer != null && leftTextContainer.activeSelf)
            leftTextContainer.SetActive(false);
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
