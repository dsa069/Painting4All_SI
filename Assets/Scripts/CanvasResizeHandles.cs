using UnityEngine;

public enum CanvasResizeHandleKind
{
    CornerTopLeft,
    CornerTopRight,
    CornerBottomLeft,
    CornerBottomRight,
    EdgeTop,
    EdgeBottom,
    EdgeLeft,
    EdgeRight
}

public class CanvasResizeHandleMarker : MonoBehaviour
{
    public CanvasResizeHandleKind Kind { get; private set; }

    public void Initialize(CanvasResizeHandleKind kind)
    {
        Kind = kind;
    }
}
