using UnityEngine;
using UnityEngine.UI;

public class MenuEntornoButtonHandler : MonoBehaviour
{
    private bool listenersInitialized;

    private void OnEnable()
    {
        InitializeButtonListeners();
        Debug.Log("MenuEntornoButtonHandler.OnEnable() llamado en: " + gameObject.name);
    }

    public void InitializeButtonListeners()
    {
        if (listenersInitialized)
        {
            return;
        }

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
        switch (index)
        {
            case 0:
                Debug.Log("Has pulsado el boton de entorno 0");
                break;
            case 1:
                Debug.Log("Has pulsado el boton de entorno 1");
                break;
            case 2:
                Debug.Log("Has pulsado el boton de entorno 2");
                break;
            case 3:
                Debug.Log("Has pulsado el boton de entorno 3");
                break;
            case 4:
                Debug.Log("Has pulsado el boton de entorno 4");
                break;
            case 5:
                Debug.Log("Has pulsado el boton de entorno 5");
                break;
            default:
                Debug.Log("Boton de entorno no mapeado: " + index);
                break;
        }
    }
}