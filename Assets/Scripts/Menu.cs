using UnityEngine;

public class Menu : MonoBehaviour
{
	[Header("Menu Reference")]
	[SerializeField]
	private GameObject menuGeneral;

	[SerializeField]
	private bool hideMenuOnStart = true;

	[Header("XR Placement")]
	[SerializeField]
	private float menuDistance = 1.6f;

	[SerializeField]
	private float menuVerticalOffset = -0.05f;

	[SerializeField]
	private Vector3 instantiatedMenuScale = new Vector3(0.015f, 0.015f, 0.015f);

	private GameObject menuGeneralInstance;

	private void Start()
	{
		ResolveMenuReference();

		if (menuGeneralInstance == null)
		{
			Debug.LogWarning("Menu: no se pudo resolver Menu_General al iniciar.");
			return;
		}

		if (hideMenuOnStart)
		{
			menuGeneralInstance.SetActive(false);
			Debug.Log("Menu: Menu_General oculto al iniciar.");
		}
		else
		{
			PositionMenuInFrontOfUser();
		}

		Debug.Log("Menu: Menu_General listo para recibir input.");
	}

	private void Update()
	{
		bool xPressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch); // X
		bool aPressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch); // A

		if (xPressed || aPressed)
		{
			Debug.Log($"Menu: input detectado -> X={xPressed}, A={aPressed}");
			ToggleMenu();
		}
	}

	private void ToggleMenu()
	{
		if (menuGeneralInstance == null)
		{
			ResolveMenuReference();
		}

		if (menuGeneralInstance == null)
		{
			Debug.LogWarning("Menu: no se puede alternar Menu_General porque no existe referencia ni prefab.");
			return;
		}

		bool newState = !menuGeneralInstance.activeSelf;
		menuGeneralInstance.SetActive(newState);

		if (newState)
		{
			PositionMenuInFrontOfUser();
		}

		Debug.Log(newState
			? "Menu: Toggle -> Menu_General abierto."
			: "Menu: Toggle -> Menu_General cerrado.");
	}

	private void ResolveMenuReference()
	{
		if (menuGeneral != null)
		{
			menuGeneralInstance = menuGeneral;
			return;
		}

		GameObject menuPrefab = Resources.Load<GameObject>("Prefabs/Menu_General");
		if (menuPrefab == null)
		{
			return;
		}

		menuGeneralInstance = Instantiate(menuPrefab);
		menuGeneralInstance.name = "Menu_General";
		menuGeneralInstance.transform.localScale = instantiatedMenuScale;
		menuGeneralInstance.SetActive(false);
	}

	private void PositionMenuInFrontOfUser()
	{
		if (menuGeneralInstance == null)
		{
			return;
		}

		Camera mainCamera = Camera.main;
		if (mainCamera == null)
		{
			Debug.LogWarning("Menu: Camera.main no está disponible para posicionar el menú.");
			return;
		}

		Transform menuTransform = menuGeneralInstance.transform;
		Vector3 targetPosition = mainCamera.transform.position + mainCamera.transform.forward * menuDistance + mainCamera.transform.up * menuVerticalOffset;
		menuTransform.position = targetPosition;

		Vector3 directionToCamera = mainCamera.transform.position - targetPosition;
		directionToCamera.y = 0f;

		if (directionToCamera.sqrMagnitude > 0.001f)
		{
			menuTransform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
		}
	}
}
