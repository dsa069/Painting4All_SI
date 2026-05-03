using UnityEngine;
using Meta.XR.MRUtilityKit;

public class PassthroughToggler : MonoBehaviour
{
    [Header("Referencias Necesarias")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GameObject locomotionSystem;

    public bool IsMRActive { get; private set; } = false;

    private void Awake()
    {
        UnityEngine.XR.XRSettings.useOcclusionMesh = false;
    }

    private void Start()
    {
        ToggleMR(false);
    }

    public void ToggleMR(bool enableMR)
    {
        IsMRActive = enableMR;

        if (passthroughLayer != null) passthroughLayer.enabled = enableMR;

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

        if (locomotionSystem != null) locomotionSystem.SetActive(!enableMR);

        if (MRUK.Instance != null)
        {
            MRUK.Instance.EnableWorldLock = enableMR;
            if (enableMR && MRUK.Instance.GetCurrentRoom() == null)
            {
                MRUK.Instance.LoadSceneFromDevice();
            }
        }

        ToggleAllEffectMeshes(enableMR);
        Debug.Log("Modo MR: " + (enableMR ? "ON (Bloqueado & Locomotion OFF)" : "OFF (Libre & Locomotion ON)"));
    }

    private void ToggleAllEffectMeshes(bool visible)
    {
        EffectMesh[] allMeshes = Resources.FindObjectsOfTypeAll<EffectMesh>();
        foreach (EffectMesh em in allMeshes)
        {
            if (em.gameObject.scene.name != null)
            {
                em.enabled = visible;
                MeshRenderer[] renderers = em.GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer mr in renderers)
                {
                    mr.enabled = visible;
                }
            }
        }

        if (MRUK.Instance != null && MRUK.Instance.GetCurrentRoom() != null)
        {
            GameObject roomObj = MRUK.Instance.GetCurrentRoom().gameObject;
            MeshRenderer[] roomRenderers = roomObj.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer r in roomRenderers)
            {
                r.enabled = visible;
            }
        }
    }
}