using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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

	[SerializeField]
	private float menuHeightAboveController = 0.18f;

	[SerializeField]
	private float menuForwardOffsetFromController = 0.08f;

	private GameObject menuGeneralInstance;
	private OVRInput.Controller lastMenuController = OVRInput.Controller.None;
	private GraphicRaycaster menuGraphicRaycaster;
	private EventSystem eventSystem;
	private bool wasTriggerPressed = false;

	private void Start()
	{
		ResolveMenuReference();
		InitializeMenuGraphics();

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

		if (xPressed)
		{
			lastMenuController = OVRInput.Controller.LTouch;
			Debug.Log("Menu: input detectado -> X=true, A=false");
			ToggleMenu();
		}
		else if (aPressed)
		{
			lastMenuController = OVRInput.Controller.RTouch;
			Debug.Log("Menu: input detectado -> X=false, A=true");
			ToggleMenu();
		}

		HandleMenuTriggerInteraction();
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
			RefreshMenuReferences();
			if (!PositionMenuAboveOpeningController())
			{
				PositionMenuInFrontOfUser();
			}
		}
		else
		{
			wasTriggerPressed = false;
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

	private void RefreshMenuReferences()
	{
		if (menuGeneralInstance == null)
		{
			return;
		}

		menuGraphicRaycaster = menuGeneralInstance.GetComponentInChildren<GraphicRaycaster>();
		eventSystem = EventSystem.current ?? FindObjectOfType<EventSystem>();

		if (eventSystem == null)
		{
			Debug.LogWarning("Menu: no se encontró EventSystem en la escena.");
		}

		if (menuGeneralInstance.GetComponent<MenuButtonHandler>() == null)
		{
			menuGeneralInstance.AddComponent<MenuButtonHandler>();
		}
	}

	private void HandleMenuTriggerInteraction()
	{
		if (menuGeneralInstance == null || !menuGeneralInstance.activeSelf)
		{
			return;
		}

		OVRInput.Controller interactController = GetOppositeController(lastMenuController);
		if (interactController == OVRInput.Controller.None)
		{
			return;
		}

		bool triggerPressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, interactController);
		if (triggerPressed && !wasTriggerPressed)
		{
			OnMenuTriggerPressed(interactController);
		}

		wasTriggerPressed = triggerPressed;
	}

	private void OnMenuTriggerPressed(OVRInput.Controller interactController)
	{
		if (menuGraphicRaycaster == null)
		{
			RefreshMenuReferences();
		}

		if (menuGraphicRaycaster == null || eventSystem == null)
		{
			Debug.LogWarning("Menu: falta GraphicRaycaster o EventSystem para interactuar con la UI.");
			return;
		}

		if (!TryGetControllerWorldPose(interactController, out Vector3 controllerPosition, out Quaternion controllerRotation))
		{
			Debug.LogWarning("Menu: no se pudo obtener la pose del mando interactivo.");
			return;
		}

		Ray ray = new Ray(controllerPosition, controllerRotation * Vector3.forward);
		if (RaycastMenuButton(ray, out Button hitButton))
		{
			hitButton.onClick.Invoke();
			Debug.Log($"Menu: trigger del {interactController} activó {hitButton.gameObject.name}.");
		}
	}

	private bool RaycastMenuButton(Ray ray, out Button hitButton)
	{
		hitButton = null;
		Camera mainCamera = Camera.main;
		if (mainCamera == null)
		{
			return false;
		}

		RectTransform menuRect = menuGeneralInstance.GetComponent<RectTransform>();
		if (menuRect == null)
		{
			return false;
		}

		Plane menuPlane = new Plane(menuRect.forward, menuRect.position);
		if (!menuPlane.Raycast(ray, out float enter))
		{
			return false;
		}

		Vector3 worldHit = ray.GetPoint(enter);
		Vector3 screenPoint = mainCamera.WorldToScreenPoint(worldHit);

		PointerEventData pointerData = new PointerEventData(eventSystem)
		{
			position = screenPoint
		};

		System.Collections.Generic.List<RaycastResult> results = new System.Collections.Generic.List<RaycastResult>();
		menuGraphicRaycaster.Raycast(pointerData, results);

		foreach (RaycastResult result in results)
		{
			if (result.gameObject == null)
			{
				continue;
			}

			if (result.gameObject.TryGetComponent<Button>(out Button button))
			{
				hitButton = button;
				return true;
			}
		}

		return false;
	}

	private OVRInput.Controller GetOppositeController(OVRInput.Controller controller)
	{
		if (controller == OVRInput.Controller.LTouch)
		{
			return OVRInput.Controller.RTouch;
		}

		if (controller == OVRInput.Controller.RTouch)
		{
			return OVRInput.Controller.LTouch;
		}

		return OVRInput.Controller.None;
	}

	private bool PositionMenuAboveOpeningController()
	{
		if (menuGeneralInstance == null)
		{
			return false;
		}

		if (lastMenuController != OVRInput.Controller.LTouch && lastMenuController != OVRInput.Controller.RTouch)
		{
			return false;
		}

		if (!TryGetControllerWorldPose(lastMenuController, out Vector3 controllerPosition, out Quaternion _))
		{
			Debug.LogWarning("Menu: no se pudo obtener la pose del mando, usando posicion frente al usuario.");
			return false;
		}

		Transform menuTransform = menuGeneralInstance.transform;
		Vector3 targetPosition = controllerPosition + Vector3.up * menuHeightAboveController;

		Camera mainCamera = Camera.main;
		if (mainCamera != null)
		{
			targetPosition += mainCamera.transform.forward * menuForwardOffsetFromController;
		}

		menuTransform.position = targetPosition;

		if (mainCamera != null)
		{
			Vector3 directionToCamera = mainCamera.transform.position - targetPosition;
			directionToCamera.y = 0f;

			if (directionToCamera.sqrMagnitude > 0.001f)
			{
				menuTransform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
			}
		}

		return true;
	}

	private bool TryGetControllerWorldPose(OVRInput.Controller controller, out Vector3 worldPosition, out Quaternion worldRotation)
	{
		OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();

		if (cameraRig != null)
		{
			Transform anchor = controller == OVRInput.Controller.LTouch ? cameraRig.leftHandAnchor : cameraRig.rightHandAnchor;
			if (anchor != null)
			{
				worldPosition = anchor.position;
				worldRotation = anchor.rotation;
				return true;
			}
		}

		Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
		Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);

		Transform trackingSpace = cameraRig != null ? cameraRig.trackingSpace : null;
		if (trackingSpace != null)
		{
			worldPosition = trackingSpace.TransformPoint(localPosition);
			worldRotation = trackingSpace.rotation * localRotation;
			return true;
		}

		Camera mainCamera = Camera.main;
		if (mainCamera != null)
		{
			worldPosition = mainCamera.transform.TransformPoint(localPosition);
			worldRotation = mainCamera.transform.rotation * localRotation;
			return true;
		}

		worldPosition = Vector3.zero;
		worldRotation = Quaternion.identity;
		return false;
	}
}
