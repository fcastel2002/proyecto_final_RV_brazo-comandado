using Preliy.Flange;
using TMPro;
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
    [Tooltip("Textos TMP del panel para mostrar la accion de control de J1 a J6.")]
    [SerializeField] private TextMeshProUGUI[] _jointActionTexts = new TextMeshProUGUI[6];
    [SerializeField] private string _jointActionFormat = "F3";

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

    [Header("Diagnostico IK")]
    [Tooltip("Loguea error entre el target cartesiano enviado a IK y la FK de solution.JointTarget.")]
    [SerializeField] private bool _logIkPoseError = true;
    [SerializeField, Min(0.02f)] private float _ikPoseLogInterval = 0.5f;

    /// <summary>true = modo camara activo, false = modo robot activo.</summary>
    public static bool IsCameraMode { get; private set; }

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
    private bool _orientationCaptured;
    private bool _motionActive;
    private bool _hasPrevIkTarget;
    private bool _inputSuppressed;
    private bool _diagnosticInputOverrideActive;
    private Vector3 _diagnosticInputOverrideVelocity;
    private float _nextIkPoseLogTime;
    private readonly float[] _lastJointControlActions = new float[6];

    public float[] LastJointControlActions => _lastJointControlActions;

    private void Awake()
    {
        if (_calibrationManager == null)
            _calibrationManager = FindFirstObjectByType<AnalogCalibrationManager>();
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
        bool cameraModeAllowed)
    {
        DisableInputActions();

        _moveX = moveX;
        _moveY = moveY;
        _moveZ = moveZ;
        _modoCamara = modoCamara;
        _cameraModeAllowed = cameraModeAllowed;

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
        CaptureFixedOrientation();
        ClearJointActionDisplay();
    }

    public void ToggleCameraMode()
    {
        if (!_cameraModeAllowed)
            return;

        bool wasCameraMode = IsCameraMode;
        IsCameraMode = !IsCameraMode;

        if (wasCameraMode && !IsCameraMode)
        {
            RemapAxesFromCamera();
            _orientationCaptured = false;
            ResetPIDs();
            _motionActive = false;
            CaptureFixedOrientation();
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

    private void FixedUpdate()
    {
        if (IsCameraMode) return;
        if (_controller == null || !_controller.IsValid.Value) return;

        if (_velocity.sqrMagnitude < MotionInputEpsilon)
        {
            EndMotion();
            return;
        }

        if (!_motionActive)
            BeginMotion();

        if (!_orientationCaptured) return;

        var frame = _controller.Frame.Value;
        var tool = _controller.Tool.Value;
        var configuration = _controller.Configuration.Value;
        var extJoint = _controller.MechanicalGroup.JointState.ExtJoint;

        float dt = Time.fixedDeltaTime;
        var deltaWorld = _velocity * (_speed * dt);
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

        Matrix4x4 targetPose = Matrix4x4.TRS(currentPos + deltaFrame, _fixedTcpFrameOrientation, Vector3.one);
        var target = new CartesianTarget(targetPose, configuration, extJoint);
        var solution = _controller.Solver.ComputeInverse(target, tool, frame);

        JointTarget ikTarget = null;
        bool targetAccepted = false;

        if (solution.IsValid)
        {
            ikTarget = solution.JointTarget;
            
            // Verificamos si la IK sacrificó la orientación para llegar a la posición extrema
            Matrix4x4 solvedWorldPose = _controller.Solver.ComputeForward(ikTarget, tool);
            Matrix4x4 solvedFramePose = _controller.WorldToFrame(solvedWorldPose, frame, extJoint);
            float rotError = Quaternion.Angle(targetPose.rotation, solvedFramePose.rotation);
            
            // Tolerancia estricta para asegurar comportamiento de Pick & Place puro
            if (rotError <= 0.5f)
            {
                // Verificamos que no haya un salto abrupto de configuración (ej. Wrist flip o Singularity)
                bool configJump = false;
                if (_hasPrevIkTarget)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float jump = Mathf.Abs(Mathf.DeltaAngle(_prevIkTarget[i], ikTarget[i]));
                        if (jump > 15f) // Si salta más de 15 grados en 1 frame (0.02s) es un flip
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
                }
            }
            else
            {
                // Registramos el rechazo si el log está activo
                if (_logIkPoseError && Time.time >= _nextIkPoseLogTime)
                {
                    _nextIkPoseLogTime = Time.time + Mathf.Max(_ikPoseLogInterval, 0.02f);
                    Debug.LogWarning($"[JoystickAdapter][IK] Target rechazado por límite de orientación. Error: {rotError:F2}deg > 0.5deg");
                }
            }
        }

        if (!targetAccepted)
        {
            if (!_hasPrevIkTarget)
            {
                // Si nunca tuvimos un target válido en este movimiento, abortamos
                ClearJointActionDisplay();
                return;
            }
            
            // Mantenemos la última consigna válida para que el brazo la alcance y se detenga
            // sin resetear el target, lo que causaría saltos o congelamientos
            ikTarget = new JointTarget(_prevIkTarget);
        }

        ApplyPID(ikTarget, dt);
    }

    private void CaptureFixedOrientation()
    {
        if (_controller == null || !_controller.IsValid.Value) return;
        if (_orientationCaptured) return;
        
        _fixedTcpFrameOrientation = _controller.PoseObserver.ToolCenterPointFrame.Value.rotation;
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

    private void BeginMotion()
    {
        ResetPIDs();
        CaptureFixedOrientation();
        _motionActive = true;
    }

    private void EndMotion()
    {
        ResetPIDs();
        CaptureFixedOrientation();
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
    }

    private void DisableInputActions()
    {
        _moveX?.action.Disable();
        _moveY?.action.Disable();
        _moveZ?.action.Disable();

        _modoCamara?.action.Disable();
    }

    private void ExitCameraModeForProfileChange()
    {
        if (IsCameraMode)
            RemapAxesFromCamera();

        IsCameraMode = false;
        _orientationCaptured = false;
    }

    private void ResetInputState()
    {
        _velocity = Vector3.zero;
        ResetPIDs();
        _motionActive = false;
        _orientationCaptured = false;
        CaptureFixedOrientation();
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
        float[] jEff = RobotDynamics.ComputeEffectiveInertia(robotJoints);

        var qNew = new float[6];
        for (int i = 0; i < 6; i++)
        {
            float qTarget = ikTarget[i];
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

            _lastJointControlActions[i] = torque;

            _jointVelocity[i] += (torque / jNorm) * dt;

            // Amortiguamiento viscoso: evita aceleración indefinida
            _jointVelocity[i] -= _velocityDamping * _jointVelocity[i] * dt;
            _jointVelocity[i] = Mathf.Clamp(_jointVelocity[i], -_maxJointVelocity, _maxJointVelocity);

            qNew[i] = qActual + _jointVelocity[i] * dt;
            _prevIkTarget[i] = qTarget;
        }

        _hasPrevIkTarget = true;
        UpdateJointActionDisplay();
        _controller.MechanicalGroup.SetJoints(new JointTarget(qNew), notify: true);
    }

    private void UpdateJointActionDisplay()
    {
        if (_jointActionTexts == null) return;

        int count = Mathf.Min(_jointActionTexts.Length, _lastJointControlActions.Length);
        for (int i = 0; i < count; i++)
        {
            if (_jointActionTexts[i] == null) continue;
            _jointActionTexts[i].text = _lastJointControlActions[i].ToString(_jointActionFormat);
        }
    }

    private void ClearJointActionDisplay()
    {
        for (int i = 0; i < _lastJointControlActions.Length; i++)
            _lastJointControlActions[i] = 0f;

        UpdateJointActionDisplay();
    }

    private void InitDefaultMapping()
    {
        Vector3 fwd = _endEffector.forward;
        fwd.y = 0f;
        _dirZ = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;

        Vector3 rgt = _endEffector.right;
        rgt.y = 0f;
        _dirX = rgt.sqrMagnitude > 1e-6f ? rgt.normalized : Vector3.right;

        _signZ = 1f;
        _signX = 1f;
    }

    private void RemapAxesFromCamera()
    {
        if (_endEffector == null)
        {
            Debug.LogWarning("[JoystickAdapter] _endEffector no asignado; se mantiene el mapeo actual.");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[JoystickAdapter] Camera.main no encontrada; se mantiene el mapeo actual.");
            return;
        }

        Vector3 c = _endEffector.position - cam.transform.position;
        c.y = 0f;
        if (c.sqrMagnitude < 1e-6f)
        {
            Debug.LogWarning("[JoystickAdapter] Camara sobre el efector; se mantiene el mapeo actual.");
            return;
        }
        c = c.normalized;

        Vector3 localFwd = _endEffector.forward;
        localFwd.y = 0f;
        Vector3 localRgt = _endEffector.right;
        localRgt.y = 0f;

        if (localFwd.sqrMagnitude < 1e-6f) localFwd = Vector3.forward;
        if (localRgt.sqrMagnitude < 1e-6f) localRgt = Vector3.right;
        localFwd = localFwd.normalized;
        localRgt = localRgt.normalized;

        float rawAngleFwd = Vector3.Angle(c, localFwd);
        float rawAngleRgt = Vector3.Angle(c, localRgt);

        float adjFwd = rawAngleFwd > 90f ? rawAngleFwd - 180f : rawAngleFwd;
        float adjRgt = rawAngleRgt > 90f ? rawAngleRgt - 180f : rawAngleRgt;

        bool fwdIsZ = Mathf.Abs(adjFwd) <= Mathf.Abs(adjRgt);

        Vector3 axisZ;
        float rawAngleZ;
        Vector3 axisX;

        if (fwdIsZ)
        {
            axisZ = localFwd;
            rawAngleZ = rawAngleFwd;
            axisX = localRgt;
        }
        else
        {
            axisZ = localRgt;
            rawAngleZ = rawAngleRgt;
            axisX = localFwd;
        }

        float signZ = rawAngleZ > 90f ? -1f : 1f;
        Vector3 cross = Vector3.Cross(axisX, c);
        float signX = cross.y >= 0f ? 1f : -1f;

        _dirZ = axisZ;
        _signZ = signZ;
        _dirX = axisX;
        _signX = signX;
    }
}
