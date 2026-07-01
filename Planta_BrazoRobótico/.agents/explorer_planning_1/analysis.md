# Análisis y Plan de Implementación — R1 a R4 y Diagnósticos

Este documento detalla el análisis del código base y la estrategia propuesta para implementar los cuatro nuevos requisitos (R1-R4) y su correspondiente verificación mediante diagnósticos estructurados en batch.

---

## R1. Modo de Orientación del TCP (Menú de Pausa)

### 1. Localización
El comportamiento de rotación circular del TCP (donde el TCP rota dinámicamente para mantenerse alineado respecto al eslabón 1/base giratoria) se encuentra en:
* **Archivo:** `Assets/Scripts/JoystickAdapter.cs`
* **Líneas relevantes:** 469–473 (en `FixedUpdate`):
  ```csharp
  float currentJ1 = _hasPrevIkTarget ? _prevIkTarget[0] : _controller.MechanicalGroup.JointState[0];
  float deltaJ1 = currentJ1 - _initialJ1Angle;
  Quaternion j1Rotation = Quaternion.AngleAxis(deltaJ1, Vector3.down);
  Quaternion dynamicTcpOrientation = j1Rotation * _fixedTcpFrameOrientation;
  ```
  Y su posterior aplicación al target del resolvedor IK en la línea 518:
  ```csharp
  Matrix4x4 targetPose = Matrix4x4.TRS(currentPos + deltaFrame, dynamicTcpOrientation, Vector3.one);
  ```

El código de la interfaz y del controlador del Menú de Pausa está en:
* **Archivo:** `Assets/Scripts/PauseMenuController.cs`
* **Líneas relevantes:** El método `BuildDefaultMenu` (línea 626) construye dinámicamente la UI cuando no está asignado el prefab, y `WireButtons` (línea 329) conecta los callbacks de interacción.

### 2. Estrategia y Propuesta de Código

#### Cambios en `JoystickAdapter.cs`:
Introducir una propiedad serializada y pública para activar/desactivar este comportamiento, la cual por defecto será `false` (manteniendo la orientación exacta capturada originalmente, sin rotar con J1).

```csharp
[Header("TCP Orientation")]
[Tooltip("Si es true, el TCP rota para mantenerse recto respecto a la base (J1). Si es false (por defecto), se preserva la orientación exacta capturada.")]
[SerializeField] private bool _alignOrientationWithJ1 = false;

public bool AlignOrientationWithJ1
{
    get => _alignOrientationWithJ1;
    set => _alignOrientationWithJ1 = value;
}
```

En `FixedUpdate()`, modificar la definición de `dynamicTcpOrientation`:
```csharp
// ── Guarda de orientación física ──────────────────────────────────
float currentJ1 = _hasPrevIkTarget ? _prevIkTarget[0] : _controller.MechanicalGroup.JointState[0];
float deltaJ1 = currentJ1 - _initialJ1Angle;
Quaternion j1Rotation = Quaternion.AngleAxis(deltaJ1, Vector3.down);

// Si _alignOrientationWithJ1 es false, mantenemos la orientación fija absoluta original.
Quaternion dynamicTcpOrientation = _alignOrientationWithJ1 
    ? j1Rotation * _fixedTcpFrameOrientation 
    : _fixedTcpFrameOrientation;
```

#### Cambios en `PauseMenuController.cs`:
Añadir un botón para alternar el modo de orientación.
```csharp
[SerializeField] private Button tcpOrientationButton;
```

En `BuildDefaultMenu()` (justo antes del botón de continuar):
```csharp
tcpOrientationButton = CreateButton("Orientacion: Fija Absoluta", panel.transform);
```

En `WireButtons()`:
```csharp
if (tcpOrientationButton != null)
    tcpOrientationButton.onClick.AddListener(ToggleTcpOrientationMode);
```

Implementar los métodos de control de UI:
```csharp
public void ToggleTcpOrientationMode()
{
    var joystickAdapter = FindFirstObjectByType<JoystickAdapter>();
    if (joystickAdapter != null)
    {
        joystickAdapter.AlignOrientationWithJ1 = !joystickAdapter.AlignOrientationWithJ1;
        UpdateTcpOrientationUi();
    }
}

private void UpdateTcpOrientationUi()
{
    if (tcpOrientationButton != null)
    {
        var label = tcpOrientationButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            var joystickAdapter = FindFirstObjectByType<JoystickAdapter>();
            bool align = joystickAdapter != null && joystickAdapter.AlignOrientationWithJ1;
            label.text = align ? "Orientación: Seguir Base (J1)" : "Orientación: Fija Absoluta";
        }
    }
}
```
Llamar a `UpdateTcpOrientationUi()` en `Start()` y `UpdateProfileUi()` para sincronizar el texto del botón al abrir el menú.

---

## R2. Interfaz del Modo J6 Exclusivo (Superposición)

### 1. Localización
* **J6HUDController:** `Assets/Scripts/J6HUDController.cs`. Su método `BuildUI()` (línea 81) construye un panel lateral semitransparente con un dial analógico independiente para J6.
* **GripperCamera / CameraGripperView:** Definidos en `Assets/Scenes/Planta.unity`. El GameObject `CameraGripperView` (ID `1832152286`) tiene un componente `RawImage` que renderiza la textura `RT_GripperCamera.renderTexture`.

### 2. Estrategia y Propuesta de Código
Proponemos eliminar el script `J6HUDController.cs` original (o vaciarlo) y crear un nuevo script `J6OverlayController.cs` que se adjunte a un objeto persistente. Este script construirá un dial circular traslúcido y lo colocará directamente como hijo de `CameraGripperView`, superponiéndolo de forma limpia sobre el feed de vídeo de la cámara.

#### Implementación de `J6OverlayController.cs`:
```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class J6OverlayController : MonoBehaviour
{
    [SerializeField] private Preliy.Flange.Controller controller;
    
    private GameObject overlayContainer;
    private RectTransform dialPointer;
    private TextMeshProUGUI angleText;

    private void Start()
    {
        if (controller == null)
            controller = FindFirstObjectByType<Preliy.Flange.Controller>();

        BuildOverlayUI();
    }

    private void Update()
    {
        bool isExclusive = JoystickAdapter.IsJ6ExclusiveMode;

        if (overlayContainer != null && overlayContainer.activeSelf != isExclusive)
        {
            overlayContainer.SetActive(isExclusive);
        }

        if (!isExclusive || controller == null || !controller.IsValid.Value)
            return;

        float j6Angle = controller.MechanicalGroup.JointState[5];

        if (dialPointer != null)
            dialPointer.localEulerAngles = new Vector3(0, 0, -j6Angle);

        if (angleText != null)
            angleText.text = $"{j6Angle:F1}°";
    }

    private void BuildOverlayUI()
    {
        var cameraView = GameObject.Find("CameraGripperView");
        if (cameraView == null)
        {
            Debug.LogError("[J6OverlayController] No se encontró CameraGripperView en la escena para superponer el dial.");
            return;
        }

        // Contenedor de la superposición
        overlayContainer = new GameObject("J6_Overlay_Content");
        overlayContainer.transform.SetParent(cameraView.transform, false);
        
        RectTransform containerRect = overlayContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = Vector2.zero;
        containerRect.anchorMax = Vector2.one;
        containerRect.offsetMin = Vector2.zero;
        containerRect.offsetMax = Vector2.zero;

        // Dial de fondo traslúcido para ver la cámara por debajo
        GameObject dialBg = new GameObject("Dial_BG");
        dialBg.transform.SetParent(overlayContainer.transform, false);
        RectTransform bgRect = dialBg.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.sizeDelta = new Vector2(160f, 160f);
        bgRect.anchoredPosition = Vector2.zero;
        
        Image bgImg = dialBg.AddComponent<Image>();
        bgImg.color = new Color(0.08f, 0.08f, 0.08f, 0.35f); // Alta transparencia
        bgImg.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        bgImg.raycastTarget = false;

        // Aguja indicadora
        GameObject pointer = new GameObject("Pointer");
        pointer.transform.SetParent(dialBg.transform, false);
        dialPointer = pointer.AddComponent<RectTransform>();
        dialPointer.anchorMin = new Vector2(0.5f, 0.5f);
        dialPointer.anchorMax = new Vector2(0.5f, 0.5f);
        dialPointer.sizeDelta = new Vector2(4f, 70f);
        dialPointer.pivot = new Vector2(0.5f, 0f);
        dialPointer.anchoredPosition = Vector2.zero;
        
        Image ptrImg = pointer.AddComponent<Image>();
        ptrImg.color = new Color(1f, 0.25f, 0.25f, 0.75f);
        ptrImg.raycastTarget = false;

        // Marcas de referencia fijas
        CreateMark(dialBg.transform, 0f, "0°", 65f);
        CreateMark(dialBg.transform, 90f, "90°", 65f);
        CreateMark(dialBg.transform, 180f, "180°", 65f);
        CreateMark(dialBg.transform, 270f, "-90°", 65f);

        // Texto del ángulo actual en el centro inferior de la cámara
        angleText = CreateText("AngleText", "0.0°", overlayContainer.transform,
            new Vector2(0f, -62f), new Vector2(100f, 25f), 13, Color.white, FontStyles.Bold);

        overlayContainer.SetActive(false);
    }

    private void CreateMark(Transform parent, float angleDeg, string text, float radius)
    {
        float rad = (90f - angleDeg) * Mathf.Deg2Rad;
        Vector2 pos = new Vector2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
        CreateText($"Mark_{text}", text, parent, pos, new Vector2(50f, 20f), 10, new Color(1f, 1f, 1f, 0.6f), FontStyles.Normal);
    }

    private TextMeshProUGUI CreateText(string name, string text, Transform parent, Vector2 pos, Vector2 size, int fontSize, Color color, FontStyles style)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.raycastTarget = false;
        return tmp;
    }
}
```

---

## R3. Sensibilidad del Modo J6

### 1. Localización
El control de la rotación de J6 en modo exclusivo ocurre en:
* **Archivo:** `Assets/Scripts/JoystickAdapter.cs`
* **Método:** `UpdateJ6ExclusiveControl()` (líneas 675–735).

### 2. Estrategia y Propuesta de Código
Actualmente, el ángulo del stick analógico se mapea de forma absoluta e instantánea a `_j6TargetAngle`. Para reducir la sensibilidad un factor de 4 (hacer el movimiento 4 veces más lento), implementaremos un límite de velocidad de cambio en el ángulo objetivo (`_j6TargetAngle`) de 90°/s (ya que el límite físico estándar es de 360°/s).

Modificación en `UpdateJ6ExclusiveControl()`:
```csharp
if (input.sqrMagnitude > 0.04f) // Deadzone de 0.2
{
    float angle = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
    float delta = Mathf.DeltaAngle(currentJ6, angle);
    
    // Sensibilidad reducida un factor de 4: velocidad máxima de 90°/s
    float maxSpeed = 90f; // grados por segundo
    float maxStep = maxSpeed * Time.fixedDeltaTime;
    float clampedDelta = Mathf.Clamp(delta, -maxStep, maxStep);
    
    float targetAngle = currentJ6 + clampedDelta;
    float clampedAngle = Mathf.Clamp(targetAngle, _j6MinLimit, _j6MaxLimit);
    
    if (Mathf.Approximately(clampedAngle, _j6MinLimit) || Mathf.Approximately(clampedAngle, _j6MaxLimit))
    {
        LogDiagnosticJson("J6_LimitReached", $"Se alcanzo el limite articular de J6: {clampedAngle:F1}deg", clampedAngle);
    }

    _j6TargetAngle = clampedAngle;
}
```

Para asegurar que el seguimiento físico de la articulación J6 tampoco sobrepase este límite de velocidad de 90°/s durante el modo exclusivo, modificamos `ApplyPID()` para limitar la velocidad articular de J6 a 90°/s:
```csharp
float speedLimit = (i == 5) ? 90f : _maxJointVelocity;
_jointVelocity[i] = Mathf.Clamp(_jointVelocity[i], -speedLimit, speedLimit);
```

---

## R4. Reseteo de J6 con Doble Clic del Gripper

### 1. Localización
* **Entrada del Gripper:** `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs` en el método `OnToggleGrip` (línea 95), que recibe el evento `performed` del `_toggleGripAction`.

### 2. Estrategia y Propuesta de Código

#### En `Ctrl_OnRobot_RG2_Custom.cs`:
Detectar un doble clic midiendo la diferencia de tiempo entre dos activaciones sucesivas del trigger (ventana de 400ms).
* **Un clic simple:** Invoca la lógica habitual (`_gripperController.ToggleGrip()`).
* **Un doble clic:** Cancela el efecto del primer clic (vuelve a hacer Toggle para revertir el estado físico del gripper) y solicita al `JoystickAdapter` que resetee J6 suavemente a 0°.

```csharp
private float _lastClickTime = -999f;
private const float DoubleClickTimeWindow = 0.4f; // 400ms

private void OnToggleGrip(InputAction.CallbackContext ctx)
{
    if (_inputSuppressed)
        return;

    float currentTime = Time.unscaledTime;
    if (currentTime - _lastClickTime < DoubleClickTimeWindow)
    {
        // ── DOBLE CLIC DETECTADO ──
        // Revertir el estado físico del gripper del primer clic
        _isOpen = !_isOpen;
        stroke = _isOpen ? s_max : s_min;
        speed = v_max;
        start_movement = true;

        if (_gripperController != null)
        {
            _gripperController.ToggleGrip(); // Invoca toggle nuevamente para revertir
        }

        // Ordenar al adaptador del robot retornar J6 a 0° suavemente
        var adapter = FindFirstObjectByType<JoystickAdapter>();
        if (adapter != null)
        {
            adapter.ResetJ6ToZero();
        }

        _lastClickTime = -999f; // Resetear para evitar falsas detecciones sucesivas
    }
    else
    {
        // ── CLIC SIMPLE (o primer clic de un doble clic potencial) ──
        _lastClickTime = currentTime;
        _isOpen = !_isOpen;
        stroke = _isOpen ? s_max : s_min;
        speed = v_max;
        start_movement = true;

        if (_gripperController != null)
        {
            _gripperController.ToggleGrip();
        }
    }
}
```

#### En `JoystickAdapter.cs`:
Implementar la lógica de interpolación y el método `ResetJ6ToZero()` para devolver suavemente J6 a 0°. Esto debe ser robusto y funcionar tanto en modo cartesiano como en modo J6 exclusivo.

```csharp
private bool _resettingJ6 = false;
private float _j6ResetSpeed = 90f; // 90°/s para un retorno suave pero ágil

public bool ResettingJ6 => _resettingJ6;

public void ResetJ6ToZero()
{
    if (_controller == null || !_controller.IsValid.Value) return;
    _resettingJ6 = true;
    _j6TargetAngle = _controller.MechanicalGroup.JointState[5];
}
```

En `FixedUpdate()`:
```csharp
if (_resettingJ6)
{
    float dt = Time.fixedDeltaTime;
    _j6TargetAngle = Mathf.MoveTowards(_j6TargetAngle, 0f, _j6ResetSpeed * dt);
    
    if (Mathf.Abs(_j6TargetAngle) < 0.05f)
    {
        _j6TargetAngle = 0f;
        _resettingJ6 = false;
        
        // Al finalizar en modo cartesiano, recapturamos la orientación para evitar
        // un snap/salto brusco hacia la orientación cartesiana vieja.
        _orientationCaptured = false;
        CaptureFixedOrientation();
    }
}
```

En `ApplyPID()` (dentro del bucle de articulaciones):
```csharp
float qTarget = ikTarget[i];

// Si estamos reseteando J6, sobreescribimos su objetivo de trayectoria con el valor interpolado.
if (i == 5 && _resettingJ6)
{
    qTarget = _j6TargetAngle;
}
```

---

## 5. Diagnósticos y Pruebas

### 1. Análisis de los archivos de Diagnóstico
* `Assets/Editor/ControlDiagnosticBatch.cs`: Expone funciones estáticas ejecutables desde el menú de Unity (`Tools/Control/...`) o mediante línea de comandos (`-executeMethod`). Activa el Play Mode de forma segura cargando la escena `Planta.unity`, instancia el GameObject del runner, inicia su corrutina y sale al finalizar.
* `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs`: Ejecuta barridos cinemáticos cartesianos inyectando comandos mediante `SetDiagnosticInputOverride` y capturando telemetría periódica.
* **Telemetría Registrada:** Registra errores de pose y rotación de IK, pasos angulares máximos/promedio, errores de articulación y de muñeca, así como errores de ida y vuelta (round-trip) acumulados. Todo se escribe en formato JSON en `Logs/control_*.json`.

### 2. Estrategia para Ejecutar Pruebas en Modo Batch
Para correr el diagnóstico de J6 en batch (por ejemplo, en un flujo de integración continua en PowerShell):
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath . `
  -executeMethod ControlDiagnosticBatch.RunJ6Diagnostic `
  -logFile "Logs/control_j6_diagnostic_unity.log"
```

### 3. Propuesta de Pruebas de Diagnóstico para J6 (R3 y R4)
Agregaremos un nuevo test `RunJ6Diagnostic()` en `ControlDiagnosticRunner.cs` para verificar de forma automatizada y sin intervención manual el correcto funcionamiento del factor de sensibilidad de 0.25x (R3) y del reseteo suave por doble clic (R4).

#### Añadir a `ControlDiagnosticBatch.cs`:
```csharp
[MenuItem("Tools/Control/Run J6 Diagnostic")]
public static void RunJ6DiagnosticMenu()
{
    StartRun("j6");
}
```
Y en `OnPlayModeStateChanged()`:
```csharp
if (mode == "j6")
    runner.StartCoroutine(runner.RunJ6Diagnostic());
```

#### Implementación del Test en `ControlDiagnosticRunner.cs`:
```csharp
public IEnumerator RunJ6Diagnostic()
{
    var adapter = FindFirstObjectByType<JoystickAdapter>();
    var controller = FindFirstObjectByType<Controller>();
    var gripper = FindFirstObjectByType<Ctrl_OnRobotRG2_Custom>();

    if (adapter == null || controller == null || !controller.IsValid.Value || gripper == null)
    {
        string error = "[ControlDiagnosticRunner] No se encontraron todos los componentes necesarios (Adapter, Controller, Gripper).";
        Debug.LogError(error, this);
        Completed?.Invoke(1, error);
        yield break;
    }

    Debug.Log("[ControlDiagnosticRunner] --- INICIANDO DIAGNÓSTICO J6 ---");

    // --- 1. Inicialización de Pose ---
    var initialTarget = CopyJointTarget(controller.MechanicalGroup.JointState);
    float[] joints = new float[6];
    for (int i = 0; i < 6; i++) joints[i] = controller.MechanicalGroup.JointState[i];
    joints[5] = 0f;
    controller.MechanicalGroup.SetJoints(new JointTarget(joints), notify: true);
    yield return new WaitForSeconds(0.5f); // Settle

    // --- 2. Verificar R3: Sensibilidad de J6 (Velocidad Capped a 90°/s) ---
    Debug.Log("[ControlDiagnosticRunner] Verificando R3 (Sensibilidad J6 0.25x)...");
    
    // Activar modo J6 Exclusivo mediante reflexión o llamada directa
    var field = typeof(JoystickAdapter).GetField("<IsJ6ExclusiveMode>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
    field?.SetValue(null, true);

    // Inyectar un valor objetivo lejano en _j6TargetAngle (ej. 90°)
    var targetField = typeof(JoystickAdapter).GetField("_j6TargetAngle", BindingFlags.Instance | BindingFlags.NonPublic);
    targetField?.SetValue(adapter, 90f);

    float startJ6 = controller.MechanicalGroup.JointState[5];
    float timeStart = Time.time;
    
    // Medir durante 10 ticks de FixedUpdate
    for (int i = 0; i < 10; i++)
    {
        yield return new WaitForFixedUpdate();
    }
    
    float timeEnd = Time.time;
    float endJ6 = controller.MechanicalGroup.JointState[5];
    float velocity = Mathf.Abs(endJ6 - startJ6) / (timeEnd - timeStart);
    
    Debug.Log($"[ControlDiagnosticRunner] Velocidad medida en J6: {velocity:F2}°/s. Límite esperado: <= 90°/s.");
    if (velocity > 95f) // Pequeño margen de tolerancia por integración numérica
    {
        string msg = $"FALLÓ R3: La velocidad de J6 ({velocity:F2}°/s) supera la sensibilidad reducida de 90°/s.";
        Debug.LogError(msg);
        Completed?.Invoke(1, msg);
        yield break;
    }
    Debug.Log("[ControlDiagnosticRunner] R3 VERIFICADO CON ÉXITO.");

    // --- 3. Verificar R4: Reseteo con Doble Clic ---
    Debug.Log("[ControlDiagnosticRunner] Verificando R4 (Doble clic de garra resetea J6 a 0°)...");
    
    // Colocar J6 en 45°
    joints[5] = 45f;
    controller.MechanicalGroup.SetJoints(new JointTarget(joints), notify: true);
    yield return new WaitForSeconds(0.5f);
    
    float j6Before = controller.MechanicalGroup.JointState[5];
    Debug.Log($"[ControlDiagnosticRunner] J6 antes del doble clic: {j6Before:F2}°");

    // Simular el doble clic llamando al método privado OnToggleGrip
    var onToggleGripMethod = typeof(Ctrl_OnRobotRG2_Custom).GetMethod("OnToggleGrip", BindingFlags.NonPublic | BindingFlags.Instance);
    var context = new InputAction.CallbackContext();
    
    // Primer clic
    onToggleGripMethod?.Invoke(gripper, new object[] { context });
    // Segundo clic inmediato (100ms después, dentro de la ventana de 400ms)
    yield return new WaitForSeconds(0.1f);
    onToggleGripMethod?.Invoke(gripper, new object[] { context });

    // Verificar que se activó la bandera de reseteo
    yield return new WaitForSeconds(0.1f);
    if (!adapter.ResettingJ6)
    {
        string msg = "FALLÓ R4: No se activó el estado de reseteo de J6 (ResettingJ6 es false).";
        Debug.LogError(msg);
        Completed?.Invoke(1, msg);
        yield break;
    }

    // Esperar a que finalice el reseteo
    float timeout = Time.time + 3.0f;
    while (adapter.ResettingJ6 && Time.time < timeout)
    {
        yield return new WaitForFixedUpdate();
    }

    float j6After = controller.MechanicalGroup.JointState[5];
    Debug.Log($"[ControlDiagnosticRunner] J6 después del reseteo: {j6After:F2}°");
    
    if (Mathf.Abs(j6After) > 0.1f)
    {
        string msg = $"FALLÓ R4: J6 no regresó a 0° (quedó en {j6After:F2}°).";
        Debug.LogError(msg);
        Completed?.Invoke(1, msg);
        yield break;
    }
    
    Debug.Log("[ControlDiagnosticRunner] R4 VERIFICADO CON ÉXITO.");

    // --- 4. Restauración del Estado original ---
    field?.SetValue(null, false);
    controller.MechanicalGroup.SetJoints(initialTarget, notify: true);
    
    string successMsg = "Diagnóstico J6 completado exitosamente (R3 y R4 verificados).";
    Completed?.Invoke(0, successMsg);
}
```
