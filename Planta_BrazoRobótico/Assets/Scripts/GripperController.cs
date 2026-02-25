using System.Collections.Generic;
using UnityEngine;

public class GripperController : MonoBehaviour
{
	[Header("Configuracion")]
	[Tooltip("El Rigidbody del gripper (debe ser Kinematic).")]
	[SerializeField]
	private Rigidbody gripperRigidbody;

	[Tooltip("Transform al que se emparentará el objeto agarrado (p.ej. un punto entre los dedos).")]
	[SerializeField]
	private Transform graspPoint;

	[Header("Script del gripper")]
	[SerializeField]
	private Ctrl_OnRobotRG2_Custom gripperAnimator;

	[Header("Debug")]
	[SerializeField]
	private bool debugTriggers = true;

	private GameObject grabbedObject;
	private Rigidbody grabbedRigidbody;
	private float originalMass;

	private bool isGripperClosed = true;

	private Dictionary<GameObject, int> contactCount = new Dictionary<GameObject, int>();
	public void ToggleGrip()
	{
		isGripperClosed = !isGripperClosed;
		if (debugTriggers) Debug.Log($"[GripperController] ToggleGrip -> isGripperClosed = {isGripperClosed}");

		if (isGripperClosed)
		{
			TryGrab();
		}
		else
		{
			ReleaseObject();
		}
	}

	public void NotifyFingerContact(GameObject obj, bool isTouching)
	{
		if(!contactCount.ContainsKey(obj))
		{
			contactCount[obj] = 0;
		}
		if (isTouching)
		{
			contactCount[obj]++;
			if (debugTriggers) Debug.Log($"[GripperController] NotifyFingerContact: {obj.name} touched, count={contactCount[obj]}");
		}
		else
		{
			contactCount[obj] = Mathf.Max(0, contactCount[obj] - 1);
			if (debugTriggers) Debug.Log($"[GripperController] NotifyFingerContact: {obj.name} released, count={contactCount[obj]}");
		}
		if (debugTriggers) Debug.Log($"[GripperController] NotifyFingerContact: {obj.name} isTouching={isTouching}, total contacts={contactCount[obj]}");
		if (isGripperClosed)
		{
			TryGrab();
		}
	}

	private void TryGrab()
	{
		if (grabbedObject != null) return;
		foreach (var kvp in contactCount)
		{
			if(kvp.Value >= 2)
			{
				GrabObject(kvp.Key);
				break;
			}
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
		grabbedRigidbody = grabbedObject.GetComponent<Rigidbody>();

		if (grabbedRigidbody != null)
		{
			// Guardar masa original y transferirla al gripper
			originalMass = grabbedRigidbody.mass;
			if (gripperRigidbody != null)
			{
				gripperRigidbody.mass += originalMass;
			}

			// Hacer el objeto cinemático para evitar deslizamientos
			grabbedRigidbody.isKinematic = true;
		}

		// Emparentar para movimiento 1:1 perfecto
		Transform parent = graspPoint != null ? graspPoint : transform;
		grabbedObject.transform.SetParent(parent);

		if (debugTriggers) Debug.Log($"[GripperController] GrabObject: grabbed {grabbedObject.name} (Mass: {originalMass} transferred to gripper)");

		if (gripperAnimator != null)
		{
			gripperAnimator.start_movement = false;
			gripperAnimator.ctrl_state = 0;
			gripperAnimator.in_position = true;
			if (debugTriggers) Debug.Log("[GripperController] Stopped gripper animator movement");
		}
	}

	private void ReleaseObject()
	{
		if (grabbedObject != null)
		{
			// Desemparentar
			grabbedObject.transform.SetParent(null);

			if (grabbedRigidbody != null)
			{
				// Restaurar masa al gripper
				if (gripperRigidbody != null)
				{
					gripperRigidbody.mass -= originalMass;
				}

				// Restaurar físicas del objeto
				grabbedRigidbody.isKinematic = false;
				grabbedRigidbody = null;
			}

			if (debugTriggers) Debug.Log("[GripperController] ReleaseObject: released " + grabbedObject.name);
			grabbedObject = null;
		}
		// Limpiar contactos para que el próximo agarre empiece desde cero.
		contactCount.Clear();
	}

	void Start() { }

	void Update() { }
}