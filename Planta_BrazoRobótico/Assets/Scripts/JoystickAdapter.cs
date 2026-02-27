using UnityEngine;
using UnityEngine.InputSystem;
using Preliy.Flange;

/// <summary>
/// Mueve el efector final del robot en coordenadas cartesianas con el joystick.
/// Presionar L3 alterna entre modo Robot y modo Cámara.
///
/// Al volver del modo Cámara al modo Robot se recalcula automáticamente cuál
/// eje local del efector final (right / forward) debe responder a cada palanca,
/// de modo que MoveZ siempre empuje "hacia el robot desde donde estás parado"
/// y MoveX se desplace en perpendicular.
/// </summary>
public class JoystickAdapter : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Controller")]
    [SerializeField] private Controller _controller;

    [Header("Input Actions — Robot")]
    [SerializeField] private InputActionReference _moveX;   // left stick L/R  → lado
    [SerializeField] private InputActionReference _moveY;   // right stick U/D  → altura
    [SerializeField] private InputActionReference _moveZ;   // left stick U/D   → avance

    [Header("Mode Toggle")]
    [SerializeField] private InputActionReference _modoCamara;  // L3

    [Header("Efector final (para el remapeo de ejes)")]
    [Tooltip("Asignar el transform del Joint_6 / Flange")]
    [SerializeField] private Transform _endEffector;

    [Header("Settings")]
    [Tooltip("Max linear speed (m/s)")]
    [SerializeField] private float _speed = 0.1f;

    // ─── Estado público ───────────────────────────────────────────────────────

    /// <summary>true = modo cámara activo, false = modo robot activo.</summary>
    public static bool IsCameraMode { get; private set; } = false;

    // ─── Remapeo dinámico de ejes ─────────────────────────────────────────────
    //
    //  _dirZ  → dirección mundo (XZ plano, Y=0, normalizado) que recibe MoveZ
    //  _signZ → +1 ó -1 para MoveZ
    //  _dirX  → dirección mundo (XZ plano, Y=0, normalizado) que recibe MoveX
    //  _signX → +1 ó -1 para MoveX
    //
    //  Por defecto: forward del efector → Z, right del efector → X, ambos +1.
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 _dirZ = Vector3.forward;
    private Vector3 _dirX = Vector3.right;
    private float _signZ = 1f;
    private float _signX = 1f;

    private Vector3 _velocity;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _moveX?.action.Enable();
        _moveY?.action.Enable();
        _moveZ?.action.Enable();

        if (_modoCamara != null)
        {
            _modoCamara.action.Enable();
            _modoCamara.action.performed += OnModoCamaraToggle;
        }
    }

    private void OnDisable()
    {
        _moveX?.action.Disable();
        _moveY?.action.Disable();
        _moveZ?.action.Disable();

        if (_modoCamara != null)
        {
            _modoCamara.action.performed -= OnModoCamaraToggle;
            _modoCamara.action.Disable();
        }
    }

    private void Start()
    {
        // Inicializar el remapeo con la orientación actual del efector
        if (_endEffector != null)
            InitDefaultMapping();
    }

    // ─── Toggle L3 ───────────────────────────────────────────────────────────

    private void OnModoCamaraToggle(InputAction.CallbackContext ctx)
    {
        bool wasCameraMode = IsCameraMode;
        IsCameraMode = !IsCameraMode;

        // Al VOLVER al modo robot → recalcular mapeo desde posición actual de cámara
        if (wasCameraMode && !IsCameraMode)
            RemapAxesFromCamera();

        Debug.Log($"[JoystickAdapter] Modo: {(IsCameraMode ? "CAMARA" : "ROBOT")} | " +
                  $"dirZ={_dirZ * _signZ} | dirX={_dirX * _signX}");
    }

    // ─── Update / FixedUpdate ─────────────────────────────────────────────────

    private void Update()
    {
        if (IsCameraMode)
        {
            _velocity = Vector3.zero;
            return;
        }

        float rawX = _moveX?.action.ReadValue<float>() ?? 0f;
        float rawY = _moveY?.action.ReadValue<float>() ?? 0f;
        float rawZ = _moveZ?.action.ReadValue<float>() ?? 0f;

        // Construir el vector de velocidad en espacio mundo usando el remapeo
        _velocity = _dirX * (rawX * _signX)
                  + Vector3.up * rawY
                  + _dirZ * (rawZ * _signZ);
    }

    private void FixedUpdate()
    {
        if (IsCameraMode) return;
        if (_controller == null || !_controller.IsValid.Value) return;
        if (_velocity.sqrMagnitude < 1e-6f) return;

        var delta = _velocity * (_speed * Time.fixedDeltaTime);
        var currentPose = _controller.PoseObserver.ToolCenterPointFrame.Value;
        var offset = Matrix4x4.TRS(delta, Quaternion.identity, Vector3.one);
        var targetPose = offset * currentPose;

        var frame = _controller.Frame.Value;
        var tool = _controller.Tool.Value;
        var configuration = _controller.Configuration.Value;
        var extJoint = _controller.MechanicalGroup.JointState.ExtJoint;

        var target = new CartesianTarget(targetPose, configuration, extJoint);
        var solution = _controller.Solver.ComputeInverse(target, tool, frame);

        if (!solution.IsValid) return;

        _controller.MechanicalGroup.SetJoints(solution.JointTarget, notify: true);
    }

    // ─── Remapeo de ejes ─────────────────────────────────────────────────────

    /// <summary>
    /// Inicialización por defecto: forward del efector → MoveZ,
    /// right del efector → MoveX, sin inversión.
    /// </summary>
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

    /// <summary>
    /// Recalcula qué eje local del efector (right/forward, proyectados en XZ)
    /// se asigna a MoveZ y MoveX en función de la posición actual de la cámara,
    /// siguiendo el algoritmo geométrico de cinco pasos descrito en el brief.
    /// </summary>
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

        // ── Paso 1: Vector C (efector − cámara), proyectado en XZ ────────────
        Vector3 C = _endEffector.position - cam.transform.position;
        C.y = 0f;
        if (C.sqrMagnitude < 1e-6f)
        {
            Debug.LogWarning("[JoystickAdapter] Cámara sobre el efector; se mantiene el mapeo actual.");
            return;
        }
        C = C.normalized;

        // ── Paso 2: Ejes locales proyectados en XZ ───────────────────────────
        Vector3 localFwd = _endEffector.forward; localFwd.y = 0f;
        Vector3 localRgt = _endEffector.right; localRgt.y = 0f;

        // Evitar vectores nulos (si el efector apunta exactamente hacia arriba/abajo)
        if (localFwd.sqrMagnitude < 1e-6f) localFwd = Vector3.forward;
        if (localRgt.sqrMagnitude < 1e-6f) localRgt = Vector3.right;
        localFwd = localFwd.normalized;
        localRgt = localRgt.normalized;

        float rawAngleFwd = Vector3.Angle(C, localFwd);   // 0..180
        float rawAngleRgt = Vector3.Angle(C, localRgt);   // 0..180

        // ── Paso 3: Ajustar ángulos al rango [-90, 90] ───────────────────────
        //   Si el ángulo es > 90°, el vector "apunta en dirección opuesta" a C.
        //   Restamos 180° para obtener cuánto falta respecto al vector invertido.
        float adjFwd = rawAngleFwd > 90f ? rawAngleFwd - 180f : rawAngleFwd;
        float adjRgt = rawAngleRgt > 90f ? rawAngleRgt - 180f : rawAngleRgt;

        // El ángulo ajustado negativo significa que el eje INVERTIDO es el
        // que apunta hacia C; su valor absoluto indica qué tan bien se alinea.

        bool fwdIsZ = Mathf.Abs(adjFwd) <= Mathf.Abs(adjRgt);  // fwd → MoveZ?

        // ── Paso 4: Asignar MoveZ ─────────────────────────────────────────────
        Vector3 axisZ;
        float signZ;
        float rawAngleZ;
        Vector3 axisX;
        float rawAngleX;

        if (fwdIsZ)
        {
            axisZ = localFwd;
            rawAngleZ = rawAngleFwd;
            axisX = localRgt;
            rawAngleX = rawAngleRgt;
        }
        else
        {
            axisZ = localRgt;
            rawAngleZ = rawAngleRgt;
            axisX = localFwd;
            rawAngleX = rawAngleFwd;
        }

        // Si el ángulo original era > 90° el eje apunta "al revés" respecto a C
        // → invertir el sentido positivo de MoveZ
        signZ = rawAngleZ > 90f ? -1f : 1f;

        // ── Paso 5: Asignar MoveX y determinar su signo ───────────────────────
        // Producto vectorial: axisX × C  (ambos en XZ → resultado en Y)
        // Si el Y del resultado es positivo el producto apunta en la dirección
        // del mundo +Y → no invertir.  Si es negativo → invertir.
        Vector3 cross = Vector3.Cross(axisX, C);
        float signX = cross.y >= 0f ? 1f : -1f;

        // ── Guardar resultados ────────────────────────────────────────────────
        _dirZ = axisZ;
        _signZ = signZ;
        _dirX = axisX;
        _signX = signX;
    }
}