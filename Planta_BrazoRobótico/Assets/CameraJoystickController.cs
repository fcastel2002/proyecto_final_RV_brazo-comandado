using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controla la cámara en modo primera persona cuando JoystickAdapter.IsCameraMode == true.
///
/// Análogico izquierdo:
///   Up/Down    → avanzar / retroceder  (MoveForward)
///   Left/Right → desplazarse lateral   (MoveSide)
///
/// Análogico derecho:
///   Up/Down    → rotar vista arriba / abajo  (ViewUp  → Pitch)
///   Left/Right → rotar vista a los lados     (ViewSide → Yaw)
///
/// Adjuntar este componente a la Main Camera o a un camera rig vacío
/// que tenga la cámara como hijo.
/// </summary>
public class CameraJoystickController : MonoBehaviour
{
    [Header("Input Actions — Traslación")]
    [SerializeField] private InputActionReference _moveForward;   // Left Stick Up/Down
    [SerializeField] private InputActionReference _moveSide;      // Left Stick Left/Right

    [Header("Input Actions — Rotación")]
    [SerializeField] private InputActionReference _viewUp;        // Right Stick Up/Down   → Pitch
    [SerializeField] private InputActionReference _viewSide;      // Right Stick Left/Right → Yaw

    [Header("Calibration")]
    [SerializeField] private AnalogCalibrationManager _calibrationManager;

    [Header("Velocidades")]
    [Tooltip("Velocidad de traslación (m/s)")]
    [SerializeField] private float _moveSpeed = 5f;

    [Tooltip("Velocidad de rotación (grados/s)")]
    [SerializeField] private float _lookSpeed = 90f;

    [Tooltip("Límite de pitch para no voltear la cámara (grados)")]
    [SerializeField] private float _pitchLimit = 80f;

    // Acumulador de pitch para no superar el límite
    private float _currentPitch = 0f;
    private bool _inputSuppressed;

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
        InputActionReference moveForward,
        InputActionReference moveSide,
        InputActionReference viewUp,
        InputActionReference viewSide)
    {
        DisableInputActions();

        _moveForward = moveForward;
        _moveSide = moveSide;
        _viewUp = viewUp;
        _viewSide = viewSide;

        if (isActiveAndEnabled)
            EnableInputActions();
    }

    public void SetInputSuppressed(bool suppressed)
    {
        _inputSuppressed = suppressed;
    }

    private void Start()
    {
        // Inicializar el pitch con la rotación actual de la cámara
        _currentPitch = transform.eulerAngles.x;
        // Normalizar a rango [-180, 180]
        if (_currentPitch > 180f) _currentPitch -= 360f;
    }

    private void Update()
    {
        if (_inputSuppressed) return;
        if (!JoystickAdapter.IsCameraMode) return;

        float dt = Time.deltaTime;

        // ── Traslación ────────────────────────────────────────────────
        float forward = ReadAxis(_moveForward);
        float side = ReadAxis(_moveSide);

        // Movimiento relativo a la orientación actual de la cámara
        Vector3 flatForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        Vector3 flatRight = new Vector3(transform.right.x, 0f, transform.right.z).normalized;
        Vector3 move = (flatForward * forward + flatRight * side) * (_moveSpeed * dt);
        transform.position += move;

        // ── Rotación ──────────────────────────────────────────────────
        float viewUp = ReadAxis(_viewUp);
        float viewSide = ReadAxis(_viewSide);

        // Yaw: rotar alrededor del eje Y global (izquierda/derecha)
        float yawDelta = viewSide * _lookSpeed * dt;
        transform.Rotate(Vector3.up, yawDelta, Space.World);

        // Pitch: rotar alrededor del eje X local (arriba/abajo) con límite
        // Negativo porque stick arriba (positivo) debe hacer mirar hacia arriba (pitch negativo)
        float pitchDelta = -viewUp * _lookSpeed * dt;
        _currentPitch = Mathf.Clamp(_currentPitch + pitchDelta, -_pitchLimit, _pitchLimit);

        // Aplicar pitch de forma absoluta para evitar acumulación de error numérico
        Vector3 euler = transform.eulerAngles;
        transform.eulerAngles = new Vector3(_currentPitch, euler.y, 0f);
    }

    private void EnableInputActions()
    {
        _moveForward?.action.Enable();
        _moveSide?.action.Enable();
        _viewUp?.action.Enable();
        _viewSide?.action.Enable();
    }

    private void DisableInputActions()
    {
        _moveForward?.action.Disable();
        _moveSide?.action.Disable();
        _viewUp?.action.Disable();
        _viewSide?.action.Disable();
    }

    private float ReadAxis(InputActionReference actionReference)
    {
        return _calibrationManager != null
            ? _calibrationManager.ReadCalibrated(actionReference)
            : actionReference?.action.ReadValue<float>() ?? 0f;
    }
}
