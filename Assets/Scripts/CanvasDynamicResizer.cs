using UnityEngine;

public class CanvasDynamicResizer : MonoBehaviour
{
    [Header("Configuración")]
    public float resizeSensitivity = 1.0f;
    public Vector3 minScale = new Vector3(0.5f, 0.5f, 0.5f);
    public Vector3 maxScale = new Vector3(3.0f, 3.0f, 3.0f);

    [Header("Detección de bordes")]
    [SerializeField] private float raycastMaxDistance = 100f;
    [SerializeField] private Transform leftControllerTransform;
    [SerializeField] private Transform rightControllerTransform;

    [Header("Estado Interno (Solo lectura)")]
    public bool isHeldByHandA = false;
    public bool isResizing = false;

    // Referencias para el cálculo
    private Vector3 initialHandPosition;
    private Vector3 initialCanvasScale;
    private OVRInput.Controller handAController;
    private OVRInput.Controller handBController;

    // Tipo de borde interactuado
    private enum ResizeAxis { None, Horizontal, Vertical, Proportional }
    private ResizeAxis currentResizeAxis = ResizeAxis.None;

    private bool triedAutoDetectControllers;

    private void Awake()
    {
        TryAutoDetectControllers();
    }

    void Update()
    {
        HandleHoldLogic();

        // RESTRICCIÓN: Solo si está sujeto por la Mano A, evaluamos la Mano B
        if (isHeldByHandA)
        {
            HandleResizeLogic();
        }
        else
        {
            // Si no está sujeto, nos aseguramos de cancelar cualquier redimensión activa
            if (isResizing) StopResizing();
        }
    }

    private void HandleHoldLogic()
    {
        // Simplificación: Detectamos si ALGUN mando está pulsando el Grip (Gatillo lateral)
        bool leftGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool rightGrip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);

        if (!isHeldByHandA && (leftGrip || rightGrip))
        {
            isHeldByHandA = true;
            handAController = leftGrip ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            handBController = leftGrip ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        }
        else if (isHeldByHandA && !OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, handAController))
        {
            isHeldByHandA = false;
        }
    }

    private void HandleResizeLogic()
    {
        // Detectar si la Mano B pulsa el gatillo frontal (Index)
        bool handBIndex = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, handBController);

        if (handBIndex && !isResizing)
        {
            // AQUÍ: Lógica de detección del borde (Raycast o Collider overlap)
            // Supongamos que tienes un método que te devuelve qué borde estás tocando
            GameObject touchedEdge = CheckIfHandBIsTouchingEdge(); 

            if (touchedEdge != null)
            {
                StartResizing(touchedEdge);
            }
        }
        else if (!handBIndex && isResizing)
        {
            StopResizing();
        }

        if (isResizing)
        {
            ApplyResize();
        }
    }

    private void StartResizing(GameObject edge)
    {
        isResizing = true;
        initialHandPosition = OVRInput.GetLocalControllerPosition(handBController);
        initialCanvasScale = transform.localScale;

        // Determinar cómo escalar según el collider tocado (Esquina vs Borde)
        CanvasResizeHandleMarker marker = edge.GetComponent<CanvasResizeHandleMarker>();
        if (marker != null)
        {
            switch (marker.Kind)
            {
                case CanvasResizeHandleKind.EdgeLeft:
                case CanvasResizeHandleKind.EdgeRight:
                    currentResizeAxis = ResizeAxis.Horizontal;
                    break;
                case CanvasResizeHandleKind.EdgeTop:
                case CanvasResizeHandleKind.EdgeBottom:
                    currentResizeAxis = ResizeAxis.Vertical;
                    break;
                default:
                    currentResizeAxis = ResizeAxis.Proportional;
                    break;
            }
        }
        else
        {
            if (edge.CompareTag("EdgeX")) currentResizeAxis = ResizeAxis.Horizontal;
            else if (edge.CompareTag("EdgeY")) currentResizeAxis = ResizeAxis.Vertical;
            else if (edge.CompareTag("Corner")) currentResizeAxis = ResizeAxis.Proportional;
        }
    }

    private void StopResizing()
    {
        isResizing = false;
        currentResizeAxis = ResizeAxis.None;
    }

    private void ApplyResize()
    {
        Vector3 currentHandPos = OVRInput.GetLocalControllerPosition(handBController);
        
        // 1. Calcular el vector de movimiento de la mano
        Vector3 handMovement = currentHandPos - initialHandPosition;

        // 2. Convertir el movimiento del mundo al espacio local del canvas
        // Esto es VITAL: Si el canvas está rotado, mover la mano en Z del mundo 
        // no significa hacer el canvas más grande.
        Vector3 localMovement = transform.InverseTransformDirection(handMovement);

        Vector3 newScale = initialCanvasScale;

        // 3. Aplicar la matemática según el eje
        switch (currentResizeAxis)
        {
            case ResizeAxis.Horizontal:
                newScale.x += localMovement.x * resizeSensitivity;
                break;
            case ResizeAxis.Vertical:
                newScale.y += localMovement.y * resizeSensitivity;
                break;
            case ResizeAxis.Proportional:
                // Usamos la magnitud o el promedio del movimiento para escalar uniformemente
                float uniformScale = (localMovement.x + localMovement.y) * resizeSensitivity;
                newScale += new Vector3(uniformScale, uniformScale, uniformScale);
                break;
        }

        // 4. Aplicar límites de seguridad (Clamp) para que no se invierta o crezca infinitamente
        newScale.x = Mathf.Clamp(newScale.x, minScale.x, maxScale.x);
        newScale.y = Mathf.Clamp(newScale.y, minScale.y, maxScale.y);
        newScale.z = Mathf.Clamp(newScale.z, minScale.z, maxScale.z);

        // 5. Asignar la nueva escala
        transform.localScale = newScale;
    }

    // Método simulado: Sustituye esto por tu sistema actual de detección (Triggers o Raycast)
    private GameObject CheckIfHandBIsTouchingEdge()
    {
        Transform controller = GetControllerTransform(handBController);
        if (controller == null)
        {
            TryAutoDetectControllers();
            controller = GetControllerTransform(handBController);
            if (controller == null)
            {
                return null;
            }
        }

        Ray ray = new Ray(controller.position, controller.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, raycastMaxDistance, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            return null;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            CanvasResizeHandleMarker marker = hits[i].collider.GetComponent<CanvasResizeHandleMarker>();
            if (marker != null)
            {
                return marker.gameObject;
            }

            if (hits[i].collider.CompareTag("EdgeX") || hits[i].collider.CompareTag("EdgeY") || hits[i].collider.CompareTag("Corner"))
            {
                return hits[i].collider.gameObject;
            }
        }

        return null;
    }

    private Transform GetControllerTransform(OVRInput.Controller controller)
    {
        if ((controller & OVRInput.Controller.LTouch) != 0 || (controller & OVRInput.Controller.LHand) != 0)
        {
            return leftControllerTransform;
        }

        if ((controller & OVRInput.Controller.RTouch) != 0 || (controller & OVRInput.Controller.RHand) != 0)
        {
            return rightControllerTransform;
        }

        return null;
    }

    private void TryAutoDetectControllers()
    {
        if (triedAutoDetectControllers)
        {
            return;
        }

        triedAutoDetectControllers = true;
        if (leftControllerTransform != null && rightControllerTransform != null)
        {
            return;
        }

        Transform[] allObjects = FindObjectsOfType<Transform>();
        foreach (Transform t in allObjects)
        {
            if (leftControllerTransform == null && (t.name == "LeftControllerAnchor" || t.name == "LeftHand" || t.name == "LeftHandAnchor"))
            {
                leftControllerTransform = t;
            }

            if (rightControllerTransform == null && (t.name == "RightControllerAnchor" || t.name == "RightHand" || t.name == "RightHandAnchor"))
            {
                rightControllerTransform = t;
            }

            if (leftControllerTransform != null && rightControllerTransform != null)
            {
                return;
            }
        }
    }
}