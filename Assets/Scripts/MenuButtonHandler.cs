using UnityEngine;
using UnityEngine.UI;

public class MenuButtonHandler : MonoBehaviour
{
	private void Start()
	{
		if (gameObject.name == "Menu_Entornos")
		{
			Debug.Log("MenuButtonHandler: Saltando inicializacion para Menu_Entornos (usa MenuEntornoButtonHandler)");
			return;
		}

		Button[] buttons = GetComponentsInChildren<Button>(true);
		for (int i = 0; i < buttons.Length; i++)
		{
			int index = i;
			string buttonName = buttons[i].gameObject.name;
			buttons[i].onClick.AddListener(() => OnRadialButtonClick(index, buttonName));
		}
		
		Debug.Log("MenuButtonHandler: Inicializado para " + gameObject.name + " con " + buttons.Length + " botones");
	}

	private void OnRadialButtonClick(int index, string buttonName)
	{
		switch (index)
		{
			case 0:
				Debug.Log("Has pulsado el boton de Pincel");
				// TODO: Implementar logica aqui
				
				break;
			case 1:
				Debug.Log("Has pulsado el boton de Paleta");
				// TODO: Implementar logica aqui

				break;
			case 2:
				Debug.Log("Has pulsado el boton de Casa");
				if (Menu.Instance != null)
				{
					Menu.Instance.OpenEntornoMenu(Menu.Instance.MenuGeneralPosition, Menu.Instance.MenuGeneralRotation);
				}

				break;
			case 3:
				Debug.Log("Has pulsado el boton de Mano");
				// TODO: Implementar logica aqui

				break;
			case 4:
				Debug.Log("Has pulsado el boton de Salir");
				// TODO: Implementar logica aqui

				break;
			default:
				Debug.Log("No encontrado");
				break;
		}
	}
}
