using UnityEngine;
using UnityEngine.UI;

public class MenuEntornoButtonHandler : MonoBehaviour
{
    [Header("Configuración de Carpeta")]
    [SerializeField] private GameObject carpetaEntornos;

    private bool listenersInitialized;

    private void OnEnable()
    {
        InitializeButtonListeners();
        Debug.Log("MenuEntornoButtonHandler.OnEnable() llamado en: " + gameObject.name);
    }

    public void InitializeButtonListeners()
    {
        if (listenersInitialized) return;

        Button[] buttons = GetComponentsInChildren<Button>(true);
        Debug.Log("MenuEntornoButtonHandler: Encontrados " + buttons.Length + " botones en " + gameObject.name);

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            string buttonName = buttons[i].gameObject.name;
            Debug.Log("MenuEntornoButtonHandler: Agregando listener para botón " + i + ": " + buttonName);
            buttons[i].onClick.AddListener(() => OnRadialButtonClick(index, buttonName));
        }

        listenersInitialized = true;
    }

    private void OnRadialButtonClick(int index, string buttonName)
    {
        if (Menu.Instance != null) Menu.Instance.CloseEntornoMenu();

        DayAndNight dayAndNight = FindObjectOfType<DayAndNight>();
        if (dayAndNight == null) Debug.LogWarning("MenuEntornoButtonHandler: No se encontró el script DayAndNight en la escena.");
        
        bool shouldEnableMR = (index == 5);
        PassthroughToggler toggler = FindObjectOfType<PassthroughToggler>();
        if (toggler != null) toggler.ToggleMR(shouldEnableMR);

        if (carpetaEntornos != null)
        {
            foreach (Transform hijo in carpetaEntornos.transform) 
            {
                hijo.gameObject.SetActive(false);
            }
        }

        Transform playerTransform = null;
        OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();
        if (cameraRig != null) playerTransform = cameraRig.transform;
        else if (Camera.main != null) playerTransform = Camera.main.transform.root;

        switch (index)
        {
            case 0: // Blanco / Sol
                Debug.Log("Has pulsado el boton de entorno Sol (Blanco)");
                if (carpetaEntornos != null) carpetaEntornos.transform.Find("Light").gameObject.SetActive(true);
                TeleportPlayer(playerTransform, new Vector3(6000, 0, 0));
                if (dayAndNight != null) dayAndNight.SetWhiteSuperNova();
                break;
            case 1: // Negro / Luna
                Debug.Log("Has pulsado el boton de entorno Luna (Negro)");
                if (carpetaEntornos != null) carpetaEntornos.transform.Find("Dark").gameObject.SetActive(true);
                TeleportPlayer(playerTransform, new Vector3(8000, 0, 0));
                if (dayAndNight != null) dayAndNight.SetDarkNight();
                break;
            case 2: // Bosque
                Debug.Log("Has pulsado el boton de entorno Bosque");
                if (carpetaEntornos != null) carpetaEntornos.transform.Find("Bosque").gameObject.SetActive(true);
                TeleportPlayer(playerTransform, new Vector3(50, 3, 50));
                if (dayAndNight != null) dayAndNight.SetForestDay();
                break;
            case 3: // Playa
                Debug.Log("Has pulsado el boton de entorno Playa");
                if (carpetaEntornos != null) carpetaEntornos.transform.Find("Playa").gameObject.SetActive(true);
                TeleportPlayer(playerTransform, new Vector3(2000, 3, 25));
                if (dayAndNight != null) dayAndNight.SetBeachSunset();
                break;
            case 4: // Carcel / Prision
                Debug.Log("Has pulsado el boton de entorno Carcel (Prision)");
                if (carpetaEntornos != null) carpetaEntornos.transform.Find("Prision").gameObject.SetActive(true);
                TeleportPlayer(playerTransform, new Vector3(4000, 0, 0));
                if (dayAndNight != null) dayAndNight.SetForestDay();
                break;
            case 5: // MR
                Debug.Log("Has pulsado el boton de entorno MR");
                break;
            default:
                Debug.Log("Boton de entorno no mapeado: " + index);
                break;
        }
    }

    private void TeleportPlayer(Transform playerTransform, Vector3 destination)
    {
        if (playerTransform != null)
        {
            playerTransform.position = destination;
            Debug.Log($"Jugador teletransportado a {destination}.");
        }
        else
        {
            Debug.LogWarning("MenuEntornoButtonHandler: No se encontró al jugador para el teletransporte (OVRCameraRig).");
        }
    }
}