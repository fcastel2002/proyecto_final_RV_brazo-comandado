
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
		if (other.CompareTag("Agarrable"))
		{
			gripperController?.NotifyFingerContact(other.gameObject, true);
			if (debug) Debug.Log($"[GripperTriggerForwarder] OnTriggerEnter ({gameObject.name}) with {other.name}");
		}
		// Optionally forward enter
	}
	private void OnTriggerExit(Collider other)
	{
		if (debug) Debug.Log($"[GripperTriggerForwarder] OnTriggerExit ({gameObject.name}) with {other.name}");
		if(other.CompareTag("Agarrable"))
		{
			gripperController?.NotifyFingerContact(other.gameObject, false);
		}
	}
}