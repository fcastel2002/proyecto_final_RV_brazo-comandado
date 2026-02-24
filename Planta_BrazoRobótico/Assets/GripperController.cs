using UnityEngine;
using UnityEngine.InputSystem;

public class GripperController : MonoBehaviour
{
	[Header("Configuracion")]
	[SerializeField]
	private Transform graspPoint;

	[Header("Debug")]
	[SerializeField]
	private bool debugTriggers = true;

	private GameObject grabbedObject;
	private bool isGripperClosed = true;

	public void ToggleGrip()
	{
		isGripperClosed = !isGripperClosed;
		if (debugTriggers) Debug.Log($"[GripperController] ToggleGrip -> isGripperClosed = {isGripperClosed}");

		if (!isGripperClosed && grabbedObject != null)
		{
			if (debugTriggers) Debug.Log("[GripperController] Releasing grabbedObject");
			ReleaseObject();
		}
	}

	// Public method that child forwarders can call
	public void NotifyTriggerStay(Collider other)
	{
		if (debugTriggers) Debug.Log($"[GripperController] NotifyTriggerStay from {other.name} (tag={other.tag})");
		CheckAndGrab(other);
	}

	// Keep original behaviour if this script is attached to the same GameObject as the collider
	private void OnTriggerStay(Collider other)
	{
		if (debugTriggers) Debug.Log($"[GripperController] OnTriggerStay on {gameObject.name} with {other.name} (tag={other.tag})");
		CheckAndGrab(other);
	}

	// Extracted logic used by both OnTriggerStay and NotifyTriggerStay
	private void CheckAndGrab(Collider other)
	{
		if (isGripperClosed && grabbedObject == null && other.CompareTag("Agarrable"))
		{
			if (debugTriggers) Debug.Log("[GripperController] Grabbing object " + other.name);
			GrabObject(other.gameObject);
		}
	}

	private void GrabObject(GameObject objectToGrab)
	{
		grabbedObject = objectToGrab;
		Rigidbody rb = grabbedObject.GetComponent<Rigidbody>();

		if (rb != null)
		{
			rb.isKinematic = true;
		}

		grabbedObject.transform.SetParent(graspPoint);
		if (debugTriggers) Debug.Log("[GripperController] GrabObject: parented " + grabbedObject.name + " to " + (graspPoint ? graspPoint.name : "null"));
	}

	private void ReleaseObject()
	{
		if (grabbedObject != null)
		{
			Rigidbody rb = grabbedObject.GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.isKinematic = false;
			}
			grabbedObject.transform.SetParent(null);
			if (debugTriggers) Debug.Log("[GripperController] ReleaseObject: released " + grabbedObject.name);
			grabbedObject = null;
		}
	}

	void Start() { }

	void Update() { }
}