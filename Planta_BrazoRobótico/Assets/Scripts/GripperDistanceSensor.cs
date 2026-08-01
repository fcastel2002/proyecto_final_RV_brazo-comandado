using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GripperDistanceSensor : MonoBehaviour
{
    [Header("Sensor")]
    [SerializeField] private Transform sensorOrigin;
    [SerializeField] private Vector3 raycastOffset = Vector3.zero;
    [SerializeField] private Vector3 localDirection = Vector3.forward;
    [SerializeField] private float maxDistance = 0.4f;
    [SerializeField, Range(1f, 75f)] private float coneHalfAngle = 22.5f;
    [SerializeField, Range(3, 24)] private int coneSamples = 8;
    [SerializeField, Min(0f)] private float coneTipRadius = 0.005f;
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

    [Header("Joystick vibration")]
    [SerializeField] private JoystickVibrationHidOutput joystickVibration;
    [SerializeField] private GripperController gripperController;
    [SerializeField] private bool autoFindJoystickVibration = true;
    [SerializeField] private bool vibrateOnProximity = true;
    [SerializeField] private float vibrationStartDistance = 0.15f;
    [SerializeField] private float vibrationStopDistance = 0.18f;
    [SerializeField] private bool stopVibrationOnDisable = true;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color hitColor = Color.green;
    [SerializeField] private Color missColor = Color.red;

    public bool HasHit { get; private set; }
    public float Distance { get; private set; }
    public RaycastHit Hit { get; private set; }
    public Collider DetectedCollider { get; private set; }
    public Vector3 DetectionPoint { get; private set; }
    public bool IsGripDistanceSafe { get; private set; }
    public bool IsVibratingForProximity { get; private set; }

    private const int OverlapBufferSize = 64;
    private const int GizmoSegments = 24;
    private static readonly HashSet<GripperDistanceSensor> VibratingSensors = new HashSet<GripperDistanceSensor>();
    private readonly Collider[] overlapBuffer = new Collider[OverlapBufferSize];
    private Transform ignoredHierarchyRoot;

    private Transform Origin => sensorOrigin != null ? sensorOrigin : transform;

    private void Awake()
    {
        if (joystickVibration == null && autoFindJoystickVibration)
            joystickVibration = FindFirstObjectByType<JoystickVibrationHidOutput>();

        if (gripperController == null)
            gripperController = FindFirstObjectByType<GripperController>();

        ignoredHierarchyRoot = gripperController != null
            ? gripperController.transform.root
            : transform.root;

        vibrationStartDistance = Mathf.Max(0f, vibrationStartDistance);
        vibrationStopDistance = Mathf.Max(vibrationStartDistance, vibrationStopDistance);
    }

    private void Update()
    {
        MeasureDistance();
        UpdateUi();
        UpdateJoystickVibration();
    }

    private void OnDisable()
    {
        if (!stopVibrationOnDisable) return;

        SetProximityVibration(false);
    }

    private void MeasureDistance()
    {
        Transform origin = Origin;
        Vector3 direction = origin.TransformDirection(localDirection.normalized);
        Vector3 startPoint = origin.position + origin.TransformDirection(raycastOffset);

        HasHit = false;
        Hit = default;
        DetectedCollider = null;
        DetectionPoint = startPoint + direction * maxDistance;
        Distance = maxDistance;

        float coneSlope = Mathf.Tan(coneHalfAngle * Mathf.Deg2Rad);
        for (int sampleIndex = 1; sampleIndex <= coneSamples; sampleIndex++)
        {
            float sampleRatio = sampleIndex / (float)coneSamples;
            float axialDistance = maxDistance * sampleRatio;
            float sampleRadius = coneTipRadius + axialDistance * coneSlope;
            Vector3 sampleCenter = startPoint + direction * axialDistance;

            int overlapCount = Physics.OverlapSphereNonAlloc(
                sampleCenter,
                sampleRadius,
                overlapBuffer,
                detectionMask,
                triggerInteraction);

            for (int colliderIndex = 0; colliderIndex < overlapCount; colliderIndex++)
            {
                Collider candidate = overlapBuffer[colliderIndex];
                if (!CanDetect(candidate)) continue;

                Vector3 closestPoint = candidate.ClosestPoint(sampleCenter);
                Vector3 fromOrigin = closestPoint - startPoint;
                float candidateAxialDistance = Vector3.Dot(fromOrigin, direction);
                if (candidateAxialDistance < 0f || candidateAxialDistance > maxDistance) continue;

                Vector3 radialVector = fromOrigin - direction * candidateAxialDistance;
                float allowedRadius = coneTipRadius + candidateAxialDistance * coneSlope;
                if (radialVector.sqrMagnitude > allowedRadius * allowedRadius) continue;

                float candidateDistance = fromOrigin.magnitude;
                if (HasHit && candidateDistance >= Distance) continue;

                HasHit = true;
                Distance = candidateDistance;
                DetectedCollider = candidate;
                DetectionPoint = closestPoint;
            }
        }

        IsGripDistanceSafe = HasHit &&
            Distance >= safeGripMinDistance &&
            Distance <= safeGripMaxDistance;
    }

    private bool CanDetect(Collider candidate)
    {
        if (candidate == null || !candidate.enabled) return false;

        return ignoredHierarchyRoot == null ||
            !candidate.transform.IsChildOf(ignoredHierarchyRoot);
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

    private void UpdateJoystickVibration()
    {
        if (!vibrateOnProximity || joystickVibration == null)
        {
            SetProximityVibration(false);
            return;
        }

        // Si ya tenemos un objeto agarrado, no debemos vibrar
        if (gripperController != null && gripperController.IsHoldingObject)
        {
            SetProximityVibration(false);
            return;
        }

        float threshold = IsVibratingForProximity
            ? vibrationStopDistance
            : vibrationStartDistance;

        bool shouldVibrate = HasHit && Distance <= threshold;
        SetProximityVibration(shouldVibrate);
    }

    private void SetProximityVibration(bool enabled)
    {
        if (IsVibratingForProximity == enabled) return;

        IsVibratingForProximity = enabled;
        if (enabled)
            VibratingSensors.Add(this);
        else
            VibratingSensors.Remove(this);

        joystickVibration?.SetMotor(VibratingSensors.Count > 0);
    }

    private void OnValidate()
    {
        maxDistance = Mathf.Max(0f, maxDistance);
        coneHalfAngle = Mathf.Clamp(coneHalfAngle, 1f, 75f);
        coneSamples = Mathf.Clamp(coneSamples, 3, 24);
        coneTipRadius = Mathf.Max(0f, coneTipRadius);
        safeGripMinDistance = Mathf.Max(0f, safeGripMinDistance);
        safeGripMaxDistance = Mathf.Max(safeGripMinDistance, safeGripMaxDistance);
        
        vibrationStartDistance = Mathf.Max(0f, vibrationStartDistance);
        vibrationStopDistance = Mathf.Max(vibrationStartDistance, vibrationStopDistance);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Transform origin = Origin;
        if (origin == null) return;

        Vector3 start = origin.position + origin.TransformDirection(raycastOffset);
        Vector3 direction = origin.TransformDirection(localDirection.normalized);
        float baseRadius = coneTipRadius + maxDistance * Mathf.Tan(coneHalfAngle * Mathf.Deg2Rad);
        BuildConeBasis(direction, out Vector3 right, out Vector3 up);

        Gizmos.color = Application.isPlaying && HasHit ? hitColor : missColor;
        DrawConeGizmo(start, direction, right, up, baseRadius);

        if (Application.isPlaying && HasHit)
            Gizmos.DrawWireSphere(DetectionPoint, Mathf.Max(0.005f, coneTipRadius));
    }

    private void DrawConeGizmo(Vector3 start, Vector3 direction, Vector3 right, Vector3 up, float baseRadius)
    {
        Vector3 baseCenter = start + direction * maxDistance;
        Vector3 previousPoint = baseCenter + right * baseRadius;

        for (int segment = 1; segment <= GizmoSegments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / GizmoSegments;
            Vector3 radialDirection = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            Vector3 currentPoint = baseCenter + radialDirection * baseRadius;
            Gizmos.DrawLine(previousPoint, currentPoint);

            if (segment % 6 == 0)
                Gizmos.DrawLine(start, currentPoint);

            previousPoint = currentPoint;
        }

        for (int ringIndex = 1; ringIndex < 4; ringIndex++)
        {
            float ratio = ringIndex / 4f;
            DrawConeRing(
                start + direction * maxDistance * ratio,
                right,
                up,
                coneTipRadius + (baseRadius - coneTipRadius) * ratio);
        }
    }

    private static void DrawConeRing(Vector3 center, Vector3 right, Vector3 up, float radius)
    {
        Vector3 previousPoint = center + right * radius;
        for (int segment = 1; segment <= GizmoSegments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / GizmoSegments;
            Vector3 currentPoint = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }

    private static void BuildConeBasis(Vector3 direction, out Vector3 right, out Vector3 up)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f
            ? Vector3.forward
            : Vector3.up;
        right = Vector3.Cross(direction, reference).normalized;
        up = Vector3.Cross(right, direction).normalized;
    }
}
