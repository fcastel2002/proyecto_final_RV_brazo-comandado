using UnityEngine;

/// <summary>
/// Cuenta contactos del gripper contra objetos que NO tienen tag "Agarrable" (paredes, racks, piso, etc.),
/// para <see cref="PerformanceMetricsTracker"/>. Usa triggers, no colisión sólida: el Rigidbody del gripper
/// es Kinematic y los obstáculos del entorno suelen ser colliders estáticos sin Rigidbody, combinación para
/// la que Unity no garantiza eventos OnCollisionEnter. Es el mismo mecanismo que ya usa
/// <see cref="GripperTriggerForwarder"/> para detectar el agarre, aplicado al caso opuesto.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GripperCollisionCounter : MonoBehaviour
{
    [Tooltip("Se ignoran los contactos con objetos que cuelguen de este transform (el propio robot/gripper, para no contarse a sí mismo).")]
    [SerializeField] private Transform _ignoreRoot;

    [SerializeField] private PerformanceMetricsTracker _metricsTracker;

    private void Awake()
    {
        if (_metricsTracker == null)
            _metricsTracker = FindFirstObjectByType<PerformanceMetricsTracker>();
    }

    private void OnTriggerEnter(Collider other) => Notify(other, true);

    private void OnTriggerExit(Collider other) => Notify(other, false);

    private void Notify(Collider other, bool isEntering)
    {
        if (!TryGetCollisionRoot(other, out GameObject root)) return;
        _metricsTracker?.NotifyCollisionContact(root, isEntering);
    }

    private bool TryGetCollisionRoot(Collider other, out GameObject root)
    {
        root = null;
        if (other == null) return false;

        Transform current = other.transform;
        while (current != null)
        {
            if (current.CompareTag("Agarrable")) return false;
            if (_ignoreRoot != null && current == _ignoreRoot) return false;
            current = current.parent;
        }

        root = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
        return true;
    }
}
