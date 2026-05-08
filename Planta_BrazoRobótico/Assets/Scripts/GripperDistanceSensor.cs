using TMPro;
using UnityEngine;

public class GripperDistanceSensor : MonoBehaviour
{
    [Header("Sensor")]
    [SerializeField] private Transform sensorOrigin;
    [SerializeField] private Vector3 localDirection = Vector3.down;
    [SerializeField] private float maxDistance = 1.0f;
    [SerializeField] private float sphereRadius = 0.015f;
    [SerializeField] private LayerMask detectionMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] private float safeGripMinDistance = 0.01f;
    [SerializeField] private float safeGripMaxDistance = 0.05f;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI distanceText;
    [SerializeField] private TextMeshProUGUI operatorStatusText;
    [SerializeField] private string noHitText = "--";
    [SerializeField] private string safeGripText = "Seguro para agarrar";
    [SerializeField] private string unsafeGripText = "Ajustar posicion";
    [SerializeField] private string noDetectionStatusText = "Sin objeto detectado";

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color hitColor = Color.green;
    [SerializeField] private Color missColor = Color.red;

    public bool HasHit { get; private set; }
    public float Distance { get; private set; }
    public RaycastHit Hit { get; private set; }
    public bool IsGripDistanceSafe { get; private set; }

    private Transform Origin => sensorOrigin != null ? sensorOrigin : transform;

    private void Update()
    {
        MeasureDistance();
        UpdateUi();
    }

    private void MeasureDistance()
    {
        Transform origin = Origin;
        Vector3 direction = origin.TransformDirection(localDirection.normalized);

        HasHit = Physics.SphereCast(
            origin.position,
            sphereRadius,
            direction,
            out RaycastHit hit,
            maxDistance,
            detectionMask,
            triggerInteraction
        );

        if (HasHit)
        {
            Hit = hit;
            Distance = hit.distance;
            IsGripDistanceSafe = Distance >= safeGripMinDistance && Distance <= safeGripMaxDistance;
        }
        else
        {
            Hit = default;
            Distance = maxDistance;
            IsGripDistanceSafe = false;
        }
    }

    private void UpdateUi()
    {
        if (distanceText != null)
        {
            distanceText.text = HasHit
                ? $"{Distance * 1000f:F0} mm"
                : noHitText;
        }

        if (operatorStatusText == null) return;

        if (!HasHit)
        {
            operatorStatusText.text = noDetectionStatusText;
            return;
        }

        operatorStatusText.text = IsGripDistanceSafe ? safeGripText : unsafeGripText;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Transform origin = Origin;
        if (origin == null) return;

        Vector3 start = origin.position;
        Vector3 direction = origin.TransformDirection(localDirection.normalized);
        Vector3 end = start + direction * maxDistance;

        bool hasHit = Physics.SphereCast(
            start,
            sphereRadius,
            direction,
            out RaycastHit hit,
            maxDistance,
            detectionMask,
            triggerInteraction
        );

        Gizmos.color = hasHit ? hitColor : missColor;
        Gizmos.DrawLine(start, hasHit ? hit.point : end);
        Gizmos.DrawWireSphere(hasHit ? hit.point : end, sphereRadius);
    }
}
