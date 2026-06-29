# Arquitectura de Control - Planta BrazoRobotico

**Fecha:** 2026-06-24
**Branch:** FisicaEslabones
**Robot:** KUKA KR210 R3100-2
**Libreria IK:** Flange 1.0.11

---

## Objetivo de esta nota

Este documento describe la cadena real de control usada por `JoystickAdapter`.

Las pruebas de cambio y el feedback observado se registran aparte en:

`Assets/Scripts/_REGISTRO_PRUEBAS_CONTROL.md`

La version actual evita soluciones que no funcionaron bien:

- No mantiene un ultimo objetivo IK activo cuando los ejes del joystick vuelven a cero.
- No detecta "movimiento vertical puro" ni aplica una correccion especial de orientacion.
- No fija la orientacion del TCP contra mundo Unity.
- No usa seguimiento articular directo de la solucion IK; esa prueba empeoro el comportamiento.

La orientacion fija del TCP se expresa respecto del frame del robot/Flange. Por defecto se preserva la rotacion actual exacta del TCP, sin reconstruirla contra mundo ni forzar una nueva alineacion de eje.

---

## Flujo actual

```text
PS4 Controller / Input System
        |
        |  _moveX / _moveY / _moveZ
        v
JoystickAdapter.Update()
        |
        |  rawX/Y/Z calibrados
        |  _velocity = _dirX * X + Vector3.up * Y + _dirZ * Z
        v
JoystickAdapter.FixedUpdate()
        |
        |  si _velocity ~= 0:
        |      ResetPIDs()
        |      CaptureFixedOrientation()
        |      return
        |
        |  si empieza un nuevo recorrido:
        |      ResetPIDs()
        |      CaptureFixedOrientation()
        |
        |  deltaWorld = _velocity * _speed * fixedDeltaTime
        |  deltaFrame = WorldVectorToFrame(deltaWorld, frame, extJoint)
        |  currentPose = solucion IK anterior o ToolCenterPointFrame
        |  targetPose = TRS(currentPos + deltaFrame, _fixedTcpFrameOrientation, one)
        v
CartesianTarget(targetPose, configuration, extJoint)
        |
        v
Flange Solver.ComputeInverse(target, tool, frame)
        |
        |  solution.JointTarget
        v
JoystickAdapter.ApplyPID(solution.JointTarget)
        |
        |  velocidad estimada del JointTarget
        |  JointPID + RobotDynamics.ComputeEffectiveInertia()
        |  inertia floor per joint group
        |  damping + velocity clamp
        v
MechanicalGroup.SetJoints(new JointTarget(qNew), notify: true)
```

Flange recibe posiciones articulares. Unity Physics no aplica torques ni fuerzas al brazo principal.

---

## 1. Entrada de joystick

**Archivo:** `Assets/Scripts/JoystickAdapter.cs`

| Campo | Rol |
|---|---|
| `_moveX` | Movimiento lateral remapeado respecto de la vista del operador. |
| `_moveY` | Movimiento vertical en mundo (`Vector3.up`). |
| `_moveZ` | Movimiento frontal remapeado respecto de la vista del operador. |
| `_modoCamara` | Toggle entre modo robot y modo camara. |
| `_calibrationManager` | Lee ejes calibrados si esta disponible. |

`Update()` solo arma una velocidad cartesiana. No integra posicion y no llama IK.

```csharp
float rawX = ReadAxis(_moveX, _invertMoveX);
float rawY = ReadAxis(_moveY, _invertMoveY);
float rawZ = ReadAxis(_moveZ, _invertMoveZ);

_velocity = _dirX * (rawX * _signX)
          + Vector3.up * rawY
          + _dirZ * (rawZ * _signZ);
```

Si el input esta suprimido o el modo camara esta activo, `_velocity` queda en cero y se limpia la UI de acciones articulares.

`RemapAxesFromCamera()` recalcula `_dirX`, `_dirZ`, `_signX` y `_signZ` al volver desde el modo camara para que los ejes horizontales se sientan naturales desde la posicion del operador.

---

## 2. Movimiento cartesiano e IK

**Archivo:** `Assets/Scripts/JoystickAdapter.cs`

`FixedUpdate()` es el unico lugar donde se genera un objetivo cartesiano para IK.

Si el joystick vuelve a reposo, el controlador no sigue persiguiendo el ultimo objetivo valido. Tambien limpia memoria dinamica y refresca la orientacion fija desde la pose real actual:

```csharp
if (_velocity.sqrMagnitude < MotionInputEpsilon)
{
    EndMotion();
    return;
}
```

Cuando empieza un recorrido nuevo, `BeginMotion()` reinicia las memorias del PID y captura la orientacion actual del TCP antes de calcular IK:

```csharp
if (!_motionActive)
    BeginMotion();
```

Con input activo, el desplazamiento del joystick se calcula en mundo Unity y luego se convierte al frame activo de Flange. La pose objetivo se arma en ese frame para que la orientacion del TCP quede fija respecto del robot, no respecto del mundo:

```csharp
var deltaWorld = _velocity * (_speed * Time.fixedDeltaTime);
var deltaFrame = WorldVectorToFrame(deltaWorld, frame, extJoint);
var currentPose = _controller.PoseObserver.ToolCenterPointFrame.Value;
Vector3 currentPos = (Vector3)currentPose.GetColumn(3);
El controlador traza una trayectoria matemática partiendo del IK previo para evitar que la inercia del PID corrompa la trayectoria (Desacoplamiento de Trayectoria Cartesiana):

```csharp
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
```

La meta IK es directamente un paso en línea recta desde la meta anterior, manteniendo la orientación constante.

```csharp
targetPose = Matrix4x4.TRS(
    currentPos + deltaFrame,
    _fixedTcpFrameOrientation,
    Vector3.one);
```

La solucion IK sigue usando el frame/tool/configuracion actuales de Flange. `targetPose` ya llega expresado en el `frame` activo:

```csharp
var frame = _controller.Frame.Value;
var tool = _controller.Tool.Value;
var configuration = _controller.Configuration.Value;
var extJoint = _controller.MechanicalGroup.JointState.ExtJoint;

var target = new CartesianTarget(targetPose, configuration, extJoint);
var solution = _controller.Solver.ComputeInverse(target, tool, frame);
```

Si `solution.IsValid` es falso, no se aplica `SetJoints()` en ese tick.

Diagnostico opcional:

```csharp
LogIkPoseError(currentPose, targetPose, solution.JointTarget, tool, frame, extJoint);
```

Con `_logIkPoseError` activo, se compara `targetPose` contra la FK de `solution.JointTarget` en el mismo frame. Si `poseErr` y `rotErr` son pequenos, la IK esta devolviendo una solucion coherente y el error observable esta despues de IK. Si esos errores son grandes, el problema esta antes o dentro de la resolucion IK/configuracion. El log tambien incluye `targetStep`, `targetRotStep`, `stepScale` y `jointStepLimit`, que indican cuanto salto de posicion/orientacion se le esta pidiendo al target y cuanto se redujo el paso cartesiano.

Para ensayos automaticos, `JoystickAdapter` expone:

```csharp
SetDiagnosticInputOverride(Vector3 worldVelocity);
ClearDiagnosticInputOverride();
LastIkDiagnostic;
```

Estos hooks no se usan en la operacion normal con joystick. Permiten que un runner de diagnostico inyecte una velocidad cartesiana y lea las mismas metricas de IK/PID sin depender de input fisico.

---

## 3. Orientacion del TCP

La orientacion del TCP se captura y queda fija mientras se traslada:

```csharp
_fixedTcpFrameOrientation = _controller.PoseObserver.ToolCenterPointFrame.Value.rotation;
_orientationCaptured = true;
```

La orientacion fija se toma directamente desde la pose actual, preservando la rotacion exacta del TCP respecto del robot. La orientacion que se bloquea es una rotacion completa en el frame del robot.

Eventos que recapturan orientacion:

| Evento | Accion |
|---|---|
| `Start()` | `CaptureFixedOrientation()` |
| Comienzo de un recorrido | `ResetPIDs()`, `CaptureFixedOrientation()` |
| Reposo del joystick | `ResetPIDs()`, `CaptureFixedOrientation()` |
| Volver de modo camara a modo robot | `RemapAxesFromCamera()`, `ResetPIDs()`, `CaptureFixedOrientation()` |
| Cambio de perfil de input | Sale de modo camara, resetea estado y recaptura orientacion |

No hay una rama especial para movimiento vertical. Si el TCP aparece inclinado durante un desplazamiento vertical, la causa probable no es el input vertical en si, sino alguna combinacion de frame/tool, orientacion TCP esperada, solucion IK equivalente, integral acumulada, o respuesta dinamica de la muneca.

---

## 4. PID por articulacion

**Archivos:** `Assets/Scripts/JoystickAdapter.cs`, `Assets/Scripts/JointPID.cs`

La salida de IK es un `JointTarget` y se pasa a `ApplyPID(solution.JointTarget)`. La prueba de seguimiento articular directo con `MoveTowardsAngle` empeoro el comportamiento y fue revertida.

El PID calcula un torque virtual en grados/s2 a inercia de referencia.

La derivada trabaja como error de velocidad entre el target IK estimado y la medicion articular:

```csharp
float error = Mathf.DeltaAngle(current, setpoint);
_integral = Mathf.Clamp(_integral + error * dt, -MaxIntegral, MaxIntegral);

float measuredVelocity = _hasPrevCurrent && dt > 1e-6f
    ? Mathf.DeltaAngle(_prevCurrent, current) / dt
    : 0f;

float derivative = setpointVelocity - measuredVelocity;
return Kp * error + Ki * _integral + Kd * derivative;
```

`setpointVelocity` se estima en `JoystickAdapter` desde la diferencia angular entre el `JointTarget` IK actual y el anterior:

```csharp
float qTargetVelocity = _hasPrevIkTarget && dt > 1e-6f 
    ? Mathf.DeltaAngle(_prevIkTarget[jointIndex], qTarget) / dt 
    : 0f;
```

La estimación alimenta tanto al término derivativo como a un cálculo de feedforward estricto que contrarresta matemáticamente el amortiguamiento viscoso. 
El primer tick despues de `ResetPIDs()`, `BeginMotion()` o una IK invalida usa `qTargetVelocity = 0`, porque todavia no hay target previo confiable.

`Reset()` limpia integral y medicion previa:

```csharp
_integral = 0f;
_prevCurrent = 0f;
_hasPrevCurrent = false;
```

`ResetPIDs()` tambien limpia `_prevIkTarget` y `_hasPrevIkTarget`, para que el primer tick de un movimiento nuevo no use una velocidad de target stale.

---

## 5. Inercia simulada y muneca

**Archivo:** `Assets/Scripts/RobotDynamics.cs`

`ComputeEffectiveInertia()` estima:

```text
J_eff[i] = sum(j >= i) masa_j * distancia_perpendicular_ij^2
```

**Archivo:** `Assets/Scripts/JoystickAdapter.cs`

`ApplyPID()` divide el torque virtual por una inercia normalizada:

```csharp
float jNorm = Mathf.Max(jEff[i] / _referenceInertia, 0.05f);
_jointVelocity[i] += (torque / jNorm) * dt;
```

Se mantiene un piso de inercia mínima del 5% para evitar inestabilidad numérica o multiplicaciones explosivas del control PID cuando `jEff` cae casi a cero en los eslabones livianos de la muñeca.

Despues de integrar aceleracion, se aplica amortiguamiento y limite de velocidad:

```csharp
_jointVelocity[i] -= _velocityDamping * _jointVelocity[i] * dt;
_jointVelocity[i] = Mathf.Clamp(_jointVelocity[i], -_maxJointVelocity, _maxJointVelocity);
qNew[i] = qActual + _jointVelocity[i] * dt;
```

---

## 6. Resets de estado

| Evento | Accion actual |
|---|---|
| Modo camara activo | No se mueve el brazo. `_velocity` queda en cero. |
| Volver a modo robot | Remapea ejes, resetea PIDs/velocidades, recaptura orientacion. |
| Input suprimido | `_velocity = Vector3.zero`, reset de PIDs/velocidades, UI en cero. |
| Joystick en reposo | Resetea PIDs/velocidades, recaptura orientacion y retorna. No hay target activo. |
| Inicio de recorrido | Resetea PIDs/velocidades y captura la orientacion TCP actual antes de IK. |
| IK invalida | Limpia UI y no aplica nuevos joints en ese tick. |

---

## 7. Scripts no relacionados con la cadena principal

| Script | Rol | Relacion con PID/IK |
|---|---|---|
| `GripperController.cs` | Logica de agarre/suelta | Independiente |
| `Ctrl_OnRobot_RG2_Custom.cs` | Animacion de dedos del gripper | Independiente |
| `GripperDistanceSensor.cs` | Sensor de distancia del gripper | Independiente |
| `GripperTopCameraFollow.cs` | Camara superior del gripper | Independiente |
| `GripperTriggerForwarder.cs` | Reenvio de triggers | Independiente |
| `JoystickVibrationHidOutput.cs` | Vibracion del joystick | Independiente |
| `AxisUIController.cs` | UI de ejes | Independiente |
| `JointStatePublisher.cs` | Publicacion de estado articular | Independiente |
| `RobotTest.cs` | Pruebas/manual | Independiente |

---

## 8. Diagnostico automatico

**Archivos:** `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs`, `Assets/Editor/ControlDiagnosticBatch.cs`

`ControlDiagnosticRunner` ejecuta un barrido vertical automatico en Play Mode:

1. Encuentra `JoystickAdapter` y `Controller` en la escena.
2. Guarda el `JointTarget` inicial.
3. Captura pose TCP y joints de inicio para medir ida/vuelta.
4. Inyecta velocidad `Vector3.up` durante 120 ticks.
5. Deja reposar 20 ticks.
6. Inyecta velocidad `Vector3.down` durante 120 ticks.
7. Deja reposar 20 ticks.
8. Calcula error final respecto del inicio y deriva durante reposos.
9. Restaura el `JointTarget` inicial.
10. Escribe un resumen JSON en `Logs/control_vertical_sweep_latest.json`.

Tambien puede ejecutar una matriz de variantes con `RunVerticalSweepMatrix()`. Esa matriz cambia temporalmente parametros privados del `JoystickAdapter` durante Play Mode y los restaura al terminar. Actualmente compara:

- `scene_current`: valores serializados en la escena.
- `joint_limit_3`: `_ikJointStepLimitMultiplier = 3`.
- `joint_limit_2`: `_ikJointStepLimitMultiplier = 2`.
- `joint_limit_1`: `_ikJointStepLimitMultiplier = 1`.
- `joint_limit_2_maxvel_120`: `_ikJointStepLimitMultiplier = 2`, `_maxJointVelocity = 120`.

El reporte queda en `Logs/control_vertical_sweep_matrix_latest.json` e incluye, por variante, error de orientacion, error articular, `stepScale` y distancia TCP recorrida por segmento. Tambien mide `finalTcpWorldError`, `finalTcpFrameError`, `finalTcpFrameRotationError`, `finalMaxJointRoundTripError`, `maxRestWorldDrift` y `netWorldYDisplacement`. Esto evita aceptar una variante que "mejora" solo porque casi no se mueve o porque baja el error articular pero no vuelve al punto inicial.

Comando batch usado:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath . `
  -executeMethod ControlDiagnosticBatch.RunVerticalSweep `
  -logFile "Logs/control_vertical_sweep_unity.log"
```

Para la matriz:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath . `
  -executeMethod ControlDiagnosticBatch.RunVerticalSweepMatrix `
  -logFile "Logs/control_vertical_sweep_matrix_unity.log"
```

No usar `-nographics` para este proyecto: URP intenta crear `RenderTexture` y puede fallar con dispositivo grafico nulo. El runner no requiere modificar ni guardar la escena.
