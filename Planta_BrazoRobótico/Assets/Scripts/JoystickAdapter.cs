using Preliy.Flange;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mueve el efector final del robot en coordenadas cartesianas con el joystick.
/// Presionar L3 alterna entre modo Robot y modo Camara.
///
/// Al volver del modo Camara al modo Robot se recalcula automaticamente cual
/// eje local del efector final (right / forward) debe responder a cada palanca,
/// de modo que MoveZ siempre empuje "hacia el robot desde donde estas parado"
/// y MoveX se desplace en perpendicular.
/// </summary>
public class JoystickAdapter : MonoBehaviour
{
    public struct IkDiagnosticSample
    {
        public bool IsValid;
        public float Time;
        public float PoseError;
        public float RotationError;
        public float TargetStep;
        public float TargetRotationStep;
        public float MaxJointError;
        public float WristJointError;
        public float StepScale;
        public float JointStepLimit;
        public float InputMagnitude;
    }

    [Header("Controller")]
    [SerializeField] private Controller _controller;

    [Header("Input Actions - Robot")]
    [SerializeField] private InputActionReference _moveX;
    [SerializeField] private InputActionReference _moveY;
    [SerializeField] private InputActionReference _moveZ;

    [Header("Input Actions - J6")]
    [SerializeField] private InputActionReference _j6AntiHorAction;
    [SerializeField] private InputActionReference _j6HorAction;
    [SerializeField] private InputActionReference _j6HomeAction;

    [Header("Axis Direction")]
    [SerializeField] private bool _invertMoveX;
    [SerializeField] private bool _invertMoveY;
    [SerializeField] private bool _invertMoveZ;
    [SerializeField] private AnalogCalibrationManager _calibrationManager;

    [Header("Mode Toggle")]
    [SerializeField] private InputActionReference _modoCamara;
    [SerializeField] private bool _cameraModeAllowed = true;

    [Header("Efector final (para el remapeo de ejes)")]
    [Tooltip("Asignar el transform del Joint_6 / Flange")]
    [SerializeField] private Transform _endEffector;

    [Header("Settings")]
    [Tooltip("Max linear speed (m/s)")]
    [SerializeField] private float _speed = 0.1f;

    [Header("UI - Accion de control por articulacion")]
    [Tooltip("Panel estático (PID_Section) que muestra la accion de control de J1 a J6.")]
    [SerializeField] private PidActionsPanel _pidActionsPanel;

    [Header("Control PID")]
    [Tooltip("Ganancia proporcional. Unidades: (°/s²) / (°) a inercia de referencia.")]
    [SerializeField] private float _kpBase = 20f;
    [Tooltip("Ganancia integral. Unidades: (°/s²) / (°·s) a inercia de referencia.")]
    [SerializeField] private float _kiBase = 1f;
    [Tooltip("Ganancia derivativa. Unidades: (°/s²) / (°/s) a inercia de referencia.")]
    [SerializeField] private float _kdBase = 0.5f;

    [Header("Simulación de Inercia")]
    [Tooltip("Inercia de referencia (kg·m²). J2 del KUKA ≈ 100 kg·m². El resto de joints escala relativamente.")]
    [SerializeField] private float _referenceInertia = 100f;
    [Tooltip("Amortiguamiento viscoso (s⁻¹). Previene que los joints se aceleren indefinidamente.")]
    [SerializeField] private float _velocityDamping = 5f;
    [Tooltip("Velocidad angular máxima por joint (°/s). Límite de seguridad cinemático.")]
    [SerializeField] private float _maxJointVelocity = 360f;
    [Tooltip("Aceleración angular máxima por joint (°/s²). Límite de seguridad dinámico para evitar tirones fuertes.")]
    [SerializeField] private float _maxJointAcceleration = 720f;

    [Header("Simulación de Carga (Payload)")]
    [Tooltip("GripperController que reporta el objeto agarrado y su masa. Si se deja vacío, se busca automáticamente en escena.")]
    [SerializeField] private GripperController _gripperController;
    [Tooltip("Multiplicador sobre la masa del objeto agarrado al sumarla a la inercia efectiva (RobotDynamics). 1 = física real. >1 exagera el efecto; 0 lo desactiva. Nota: el feedforward de velocidad del PID cancela casi todo este efecto en régimen estable; ver _maxSimulatedPayloadMass para el efecto perceptible en velocidad.")]
    [SerializeField, Min(0f)] private float _payloadInertiaMultiplier = 1f;
    [Tooltip("Masa (kg) a partir de la cual la velocidad cartesiana cae hasta _minPayloadSpeedMultiplier. Con el 'Cubo Prueba' de Mass=20 como referencia.")]
    [SerializeField, Min(0.01f)] private float _maxSimulatedPayloadMass = 20f;
    [Tooltip("Fracción mínima de velocidad cartesiana permitida al cargar el objeto más pesado soportado (nunca 0, para no bloquear al operador).")]
    [SerializeField, Range(0.05f, 1f)] private float _minPayloadSpeedMultiplier = 0.25f;

    [Header("Diagnostico IK")]
    [Tooltip("Loguea error entre el target cartesiano enviado a IK y la FK de solution.JointTarget.")]
    [SerializeField] private bool _logIkPoseError = true;
    [SerializeField, Min(0.02f)] private float _ikPoseLogInterval = 0.5f;

    [Header("J6 Limits")]
    [SerializeField] private float _j6MinLimit = -360f;
    [SerializeField] private float _j6MaxLimit = 360f;

    [Header("TCP Orientation Mode")]
    public bool AlignOrientationWithJ1 = false;

    [Header("Workspace Safety Limits")]
    [SerializeField] private bool _enableWorkspaceLimits = true;
    [SerializeField] private float _minHorizontalRadius = 0.3f;
    [SerializeField] private float _maxHorizontalRadius = 2.6f;
    [SerializeField] private float _minHeight = -0.2f;
    [SerializeField] private float _maxHeight = 1.8f;
    [SerializeField] private bool _forceVerticalGripper = true;
    [SerializeField] private float _safetyDriftStartThreshold = 3.0f; // grados
    [SerializeField] private float _safetyDriftMaxTolerance = 5.0f; // grados

    /// <summary>true = modo camara activo, false = modo robot activo.</summary>
    public static bool IsCameraMode { get; private set; }
    public static bool IsJ6ExclusiveMode { get; private set; }

    private bool _resettingJ6 = false;
    public bool ResettingJ6 => _resettingJ6;

    public void ResetJ6ToZero()
    {
        _resettingJ6 = true;
        if (_controller != null && _controller.IsValid.Value)
        {
            _j6TargetAngle = _controller.MechanicalGroup.JointState[5];
        }
        else
        {
            _j6TargetAngle = 17.7f;
        }
        Debug.Log($"[JoystickAdapter] Reset J6 to 17.7° started. Initializing target angle to current J6: {_j6TargetAngle:F1}°");
    }

    public static void SetJ6ExclusiveMode(bool active)
    {
        IsJ6ExclusiveMode = active;
        var adapter = FindFirstObjectByType<JoystickAdapter>();
        if (adapter != null)
        {
            if (active)
            {
                if (adapter._controller != null && adapter._controller.IsValid.Value)
                {
                    adapter._j6TargetAngle = adapter._hasPrevIkTarget ? adapter._prevIkTarget[5] : adapter._controller.MechanicalGroup.JointState[5];
                    adapter._hasUnwrappedStickAngle = false;
                }
            }
            else
            {
                adapter._velocity = Vector3.zero;
                adapter._orientationCaptured = false;
                adapter.CaptureFixedOrientation();
            }
        }
    }

    public IkDiagnosticSample LastIkDiagnostic { get; private set; }

    private Vector3 _dirZ = Vector3.forward;
    private Vector3 _dirX = Vector3.right;
    private float _signZ = 1f;
    private float _signX = 1f;

    private const int FirstWristJoint = 3;
    private const float MotionInputEpsilon = 1e-6f;

    private Vector3 _velocity;
    private JointPID[] _pids;
    private float[] _jointVelocity = new float[6];
    private readonly float[] _prevIkTarget = new float[6];
    private Quaternion _fixedTcpFrameOrientation;
    private float _initialJ1Angle;
    private bool _orientationCaptured;
    private bool _motionActive;
    private bool _hasPrevIkTarget;
    private bool _inputSuppressed;
    private bool _diagnosticInputOverrideActive;
    private Vector3 _diagnosticInputOverrideVelocity;
    private float _nextIkPoseLogTime;
    private float _lastCameraClickTime;
    private float _doubleClickDeadline;
    private bool _waitingForDoubleClick;
    private float _j6TargetAngle;
    private string _jsonLogPath;
    private float _nextJsonLogTime;
    private readonly float[] _minLimits = new float[6];
    private readonly float[] _maxLimits = new float[6];
    private float _prevStickAngle;
    private bool _hasUnwrappedStickAngle;
    private readonly float[] _lastJointControlActions = new float[6];

    public float[] LastJointControlActions => _lastJointControlActions;

    private void Awake()
    {
        if (_calibrationManager == null)
            _calibrationManager = FindFirstObjectByType<AnalogCalibrationManager>();

        if (_gripperController == null)
            _gripperController = FindFirstObjectByType<GripperController>();

        if (Application.isBatchMode)
        {
            // Desactivar solo las cámaras offscreen (con targetTexture) para evitar crashes en modo -nographics
            // manteniendo la cámara principal activa para que el PlayerLoop no se congele.
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera cam in cameras)
            {
                if (cam.targetTexture != null)
                {
                    cam.enabled = false;
                    Debug.Log($"[JoystickAdapter] Cámara offscreen '{cam.name}' desactivada para evitar crashes en modo batch.");
                }
            }
        }
    }

    private void OnEnable()
    {
        EnableInputActions();
    }

    private void OnDisable()
    {
        DisableInputActions();
    }

    public void ApplyInputActions(
        InputActionReference moveX,
        InputActionReference moveY,
        InputActionReference moveZ,
        InputActionReference modoCamara,
        bool cameraModeAllowed,
        InputActionReference j6AntiHor = null,
        InputActionReference j6Hor = null,
        InputActionReference j6Home = null)
    {
        DisableInputActions();

        _moveX = moveX;
        _moveY = moveY;
        _moveZ = moveZ;
        _modoCamara = modoCamara;
        _cameraModeAllowed = cameraModeAllowed;
        _j6AntiHorAction = j6AntiHor;
        _j6HorAction = j6Hor;
        _j6HomeAction = j6Home;

        ExitCameraModeForProfileChange();
        ResetInputState();

        if (isActiveAndEnabled)
            EnableInputActions();
    }

    private void Start()
    {
        if (_endEffector != null)
            InitDefaultMapping();

        InitPIDs();
        TryLoadJointLimitsDynamic();
        
        // Fuerza el inicio de J6 a 17.7 grados para evitar error de wrap y salto en orientacion (e.g., 350 grados en lugar de -10)
        if (_controller != null && _controller.IsValid.Value)
        {
            float[] initJoints = new float[6];
            for (int i = 0; i < 6; i++) initJoints[i] = _controller.MechanicalGroup.JointState[i];
            
            if (Mathf.Abs(initJoints[5] - 17.7f) > 0.1f)
            {
                initJoints[5] = 17.7f;
                _controller.MechanicalGroup.SetJoints(new Preliy.Flange.JointTarget(initJoints), notify: true);
                Debug.Log("[JoystickAdapter] J6 inicializado forzadamente a 17.7 grados.");
            }
        }
        
        // Inicializar ruta del log de diagnóstico JSON
        string logsDir = Application.dataPath + "/../Logs";
        if (!System.IO.Directory.Exists(logsDir))
        {
            System.IO.Directory.CreateDirectory(logsDir);
        }
        _jsonLogPath = logsDir + "/control_diagnostics_log.json";
        
        CaptureFixedOrientation();
        ClearJointActionDisplay();
        
        // Registrar inicio del sistema de diagnóstico
        LogDiagnosticJson("System_Start", "Sistema de control y diagnosticos estructurados inicializado exitosamente.");
    }

    private void TryLoadJointLimitsDynamic()
    {
        // Inicializar con valores por defecto seguros por si falla la reflexión
        for (int i = 0; i < 6; i++)
        {
            _minLimits[i] = -360f;
            _maxLimits[i] = 360f;
        }
        
        // J6 conserva sus campos serializados para compatibilidad
        _minLimits[5] = _j6MinLimit;
        _maxLimits[5] = _j6MaxLimit;

        try
        {
            if (_controller != null && _controller.MechanicalGroup != null && _controller.MechanicalGroup.RobotJoints != null)
            {
                int count = Mathf.Min(6, _controller.MechanicalGroup.RobotJoints.Count);
                for (int i = 0; i < count; i++)
                {
                    var joint = _controller.MechanicalGroup.RobotJoints[i];
                    var configField = joint.GetType().GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (configField != null)
                    {
                        var config = configField.GetValue(joint);
                        if (config != null)
                        {
                            var limitsProp = config.GetType().GetProperty("Limits", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (limitsProp != null)
                            {
                                Vector2 limits = (Vector2)limitsProp.GetValue(config);
                                _minLimits[i] = limits.x;
                                _maxLimits[i] = limits.y;
                                Debug.Log($"[JoystickAdapter] Límites J{i+1} cargados dinámicamente: [{_minLimits[i]}, {_maxLimits[i]}]");
                            }
                        }
                    }
                }
                
                // Sincronizar campos antiguos de J6 para compatibilidad
                _j6MinLimit = _minLimits[5];
                _j6MaxLimit = _maxLimits[5];
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[JoystickAdapter] No se pudieron cargar los límites dinámicamente por reflexión: {e.Message}. Usando valores por defecto.");
        }
    }

    private void LogDiagnosticJson(string eventType, string message, float driftVal = 0f, float speedMult = 1f)
    {
        if (string.IsNullOrEmpty(_jsonLogPath)) return;
        if (Time.time < _nextJsonLogTime) return;
        _nextJsonLogTime = Time.time + 0.1f; // Limitar a 10 Hz máximo para evitar I/O bottleneck

        try
        {
            float[] joints = new float[6];
            if (_controller != null && _controller.IsValid.Value)
            {
                for (int i = 0; i < 6; i++)
                    joints[i] = _controller.MechanicalGroup.JointState[i];
            }

            string jointsJson = $"[{joints[0]:F2},{joints[1]:F2},{joints[2]:F2},{joints[3]:F2},{joints[4]:F2},{joints[5]:F2}]";
            Vector3 tcpPos = Vector3.zero;
            if (_controller != null && _controller.IsValid.Value)
            {
                tcpPos = (Vector3)_controller.PoseObserver.ToolCenterPointFrame.Value.GetColumn(3);
            }
            string tcpJson = $"[{tcpPos.x:F4},{tcpPos.y:F4},{tcpPos.z:F4}]";

            string jsonLine = $"{{\"time\":{Time.time:F3},\"event\":\"{eventType}\",\"msg\":\"{message}\",\"drift\":{driftVal:F3},\"speedMult\":{speedMult:F3},\"tcp\":{tcpJson},\"joints\":{jointsJson}}}";
            
            System.IO.File.AppendAllText(_jsonLogPath, jsonLine + "\n");
        }
        catch (System.Exception e)
        {
            // Failsafe silencioso para no interrumpir el hilo principal si falla la escritura en disco
            Debug.LogWarning($"[JoystickAdapter] Falló la escritura del log JSON: {e.Message}");
        }
    }

    public void ToggleCameraMode()
    {
        if (!_cameraModeAllowed)
            return;

        float currentTime = Time.unscaledTime;
        float delta = currentTime - _lastCameraClickTime;
        _lastCameraClickTime = currentTime;

        if (_waitingForDoubleClick)
        {
            // Segundo clic llegó dentro de la ventana: activar/desactivar modo J6
            _waitingForDoubleClick = false;
            SetJ6ExclusiveMode(!IsJ6ExclusiveMode);
            Debug.Log($"[JoystickAdapter] Modo J6 Exclusivo: {IsJ6ExclusiveMode}");
            return;
        }

        // Primer clic: NO cambiamos la cámara todavía, esperamos a ver si viene un segundo clic
        _waitingForDoubleClick = true;
        _doubleClickDeadline = currentTime + 0.4f; // 400ms de ventana
    }

    /// <summary>
    /// Llamar desde Update() para procesar el timeout del doble clic.
    /// Si la ventana expira sin segundo clic, ejecutamos el toggle de cámara normal.
    /// </summary>
    private void ProcessDoubleClickTimeout()
    {
        if (!_waitingForDoubleClick) return;
        if (Time.unscaledTime < _doubleClickDeadline) return;

        // Expiró la ventana: ejecutar toggle de cámara normal
        _waitingForDoubleClick = false;

        bool wasCameraMode = IsCameraMode;
        IsCameraMode = !IsCameraMode;

        if (wasCameraMode && !IsCameraMode)
        {
            RemapAxesFromCamera();
            ResetPIDs();
            _motionActive = false;
        }

        Debug.Log($"[JoystickAdapter] Modo: {(IsCameraMode ? "CAMARA" : "ROBOT")} | dirZ={_dirZ * _signZ} | dirX={_dirX * _signX}");
    }

    public void SetInputSuppressed(bool suppressed)
    {
        if (_inputSuppressed == suppressed)
            return;

        _inputSuppressed = suppressed;

        if (_inputSuppressed)
        {
            _velocity = Vector3.zero;
            ResetPIDs();
            _motionActive = false;
            ClearJointActionDisplay();
        }
    }

    public void SetDiagnosticInputOverride(Vector3 worldVelocity)
    {
        _diagnosticInputOverrideActive = true;
        _diagnosticInputOverrideVelocity = worldVelocity;
        _velocity = worldVelocity;
    }

    public void ClearDiagnosticInputOverride()
    {
        _diagnosticInputOverrideActive = false;
        _diagnosticInputOverrideVelocity = Vector3.zero;
        _velocity = Vector3.zero;

        if (_controller != null && _controller.IsValid.Value)
            EndMotion();
    }

    private void Update()
    {
        ProcessDoubleClickTimeout();

        if (_j6HomeAction != null && _j6HomeAction.action.WasPressedThisFrame())
        {
            ResetJ6ToZero();
        }

        if (_inputSuppressed)
        {
            _velocity = Vector3.zero;
            ClearJointActionDisplay();
            return;
        }

        if (IsCameraMode)
        {
            _velocity = Vector3.zero;
            ClearJointActionDisplay();
            return;
        }

        if (IsJ6ExclusiveMode)
        {
            _velocity = Vector3.zero;
            return;
        }

        if (_diagnosticInputOverrideActive)
        {
            _velocity = _diagnosticInputOverrideVelocity;
            return;
        }

        float rawX = ReadAxis(_moveX, _invertMoveX);
        float rawY = ReadAxis(_moveY, _invertMoveY);
        float rawZ = ReadAxis(_moveZ, _invertMoveZ);

        _velocity = _dirX * (rawX * _signX)
                  + Vector3.up * rawY
                  + _dirZ * (rawZ * _signZ);
    }

    private int _fixedUpdateCount = 0;

    private void FixedUpdate()
    {
        _fixedUpdateCount++;
        if (Application.isBatchMode && (_fixedUpdateCount < 10 || _fixedUpdateCount % 20 == 0))
        {
            Debug.Log($"[JoystickAdapter][Debug] FixedUpdate #{_fixedUpdateCount}: IsValid={(_controller != null ? _controller.IsValid.Value.ToString() : "null")}, Velocity={_velocity.magnitude:F3}, MotionActive={_motionActive}, OrientationCaptured={_orientationCaptured}");
        }

        if (IsCameraMode) return;
        if (_controller == null || !_controller.IsValid.Value) return;

        float dt = Time.fixedDeltaTime;

        if (_resettingJ6)
        {
            // Encontrar el múltiplo de 360 más cercano para volver a la posición cero física más cercana (con offset de 17.7°)
            float targetZero = Mathf.Round((_j6TargetAngle - 17.7f) / 360f) * 360f + 17.7f;
            targetZero = Mathf.Clamp(targetZero, _j6MinLimit, _j6MaxLimit);
            
            _j6TargetAngle = Mathf.MoveTowards(_j6TargetAngle, targetZero, 90f * dt);
            float currentJ6 = (_controller != null && _controller.IsValid.Value) ? _controller.MechanicalGroup.JointState[5] : 0f;
            if (Mathf.Approximately(_j6TargetAngle, targetZero) && Mathf.Abs(Mathf.DeltaAngle(currentJ6, targetZero)) < 0.1f)
            {
                _j6TargetAngle = targetZero;
                _resettingJ6 = false;
                _orientationCaptured = false;
                CaptureFixedOrientation();
                Debug.Log($"[JoystickAdapter] Reset J6 to {targetZero}° completed. Recaptured orientation.");
            }
        }

        if (IsJ6ExclusiveMode)
        {
            UpdateJ6ExclusiveControl();
            return;
        }

        if (_velocity.sqrMagnitude < MotionInputEpsilon && !_resettingJ6)
        {
            EndMotion();
            return;
        }

        if (!_motionActive)
            BeginMotion();

        if (!_orientationCaptured)
        {
            CaptureFixedOrientation();
            if (!_orientationCaptured) return;
        }

        var frame = _controller.Frame.Value;
        var tool = _controller.Tool.Value;
        var configuration = _controller.Configuration.Value;
        var extJoint = _controller.MechanicalGroup.JointState.ExtJoint;

        // ── Guarda de orientación física ──────────────────────────────────
        // Verificamos la orientación REAL del robot (no la matemática).
        // Calculamos la orientación esperada girando la referencia fija
        // según cuánto haya rotado la base (J1). En la convención DH de Flange,
        // los joints giran sobre el -Y del padre, así que usamos Vector3.down
        Quaternion dynamicTcpOrientation;
        if (AlignOrientationWithJ1)
        {
            float currentJ1 = _hasPrevIkTarget ? _prevIkTarget[0] : _controller.MechanicalGroup.JointState[0];
            float deltaJ1 = currentJ1 - _initialJ1Angle;
            Quaternion j1Rotation = Quaternion.AngleAxis(deltaJ1, Vector3.down);
            dynamicTcpOrientation = j1Rotation * _fixedTcpFrameOrientation;
        }
        else
        {
            dynamicTcpOrientation = _fixedTcpFrameOrientation;
        }

        Quaternion actualTcpOrientation = _controller.PoseObserver.ToolCenterPointFrame.Value.rotation;
        
        // El usuario pidió que si la orientación en Pitch (X) se desvía, el brazo debe seguir moviéndose
        // y recuperar su posición automáticamente a través del solver IK, sin reducir la velocidad.
        float driftSpeedMultiplier = 1f;

        // Calculamos la desviación sólo para un eventual diagnóstico, sin penalizar el joystick
        float physicalRotDrift = Quaternion.Angle(dynamicTcpOrientation, actualTcpOrientation);
        if (physicalRotDrift > 15f && _logIkPoseError && Time.time >= _nextIkPoseLogTime)
        {
            _nextIkPoseLogTime = Time.time + Mathf.Max(_ikPoseLogInterval, 1.0f); // Warning relajado a cada 1 seg máximo
            Debug.LogWarning($"[JoystickAdapter][Orientación] Desviación temporal detectada ({physicalRotDrift:F1}deg). El IK intentará corregirlo automáticamente.");
        }

        // Penaliza la velocidad cartesiana según la masa del objeto agarrado, simulando el peso de la carga.
        // Se aplica ANTES de la IK (no por joint) para no romper la relación geométrica entre articulaciones.
        float payloadSpeedMultiplier = 1f;
        if (_gripperController != null && _gripperController.IsHoldingObject)
        {
            float massRatio = Mathf.Clamp01(_gripperController.GrabbedMass / _maxSimulatedPayloadMass);
            payloadSpeedMultiplier = Mathf.Lerp(1f, _minPayloadSpeedMultiplier, massRatio);
        }

        // ── Cálculo de trayectoria ───────────────────────────────────────
        var deltaWorld = _velocity * (_speed * dt * driftSpeedMultiplier * payloadSpeedMultiplier);
        var deltaFrame = WorldVectorToFrame(deltaWorld, frame, extJoint);
        
        Matrix4x4 currentPose;
        if (_hasPrevIkTarget)
        {
            Matrix4x4 prevWorldPose = _controller.Solver.ComputeForward(new JointTarget(_prevIkTarget), tool);
            Matrix4x4 frameWorldPose = _controller.GetFrame(frame).GetWorldFrame(_controller, extJoint);
            currentPose = frameWorldPose.inverse * prevWorldPose;
        }
        else
        {
            currentPose = _controller.PoseObserver.ToolCenterPointFrame.Value;
        }
        
        Vector3 currentPos = (Vector3)currentPose.GetColumn(3);
        Vector3 targetPosInFrame = currentPos + deltaFrame;

        if (_enableWorkspaceLimits)
        {
            // Truncar radio horizontal (en plano XZ de la base del robot)
            Vector2 horizontalPos = new Vector2(targetPosInFrame.x, targetPosInFrame.z);
            float distHorizontal = horizontalPos.magnitude;
            if (distHorizontal > _maxHorizontalRadius)
            {
                horizontalPos = horizontalPos.normalized * _maxHorizontalRadius;
                targetPosInFrame.x = horizontalPos.x;
                targetPosInFrame.z = horizontalPos.y;
            }
            else if (distHorizontal < _minHorizontalRadius)
            {
                horizontalPos = horizontalPos.normalized * _minHorizontalRadius;
                targetPosInFrame.x = horizontalPos.x;
                targetPosInFrame.z = horizontalPos.y;
            }

            // Truncar altura (eje Y del frame)
            targetPosInFrame.y = Mathf.Clamp(targetPosInFrame.y, _minHeight, _maxHeight);
        }

        Matrix4x4 targetPose = Matrix4x4.TRS(targetPosInFrame, dynamicTcpOrientation, Vector3.one);
        var target = new CartesianTarget(targetPose, configuration, extJoint);
        var solution = _controller.Solver.ComputeInverse(target, tool, frame);

        // ── Validación IK ────────────────────────────────────────────────
        JointTarget ikTarget = null;
        bool targetAccepted = false;

        if (solution.IsValid)
        {
            ikTarget = solution.JointTarget;
            
            // Verificamos si la IK sacrificó la orientación para llegar a la posición extrema.
            // Comparamos la FK del resultado IK contra nuestra orientación dinámica esperada,
            // NO contra el targetPose (que siempre la incluye), para máxima robustez.
            Matrix4x4 solvedWorldPose = _controller.Solver.ComputeForward(ikTarget, tool);
            Matrix4x4 solvedFramePose = _controller.WorldToFrame(solvedWorldPose, frame, extJoint);
            float rotError = Quaternion.Angle(dynamicTcpOrientation, solvedFramePose.rotation);
            
            // Tolerancia estricta para asegurar comportamiento de Pick & Place puro
            if (rotError <= 0.5f)
            {
                // Verificamos que no haya un salto abrupto de configuración (ej. Wrist flip)
                bool configJump = false;
                if (_hasPrevIkTarget)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float jump = Mathf.Abs(Mathf.DeltaAngle(_prevIkTarget[i], ikTarget[i]));
                        if (jump > 15f)
                        {
                            configJump = true;
                            break;
                        }
                    }
                }

                if (!configJump)
                {
                    targetAccepted = true;
                    LogIkPoseError(currentPose, targetPose, ikTarget, tool, frame, extJoint);
                }
                else
                {
                    if (_logIkPoseError && Time.time >= _nextIkPoseLogTime)
                    {
                        _nextIkPoseLogTime = Time.time + Mathf.Max(_ikPoseLogInterval, 0.02f);
                        Debug.LogWarning($"[JoystickAdapter][IK] Target rechazado por salto de configuración (IK Flip).");
                    }
                    LogDiagnosticJson("IkRejected_ConfigJump", "Salto abrupto de configuracion detectado (IK Flip).", 0f, driftSpeedMultiplier);
                }
            }
            else
            {
                if (_logIkPoseError && Time.time >= _nextIkPoseLogTime)
                {
                    _nextIkPoseLogTime = Time.time + Mathf.Max(_ikPoseLogInterval, 0.02f);
                    Debug.LogWarning($"[JoystickAdapter][IK] Target rechazado por límite de orientación. Error: {rotError:F2}deg > 0.5deg");
                }
                LogDiagnosticJson("IkRejected_OrientationLimit", $"Límite de orientacion excedido: {rotError:F2}deg > 0.5deg", rotError, driftSpeedMultiplier);
            }
        }
        else
        {
            if (_logIkPoseError && Time.time >= _nextIkPoseLogTime)
            {
                _nextIkPoseLogTime = Time.time + Mathf.Max(_ikPoseLogInterval, 0.02f);
                Debug.LogWarning($"[JoystickAdapter][IK] Target rechazado: Solución IK no válida (fuera del espacio de trabajo).");
            }
            LogDiagnosticJson("IkInvalid_OutOfWorkspace", "La pose objetivo esta fuera del espacio de trabajo del robot.", 0f, driftSpeedMultiplier);
        }

        if (!targetAccepted)
        {
            if (!_hasPrevIkTarget)
            {
                ClearJointActionDisplay();
                return;
            }
            
            // Mantenemos la última consigna válida para que el brazo la alcance y se detenga
            ikTarget = new JointTarget(_prevIkTarget);
        }

        // Registrar telemetría periódica de movimiento cartesiano
        LogDiagnosticJson("Telemetry_Cartesian", $"Movimiento cartesiano activo. Error orientacion: {physicalRotDrift:F3}deg", physicalRotDrift, driftSpeedMultiplier);

        ApplyPID(ikTarget, dt);
    }

    private void CaptureFixedOrientation()
    {
        if (_controller == null || !_controller.IsValid.Value) return;
        if (_orientationCaptured) return;
        
        var frame = _controller.Frame.Value;
        var tool = _controller.Tool.Value;
        var extJoint = _controller.MechanicalGroup.JointState.ExtJoint;

        // Copiamos los joints actuales a un arreglo para construir el JointTarget correctamente
        float[] currentQ = new float[6];
        for(int i=0; i<6; i++) currentQ[i] = _controller.MechanicalGroup.JointState[i];
        
        // Calculamos la pose física actual en el espacio del MUNDO real
        Matrix4x4 actualWorldPose = _controller.Solver.ComputeForward(new JointTarget(currentQ), tool);
        Quaternion currentWorldRot = actualWorldPose.rotation;

        Quaternion desiredWorldRot;
        if (_forceVerticalGripper)
        {
            // El eje Y de la pinza (currentWorldRot * Vector3.up) debe mirar estrictamente al -Y del mundo real (Vector3.down)
            Vector3 currentWorldUp = currentWorldRot * Vector3.up;
            desiredWorldRot = Quaternion.FromToRotation(currentWorldUp, Vector3.down) * currentWorldRot;
        }
        else
        {
            desiredWorldRot = currentWorldRot;
        }

        // Ahora bajamos la orientación perfecta deseada desde el MUNDO al FRAME local del robot (base)
        Matrix4x4 desiredWorldPoseMatrix = Matrix4x4.TRS(Vector3.zero, desiredWorldRot, Vector3.one);
        Matrix4x4 desiredFramePoseMatrix = _controller.WorldToFrame(desiredWorldPoseMatrix, frame, extJoint);
        
        _fixedTcpFrameOrientation = desiredFramePoseMatrix.rotation;
        _initialJ1Angle = _controller.MechanicalGroup.JointState[0];
        _orientationCaptured = true;
    }

    private Vector3 WorldVectorToFrame(Vector3 worldVector, int frame, ExtJoint extJoint)
    {
        Quaternion frameWorldRotation = _controller.GetFrame(frame).GetWorldFrame(_controller, extJoint).rotation;
        return Quaternion.Inverse(frameWorldRotation) * worldVector;
    }

    private void LogIkPoseError(Matrix4x4 currentPose, Matrix4x4 targetPose, JointTarget ikTarget, int tool, int frame, ExtJoint extJoint)
    {
        Matrix4x4 solvedWorldPose = _controller.Solver.ComputeForward(ikTarget, tool);
        Matrix4x4 solvedFramePose = _controller.WorldToFrame(solvedWorldPose, frame, extJoint);

        Vector3 targetPos = (Vector3)targetPose.GetColumn(3);
        Vector3 currentPos = (Vector3)currentPose.GetColumn(3);
        Vector3 solvedPos = (Vector3)solvedFramePose.GetColumn(3);
        float targetStep = Vector3.Distance(currentPos, targetPos);
        float targetRotStep = Quaternion.Angle(currentPose.rotation, targetPose.rotation);
        float posError = Vector3.Distance(targetPos, solvedPos);
        float rotError = Quaternion.Angle(targetPose.rotation, solvedFramePose.rotation);

        float maxJointError = 0f;
        float wristJointError = 0f;
        for (int i = 0; i < 6; i++)
        {
            float jointError = Mathf.Abs(Mathf.DeltaAngle(_controller.MechanicalGroup.JointState[i], ikTarget[i]));
            maxJointError = Mathf.Max(maxJointError, jointError);
            if (i >= FirstWristJoint)
                wristJointError = Mathf.Max(wristJointError, jointError);
        }

        LastIkDiagnostic = new IkDiagnosticSample
        {
            IsValid = true,
            Time = Time.time,
            PoseError = posError,
            RotationError = rotError,
            TargetStep = targetStep,
            TargetRotationStep = targetRotStep,
            MaxJointError = maxJointError,
            WristJointError = wristJointError,
            StepScale = 1f,
            JointStepLimit = 360f,
            InputMagnitude = _velocity.magnitude
        };

        if (!_logIkPoseError || Time.time < _nextIkPoseLogTime)
            return;

        _nextIkPoseLogTime = Time.time + Mathf.Max(_ikPoseLogInterval, 0.02f);

        Debug.Log(
            $"[JoystickAdapter][IK] poseErr={posError:F4}m rotErr={rotError:F2}deg " +
            $"targetStep={targetStep:F4}m targetRotStep={targetRotStep:F2}deg " +
            $"maxJointErr={maxJointError:F2}deg wristErr={wristJointError:F2}deg " +
            $"input={_velocity.magnitude:F3}",
            this);
    }

    private void UpdateJ6ExclusiveControl()
    {
        float currentJ6 = (_controller != null && _controller.IsValid.Value) ? _controller.MechanicalGroup.JointState[5] : 0f;

        if (!_resettingJ6)
        {
            // 1. Verificar botones de rotación J6 primero
            float buttonInput = 0f;
            if (_j6AntiHorAction != null && _j6AntiHorAction.action.ReadValue<float>() > 0.5f)
                buttonInput -= 1f;
            if (_j6HorAction != null && _j6HorAction.action.ReadValue<float>() > 0.5f)
                buttonInput += 1f;

            if (Mathf.Abs(buttonInput) > 0.1f)
            {
                // Rotar J6 a velocidad constante (45°/s)
                _j6TargetAngle += buttonInput * 45f * Time.fixedDeltaTime;
                _j6TargetAngle = Mathf.Clamp(_j6TargetAngle, _j6MinLimit, _j6MaxLimit);
                _hasUnwrappedStickAngle = false;
            }
            else
            {
                // 2. Si no hay botones, usar el analógico
                // MoveX = leftStick horizontal, MoveZ = leftStick vertical (mismo analógico)
                Vector2 input;
                if (_diagnosticInputOverrideActive)
                {
                    input = new Vector2(_diagnosticInputOverrideVelocity.x, _diagnosticInputOverrideVelocity.z);
                }
                else
                {
                    // Ignoramos _signX y _signZ en modo J6 porque la cámara del gripper siempre está alineada.
                    float rawX = ReadAxis(_moveX, _invertMoveX);
                    float rawZ = ReadAxis(_moveZ, _invertMoveZ);
                    input = new Vector2(rawX, rawZ);
                }

                if (input.sqrMagnitude > 0.04f) // Deadzone de 0.2
                {
                    // Atan2(y, x) -> Aquí pasamos (x, z) para que 0° sea adelante y crezca en sentido horario
                    float angle = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
                    
                    if (!_hasUnwrappedStickAngle)
                    {
                        _prevStickAngle = angle;
                        _hasUnwrappedStickAngle = true;
                        // _j6TargetAngle ya está inicializado correctamente (e.g. desde currentJ6)
                    }
                    else
                    {
                        float deltaStick = Mathf.DeltaAngle(_prevStickAngle, angle);
                        _prevStickAngle = angle;
                        
                        // Limitar deltaStick para robustez extrema (evita fallos al cruzar la zona muerta)
                        deltaStick = Mathf.Clamp(deltaStick, -90f, 90f);
                        
                        // Gear ratio 1:4 e inversión del giro (negativo)
                        float targetDelta = -deltaStick * 0.25f;
                        _j6TargetAngle += targetDelta;
                    }
                    
                    // Asignación absoluta limitada por los límites físicos de J6
                    _j6TargetAngle = Mathf.Clamp(_j6TargetAngle, _j6MinLimit, _j6MaxLimit);
                    
                    if (Mathf.Approximately(_j6TargetAngle, _j6MinLimit) || Mathf.Approximately(_j6TargetAngle, _j6MaxLimit))
                    {
                        LogDiagnosticJson("J6_LimitReached", $"Se alcanzo el limite articular de J6: {_j6TargetAngle:F1}deg", _j6TargetAngle);
                    }
                }
                else
                {
                    // Si el analógico vuelve al centro y no hay botones, frenamos en la posición actual
                    // y reseteamos el PID/velocidad de J6 para frenar en seco.
                    if (_controller != null && _controller.IsValid.Value)
                    {
                        _j6TargetAngle = currentJ6;
                        _prevIkTarget[5] = currentJ6;
                        _pids[5].Reset();
                        _jointVelocity[5] = 0f;
                        _hasUnwrappedStickAngle = false;
                    }
                }
            }
        }

        // Generar un nuevo JointTarget usando la posición de las articulaciones actuales
        // (o previas) pero sobreescribiendo el índice 5 (J6).
        float[] joints = new float[6];
        for (int i = 0; i < 6; i++)
        {
            joints[i] = _hasPrevIkTarget ? _prevIkTarget[i] : _controller.MechanicalGroup.JointState[i];
        }
        joints[5] = _j6TargetAngle;

        // IMPORTANTE: NO hacemos joints.CopyTo(_prevIkTarget) aquí.
        // Dejamos que ApplyPID lo haga al final de su ejecución para que pueda
        // calcular correctamente qTargetVelocity y aplicar el torque feedforward.

        // Registrar telemetría del modo J6 exclusivo
        float j6Drift = Mathf.Abs(Mathf.DeltaAngle(currentJ6, _j6TargetAngle));
        LogDiagnosticJson("Telemetry_J6Exclusive", $"Modo J6 activo. Target: {_j6TargetAngle:F1}deg, Actual: {currentJ6:F1}deg, Entrada: {(_diagnosticInputOverrideActive ? _diagnosticInputOverrideVelocity.magnitude : 0f):F2}", j6Drift, 1f);

        float dt = Time.fixedDeltaTime;
        ApplyPID(new JointTarget(joints), dt);
    }

    private void BeginMotion()
    {
        ResetPIDs();
        CaptureFixedOrientation();
        _motionActive = true;
    }

    private void EndMotion()
    {
        ResetPIDs();
        _motionActive = false;
    }

    private float ReadAxis(InputActionReference actionReference, bool invert)
    {
        float value = _calibrationManager != null
            ? _calibrationManager.ReadCalibrated(actionReference)
            : actionReference?.action.ReadValue<float>() ?? 0f;

        return invert ? -value : value;
    }

    private void EnableInputActions()
    {
        _moveX?.action.Enable();
        _moveY?.action.Enable();
        _moveZ?.action.Enable();

        _modoCamara?.action.Enable();

        _j6AntiHorAction?.action.Enable();
        _j6HorAction?.action.Enable();
        _j6HomeAction?.action.Enable();
    }

    private void DisableInputActions()
    {
        _moveX?.action.Disable();
        _moveY?.action.Disable();
        _moveZ?.action.Disable();

        _modoCamara?.action.Disable();

        _j6AntiHorAction?.action.Disable();
        _j6HorAction?.action.Disable();
        _j6HomeAction?.action.Disable();
    }

    private void ExitCameraModeForProfileChange()
    {
        if (IsCameraMode)
            RemapAxesFromCamera();

        IsCameraMode = false;
    }

    private void ResetInputState()
    {
        _velocity = Vector3.zero;
        ResetPIDs();
        _motionActive = false;
        ClearJointActionDisplay();
    }

    private void InitPIDs()
    {
        _pids = new JointPID[6];
        for (int i = 0; i < 6; i++)
            _pids[i] = new JointPID(_kpBase, _kiBase, _kdBase);
    }

    private void ResetPIDs()
    {
        if (_pids != null)
        {
            foreach (var pid in _pids)
                pid.Reset();
        }

        System.Array.Clear(_jointVelocity, 0, 6);
        System.Array.Clear(_prevIkTarget, 0, 6);
        _hasPrevIkTarget = false;
        ClearJointActionDisplay();
    }

    private void ApplyPID(JointTarget ikTarget, float dt)
    {
        var robotJoints = _controller.MechanicalGroup.RobotJoints;

        float payloadMass = 0f;
        Vector3 payloadWorldPos = Vector3.zero;
        if (_gripperController != null && _gripperController.IsHoldingObject)
        {
            payloadMass = _gripperController.GrabbedMass * _payloadInertiaMultiplier;
            payloadWorldPos = _gripperController.GrabbedWorldPosition;
        }

        float[] jEff = RobotDynamics.ComputeEffectiveInertia(robotJoints, payloadMass, payloadWorldPos);

        var qNew = new float[6];
        for (int i = 0; i < 6; i++)
        {
            float qTarget = ikTarget[i];
            if (_resettingJ6 && i == 5)
            {
                qTarget = _j6TargetAngle;
            }
            float qActual = _controller.MechanicalGroup.JointState[i];
            
            // Calculamos la velocidad deseada para compensar perfectamente el amortiguamiento
            float qTargetVelocity = _hasPrevIkTarget && dt > 1e-6f 
                ? Mathf.DeltaAngle(_prevIkTarget[i], qTarget) / dt 
                : 0f;

            // PID → torque virtual (°/s² a inercia de referencia)
            float torque = _pids[i].Compute(qTarget, qActual, dt, qTargetVelocity);
            _lastJointControlActions[i] = torque;

            // Aceleración = torque / inercia normalizada → joints más pesados aceleran más lento
            float jNorm = Mathf.Max(jEff[i] / _referenceInertia, 0.05f);

            // Feedforward exacto: genera el torque necesario para mantener la velocidad venciendo el amortiguamiento
            float discreteDampingFactor = 1f / Mathf.Max(0.1f, 1f - _velocityDamping * dt);
            float feedforwardTorque = (jNorm / dt) * (qTargetVelocity * discreteDampingFactor - _jointVelocity[i]);
            torque += feedforwardTorque;

            float acceleration = torque / jNorm;
            acceleration = Mathf.Clamp(acceleration, -_maxJointAcceleration, _maxJointAcceleration);
            
            // Guardar para el HUD de GUI en pantalla
            _lastJointControlActions[i] = acceleration;

            _jointVelocity[i] += acceleration * dt;

            // Amortiguamiento viscoso: evita aceleración indefinida
            _jointVelocity[i] -= _velocityDamping * _jointVelocity[i] * dt;
            
            float maxVel = (i == 5 && IsJ6ExclusiveMode) ? 45f : _maxJointVelocity;
            _jointVelocity[i] = Mathf.Clamp(_jointVelocity[i], -maxVel, maxVel);

            float targetQNew = qActual + _jointVelocity[i] * dt;
            qNew[i] = Mathf.Clamp(targetQNew, _minLimits[i], _maxLimits[i]);

            // Si se golpea un límite articular físico, frenamos la velocidad acumulada
            // en esa dirección y reseteamos el término integral del PID para evitar windup
            if (Mathf.Approximately(qNew[i], _minLimits[i]) && _jointVelocity[i] < 0f)
            {
                _jointVelocity[i] = 0f;
                _pids[i].Reset();
            }
            else if (Mathf.Approximately(qNew[i], _maxLimits[i]) && _jointVelocity[i] > 0f)
            {
                _jointVelocity[i] = 0f;
                _pids[i].Reset();
            }

            _prevIkTarget[i] = qTarget;
        }

        _hasPrevIkTarget = true;
        UpdateJointActionDisplay();
        _controller.MechanicalGroup.SetJoints(new JointTarget(qNew), notify: true);
    }

    private void UpdateJointActionDisplay()
    {
        if (_pidActionsPanel == null) return;

        for (int i = 0; i < _lastJointControlActions.Length; i++)
            _pidActionsPanel.SetJointAction(i, _lastJointControlActions[i]);
    }

    private void ClearJointActionDisplay()
    {
        for (int i = 0; i < _lastJointControlActions.Length; i++)
            _lastJointControlActions[i] = 0f;

        UpdateJointActionDisplay();
    }

    private void InitDefaultMapping()
    {
        // El mapeo por defecto ahora es global al mundo (robot base)
        // para que mover J6 no invierta ni tuerza los controles direccionales.
        _dirZ = Vector3.forward;
        _dirX = Vector3.right;
        _signZ = 1f;
        _signX = 1f;
    }

    private void RemapAxesFromCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[JoystickAdapter] Camera.main no encontrada; se mantiene el mapeo actual.");
            return;
        }

        Vector3 targetPos = _endEffector != null ? _endEffector.position : Vector3.zero;
        Vector3 c = targetPos - cam.transform.position;
        c.y = 0f;
        
        if (c.sqrMagnitude < 1e-6f)
        {
            // Fallback si la cámara está exactamente arriba
            c = Vector3.forward;
        }
        
        c = c.normalized;

        // "Adelante" (Z) ahora es directamente el vector desde la cámara al robot.
        // "Derecha" (X) es el producto cruzado, perpendicular a la cámara.
        _dirZ = c;
        _signZ = 1f;
        
        _dirX = Vector3.Cross(Vector3.up, _dirZ).normalized;
        _signX = 1f;
    }
}
