using UnityEngine;

public class PassthroughToggler : MonoBehaviour
{
    [Header("Referencias Necesarias")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Camera mainCamera;
    public bool IsMRActive { get; private set; } = false;
    private void Start()
    {
        ToggleMR(false);
    }
    public void ToggleMR(bool enableMR)
    {
        IsMRActive = enableMR;
        if (passthroughLayer != null)
        {
            passthroughLayer.enabled = enableMR;
        }
        if (mainCamera != null)
        {
            if (enableMR)
            {
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = new Color(0, 0, 0, 0);
            }
            else
            {
                mainCamera.clearFlags = CameraClearFlags.Skybox;
            }
        }
        Debug.Log("Modo MR cambiado a: " + (enableMR ? "ON" : "OFF"));
    }
}