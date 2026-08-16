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

	[Tooltip("Cuánto deben abrirse los dedos (0-1) DESDE donde estaban al ordenar la suelta, antes de devolver la pieza a la física.")]
	[SerializeField]
	[Range(0f, 1f)]
	private float releaseOpeningDelta = 0.2f;

	[Tooltip("Tiempo máximo (s) esperando esa apertura antes de soltar igual. Evita que la pieza quede pegada si la animación no avanza.")]
	[SerializeField]
	[Min(0.05f)]
	private float releaseTimeout = 1f;

	[Header("Debug")]
	[Tooltip("Fuerza los logs de este componente aunque el modo debug global del menú de pausa esté apagado.")]
	[SerializeField]
	private bool debugTriggers;

	/// <summary>Logs activos por el flag local del Inspector o por el modo debug del menú de pausa.</summary>
	private bool LogEnabled => debugTriggers || DebugSettings.IsEnabled;

	private GameObject grabbedObject;
	private Rigidbody grabbedRigidbody;
	private float originalMass;
	private bool grabbedWasKinematic;
	private float pickupMinimumY;
	private float gripperBaseMass;

	private bool isGripperClosed = true;
	private bool isClosing;
	private bool isReleasing;
	private float releaseWaitTime;
	private float releaseStartOpening;

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

	private void Awake()
	{
		// Masa del gripper vacío. La masa con carga se reasigna siempre como base + payload en vez de
		// acumular sumas y restas, que derivan si algún agarre y su suelta no se emparejan.
		if (gripperRigidbody != null) gripperBaseMass = gripperRigidbody.mass;
	}

	public void ToggleGrip()
	{
		isGripperClosed = !isGripperClosed;
		if (LogEnabled) Debug.Log($"[GripperController] ToggleGrip -> isGripperClosed = {isGripperClosed}");

		if (isGripperClosed)
		{
			// Si se vuelve a cerrar antes de que los dedos abrieran lo suficiente, la pieza todavía no
			// volvió a la física: se cancela la suelta pendiente y se mantiene el agarre.
			isReleasing = false;

			// La ventana de agarre solo existe durante una orden explícita de cierre.
			// Así, tocar un objeto con la garra ya cerrada nunca lo adhiere.
			isClosing = gripperAnimator != null && gripperAnimator.start_movement;
			TryGrab();
		}
		else
		{
			isClosing = false;
			BeginRelease();
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

		if (LogEnabled)
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
		isReleasing = false;
		grabbedObject = objectToGrab;
		grabbedRigidbody = grabbedObject.GetComponent<Rigidbody>();
		Physics.SyncTransforms();
		pickupMinimumY = GetLowestSolidPoint(grabbedObject);

		if (grabbedRigidbody != null)
		{
			// Guardar el estado original para devolverlo intacto al soltar.
			originalMass = grabbedRigidbody.mass;
			grabbedWasKinematic = grabbedRigidbody.isKinematic;

			// Hacer el objeto cinemático para evitar deslizamientos
			grabbedRigidbody.isKinematic = true;
		}
		else
		{
			// Sin Rigidbody no hay masa que transferir. Si no se limpia, GrabbedMass seguiría
			// devolviendo la masa de la pieza anterior y falsearía la inercia y el frenado por payload.
			originalMass = 0f;
			grabbedWasKinematic = false;
		}

		RefreshGripperMass();

		// Emparentar para movimiento 1:1 perfecto
		Transform parent = graspPoint != null ? graspPoint : transform;
		grabbedObject.transform.SetParent(parent);

		if (LogEnabled) Debug.Log($"[GripperController] GrabObject: grabbed {grabbedObject.name} (Mass: {originalMass} transferred to gripper)");

		if (gripperAnimator != null)
		{
			gripperAnimator.StopMotion();
			if (LogEnabled) Debug.Log("[GripperController] Stopped gripper animator movement");
		}
	}

	/// <summary>
	/// Marca la intención de soltar, pero difiere la devolución a la física hasta que los dedos hayan
	/// abierto (ver <see cref="UpdatePendingRelease"/>). Si el objeto vuelve a ser dinámico con los dedos
	/// todavía cerrados encima, Unity resuelve esa penetración y la pieza salta o sale disparada.
	/// </summary>
	private void BeginRelease()
	{
		if (grabbedObject == null)
		{
			// Nada que devolver: solo limpiar contactos, como hacía la suelta inmediata.
			isReleasing = false;
			fingerContacts.Clear();
			return;
		}

		isReleasing = true;
		releaseWaitTime = 0f;
		releaseStartOpening = gripperAnimator != null ? gripperAnimator.OpeningFraction : 0f;

		// Sin animador no hay apertura que esperar: se conserva el comportamiento inmediato.
		if (gripperAnimator == null) ReleaseObject();
	}

	private void UpdatePendingRelease()
	{
		if (!isReleasing) return;

		releaseWaitTime += Time.fixedDeltaTime;

		bool fingersClear = HaveFingersClearedPayload();
		if (!fingersClear && releaseWaitTime < releaseTimeout) return;

		if (!fingersClear)
		{
			Debug.LogWarning(
				$"[GripperController] Suelta forzada tras {releaseTimeout:F2} s: los dedos solo abrieron " +
				$"{gripperAnimator.OpeningFraction - releaseStartOpening:F2} de los {releaseOpeningDelta:F2} pedidos.");
		}

		ReleaseObject();
	}

	/// <summary>
	/// Los dedos ya se separaron de la pieza. Se mide como INCREMENTO respecto de la apertura que tenían
	/// al ordenar la suelta, no como valor absoluto: al agarrar, los dedos quedan detenidos apoyados sobre
	/// la pieza, así que una pieza ancha arranca la suelta con una apertura alta y un umbral absoluto se
	/// cumpliría en el primer tick, justo lo que hay que evitar.
	/// </summary>
	private bool HaveFingersClearedPayload()
	{
		if (gripperAnimator == null) return true;

		float opening = gripperAnimator.OpeningFraction;
		if (opening - releaseStartOpening >= releaseOpeningDelta) return true;

		// Con una pieza casi tan ancha como la carrera del RG2 ese incremento puede no existir:
		// alcanza con que la animación haya llegado al tope de apertura.
		return gripperAnimator.IsInPosition && opening > releaseStartOpening;
	}

	private void ReleaseObject()
	{
		isReleasing = false;
		releaseWaitTime = 0f;

		if (grabbedObject != null)
		{
			MoveGrabbedObjectToSafeReleaseHeight();

			// Desemparentar
			grabbedObject.transform.SetParent(null);

			if (grabbedRigidbody != null)
			{
				// Restaurar físicas del objeto tal como estaban antes del agarre.
				grabbedRigidbody.isKinematic = grabbedWasKinematic;
				if (!grabbedWasKinematic)
				{
					grabbedRigidbody.linearVelocity = Vector3.zero;
					grabbedRigidbody.angularVelocity = Vector3.zero;
				}
				grabbedRigidbody = null;
			}

			if (LogEnabled) Debug.Log("[GripperController] ReleaseObject: released " + grabbedObject.name);
			grabbedObject = null;
		}

		grabbedRigidbody = null;
		originalMass = 0f;
		grabbedWasKinematic = false;

		// Devolver el gripper a su masa en vacío. Fuera del if para cubrir también el caso de que la
		// pieza haya sido destruida mientras estaba tomada.
		RefreshGripperMass();

		// Limpiar contactos para que el próximo agarre empiece desde cero.
		fingerContacts.Clear();
	}

	private void RefreshGripperMass()
	{
		if (gripperRigidbody == null) return;

		gripperRigidbody.mass = gripperBaseMass + GrabbedMass;
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

		if (LogEnabled)
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
		UpdatePendingRelease();

		if (!isClosing || gripperAnimator == null) return;

		if (!gripperAnimator.start_movement && gripperAnimator.ctrl_state == 0)
		{
			isClosing = false;
		}
	}

	private void OnValidate()
	{
		releaseClearance = Mathf.Max(0f, releaseClearance);
		releaseOpeningDelta = Mathf.Clamp01(releaseOpeningDelta);
		releaseTimeout = Mathf.Max(0.05f, releaseTimeout);
	}
}
