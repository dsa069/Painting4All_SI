using UnityEngine;

public class PassthroughToggler : MonoBehaviour
{
    [Header("Referencias Necesarias")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GestureUIController gestureController;

    private bool isMRActive = false;
    private bool wasT1LeftActiveLastFrame = false;
    private bool wasT1RightActiveLastFrame = false;

    private void Start()
    {
        if (gestureController == null)
        {
            gestureController = FindObjectOfType<GestureUIController>();
        }
        ToggleMR(false);
    }

    private void Update()
    {
        if (gestureController == null) return;
        bool t1LeftNow = gestureController.IsT1ActiveLeft;
        bool t1RightNow = gestureController.IsT1ActiveRight;
        bool t1LeftTriggered = t1LeftNow && !wasT1LeftActiveLastFrame;
        bool t1RightTriggered = t1RightNow && !wasT1RightActiveLastFrame;
        if (t1LeftTriggered || t1RightTriggered)
        {
            ToggleMR(!isMRActive);
        }
        wasT1LeftActiveLastFrame = t1LeftNow;
        wasT1RightActiveLastFrame = t1RightNow;
    }

    public void ToggleMR(bool enableMR)
    {
        isMRActive = enableMR;
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