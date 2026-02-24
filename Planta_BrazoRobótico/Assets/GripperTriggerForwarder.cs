
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class GripperTriggerForwarder : MonoBehaviour
{
	[Tooltip("If empty, will try to find a GripperController in parents.")]
	[SerializeField]
	private GripperController gripperController;

	[Header("Debug")]
	[SerializeField]
	private bool debug = true;

	private void Awake()
	{
		if (gripperController == null)
		{
			gripperController = GetComponentInParent<GripperController>();
			if (debug) Debug.Log($"[GripperTriggerForwarder] Auto-found GripperController: {(gripperController ? gripperController.name : "null")}");
		}
	}

	private void OnTriggerEnter(Collider other)
	{
		if (debug) Debug.Log($"[GripperTriggerForwarder] OnTriggerEnter ({gameObject.name}) with {other.name}");
		// Optionally forward enter
	}

	private void OnTriggerStay(Collider other)
	{
		if (debug) Debug.Log($"[GripperTriggerForwarder] OnTriggerStay ({gameObject.name}) with {other.name}");
		gripperController?.NotifyTriggerStay(other);
	}

	private void OnTriggerExit(Collider other)
	{
		if (debug) Debug.Log($"[GripperTriggerForwarder] OnTriggerExit ({gameObject.name}) with {other.name}");
		// Optionally forward exit
	}
}