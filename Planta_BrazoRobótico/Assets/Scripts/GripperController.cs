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

	[Tooltip("Al soltar, la pieza hereda la velocidad del punto de agarre. Desactivado cae en vertical, que es más predecible para formación.")]
	[SerializeField]
	private bool inheritReleaseVelocity;

	[Tooltip("Tope (m/s) de la velocidad heredada al soltar.")]
	[SerializeField]
	[Min(0f)]
	private float maxInheritedReleaseSpeed = 1.5f;

	[Header("Debug")]
	[Tooltip("Fuerza los logs de este componente aunque el modo debug global del menú de pausa esté apagado.")]
	[SerializeField]
	private bool debugTriggers;

	/// <summary>Logs activos por el flag local del Inspector o por el modo debug del menú de pausa.</summary>
	private bool LogEnabled => debugTriggers || DebugSettings.IsEnabled;

	private GameObject grabbedObject;
	private Rigidbody grabbedRigidbody;
	private Collider[] payloadColliders;
	private Collider[] gripperColliders;
	private float originalMass;
	private bool grabbedWasKinematic;
	private Vector3 grabbedOriginalLocalScale;
	private float pickupMinimumY;
	private float gripperBaseMass;

	private Vector3 lastGraspPosition;
	private Vector3 graspVelocity;
	private bool hasLastGraspPosition;

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

		// Se cachea en Awake, cuando todavía no cuelga ninguna pieza del gripper: así estos colliders
		// son siempre los del gripper y nunca los de la carga, que se mide aparte.
		gripperColliders = GetComponentsInChildren<Collider>();
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

	/// <summary>
	/// Entre todas las piezas que cumplen el criterio de contactos opuestos, se agarra la más cercana
	/// al punto de agarre. Antes se tomaba la primera del diccionario, y el orden de iteración de un
	/// Dictionary no está definido: con dos piezas entre los dedos, cuál se llevaba era arbitrario y
	/// no reproducible para el operario.
	/// </summary>
	private void TryGrab()
	{
		if (grabbedObject != null || !isClosing) return;

		Vector3 referencePoint = graspPoint != null ? graspPoint.position : transform.position;
		GameObject objectToGrab = null;
		float bestSqrDistance = float.MaxValue;

		foreach (KeyValuePair<GameObject, HashSet<GripperTriggerForwarder>> entry in fingerContacts)
		{
			if (entry.Key == null || !HasOpposingInnerContacts(entry.Value)) continue;

			float sqrDistance = (entry.Key.transform.position - referencePoint).sqrMagnitude;
			if (sqrDistance >= bestSqrDistance) continue;

			bestSqrDistance = sqrDistance;
			objectToGrab = entry.Key;
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
		// Se cachea una sola vez por agarre: TryGetPayloadBounds() lo consulta desde el veto de
		// colisión, que corre en cada FixedUpdate.
		payloadColliders = grabbedObject.GetComponentsInChildren<Collider>();
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
		Vector3 worldScaleBefore = grabbedObject.transform.lossyScale;
		grabbedOriginalLocalScale = grabbedObject.transform.localScale;
		grabbedObject.transform.SetParent(parent);
		PreserveWorldScale(grabbedObject.transform, worldScaleBefore);

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

			// Desemparentar y devolver la escala local exacta que tenía antes del agarre.
			grabbedObject.transform.SetParent(null);
			grabbedObject.transform.localScale = grabbedOriginalLocalScale;

			if (grabbedRigidbody != null)
			{
				// Restaurar físicas del objeto tal como estaban antes del agarre.
				grabbedRigidbody.isKinematic = grabbedWasKinematic;
				if (!grabbedWasKinematic)
				{
					grabbedRigidbody.linearVelocity = inheritReleaseVelocity
						? Vector3.ClampMagnitude(graspVelocity, maxInheritedReleaseSpeed)
						: Vector3.zero;
					grabbedRigidbody.angularVelocity = Vector3.zero;
				}
				grabbedRigidbody = null;
			}

			if (LogEnabled) Debug.Log("[GripperController] ReleaseObject: released " + grabbedObject.name);
			grabbedObject = null;
		}

		grabbedRigidbody = null;
		payloadColliders = null;
		originalMass = 0f;
		grabbedWasKinematic = false;
		grabbedOriginalLocalScale = Vector3.one;

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

	/// <summary>
	/// AABB mundial de la pieza agarrada, considerando solo colliders sólidos. Devuelve false si no hay
	/// pieza o no tiene ninguno.
	///
	/// Lo consume <see cref="JoystickAdapter"/> para incluir el volumen de la pieza en el veto de
	/// colisión: al agarrarla pasa a colgar del robot, así que el barrido del gripper la descarta como
	/// obstáculo y sin esto se la puede empotrar lateralmente contra el entorno.
	/// </summary>
	public bool TryGetPayloadBounds(out Bounds bounds)
	{
		bounds = default;
		if (grabbedObject == null) return false;

		return TryEncapsulate(payloadColliders, false, out bounds);
	}

	/// <summary>
	/// AABB mundial del propio gripper. A diferencia del payload aquí SÍ se cuentan los triggers: los
	/// volúmenes de las caras internas de los dedos son triggers y son justamente la parte más baja de
	/// la garra, que es lo que interesa para el piso duro.
	/// </summary>
	public bool TryGetGripperBounds(out Bounds bounds)
	{
		return TryEncapsulate(gripperColliders, true, out bounds);
	}

	private static bool TryEncapsulate(Collider[] colliders, bool includeTriggers, out Bounds bounds)
	{
		bounds = default;
		if (colliders == null) return false;

		bool hasBounds = false;
		for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
		{
			Collider candidate = colliders[colliderIndex];
			if (candidate == null || !candidate.enabled) continue;
			if (candidate.isTrigger && !includeTriggers) continue;

			if (hasBounds)
			{
				bounds.Encapsulate(candidate.bounds);
			}
			else
			{
				bounds = candidate.bounds;
				hasBounds = true;
			}
		}

		return hasBounds;
	}

	/// <summary>
	/// <c>SetParent</c> preserva la pose mundial pero NO la escala: si el punto de agarre arrastra una
	/// escala distinta de 1, la pieza se deforma en el momento de agarrarla. Se corrige la escala local
	/// para que la escala mundial siga siendo la de antes de emparentar.
	/// </summary>
	private void PreserveWorldScale(Transform target, Vector3 worldScaleBefore)
	{
		Vector3 worldScaleAfter = target.lossyScale;
		if (IsApproximately(worldScaleAfter, worldScaleBefore)) return;

		Vector3 localScale = target.localScale;
		target.localScale = new Vector3(
			RescaleAxis(localScale.x, worldScaleBefore.x, worldScaleAfter.x),
			RescaleAxis(localScale.y, worldScaleBefore.y, worldScaleAfter.y),
			RescaleAxis(localScale.z, worldScaleBefore.z, worldScaleAfter.z));

		if (LogEnabled)
		{
			Debug.LogWarning(
				$"[GripperController] El punto de agarre deformaba {target.name} " +
				$"({worldScaleBefore} -> {worldScaleAfter}); se corrigió la escala. " +
				"Conviene revisar la escala del graspPoint en la jerarquía.");
		}
	}

	private static float RescaleAxis(float localScale, float worldBefore, float worldAfter)
	{
		return Mathf.Approximately(worldAfter, 0f) ? localScale : localScale * (worldBefore / worldAfter);
	}

	private static bool IsApproximately(Vector3 a, Vector3 b)
	{
		return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) && Mathf.Approximately(a.z, b.z);
	}

	/// <summary>
	/// Velocidad del punto de agarre, estimada por diferencia de posición entre ticks de física. Se
	/// mide siempre, haya pieza o no, para que al soltar el valor ya esté disponible.
	/// </summary>
	private void TrackGraspVelocity()
	{
		Transform anchor = graspPoint != null ? graspPoint : transform;
		Vector3 position = anchor.position;

		if (hasLastGraspPosition && Time.fixedDeltaTime > 0f)
		{
			graspVelocity = (position - lastGraspPosition) / Time.fixedDeltaTime;
		}

		lastGraspPosition = position;
		hasLastGraspPosition = true;
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
		// Antes de la suelta: al liberar la pieza se le transfiere esta velocidad.
		TrackGraspVelocity();
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
		maxInheritedReleaseSpeed = Mathf.Max(0f, maxInheritedReleaseSpeed);
	}
}
