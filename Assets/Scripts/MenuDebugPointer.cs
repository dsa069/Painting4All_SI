using UnityEngine;
using UnityEngine.UI;

public class MenuDebugPointer : MonoBehaviour
{
	[Header("Config")]
	[SerializeField]
	private float rayLength = 10f;

	[SerializeField]
	private OVRInput.Controller controller = OVRInput.Controller.RTouch;

	private LineRenderer lineRenderer;
	private Transform menuTarget;

	void Start()
	{
		lineRenderer = gameObject.AddComponent<LineRenderer>();
		lineRenderer.startWidth = 0.005f;
		lineRenderer.endWidth = 0.005f;
		lineRenderer.positionCount = 2;
		lineRenderer.useWorldSpace = true;

		Material material = new Material(Shader.Find("Sprites/Default"));
		material.color = Color.green;
		lineRenderer.material = material;

		Debug.Log("MenuDebugPointer: inicializado en " + gameObject.name);
	}

	void Update()
	{
		if (menuTarget == null || !menuTarget.gameObject.activeSelf)
		{
			menuTarget = GetMenuTarget();
			lineRenderer.enabled = menuTarget != null;
			return;
		}

		lineRenderer.enabled = true;

		OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();
		if (cameraRig == null)
		{
			return;
		}

		Transform anchor = controller == OVRInput.Controller.LTouch
			? cameraRig.leftHandAnchor
			: cameraRig.rightHandAnchor;

		if (anchor == null)
		{
			return;
		}

		Vector3 rayOrigin = anchor.position;
		Vector3 rayDirection = anchor.forward;
		Ray ray = new Ray(rayOrigin, rayDirection);

		bool hitButton = CheckMenuButtonHit(ray);

		if (hitButton)
		{
			lineRenderer.material.color = Color.yellow;
		}
		else
		{
			RectTransform menuRect = menuTarget.GetComponent<RectTransform>();
			if (menuRect != null)
			{
				Vector3 menuForward = menuRect.forward;
				Vector3 menuPosition = menuRect.position;
				float denom = Vector3.Dot(menuForward, rayDirection);

				if (Mathf.Abs(denom) > 1e-6f)
				{
					float t = Vector3.Dot(menuForward, menuPosition - rayOrigin) / denom;
					if (t >= 0f)
					{
						Vector3 worldHit = rayOrigin + rayDirection * Mathf.Min(t, rayLength);
						lineRenderer.SetPosition(0, rayOrigin);
						lineRenderer.SetPosition(1, worldHit);
					}
					else
					{
						lineRenderer.SetPosition(0, rayOrigin);
						lineRenderer.SetPosition(1, rayOrigin + rayDirection * rayLength);
					}
				}
				else
				{
					lineRenderer.SetPosition(0, rayOrigin);
					lineRenderer.SetPosition(1, rayOrigin + rayDirection * rayLength);
				}
			}
			else
			{
				lineRenderer.SetPosition(0, rayOrigin);
				lineRenderer.SetPosition(1, rayOrigin + rayDirection * rayLength);
			}

			lineRenderer.material.color = Color.green;
		}
	}

	private Transform GetMenuTarget()
	{
		if (Menu.Instance != null)
		{
			GameObject activeMenu = Menu.Instance.ActiveMenuInstance;
			if (activeMenu != null && activeMenu.activeSelf)
			{
				return activeMenu.transform;
			}
		}
		return null;
	}

	private bool CheckMenuButtonHit(Ray ray)
	{
		if (menuTarget == null)
		{
			return false;
		}

		RectTransform menuRect = menuTarget.GetComponent<RectTransform>();
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
					Button[] buttons = menuTarget.GetComponentsInChildren<Button>();
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
							return true;
						}
					}
				}
			}
		}

		return false;
	}
}