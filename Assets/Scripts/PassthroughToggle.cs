using UnityEngine;
using Meta.XR.MRUtilityKit;

public class PassthroughToggler : MonoBehaviour
{
    [Header("Referencias Necesarias")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GameObject locomotionSystem;

    private void Start() => ToggleMR(false);

    public void ToggleMR(bool enableMR)
    {
        passthroughLayer.enabled = enableMR;
        locomotionSystem.SetActive(!enableMR);
        MRUK.Instance.EnableWorldLock = enableMR;
        mainCamera.clearFlags = enableMR ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
        if (enableMR) MRUK.Instance.LoadSceneFromDevice();
        foreach (var em in Resources.FindObjectsOfTypeAll<EffectMesh>())
        {
            if (em.gameObject.scene.name != null) em.gameObject.SetActive(enableMR);
        }
        if (MRUK.Instance?.GetCurrentRoom() != null) MRUK.Instance.GetCurrentRoom().gameObject.SetActive(enableMR);
        Debug.Log("Modo MR: " + (enableMR ? "ON" : "OFF"));
    }
}