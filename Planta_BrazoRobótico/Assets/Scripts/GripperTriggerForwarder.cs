using System.Collections.Generic;
using UnityEngine;

public enum GripperFingerSide
{
	Auto,
	Left,
	Right
}

[RequireComponent(typeof(Collider))]
public class GripperTriggerForwarder : MonoBehaviour
{
	[Tooltip("If empty, will try to find a GripperController in parents.")]
	[SerializeField]
	private GripperController gripperController;

	[Header("Superficie de agarre")]
	[Tooltip("Debe estar activo únicamente en el volumen que representa la pared interna del dedo.")]
	[SerializeField]
	private bool isInnerFace = true;

	[Tooltip("Auto usa el nombre L_/R_ del dedo y, como respaldo, su posición respecto del gripper.")]
	[SerializeField]
	private GripperFingerSide fingerSide = GripperFingerSide.Auto;

	[Header("Debug")]
	[SerializeField]
	private bool debug = true;

	private readonly Dictionary<GameObject, HashSet<Collider>> activeContacts =
		new Dictionary<GameObject, HashSet<Collider>>();

	public bool IsInnerFace => isInnerFace;

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
		if (!TryGetGrabbableObject(other, out GameObject grabbable)) return;

		if (!activeContacts.TryGetValue(grabbable, out HashSet<Collider> colliders))
		{
			colliders = new HashSet<Collider>();
			activeContacts.Add(grabbable, colliders);
		}

		if (colliders.Add(other) && colliders.Count == 1)
		{
			gripperController?.NotifyFingerContact(grabbable, this, true);
			if (debug) Debug.Log($"[GripperTriggerForwarder] Contacto interno ({gameObject.name}) con {grabbable.name}");
		}
	}

	private void OnTriggerExit(Collider other)
	{
		if (!TryGetGrabbableObject(other, out GameObject grabbable)) return;
		if (!activeContacts.TryGetValue(grabbable, out HashSet<Collider> colliders)) return;

		colliders.Remove(other);
		if (colliders.Count == 0)
		{
			activeContacts.Remove(grabbable);
			gripperController?.NotifyFingerContact(grabbable, this, false);
			if (debug) Debug.Log($"[GripperTriggerForwarder] Fin de contacto interno ({gameObject.name}) con {grabbable.name}");
		}
	}

	private void OnDisable()
	{
		foreach (GameObject grabbable in activeContacts.Keys)
		{
			if (grabbable != null)
			{
				gripperController?.NotifyFingerContact(grabbable, this, false);
			}
		}

		activeContacts.Clear();
	}

	public GripperFingerSide ResolveFingerSide(Transform gripperRoot)
	{
		if (fingerSide != GripperFingerSide.Auto) return fingerSide;

		Transform current = transform;
		while (current != null && current != gripperRoot)
		{
			string objectName = current.name.ToLowerInvariant();
			if (objectName.StartsWith("l_") || objectName.Contains("left")) return GripperFingerSide.Left;
			if (objectName.StartsWith("r_") || objectName.Contains("right")) return GripperFingerSide.Right;
			current = current.parent;
		}

		if (gripperRoot == null) return GripperFingerSide.Auto;

		float localX = gripperRoot.InverseTransformPoint(transform.position).x;
		if (Mathf.Abs(localX) <= Mathf.Epsilon) return GripperFingerSide.Auto;

		return localX < 0f ? GripperFingerSide.Left : GripperFingerSide.Right;
	}

	private static bool TryGetGrabbableObject(Collider other, out GameObject grabbable)
	{
		grabbable = null;
		if (other == null) return false;

		Transform current = other.transform;
		while (current != null)
		{
			if (current.CompareTag("Agarrable"))
			{
				grabbable = other.attachedRigidbody != null
					? other.attachedRigidbody.gameObject
					: current.gameObject;
				return true;
			}

			current = current.parent;
		}

		return false;
	}
}
