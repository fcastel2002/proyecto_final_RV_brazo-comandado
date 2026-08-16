using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GripperDistanceSensor : MonoBehaviour
{
    /// <summary>Como mide el sensor la distancia al objeto mas cercano.</summary>
    public enum DetectionKind
    {
        /// <summary>Barrido de esferas escalonadas que aproxima un cono. Modo de los sensores laterales.</summary>
        Cone = 0,

        /// <summary>Trazado de un rayo con grosor sobre el eje del sensor. Modo del sensor inferior.</summary>
        SphereCast = 1
    }

    [Header("Sensor")]
    [Tooltip("Cone = barrido de esferas (sensores laterales). SphereCast = trazado de rayo con grosor (sensor inferior).")]
    [SerializeField] private DetectionKind detectionMode = DetectionKind.Cone;
    [Tooltip("Radio del rayo trazado en modo SphereCast (m). 0 = rayo puro, sin grosor.")]
    [SerializeField, Min(0f)] private float castRadius = 0.01f;
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

    [Header("Frenado por proximidad")]
    [Tooltip("Si esta activo, este sensor es el que hace que JoystickAdapter reduzca la velocidad del brazo. Solo debe marcarse en el sensor inferior: los laterales ven el suelo y dejarian el brazo frenado siempre.")]
    [SerializeField] private bool contributesToSpeedReduction = false;
    [Tooltip("Histeresis de salida: el frenado se libera al superar umbral * este factor, para que el multiplicador no oscile en el borde.")]
    [SerializeField, Min(1f)] private float slowdownReleaseFactor = 1.15f;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI distanceText;
    [SerializeField] private TextMeshProUGUI operatorStatusText;
    [SerializeField] private string noHitText = "--";
    [SerializeField] private string safeGripText = "Seguro para agarrar";
    [SerializeField] private string unsafeGripText = "Ajustar posicion";
    [SerializeField] private string noDetectionStatusText = "Sin objeto detectado";
    [Tooltip("Colorea el texto de distancia segun el estado. Solo afecta a sensores con distanceText asignado.")]
    [SerializeField] private bool colorizeDistanceText = true;
    [SerializeField] private Color noHitDistanceColor = new Color(0.70f, 0.70f, 0.70f, 1f);
    [SerializeField] private Color normalDistanceColor = Color.white;
    [SerializeField] private Color slowdownDistanceColor = new Color(1f, 0.69f, 0.13f, 1f);
    [SerializeField] private Color safeGripDistanceColor = new Color(0.30f, 0.85f, 0.39f, 1f);

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

    /// <summary>true si este sensor debe hacer que el brazo se mueva mas lento.</summary>
    public bool ContributesToSpeedReduction => contributesToSpeedReduction;

    /// <summary>
    /// Intensidad del frenado: 0 = sin frenar (en el umbral o mas lejos), 1 = pegado al objeto.
    /// Curva lineal entre ambos. La consume JoystickAdapter para interpolar el multiplicador de
    /// velocidad, de modo que la desaceleracion sea continua y no un escalon.
    /// </summary>
    public float ProximityFactor { get; private set; }

    /// <summary>
    /// Version booleana de <see cref="ProximityFactor"/>, con histeresis. Solo para HUD (color del
    /// texto y avisos): evita que el indicador parpadee justo en el borde del umbral.
    /// </summary>
    public bool IsWithinSlowdownRange { get; private set; }

    private const int OverlapBufferSize = 64;
    private const int GizmoSegments = 24;
    private const string GrabbableTag = "Agarrable";
    private static readonly HashSet<GripperDistanceSensor> VibratingSensors = new HashSet<GripperDistanceSensor>();
    private readonly Collider[] overlapBuffer = new Collider[OverlapBufferSize];
    private readonly RaycastHit[] castBuffer = new RaycastHit[OverlapBufferSize];
    private Transform ignoredHierarchyRoot;
    private GameObject cachedPayloadObject;
    private Collider[] cachedPayloadColliders;

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
        // El estado de frenado solo se recalcula desde Update(): si el componente se deshabilita
        // mientras esta activo, se quedaria congelado y JoystickAdapter dejaria el brazo frenado
        // indefinidamente.
        ResetSlowdownState();

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

        if (detectionMode == DetectionKind.SphereCast)
            MeasureWithSphereCast(startPoint, direction);
        else
            MeasureWithCone(startPoint, direction);

        // Con una pieza agarrada, la lectura util no es la distancia desde el sensor sino el hueco
        // libre POR DEBAJO de la pieza: el propio objeto agarrado no se detecta (es hijo del robot,
        // lo descarta CanDetect), asi que hay que descontar cuanto sobresale.
        if (HasHit)
        {
            float payloadExtent = GetPayloadExtent(startPoint, direction);
            if (payloadExtent > 0f)
                Distance = Mathf.Max(0f, Distance - payloadExtent);
        }

        // Acotado a piezas agarrables: al ampliar la mascara el sensor ve tambien el suelo, y sin
        // este filtro el mensaje "Seguro para agarrar" aparaceria al acercarse al piso.
        IsGripDistanceSafe = HasHit &&
            Distance >= safeGripMinDistance &&
            Distance <= safeGripMaxDistance &&
            DetectedCollider != null &&
            DetectedCollider.CompareTag(GrabbableTag);

        UpdateSlowdownState();
    }

    /// <summary>
    /// Cuanto sobresale la pieza agarrada a lo largo del eje del sensor, en metros. Se proyectan los
    /// 8 vertices del AABB de cada collider sobre la direccion de medida y se toma el maximo.
    /// </summary>
    private float GetPayloadExtent(Vector3 startPoint, Vector3 direction)
    {
        GameObject payload = gripperController != null ? gripperController.GrabbedObject : null;
        if (payload == null)
        {
            cachedPayloadObject = null;
            cachedPayloadColliders = null;
            return 0f;
        }

        // Se cachea porque MeasureDistance() corre en Update(): un GetComponentsInChildren por
        // fotograma seria una asignacion por fotograma.
        if (!ReferenceEquals(payload, cachedPayloadObject))
        {
            cachedPayloadObject = payload;
            cachedPayloadColliders = payload.GetComponentsInChildren<Collider>();
        }

        if (cachedPayloadColliders == null) return 0f;

        float extent = 0f;
        for (int colliderIndex = 0; colliderIndex < cachedPayloadColliders.Length; colliderIndex++)
        {
            Collider payloadCollider = cachedPayloadColliders[colliderIndex];
            if (payloadCollider == null || !payloadCollider.enabled) continue;

            Bounds bounds = payloadCollider.bounds;
            Vector3 center = bounds.center;
            Vector3 ext = bounds.extents;

            // |Dot| de los semiejes = alcance del AABB sobre la direccion desde su centro.
            float projectedRadius =
                Mathf.Abs(direction.x) * ext.x +
                Mathf.Abs(direction.y) * ext.y +
                Mathf.Abs(direction.z) * ext.z;

            float reach = Vector3.Dot(center - startPoint, direction) + projectedRadius;
            if (reach > extent)
                extent = reach;
        }

        return Mathf.Max(0f, extent);
    }

    /// <summary>
    /// Traza un rayo con grosor sobre el eje del sensor y se queda con el impacto valido mas cercano.
    /// Se usa SphereCastNonAlloc (y no un SphereCast simple) porque hay que descartar los impactos
    /// contra el propio robot via <see cref="CanDetect"/>: un cast simple devolveria el primero,
    /// que puede ser la propia garra.
    /// </summary>
    private void MeasureWithSphereCast(Vector3 startPoint, Vector3 direction)
    {
        // Retrocedemos el origen el radio del trazado para que un objeto que ya solapa el punto
        // de partida devuelva una distancia util en vez de 0.
        float radius = Mathf.Max(0f, castRadius);
        Vector3 castOrigin = startPoint - direction * radius;
        float castDistance = maxDistance + radius;

        int hitCount = radius > 0f
            ? Physics.SphereCastNonAlloc(castOrigin, radius, direction, castBuffer, castDistance, detectionMask, triggerInteraction)
            : Physics.RaycastNonAlloc(castOrigin, direction, castBuffer, castDistance, detectionMask, triggerInteraction);

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit candidate = castBuffer[hitIndex];
            if (!CanDetect(candidate.collider)) continue;

            // Si el cast arranca ya solapado, Unity devuelve distance 0 y point (0,0,0):
            // en ese caso caemos al punto mas cercano del collider.
            Vector3 hitPoint = candidate.distance > 0f
                ? candidate.point
                : candidate.collider.ClosestPoint(startPoint);

            // Distancia euclidea al punto de impacto: misma semantica que el modo cono, para que
            // la lectura en mm no de un salto al cambiar de modo de deteccion.
            float candidateDistance = Vector3.Distance(startPoint, hitPoint);
            if (candidateDistance > maxDistance) continue;
            if (HasHit && candidateDistance >= Distance) continue;

            HasHit = true;
            Distance = candidateDistance;
            Hit = candidate;
            DetectedCollider = candidate.collider;
            DetectionPoint = hitPoint;
        }
    }

    private void UpdateSlowdownState()
    {
        if (!contributesToSpeedReduction || !ProximitySlowdownSettings.IsEnabled || !HasHit)
        {
            ResetSlowdownState();
            return;
        }

        float threshold = ProximitySlowdownSettings.ThresholdMeters;

        // Curva lineal: 0 en el umbral, 1 al contacto. El piso del frenado (cuanto se reduce como
        // maximo) lo decide JoystickAdapter; aqui solo vive la geometria.
        ProximityFactor = Mathf.Clamp01(1f - Distance / threshold);

        // El booleano es solo para el HUD y lleva histeresis para no parpadear en el borde.
        float activeThreshold = IsWithinSlowdownRange
            ? threshold * Mathf.Max(1f, slowdownReleaseFactor)
            : threshold;

        IsWithinSlowdownRange = Distance <= activeThreshold;
    }

    private void ResetSlowdownState()
    {
        ProximityFactor = 0f;
        IsWithinSlowdownRange = false;
    }

    private void MeasureWithCone(Vector3 startPoint, Vector3 direction)
    {
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

            if (colorizeDistanceText)
                distanceText.color = ResolveDistanceColor();
        }

        if (operatorStatusText == null) return;

        if (!HasHit)
        {
            operatorStatusText.text = noDetectionStatusText;
            return;
        }

        operatorStatusText.text = IsGripDistanceSafe ? safeGripText : unsafeGripText;
    }

    /// <summary>
    /// Prioridad: sin objeto > rango de agarre > zona de frenado > normal. El agarre gana al
    /// frenado porque siempre queda por dentro del umbral y es la informacion mas accionable.
    /// </summary>
    private Color ResolveDistanceColor()
    {
        if (!HasHit) return noHitDistanceColor;
        if (IsGripDistanceSafe) return safeGripDistanceColor;
        if (IsWithinSlowdownRange) return slowdownDistanceColor;

        return normalDistanceColor;
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
        castRadius = Mathf.Max(0f, castRadius);
        slowdownReleaseFactor = Mathf.Max(1f, slowdownReleaseFactor);
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

        Gizmos.color = Application.isPlaying && HasHit ? hitColor : missColor;

        if (detectionMode == DetectionKind.SphereCast)
        {
            DrawSphereCastGizmo(start, direction);
        }
        else
        {
            float baseRadius = coneTipRadius + maxDistance * Mathf.Tan(coneHalfAngle * Mathf.Deg2Rad);
            BuildConeBasis(direction, out Vector3 right, out Vector3 up);
            DrawConeGizmo(start, direction, right, up, baseRadius);
        }

        if (Application.isPlaying && HasHit)
            Gizmos.DrawWireSphere(DetectionPoint, Mathf.Max(0.005f, coneTipRadius));
    }

    private void DrawSphereCastGizmo(Vector3 start, Vector3 direction)
    {
        float radius = Mathf.Max(0f, castRadius);
        float length = Application.isPlaying && HasHit ? Distance : maxDistance;
        Vector3 end = start + direction * length;

        Gizmos.DrawLine(start, end);
        if (radius <= 0f) return;

        BuildConeBasis(direction, out Vector3 right, out Vector3 up);
        Gizmos.DrawWireSphere(start, radius);
        Gizmos.DrawWireSphere(end, radius);

        Gizmos.DrawLine(start + right * radius, end + right * radius);
        Gizmos.DrawLine(start - right * radius, end - right * radius);
        Gizmos.DrawLine(start + up * radius, end + up * radius);
        Gizmos.DrawLine(start - up * radius, end - up * radius);
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
