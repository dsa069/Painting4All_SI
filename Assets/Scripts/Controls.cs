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

    // Variable maldita
    //[Header("Gesture Settings")]
    //[SerializeField] 
    //private float fistClosureThreshold = 0.25f;
    
    [SerializeField] 
    private float pointingFingerRelaxThreshold = 0.5f;

    [Header("Pinch Detection Settings")]
    
    [SerializeField]
    private float thumbMinimumStrength = 0.40f; // Verificar que el pulgar también pinza
    
    [SerializeField]
    private int hysteresisFramesT1 = 2;
    
    [SerializeField]
    private int hysteresisFramesT2 = 3;
    
    [SerializeField]
    private int hysteresisFramesB1 = 4;
    
    [SerializeField]
    private int hysteresisFramesB2 = 5;

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

    // Histéresis: rastrear frames consecutivos para cada pinza y mano
    private Dictionary<string, int> pinchFrameCounterLeft = new Dictionary<string, int>();
    private Dictionary<string, int> pinchFrameCounterRight = new Dictionary<string, int>();

    void Start()
    {
        InitializeHandReferences();
        InitializeGestureCanvas();
        BuildGestureMap();
        InitializeHysteresisCounters();
    }

    void Update()
    {
        // Procesar mano DERECHA de forma independiente
        if (rightHand != null && rightHand.IsTrackedDataValid)
        {
            string rightGesture = DetectActiveGesture(rightHand);
            if (!string.IsNullOrEmpty(rightGesture))
            {
                ShowGestureForHand(rightGesture, rightHand, true);
            }
            else
            {
                HideGestureForHand(true);
            }
        }
        else
        {
            HideGestureForHand(true);
        }

        // Procesar mano IZQUIERDA de forma independiente
        if (leftHand != null && leftHand.IsTrackedDataValid)
        {
            string leftGesture = DetectActiveGesture(leftHand);
            if (!string.IsNullOrEmpty(leftGesture))
            {
                ShowGestureForHand(leftGesture, leftHand, false);
            }
            else
            {
                HideGestureForHand(false);
            }
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

        // Permitir funcionamiento independiente: solo error si NINGUNA mano se encuentra
        if (leftHand == null && rightHand == null)
        {
            Debug.LogError("ERROR: No se pudieron encontrar referencias a manos IHand. " +
                "Verifica que LeftHandAnchor y RightHandAnchor existan en la escena con componentes IHand.");
        }
        else if (leftHand == null)
        {
            Debug.LogWarning("⚠ Mano izquierda no detectada. Solo funcionará la mano derecha.");
        }
        else if (rightHand == null)
        {
            Debug.LogWarning("⚠ Mano derecha no detectada. Solo funcionará la mano izquierda.");
        }
        else
        {
            Debug.Log("✓ Ambas manos detectadas correctamente.");
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
    /// Inicializa los contadores de histéresis para cada pinza y mano
    /// </summary>
    private void InitializeHysteresisCounters()
    {
        // Inicializar contadores para mano izquierda
        pinchFrameCounterLeft["T1"] = 0;
        pinchFrameCounterLeft["T2"] = 0;
        pinchFrameCounterLeft["B1"] = 0;
        pinchFrameCounterLeft["B2"] = 0;

        // Inicializar contadores para mano derecha
        pinchFrameCounterRight["T1"] = 0;
        pinchFrameCounterRight["T2"] = 0;
        pinchFrameCounterRight["B1"] = 0;
        pinchFrameCounterRight["B2"] = 0;
    }

    /// <summary>
    /// Detecta T1: Pinza Index + Thumb (más confiable)
    /// </summary>
    private bool IsPinchT1(IHand hand)
    {
        if (hand == null)
            return false;

        float indexStrength = hand.GetFingerPinchStrength(HandFinger.Index);
        float thumbStrength = hand.GetFingerPinchStrength(HandFinger.Thumb);

        return indexStrength > 0.60f && thumbStrength > thumbMinimumStrength;
    }

    /// <summary>
    /// Detecta T2: Pinza Middle + Thumb (requiere validación dual)
    /// </summary>
    private bool IsPinchT2(IHand hand)
    {
        if (hand == null)
            return false;

        float middleStrength = hand.GetFingerPinchStrength(HandFinger.Middle);
        float thumbStrength = hand.GetFingerPinchStrength(HandFinger.Thumb);

        return middleStrength > 0.5f && thumbStrength > thumbMinimumStrength;
    }

    /// <summary>
    /// Detecta B1: Pinza Ring + Thumb (difícil, requiere sensibilidad)
    /// </summary>
    private bool IsPinchB1(IHand hand)
    {
        if (hand == null)
            return false;

        float ringStrength = hand.GetFingerPinchStrength(HandFinger.Ring);
        float thumbStrength = hand.GetFingerPinchStrength(HandFinger.Thumb);

        return ringStrength > 0.40f && thumbStrength > thumbMinimumStrength;
    }

    /// <summary>
    /// Detecta B2: Pinza Pinky + Thumb (muy difícil, máxima sensibilidad)
    /// </summary>
    private bool IsPinchB2(IHand hand)
    {
        if (hand == null)
            return false;

        float pinkyStrength = hand.GetFingerPinchStrength(HandFinger.Pinky);
        float thumbStrength = hand.GetFingerPinchStrength(HandFinger.Thumb);

        return pinkyStrength > 0.30f && thumbStrength > thumbMinimumStrength;
    }

    /// <summary>
    /// Valida que el tracking de una mano sea válido
    /// </summary>
    private bool IsHandTracked(IHand hand)
    {
        return hand != null && hand.IsTrackedDataValid;
    }

    /// <summary>
    /// Detecta cuál es el gesto activo en una mano específica
    /// Retorna el nombre del gesto o null si no hay ninguno
    /// Incluye validación de fuerza y histéresis para mejor estabilidad
    /// </summary>
    private string DetectActiveGesture(IHand hand)
    {
        if (hand == null || !hand.IsTrackedDataValid)
            return null;

        bool isRightHand = (hand == rightHand);
        var frameCounter = isRightHand ? pinchFrameCounterRight : pinchFrameCounterLeft;

        if (IsFistClosed(hand))
            return "Fist";
        
        if (IsPinchB1(hand))
        {
            frameCounter["B1"] = frameCounter["B1"] + 1;
            if (frameCounter["B1"] >= hysteresisFramesB1)
                return "RingThumb";
        }
        else
            frameCounter["B1"] = 0;

        if (IsPinchB2(hand))
        {
            frameCounter["B2"] = frameCounter["B2"] + 1;
            if (frameCounter["B2"] >= hysteresisFramesB2)
                return "PinkyThumb";
        }
        else
            frameCounter["B2"] = 0;

            
        //if (IsPointing(hand))
        //    return "Pointing";

        if (IsPinchT2(hand))
        {
            frameCounter["T2"] = frameCounter["T2"] + 1;
            if (frameCounter["T2"] >= hysteresisFramesT2)
                return "MiddleThumb";
        }
        else
            frameCounter["T2"] = 0;

        // Verificar cada pinza con validación robusta
        if (IsPinchT1(hand))
        {
            frameCounter["T1"] = frameCounter["T1"] + 1;
            if (frameCounter["T1"] >= hysteresisFramesT1)
                return "IndexThumb";
        }
        else
            frameCounter["T1"] = 0;

        return null;
    }

    /// <summary>
    /// Expone el estado de T1 (pinza índice-pulgar) en la mano derecha
    /// Devuelve true si el gesto T1 está activo
    /// </summary>
    public bool IsT1ActiveRight
    {
        get
        {
            if (rightHand == null || !rightHand.IsTrackedDataValid)
                return false;
            return IsPinchT1(rightHand);
        }
    }

    /// <summary>
    /// Expone el estado de T1 (pinza índice-pulgar) en la mano izquierda
    /// Devuelve true si el gesto T1 está activo
    /// </summary>
    public bool IsT1ActiveLeft
    {
        get
        {
            if (leftHand == null || !leftHand.IsTrackedDataValid)
                return false;
            return IsPinchT1(leftHand);
        }
    }

    /// <summary>
    /// Devuelve true si T1 está activo en CUALQUIER mano (izquierda o derecha)
    /// Útil para activar acciones que deben dispararse con cualquier mano
    /// </summary>
    public bool IsT1Active()
    {
        return IsT1ActiveRight || IsT1ActiveLeft;
    }

    /// <summary>
    /// Detecta si todos los dedos están cerrados (puño cerrado)
    /// Usa promedio de fuerza de pinch para ser más tolerante con variaciones naturales
    /// NO se activa si hay alguna pinza en progreso
    /// </summary>
    private bool IsFistClosed(IHand hand)
    {
        if (hand == null)
            return false;

        // Si hay alguna pinza activa, no es puño
       /* if (hand.GetFingerIsPinching(HandFinger.Index) ||
            hand.GetFingerIsPinching(HandFinger.Middle) ||
            hand.GetFingerIsPinching(HandFinger.Ring) ||
            hand.GetFingerIsPinching(HandFinger.Pinky))
            return false;*/

        float indexStrength = hand.GetFingerPinchStrength(HandFinger.Index);
        float middleStrength = hand.GetFingerPinchStrength(HandFinger.Middle);
        float ringStrength = hand.GetFingerPinchStrength(HandFinger.Ring);
        float pinkyStrength = hand.GetFingerPinchStrength(HandFinger.Pinky);

        // Usar promedio: más tolerante que requerir TODOS > threshold
        float averageStrength = (indexStrength + middleStrength + ringStrength + pinkyStrength) / 4f;
        return averageStrength > 0.32f;
    }

    /// <summary>
    /// Detecta si el índice está extendido (pointing gesture)
    /// Verifica que el índice sea significativamente más relajado que los otros dedos
    /// para distinguir pointing de una palma abierta en reposo
    /// NO se activa si hay alguna pinza en progreso
    /// </summary>
    private bool IsPointing(IHand hand)
    {
        if (hand == null)
            return false;

        // Si hay alguna pinza activa, no es pointing
       /* if (hand.GetFingerIsPinching(HandFinger.Index) ||
            hand.GetFingerIsPinching(HandFinger.Middle) ||
            hand.GetFingerIsPinching(HandFinger.Ring) ||
            hand.GetFingerIsPinching(HandFinger.Pinky))
            return false;
*/
        // El índice debe estar extendido (baja fuerza de pinch)
        // y otros dedos DEBEN estar más cerrados para distinguir de palma abierta
        float indexPinch = hand.GetFingerPinchStrength(HandFinger.Index);
        float middlePinch = hand.GetFingerPinchStrength(HandFinger.Middle);
        float ringPinch = hand.GetFingerPinchStrength(HandFinger.Ring);
        float pinkyPinch = hand.GetFingerPinchStrength(HandFinger.Pinky);

        // El índice debe estar extendido (bajo)
        bool indexExtended = indexPinch < pointingFingerRelaxThreshold;
        
        // Los otros dedos DEBEN estar significativamente más cerrados que el índice
        // Esto crea el patrón único de "pointing": índice extendido + otros dedos curvados
        bool otherFingersCurled = (middlePinch > indexPinch + 0.25f) ||
                                   (ringPinch > indexPinch + 0.25f);
        
        // Validar que el pointer pose es válido
        bool hasValidPointerPose = hand.GetPointerPose(out _);

        return indexExtended && otherFingersCurled && hasValidPointerPose;
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
