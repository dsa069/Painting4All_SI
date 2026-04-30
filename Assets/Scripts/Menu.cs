using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public class Menu : MonoBehaviour
{
	[Header("Menu Reference")]
	[SerializeField]
	private GameObject menuGeneral;

	[Header("Entorno Menu Reference")]
	[SerializeField]
	private GameObject menuEntornoPrefab;

	[Header("Herramientas Menu Reference")]
	[SerializeField]
	private GameObject menuHerramientasPrefab;

	[SerializeField]
	private bool hideMenuOnStart = true;

	private static Menu instance;
	private GameObject menuGeneralInstance;
	private GameObject menuEntornoInstance;
	private GameObject menuHerramientasInstance;

	public GameObject ActiveMenuInstance
	{
		get
		{
			// Returns the currently active menu instance
			if (menuGeneralInstance != null && menuGeneralInstance.activeSelf)
				return menuGeneralInstance;
			if (menuEntornoInstance != null && menuEntornoInstance.activeSelf)
				return menuEntornoInstance;
			if (menuHerramientasInstance != null && menuHerramientasInstance.activeSelf)
				return menuHerramientasInstance;
			return null;
		}
	}

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

	private GestureUIController gestureController;
    private bool wasB2LeftActiveLastFrame = false;
    private bool wasB2RightActiveLastFrame = false;

	private OVRInput.Controller lastMenuController = OVRInput.Controller.None;
	private GraphicRaycaster menuGraphicRaycaster;
	private EventSystem eventSystem;
	private bool wasTriggerPressed = false;

	private void Start()
    {
		instance = this;
        gestureController = FindObjectOfType<GestureUIController>();
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

	public static Menu Instance => instance;

	public Vector3 MenuGeneralPosition => menuGeneralInstance != null ? menuGeneralInstance.transform.position : Vector3.zero;
	public Quaternion MenuGeneralRotation => menuGeneralInstance != null ? menuGeneralInstance.transform.rotation : Quaternion.identity;

	public void OpenEntornoMenu(Vector3 position, Quaternion rotation)
	{
		if (menuGeneralInstance != null)
		{
			menuGeneralInstance.SetActive(false);
		}

		if (menuEntornoInstance != null)
		{
			Destroy(menuEntornoInstance);
		}

		if (menuEntornoPrefab == null)
		{
			menuEntornoPrefab = Resources.Load<GameObject>("Prefabs/Menu_Entornos");
		}

		if (menuEntornoPrefab == null)
		{
			Debug.LogWarning("Menu: no se pudo cargar Menu_Entornos prefab.");
			return;
		}

		menuEntornoInstance = Instantiate(menuEntornoPrefab);
		menuEntornoInstance.name = "Menu_Entornos";

		Vector3 forwardOffset = Vector3.zero;
		if (Camera.main != null)
		{
			forwardOffset = Camera.main.transform.forward * 2f;
		}
		menuEntornoInstance.transform.position = position + forwardOffset;
		menuEntornoInstance.transform.rotation = rotation;
		menuEntornoInstance.transform.localScale = new Vector3(0.009f, 0.009f, 0.009f);
		menuEntornoInstance.SetActive(true);

		menuEntornoInstance.AddComponent<MenuEntornoButtonHandler>();

		Debug.Log("Menu: Menu_Entornos abierto.");
	}

	public void CloseEntornoMenu()
	{
		if (menuEntornoInstance != null)
		{
			Destroy(menuEntornoInstance);
			menuEntornoInstance = null;
			Debug.Log("Menu: Menu_Entornos cerrado.");
		}
	}

	public void OpenHerramientasMenu(Vector3 position, Quaternion rotation)
	{
		if (menuGeneralInstance != null)
		{
			menuGeneralInstance.SetActive(false);
		}

		if (menuHerramientasInstance != null)
		{
			Destroy(menuHerramientasInstance);
		}

		if (menuHerramientasPrefab == null)
		{
			menuHerramientasPrefab = Resources.Load<GameObject>("Prefabs/Menu_Herramientas");
		}

		if (menuHerramientasPrefab == null)
		{
			Debug.LogWarning("Menu: no se pudo cargar Menu_Herramientas prefab.");
			return;
		}

		menuHerramientasInstance = Instantiate(menuHerramientasPrefab);
		menuHerramientasInstance.name = "Menu_Herramientas";

		Vector3 forwardOffset = Vector3.zero;
		if (Camera.main != null)
		{
			forwardOffset = Camera.main.transform.forward * 0.3f;
		}
		menuHerramientasInstance.transform.position = position + forwardOffset;
		menuHerramientasInstance.transform.rotation = rotation;
		menuHerramientasInstance.transform.localScale = new Vector3(0.009f, 0.009f, 0.009f);
		menuHerramientasInstance.SetActive(true);

		menuHerramientasInstance.AddComponent<MenuHerramientasButtonHandler>();

		Debug.Log("Menu: Menu_Herramientas abierto.");
	}

	public void CloseHerramientasMenu()
	{
		if (menuHerramientasInstance != null)
		{
			Destroy(menuHerramientasInstance);
			menuHerramientasInstance = null;
			Debug.Log("Menu: Menu_Herramientas cerrado.");
		}
	}

	private void Update()
    {
        bool xPressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch); // X
        bool aPressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch); // A

        bool b2LeftNow = (gestureController != null) && gestureController.IsB2ActiveLeft;
        bool b2RightNow = (gestureController != null) && gestureController.IsB2ActiveRight;

        if (xPressed || (b2LeftNow && !wasB2LeftActiveLastFrame))
        {
			Debug.Log("Menu: input detectado -> X=true, A=false");
            HandleMenuButtonPressed(OVRInput.Controller.LTouch);
        }
        else if (aPressed || (b2RightNow && !wasB2RightActiveLastFrame))
        {
			Debug.Log("Menu: input detectado -> X=false, A=true");
            HandleMenuButtonPressed(OVRInput.Controller.RTouch);
        }

        wasB2LeftActiveLastFrame = b2LeftNow;
        wasB2RightActiveLastFrame = b2RightNow;

        if (menuGeneralInstance != null && menuGeneralInstance.activeSelf)
        {
            PositionMenuAboveOpeningController();
        }

        HandleMenuTriggerInteraction();
    }

	private void HandleMenuButtonPressed(OVRInput.Controller pressedController)
	{

		CloseEntornoMenu();
		CloseHerramientasMenu();

		if (menuGeneralInstance != null &&
			menuGeneralInstance.activeSelf &&
			lastMenuController != OVRInput.Controller.None &&
			lastMenuController != pressedController)
		{
			lastMenuController = pressedController;
			MoveMenuToOpeningController();
			Debug.Log("Menu: movido al mando opuesto sin cerrar.");
			return;
		}

		lastMenuController = pressedController;
		ToggleMenu();
	}

	private void MoveMenuToOpeningController()
	{
		if (menuGeneralInstance == null)
		{
			return;
		}

		bool wasActive = menuGeneralInstance.activeSelf;
		if (wasActive)
		{
			menuGeneralInstance.SetActive(false);
		}

		if (!PositionMenuAboveOpeningController())
		{
			PositionMenuInFrontOfUser();
		}

		if (wasActive)
		{
			menuGeneralInstance.SetActive(true);
			RefreshMenuReferences();
		}

		wasTriggerPressed = false;
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

		Vector3 directionToCamera = targetPosition - mainCamera.transform.position;

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

		menuGraphicRaycaster = menuGeneralInstance.GetComponent<GraphicRaycaster>();
		if (menuGraphicRaycaster == null)
		{
			Debug.LogWarning("Menu: GraphicRaycaster no encontrado en Menu_General.");
		}

		eventSystem = EventSystem.current;
		if (eventSystem == null)
		{
			eventSystem = FindObjectOfType<EventSystem>();
		}

		if (eventSystem == null)
		{
			Debug.LogWarning("Menu: no se encontró EventSystem en la escena. Creando uno nuevo...");
			eventSystem = CreateEventSystem();
		}

		if (menuGeneralInstance.GetComponent<MenuButtonHandler>() == null)
		{
			menuGeneralInstance.AddComponent<MenuButtonHandler>();
		}
	}

	private EventSystem CreateEventSystem()
	{
		GameObject eventSystemGO = new GameObject("EventSystem");
		eventSystemGO.AddComponent<EventSystem>();
		#if ENABLE_INPUT_SYSTEM
		eventSystemGO.AddComponent<InputSystemUIInputModule>();
		#else
		eventSystemGO.AddComponent<StandaloneInputModule>();
		#endif
		return eventSystemGO.GetComponent<EventSystem>();
	}

	private void HandleMenuTriggerInteraction()
	{
		if (ActiveMenuInstance == null || !ActiveMenuInstance.activeSelf)
		{
			return;
		}

		bool triggerLeftPressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) || ((gestureController != null) && gestureController.IsT1ActiveLeft);
		bool triggerRightPressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) || ((gestureController != null) && gestureController.IsT1ActiveRight);

		if (triggerLeftPressed && !wasTriggerPressed)
		{
			OnMenuTriggerPressed(OVRInput.Controller.LTouch);
		}
		else if (triggerRightPressed && !wasTriggerPressed)
		{
			OnMenuTriggerPressed(OVRInput.Controller.RTouch);
		}

		wasTriggerPressed = triggerLeftPressed || triggerRightPressed;
	}

	private void OnMenuTriggerPressed(OVRInput.Controller interactController)
	{
		if (ActiveMenuInstance == null)
		{
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
		}
	}

	private bool RaycastMenuButton(Ray ray, out Button hitButton)
	{
		hitButton = null;

		if (ActiveMenuInstance == null)
		{
			return false;
		}

		RectTransform menuRect = ActiveMenuInstance.GetComponent<RectTransform>();
		if (menuRect == null)
		{
			return false;
		}

		Vector3 menuForward = menuRect.forward;
		Vector3 menuPosition = menuRect.position;
		float denom = Vector3.Dot(menuForward, ray.direction);

		if (Mathf.Abs(denom) > 1e-6f)
		{
			float t = Vector3.Dot(menuForward, menuPosition - ray.origin) / denom;
			if (t >= 0f)
			{
				Vector3 worldHit = ray.GetPoint(t);
				Vector2 localPoint;
				if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
					menuRect, worldHit, null, out localPoint))
				{
					Button[] buttons = ActiveMenuInstance.GetComponentsInChildren<Button>();
					foreach (Button button in buttons)
					{
						RectTransform buttonRect = button.GetComponent<RectTransform>();
						if (buttonRect == null)
						{
							continue;
						}

						Vector2 anchoredPosition = buttonRect.anchoredPosition;
						Vector2 size = buttonRect.sizeDelta;

						float halfWidth = size.x * 0.5f;
						float halfHeight = size.y * 0.5f;

						if (localPoint.x >= anchoredPosition.x - halfWidth &&
							localPoint.x <= anchoredPosition.x + halfWidth &&
							localPoint.y >= anchoredPosition.y - halfHeight &&
							localPoint.y <= anchoredPosition.y + halfHeight)
						{
							hitButton = button;
							return true;
						}
					}
				}
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
		Vector3 directionToCamera = targetPosition - mainCamera.transform.position;

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
