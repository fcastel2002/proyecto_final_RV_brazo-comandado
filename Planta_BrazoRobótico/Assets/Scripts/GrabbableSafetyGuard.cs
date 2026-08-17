using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class GrabbableSafetyGuard : MonoBehaviour
{
	[Header("Límite inferior")]
	[Tooltip("Altura mundial por debajo de la cual el objeto se considera fuera del mapa.")]
	[SerializeField]
	private float minimumWorldY = 0f;

	[Tooltip("Penetración tolerada antes de recuperar el objeto.")]
	[SerializeField]
	[Min(0f)]
	private float fallTolerance = 0.02f;

	[Tooltip("Separación vertical aplicada al restaurar la última pose segura.")]
	[SerializeField]
	[Min(0f)]
	private float recoveryClearance = 0.01f;

	[Header("Detección de apoyo")]
	[Tooltip("Distancia adicional usada para comprobar que las cuatro esquinas siguen sobre una superficie.")]
	[SerializeField]
	[Min(0.001f)]
	private float supportProbeDistance = 0.03f;

	[Tooltip("Fracción de la extensión horizontal usada para las sondas de las esquinas.")]
	[SerializeField]
	[Range(0.1f, 1f)]
	private float supportProbeInset = 0.75f;

	[SerializeField]
	private LayerMask supportLayers = ~0;

	[Tooltip("Fuerza los logs de recuperación aunque el modo debug global del menú de pausa esté apagado.")]
	[SerializeField]
	private bool logRecoveries;

	private const float ProbeStartHeight = 0.05f;
	private readonly RaycastHit[] supportHits = new RaycastHit[8];

	private Rigidbody objectRigidbody;
	private Collider[] solidColliders;
	private Vector3 lastSafePosition;
	private Quaternion lastSafeRotation;
	private bool hasSafePose;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void AttachToGrabbableObjects()
	{
		GameObject[] grabbableObjects = GameObject.FindGameObjectsWithTag("Agarrable");
		foreach (GameObject grabbableObject in grabbableObjects)
		{
			if (grabbableObject.GetComponent<Rigidbody>() != null &&
				grabbableObject.GetComponent<GrabbableSafetyGuard>() == null)
			{
				grabbableObject.AddComponent<GrabbableSafetyGuard>();
			}
		}
	}

	private void Awake()
	{
		objectRigidbody = GetComponent<Rigidbody>();
		solidColliders = GetComponentsInChildren<Collider>();
		RememberCurrentPose();
	}

	private void FixedUpdate()
	{
		if (objectRigidbody == null || objectRigidbody.isKinematic || transform.parent != null)
		{
			return;
		}

		Bounds objectBounds = GetSolidBounds();
		if (objectBounds.min.y < minimumWorldY - fallTolerance)
		{
			RecoverLastSafePose();
			return;
		}

		if (IsFullySupported(objectBounds))
		{
			RememberCurrentPose();
		}
	}

	private Bounds GetSolidBounds()
	{
		Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);
		bool hasCollider = false;

		foreach (Collider solidCollider in solidColliders)
		{
			if (solidCollider == null || !solidCollider.enabled || solidCollider.isTrigger) continue;

			if (hasCollider)
			{
				combinedBounds.Encapsulate(solidCollider.bounds);
			}
			else
			{
				combinedBounds = solidCollider.bounds;
				hasCollider = true;
			}
		}

		return combinedBounds;
	}

	private bool IsFullySupported(Bounds objectBounds)
	{
		float offsetX = objectBounds.extents.x * supportProbeInset;
		float offsetZ = objectBounds.extents.z * supportProbeInset;
		float originY = objectBounds.min.y + ProbeStartHeight;
		float castDistance = ProbeStartHeight + supportProbeDistance;

		return HasExternalSupport(new Vector3(objectBounds.center.x - offsetX, originY, objectBounds.center.z - offsetZ), castDistance) &&
			HasExternalSupport(new Vector3(objectBounds.center.x - offsetX, originY, objectBounds.center.z + offsetZ), castDistance) &&
			HasExternalSupport(new Vector3(objectBounds.center.x + offsetX, originY, objectBounds.center.z - offsetZ), castDistance) &&
			HasExternalSupport(new Vector3(objectBounds.center.x + offsetX, originY, objectBounds.center.z + offsetZ), castDistance);
	}

	private bool HasExternalSupport(Vector3 origin, float distance)
	{
		int hitCount = Physics.RaycastNonAlloc(
			origin,
			Vector3.down,
			supportHits,
			distance,
			supportLayers,
			QueryTriggerInteraction.Ignore);

		for (int i = 0; i < hitCount; i++)
		{
			Collider hitCollider = supportHits[i].collider;
			if (hitCollider != null && !hitCollider.transform.IsChildOf(transform))
			{
				return true;
			}
		}

		return false;
	}

	private void RememberCurrentPose()
	{
		lastSafePosition = objectRigidbody != null ? objectRigidbody.position : transform.position;
		lastSafeRotation = objectRigidbody != null ? objectRigidbody.rotation : transform.rotation;
		hasSafePose = true;
	}

	private void RecoverLastSafePose()
	{
		if (!hasSafePose) return;

		objectRigidbody.position = lastSafePosition + Vector3.up * recoveryClearance;
		objectRigidbody.rotation = lastSafeRotation;
		objectRigidbody.linearVelocity = Vector3.zero;
		objectRigidbody.angularVelocity = Vector3.zero;
		Physics.SyncTransforms();

		if (logRecoveries || DebugSettings.IsEnabled)
		{
			Debug.LogWarning($"[GrabbableSafetyGuard] {name} fue recuperado al salir del mapa.");
		}
	}

	private void OnValidate()
	{
		fallTolerance = Mathf.Max(0f, fallTolerance);
		recoveryClearance = Mathf.Max(0f, recoveryClearance);
		supportProbeDistance = Mathf.Max(0.001f, supportProbeDistance);
	}
}
