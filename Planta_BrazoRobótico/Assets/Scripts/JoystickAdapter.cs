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

    [Header("PID - Ganancias base")]
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
    [SerializeField] private float _maxJointVelocity = 60f;

    /// <summary>true = modo camara activo, false = modo robot activo.</summary>
    public static bool IsCameraMode { get; private set; }

    private Vector3 _dirZ = Vector3.forward;
    private Vector3 _dirX = Vector3.right;
    private float _signZ = 1f;
    private float _signX = 1f;

    private Vector3 _velocity;
    private JointPID[] _pids;
    private float[] _jointVelocity = new float[6];
    private Quaternion _fixedTcpOrientation;
    private bool _orientationCaptured;
    private bool _inputSuppressed;
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
            ResetPIDs();
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
            ClearJointActionDisplay();
        }
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

        if (_velocity.sqrMagnitude < 1e-6f)
        {
            System.Array.Clear(_jointVelocity, 0, 6);
            ClearJointActionDisplay();
            return;
        }

        if (!_orientationCaptured)
            CaptureFixedOrientation();
        if (!_orientationCaptured) return;

        var delta = _velocity * (_speed * Time.fixedDeltaTime);
        var currentPose = _controller.PoseObserver.ToolCenterPointFrame.Value;
        Vector3 currentPos = (Vector3)currentPose.GetColumn(3);
        var targetPose = Matrix4x4.TRS(currentPos + delta, _fixedTcpOrientation, Vector3.one);

        var frame = _controller.Frame.Value;
        var tool = _controller.Tool.Value;
        var configuration = _controller.Configuration.Value;
        var extJoint = _controller.MechanicalGroup.JointState.ExtJoint;

        var target = new CartesianTarget(targetPose, configuration, extJoint);
        var solution = _controller.Solver.ComputeInverse(target, tool, frame);

        if (!solution.IsValid)
        {
            ClearJointActionDisplay();
            return;
        }

        ApplyPID(solution.JointTarget, Time.fixedDeltaTime);
    }

    private void CaptureFixedOrientation()
    {
        if (_controller == null || !_controller.IsValid.Value) return;
        _fixedTcpOrientation = _controller.PoseObserver.ToolCenterPointFrame.Value.rotation;
        _orientationCaptured = true;
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
    }

    private void ResetInputState()
    {
        _velocity = Vector3.zero;
        ResetPIDs();
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
        if (_pids == null) return;

        foreach (var pid in _pids)
            pid.Reset();

        System.Array.Clear(_jointVelocity, 0, 6);
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

            // PID → torque virtual (°/s² a inercia de referencia)
            float torque = _pids[i].Compute(qTarget, qActual, dt);
            _lastJointControlActions[i] = torque;

            // Aceleración = torque / inercia normalizada → joints más pesados aceleran más lento
            float jNorm = Mathf.Max(jEff[i] / _referenceInertia, 0.05f);
            _jointVelocity[i] += (torque / jNorm) * dt;

            // Amortiguamiento viscoso: evita aceleración indefinida
            _jointVelocity[i] -= _velocityDamping * _jointVelocity[i] * dt;
            _jointVelocity[i] = Mathf.Clamp(_jointVelocity[i], -_maxJointVelocity, _maxJointVelocity);

            qNew[i] = qActual + _jointVelocity[i] * dt;
        }

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
