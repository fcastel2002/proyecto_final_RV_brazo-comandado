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

	[Header("Seguridad al soltar")]
	[Tooltip("Altura Y mundial mínima de la superficie sobre la que se puede soltar un objeto.")]
	[SerializeField]
	private float minimumReleaseWorldY = 0f;

	[Tooltip("Impide soltar el objeto por debajo de la cota inferior en la que fue recogido.")]
	[SerializeField]
	private bool preservePickupFloorHeight = true;

	[Tooltip("Separación adicional sobre el suelo al corregir una suelta subterránea.")]
	[SerializeField]
	[Min(0f)]
	private float releaseClearance = 0.005f;

	[Header("Debug")]
	[SerializeField]
	private bool debugTriggers = true;

	private GameObject grabbedObject;
	private Rigidbody grabbedRigidbody;
	private float originalMass;
	private float pickupMinimumY;

	private bool isGripperClosed = true;
	private bool isClosing;

	public bool IsHoldingObject => grabbedObject != null;

	/// <summary>
	/// Intencion de cierre de la garra, independiente de si llego a agarrar algo. Arranca en true.
	/// La consume JoystickAdapter para bloquear el descenso con la garra cerrada.
	/// </summary>
	public bool IsGripperClosed => isGripperClosed;

	/// <summary>
	/// Objeto actualmente agarrado, o null. Lo usa GripperDistanceSensor para descontar el volumen
	/// de la pieza transportada y medir el hueco libre por debajo de ella.
	/// </summary>
	public GameObject GrabbedObject => grabbedObject;

	/// <summary>Masa original (kg) del objeto actualmente agarrado, o 0 si no hay ninguno.</summary>
	public float GrabbedMass => grabbedObject != null ? originalMass : 0f;

	/// <summary>Posición mundial del punto de agarre (graspPoint), rígidamente ligado al objeto agarrado.</summary>
	public Vector3 GrabbedWorldPosition => graspPoint != null ? graspPoint.position : transform.position;

	private readonly Dictionary<GameObject, HashSet<GripperTriggerForwarder>> fingerContacts =
		new Dictionary<GameObject, HashSet<GripperTriggerForwarder>>();

	public void ToggleGrip()
	{
		isGripperClosed = !isGripperClosed;
		if (debugTriggers) Debug.Log($"[GripperController] ToggleGrip -> isGripperClosed = {isGripperClosed}");

		if (isGripperClosed)
		{
			// La ventana de agarre solo existe durante una orden explícita de cierre.
			// Así, tocar un objeto con la garra ya cerrada nunca lo adhiere.
			isClosing = gripperAnimator != null && gripperAnimator.start_movement;
			TryGrab();
		}
		else
		{
			isClosing = false;
			ReleaseObject();
		}
	}

	public void NotifyFingerContact(GameObject obj, GripperTriggerForwarder finger, bool isTouching)
	{
		if (obj == null || finger == null || !finger.IsInnerFace)
		{
			return;
		}

		if (isTouching)
		{
			if (!fingerContacts.TryGetValue(obj, out HashSet<GripperTriggerForwarder> contacts))
			{
				contacts = new HashSet<GripperTriggerForwarder>();
				fingerContacts.Add(obj, contacts);
			}

			contacts.Add(finger);
		}
		else if (fingerContacts.TryGetValue(obj, out HashSet<GripperTriggerForwarder> contacts))
		{
			contacts.Remove(finger);
			if (contacts.Count == 0)
			{
				fingerContacts.Remove(obj);
			}
		}

		if (debugTriggers)
		{
			int contactTotal = fingerContacts.TryGetValue(obj, out HashSet<GripperTriggerForwarder> currentContacts)
				? currentContacts.Count
				: 0;
			Debug.Log($"[GripperController] Contacto interno: {obj.name}, activo={isTouching}, sensores={contactTotal}");
		}

		if (isGripperClosed && isClosing)
		{
			TryGrab();
		}
	}

	private void TryGrab()
	{
		if (grabbedObject != null || !isClosing) return;

		GameObject objectToGrab = null;
		foreach (KeyValuePair<GameObject, HashSet<GripperTriggerForwarder>> entry in fingerContacts)
		{
			if (HasOpposingInnerContacts(entry.Value))
			{
				objectToGrab = entry.Key;
				break;
			}
		}

		if (objectToGrab != null)
		{
			GrabObject(objectToGrab);
		}
	}

	private bool HasOpposingInnerContacts(HashSet<GripperTriggerForwarder> contacts)
	{
		bool touchesLeft = false;
		bool touchesRight = false;

		foreach (GripperTriggerForwarder contact in contacts)
		{
			if (contact == null || !contact.IsInnerFace) continue;

			switch (contact.ResolveFingerSide(transform))
			{
				case GripperFingerSide.Left:
					touchesLeft = true;
					break;
				case GripperFingerSide.Right:
					touchesRight = true;
					break;
			}

			if (touchesLeft && touchesRight) return true;
		}

		return false;
	}

	// Public method that child forwarders can call (legacy, no-op)
	public void NotifyTriggerStay(Collider other)
	{
		// Removed to avoid grabbing objects by simply touching them with the outside of the gripper
	}

	// Keep original behaviour if this script is attached to the same GameObject as the collider (legacy, no-op)
	private void OnTriggerStay(Collider other)
	{
		// Removed to avoid grabbing objects by simply touching them with the outside of the gripper
	}

	private void GrabObject(GameObject objectToGrab)
	{
		isClosing = false;
		grabbedObject = objectToGrab;
		grabbedRigidbody = grabbedObject.GetComponent<Rigidbody>();
		Physics.SyncTransforms();
		pickupMinimumY = GetLowestSolidPoint(grabbedObject);

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
			MoveGrabbedObjectToSafeReleaseHeight();

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
				grabbedRigidbody.linearVelocity = Vector3.zero;
				grabbedRigidbody.angularVelocity = Vector3.zero;
				grabbedRigidbody = null;
			}

			if (debugTriggers) Debug.Log("[GripperController] ReleaseObject: released " + grabbedObject.name);
			grabbedObject = null;
		}
		// Limpiar contactos para que el próximo agarre empiece desde cero.
		fingerContacts.Clear();
	}

	private void MoveGrabbedObjectToSafeReleaseHeight()
	{
		Physics.SyncTransforms();

		float lowestPoint = GetLowestSolidPoint(grabbedObject);
		float releaseFloorY = preservePickupFloorHeight
			? Mathf.Max(minimumReleaseWorldY, pickupMinimumY)
			: minimumReleaseWorldY;
		float safeMinimum = releaseFloorY + releaseClearance;
		if (lowestPoint >= safeMinimum) return;

		float correction = safeMinimum - lowestPoint;
		grabbedObject.transform.position += Vector3.up * correction;
		Physics.SyncTransforms();

		if (debugTriggers)
		{
			Debug.LogWarning($"[GripperController] Se corrigió una suelta bajo el suelo en {correction:F3} m.");
		}
	}

	private static float GetLowestSolidPoint(GameObject target)
	{
		Collider[] colliders = target.GetComponentsInChildren<Collider>();
		float lowestPoint = target.transform.position.y;
		bool hasSolidCollider = false;

		foreach (Collider objectCollider in colliders)
		{
			if (objectCollider == null || !objectCollider.enabled || objectCollider.isTrigger) continue;

			lowestPoint = hasSolidCollider
				? Mathf.Min(lowestPoint, objectCollider.bounds.min.y)
				: objectCollider.bounds.min.y;
			hasSolidCollider = true;
		}

		return lowestPoint;
	}

	private void FixedUpdate()
	{
		if (!isClosing || gripperAnimator == null) return;

		if (!gripperAnimator.start_movement && gripperAnimator.ctrl_state == 0)
		{
			isClosing = false;
		}
	}

	private void OnValidate()
	{
		releaseClearance = Mathf.Max(0f, releaseClearance);
	}
}
