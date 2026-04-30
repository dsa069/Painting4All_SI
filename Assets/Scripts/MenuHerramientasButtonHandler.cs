using UnityEngine;
using UnityEngine.UI;

public class MenuHerramientasButtonHandler : MonoBehaviour
{
    private bool listenersInitialized;

    private void OnEnable()
    {
        InitializeButtonListeners();
        Debug.Log("MenuHerramientasButtonHandler.OnEnable() llamado en: " + gameObject.name);
    }

    public void InitializeButtonListeners()
    {
        if (listenersInitialized)
        {
            return;
        }

        Button[] buttons = GetComponentsInChildren<Button>(true);
        Debug.Log("MenuHerramientasButtonHandler: Encontrados " + buttons.Length + " botones en " + gameObject.name);
        
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            string buttonName = buttons[i].gameObject.name;
            Debug.Log("MenuHerramientasButtonHandler: Agregando listener para botón " + i + ": " + buttonName);
            buttons[i].onClick.AddListener(() => OnRadialButtonClick(index, buttonName));
        }

        listenersInitialized = true;
    }

    private void OnRadialButtonClick(int index, string buttonName)
    {
        Paint paintManager = FindObjectOfType<Paint>();
        if (paintManager == null)
        {
            Debug.LogWarning("No se encontró el script Paint en la escena.");
        }

        switch (index)
        {
            case 0:
                Debug.Log($"Has pulsado el boton de Pincel");
                if (paintManager != null) paintManager.SetTool(ToolType.Pincel);
                break;
            case 1:
                Debug.Log($"Has pulsado el boton de Graffiti");
                if (paintManager != null) paintManager.SetTool(ToolType.Graffiti);
                break;
            case 2:
                Debug.Log($"Has pulsado el boton de Acuarela");
                if (paintManager != null) paintManager.SetTool(ToolType.Acuarela);
                break;
            case 3:
                Debug.Log($"Has pulsado el boton de Goma");
                if (paintManager != null) paintManager.SetTool(ToolType.Goma);
                break;
            case 4:
                Debug.Log($"Has pulsado el boton de Mano");
                if (paintManager != null) paintManager.SetTool(ToolType.Mano);
                break;
            default:
                Debug.Log("No encontrado");
                break;
        }
    }
}
