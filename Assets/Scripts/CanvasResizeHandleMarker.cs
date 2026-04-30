using UnityEngine;

public class CanvasResizeHandleMarker : MonoBehaviour
{
    public CanvasResizeHandleKind Kind { get; private set; }
    
    // Tamaño fijo que queremos mantener en el mundo mundial, sin importar el padre
    private Vector3 initialWorldScale;

    private void Start()
    {
        // Guardamos su escala global inicial
        initialWorldScale = transform.lossyScale;
    }

    public void Initialize(CanvasResizeHandleKind kind)
    {
        Kind = kind;
    }

    private void LateUpdate()
    {
        // Contrarrestar la escala del padre para mantener un tamaño físico constante.
        // Esto asegura que el Collider del borde siempre sea lo suficientemente 
        // grueso para que el Raycast del usuario lo detecte.
        if (transform.parent != null)
        {
            Vector3 parentScale = transform.parent.lossyScale;
            transform.localScale = new Vector3(
                initialWorldScale.x / parentScale.x,
                initialWorldScale.y / parentScale.y,
                initialWorldScale.z / parentScale.z
            );
        }
    }
}