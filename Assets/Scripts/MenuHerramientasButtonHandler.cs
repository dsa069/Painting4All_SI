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
        switch (index)
        {
            case 0:
                Debug.Log($"Has pulsado el boton de Pincel");

                break;
            case 1:
                Debug.Log($"Has pulsado el boton de Graffiti");

                break;
            case 2:
                Debug.Log($"Has pulsado el boton de Acuarela");

                break;
            case 3:
                Debug.Log($"Has pulsado el boton de Goma");

                break;
            case 4:
                Debug.Log($"Has pulsado el boton de Mano");

                break;
            default:
                Debug.Log("No encontrado");
                break;
        }
    }
}
