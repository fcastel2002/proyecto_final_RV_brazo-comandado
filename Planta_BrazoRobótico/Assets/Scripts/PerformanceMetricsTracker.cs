using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mide métricas de desempeño de la operación de agarre: cantidad de colisiones del gripper contra
/// objetos no agarrables, cantidad de operaciones (agarre + suelta) y duración de la última operación,
/// y masa del objeto actualmente agarrado. No mueve ni controla el brazo; solo observa y reporta a
/// <see cref="PerformanceMetricsPanel"/>.
/// </summary>
public class PerformanceMetricsTracker : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GripperController _gripperController;
    [SerializeField] private PerformanceMetricsPanel _metricsPanel;

    private int _collisionCount;
    private int _operationCount;
    private float _operationStartTime;
    private bool _wasHolding;

    private readonly Dictionary<GameObject, int> _activeCollisionRefs = new Dictionary<GameObject, int>();

    private void Awake()
    {
        if (_gripperController == null)
            _gripperController = GetComponent<GripperController>();
    }

    private void Start()
    {
        if (_gripperController == null)
            _gripperController = FindFirstObjectByType<GripperController>();

        _metricsPanel?.SetCollisionCount(_collisionCount);
        _metricsPanel?.SetOperationCount(_operationCount);
        _metricsPanel?.SetOperationTime(0f);
        _metricsPanel?.SetGrabbedMass(null);
    }

    private void Update()
    {
        if (_gripperController == null) return;

        bool isHolding = _gripperController.IsHoldingObject;
        if (isHolding && !_wasHolding)
        {
            _operationStartTime = Time.time;
        }
        else if (!isHolding && _wasHolding)
        {
            float duration = Time.time - _operationStartTime;
            _operationCount++;
            _metricsPanel?.SetOperationCount(_operationCount);
            _metricsPanel?.SetOperationTime(duration);
        }
        _wasHolding = isHolding;

        _metricsPanel?.SetGrabbedMass(isHolding ? _gripperController.GrabbedMass : (float?)null);
    }

    /// <summary>
    /// Llamado por <see cref="GripperCollisionCounter"/> al entrar/salir de contacto con un objeto que
    /// no tiene tag "Agarrable". Deduplica por objeto: si dos colliders del gripper tocan la misma pieza
    /// a la vez, cuenta una sola colisión, no dos.
    /// </summary>
    public void NotifyCollisionContact(GameObject other, bool isEntering)
    {
        if (other == null) return;

        _activeCollisionRefs.TryGetValue(other, out int refCount);

        if (isEntering)
        {
            if (refCount == 0)
            {
                _collisionCount++;
                _metricsPanel?.SetCollisionCount(_collisionCount);
            }
            _activeCollisionRefs[other] = refCount + 1;
        }
        else if (refCount > 0)
        {
            if (refCount <= 1)
                _activeCollisionRefs.Remove(other);
            else
                _activeCollisionRefs[other] = refCount - 1;
        }
    }
}
