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
        Paint[] allPaints = FindObjectsOfType<Paint>();

        if (index >= 0 && index <= 15)
        {
            PaintColorType selectedColor = (PaintColorType)index;
            Paint.globalColor = selectedColor;
            
            foreach (var p in allPaints)
            {
                p.SetColor(selectedColor);
            }
            Debug.Log($"Color aplicado a todos los Paint: {selectedColor}");
        }
        else if (index >= 16 && index <= 19)
        {
            BrushThickness selectedThickness = BrushThickness.Medio;
            
            switch (index)
            {
                case 16: selectedThickness = BrushThickness.Fino; break;
                case 17: selectedThickness = BrushThickness.Medio; break;
                case 18: selectedThickness = BrushThickness.Grueso; break;
                case 19: selectedThickness = BrushThickness.ExtraGrueso; break;
            }

            Paint.globalThickness = selectedThickness;

            foreach (var p in allPaints)
            {
                p.SetThickness(selectedThickness);
            }
            Debug.Log($"Grosor aplicado a todos los Paint: {selectedThickness}");
        }
        else
        {
            Debug.LogWarning($"Índice fuera de rango: {index}");
        }

        if (Menu.Instance != null)
        {
            Menu.Instance.CloseColorGrosorMenu();
        }
    }
}