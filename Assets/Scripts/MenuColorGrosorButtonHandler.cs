using UnityEngine;
using UnityEngine.UI;

public class MenuColorGrosorButtonHandler : MonoBehaviour
{
    private bool listenersInitialized;

    private void OnEnable()
    {
        InitializeButtonListeners();
        Debug.Log("MenuColorGrosorButtonHandler.OnEnable() llamado en: " + gameObject.name);
    }

    public void InitializeButtonListeners()
    {
        if (listenersInitialized)
        {
            return;
        }

        Button[] buttons = GetComponentsInChildren<Button>(true);
        Debug.Log("MenuColorGrosorButtonHandler: Encontrados " + buttons.Length + " botones en " + gameObject.name);

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            string buttonName = buttons[i].gameObject.name;
            Debug.Log("MenuColorGrosorButtonHandler: Agregando listener para botón " + i + ": " + buttonName);
            buttons[i].onClick.AddListener(() => OnRadialButtonClick(index, buttonName));
        }

        listenersInitialized = true;
    }

    private void OnRadialButtonClick(int index, string buttonName)
    {
        Debug.Log($"MenuColorGrosorButtonHandler: Pulsado botón {index} ({buttonName})");
        switch (index)
        {
            case 0:
                Debug.Log($"White: #F9FFFE");

                break;
            case 1:
                Debug.Log($"Light gray: #9D9D97");
                
                break;
            case 2:
                Debug.Log($"Gray: #474F52");
                
                break;
            case 3:
                Debug.Log($"Black: #1D1D21");
                
                break;
            case 4:
                Debug.Log($"Brown: #835432");
                
                break;
            case 5:
                Debug.Log($"Red: #B02E26");
                
                break;
            case 6:
                Debug.Log($"Orange: #F9801D");
                
                break;
            case 7:
                Debug.Log($"Yellow: #FED83D");
                
                break;
            case 8:
                Debug.Log($"Lime: #80C71F");
                
                break;
            case 9:
                Debug.Log($"Green: #5E7C16");
                
                break;
            case 10:
                Debug.Log($"Cyan: #169C9C");
                
                break;
            case 11:
                Debug.Log($"Light Blue: #3AB3DA");
                
                break;
            case 12:
                Debug.Log($"Blue: #3C44AA");
                
                break;
            case 13:
                Debug.Log($"Purple: #8932B8");
                
                break;
            case 14:
                Debug.Log($"Magenta: #C74EBD");
                
                break;
            case 15:
                Debug.Log($"Pink: #F38BAA");
                
                break;
            case 16:
                Debug.Log($"Grosor 1");
                
                break;
            case 17:
                Debug.Log($"Grosor 2");
                
                break;
            case 18:
                Debug.Log($"Grosor 3");
                
                break;
            case 19:
                Debug.Log($"Grosor 4");
                
                break;
            default:
                Debug.Log("No existe");
                break;
        }
    }
}