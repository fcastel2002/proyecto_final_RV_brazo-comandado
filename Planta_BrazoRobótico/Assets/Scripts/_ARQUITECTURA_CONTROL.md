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
        if (_velocity.sqrMagnitude < MotionInputEpsilon)
    {
        EndMotion();
        return;
    }

    // Guarda de orientación física:
    // Verifica la orientación física del TCP contra la esperada.
    // Si el drift supera los grados en _safetyDriftStartThreshold (por defecto 3°),
    // la velocidad de la trayectoria se atenúa linealmente hasta un mínimo del 10%
    // al alcanzar _safetyDriftMaxTolerance (por defecto 5°). Esto permite al operador
    // corregir la desviación y salir de posiciones extremas sin congelar el brazo.
    float driftSpeedMultiplier = 1.0f;
    if (physicalRotDrift > _safetyDriftStartThreshold)
    {
        float range = Mathf.Max(0.1f, _safetyDriftMaxTolerance - _safetyDriftStartThreshold);
        driftSpeedMultiplier = Mathf.Lerp(1.0f, 0.1f, (physicalRotDrift - _safetyDriftStartThreshold) / range);
        driftSpeedMultiplier = Mathf.Max(driftSpeedMultiplier, 0.1f);
    }

    // Avanza trayectoria matemática y resuelve IK
    // (escalada por driftSpeedMultiplier * payloadSpeedMultiplier * proximitySpeedMultiplier)
    deltaWorld = _velocity * _speed * fixedDeltaTime
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
| `_j6AntiHorAction` | Rotación manual J6 anti-horaria (L1). |
| `_j6HorAction` | Rotación manual J6 horaria (R1). |
| `_j6HomeAction` | Disparador de Homing J6 a 17.7° (Cuadrado). |
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

Con input activo, el desplazamiento del joystick se calcula en mundo Unity y luego se convierte al frame activo de Flange. La pose objetivo se arma en ese frame para que la orientacion del TCP quede fija respecto del robot, no respecto del mundo. Adicionalmente, si `_enableWorkspaceLimits` es `true`, la posición del target se proyecta y trunca dentro del espacio dextrógiro seguro definido por los radios horizontales (`_minHorizontalRadius`/`_maxHorizontalRadius`) y límites de altura (`_minHeight`/`_maxHeight`):

```csharp
var deltaWorld = _velocity * (_speed * Time.fixedDeltaTime * driftSpeedMultiplier * payloadSpeedMultiplier * proximitySpeedMultiplier);
var deltaFrame = WorldVectorToFrame(deltaWorld, frame, extJoint);
var currentPose = ... // solucion anterior o ToolCenterPointFrame
Vector3 currentPos = (Vector3)currentPose.GetColumn(3);
Vector3 targetPosInFrame = currentPos + deltaFrame;

if (_enableWorkspaceLimits)
{
    Vector2 horizontalPos = new Vector2(targetPosInFrame.x, targetPosInFrame.z);
    float distHorizontal = horizontalPos.magnitude;
    if (distHorizontal > _maxHorizontalRadius)
        horizontalPos = horizontalPos.normalized * _maxHorizontalRadius;
    else if (distHorizontal < _minHorizontalRadius)
        horizontalPos = horizontalPos.normalized * _minHorizontalRadius;

    targetPosInFrame.x = horizontalPos.x;
    targetPosInFrame.z = horizontalPos.y;
    targetPosInFrame.y = Mathf.Clamp(targetPosInFrame.y, _minHeight, _maxHeight);
}

targetPose = Matrix4x4.TRS(
    targetPosInFrame,
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

La orientacion del TCP se captura y queda fija mientras se traslada. Si `_forceVerticalGripper` es `true`, al capturar la orientación se calcula una rotación corregida que alinea el eje Z local del efector final (el de la garra) exactamente hacia abajo (`Vector3.down` en el frame base del robot), preservando la guiñada (yaw) de la rotación actual:

```csharp
Quaternion rawRot = _controller.PoseObserver.ToolCenterPointFrame.Value.rotation;
if (_forceVerticalGripper)
{
    Vector3 localUpInFrame = rawRot * Vector3.up;
    Vector3 projectUp = Vector3.ProjectOnPlane(localUpInFrame, Vector3.down).normalized;
    if (projectUp.sqrMagnitude < 0.001f)
    {
        Vector3 localRightInFrame = rawRot * Vector3.right;
        projectUp = Vector3.ProjectOnPlane(localRightInFrame, Vector3.down).normalized;
    }
    
    if (projectUp.sqrMagnitude > 0.001f)
        _fixedTcpFrameOrientation = Quaternion.LookRotation(Vector3.down, projectUp);
    else
        _fixedTcpFrameOrientation = rawRot;
}
else
{
    _fixedTcpFrameOrientation = rawRot;
}
```

Hay dos modos de orientación disponibles mediante la propiedad `AlignOrientationWithJ1`:
- **Orientación Fija Absoluta (`AlignOrientationWithJ1 = false`)**: La orientación del TCP permanece exactamente constante en el frame del robot (`_fixedTcpFrameOrientation`), sin importar el giro de la base (J1).
- **Orientación Seguir Base (`AlignOrientationWithJ1 = true`)**: La orientación del TCP rota dinámicamente según la variación angular de la base J1 (`j1Rotation * _fixedTcpFrameOrientation`).

Para evitar condiciones de carrera en el arranque (donde el controlador de Flange puede tardar unos frames en ser válido), `FixedUpdate` intentará capturar la orientación en cuanto `_controller.IsValid.Value` sea `true` por primera vez.

Eventos que capturan orientacion:

| Evento | Accion |
|---|---|
| Inicialización (Play Mode / Inicio) | `CaptureFixedOrientation()` en cuanto el controlador se vuelve válido. |
| Volver de modo camara a modo robot | `RemapAxesFromCamera()`, `ResetPIDs()`, `CaptureFixedOrientation()` |
| Cambio de perfil de input | Sale de modo camara, resetea estado y recaptura orientacion |

> [!NOTE]
> Al soltar el joystick o detenerse (`EndMotion()`), se limpia la captura y se vuelve a invocar `CaptureFixedOrientation()`. Sin embargo, gracias a la guarda `_forceVerticalGripper`, cualquier desviación transitoria o lag físico de pitch/roll acumulado en el robot real es automáticamente descartado en la referencia, alineando de nuevo la pose de control perfectamente a la vertical. Esto sanea el drift de referencia de forma continua.

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

## 5. Inercia simulada, muneca y carga (payload) agarrada

**Archivo:** `Assets/Scripts/RobotDynamics.cs`

`ComputeEffectiveInertia()` estima:

```text
J_eff[i] = sum(j >= i) masa_j * distancia_perpendicular_ij^2
```

Acepta dos parametros opcionales, `payloadMass` y `payloadWorldPos`. Si `payloadMass > 0`, se suma un termino extra `payloadMass * distancia_perpendicular_i_payload^2` a `J_eff[i]` de cada joint, tratando el objeto agarrado como una masa puntual en `payloadWorldPos`. Con los valores por defecto (`payloadMass = 0`) el calculo es identico al anterior; nada cambia si no hay objeto agarrado.

**Archivo:** `Assets/Scripts/GripperController.cs`

Expone de solo lectura:

- `GrabbedMass`: masa original (kg) del objeto actualmente agarrado, o `0` si no hay ninguno (tambien `0` si la pieza no tiene `Rigidbody`).
- `GrabbedWorldPosition`: posicion mundial del `graspPoint`, rigidamente ligado al objeto agarrado mientras esta tomado.

Ver tambien "Ciclo de agarre y suelta" mas abajo: durante una suelta pendiente `IsHoldingObject` sigue en `true`, de modo que la carga se descuenta de la inercia recien cuando la pieza vuelve a la fisica (~0.07 s despues de pulsar el boton, con los valores por defecto).

**Archivo:** `Assets/Scripts/JoystickAdapter.cs`

`Awake()` cachea una referencia a `GripperController` (busqueda automatica con `FindFirstObjectByType<GripperController>()` si el campo `_gripperController` quedo sin asignar en el Inspector, mismo patron que ya usa `GripperDistanceSensor`).

`ApplyPID()` consulta `_gripperController.IsHoldingObject` antes de calcular `jEff`:

```csharp
float payloadMass = 0f;
Vector3 payloadWorldPos = Vector3.zero;
if (_gripperController != null && _gripperController.IsHoldingObject)
{
    payloadMass = _gripperController.GrabbedMass * _payloadInertiaMultiplier;
    payloadWorldPos = _gripperController.GrabbedWorldPosition;
}

float[] jEff = RobotDynamics.ComputeEffectiveInertia(robotJoints, payloadMass, payloadWorldPos);
```

`_payloadInertiaMultiplier` (default `1`) escala la masa antes de sumarla a la inercia: `1` = fisica real (masa del `Rigidbody` del cubo), `0` desactiva el efecto, valores mayores lo exageran para fines didacticos en VR sin tocar ganancias PID ni la tabla `RobotDynamics.Links`.

> [!WARNING]
> Este mecanismo por si solo **no produce un cambio perceptible de velocidad**, sin importar cuanto se suba `_payloadInertiaMultiplier`. El feedforward de velocidad de la seccion 4 (`feedforwardTorque = (jNorm/dt) * (...)`, luego dividido por `jNorm` en `acceleration = torque / jNorm`) cancela algebraicamente el termino de inercia: la velocidad articular se empuja hacia `qTargetVelocity` en cada tick sin que la masa lo frene. Solo el termino PID puro (`Kp*error + Ki*integral + Kd*derivative`) queda dividido por `jNorm` sin cancelarse, y ese termino es chico frente al feedforward cuando el seguimiento va razonablemente bien. Verificado empiricamente: subir el multiplicador de 1 a 10 no cambio la velocidad de subida percibida (feedback del usuario, 2026-08-02). Se mantiene como termino fisicamente correcto (afecta el arranque/transitorios y el caso sin feedforward), pero **no** es el mecanismo que produce el efecto de "carga pesada = mas lento". Ver el multiplicador de velocidad cartesiana mas abajo para eso.

### Penalizacion de velocidad cartesiana por payload (el mecanismo que si es perceptible)

**Archivo:** `Assets/Scripts/JoystickAdapter.cs`, metodo `FixedUpdate()`.

Antes de construir `deltaWorld`, se calcula `payloadSpeedMultiplier` a partir de la masa del objeto agarrado y se lo multiplica junto a `driftSpeedMultiplier`:

```csharp
float payloadSpeedMultiplier = 1f;
if (_gripperController != null && _gripperController.IsHoldingObject)
{
    float massRatio = Mathf.Clamp01(_gripperController.GrabbedMass / _maxSimulatedPayloadMass);
    payloadSpeedMultiplier = Mathf.Lerp(1f, _minPayloadSpeedMultiplier, massRatio);
}

var deltaWorld = _velocity * (_speed * dt * driftSpeedMultiplier * payloadSpeedMultiplier * proximitySpeedMultiplier);
```

`_maxSimulatedPayloadMass` (kg) define la masa a partir de la cual la velocidad cae hasta el piso `_minPayloadSpeedMultiplier` (nunca 0, para no bloquear al operador). Se aplica sobre la **trayectoria cartesiana completa antes de la IK**, no sobre cada joint por separado: todos los joints ven la misma trayectoria recta, solo que mas lenta, preservando la relacion geometrica entre velocidades articulares. Clampear velocidades por joint de forma independiente ya demostro romper esa relacion y corromper la orientacion del TCP (ver entrada del 2026-06-29 "Analisis de Limite de Velocidad vs. Trayectoria Cartesiana" en `_REGISTRO_PRUEBAS_CONTROL.md`); por eso el ajuste se hace aca y no en `ApplyPID()`.

### Frenado por proximidad (2026-08-14)

**Archivos:** `Assets/Scripts/JoystickAdapter.cs`, `Assets/Scripts/GripperDistanceSensor.cs`, `Assets/Scripts/ProximitySlowdownSettings.cs`.

Tercer multiplicador de la misma cadena, en el mismo punto y por la misma razon que el de payload.
Es **progresivo**, no un escalon:

```csharp
float proximitySpeedMultiplier = 1f;
if (_proximitySensor != null)
    proximitySpeedMultiplier = Mathf.Lerp(1f, _proximitySpeedMultiplier, _proximitySensor.ProximityFactor);
```

`ProximityFactor` vale 0 en el umbral y 1 al contacto, asi que la velocidad cae de forma continua
hasta `_proximitySpeedMultiplier` (0.5) pegado al objeto.

> [!IMPORTANT]
> La primera version usaba un escalon seco (1 → 0.5 al cruzar el umbral) con un umbral de 10 cm, y
> **resultaba imperceptible aunque funcionaba**. Medido en `Logs/control_diagnostics_log.json`: el TCP
> pasaba de 2.08 m/s a 0.995 m/s, exactamente la mitad. El problema era la duracion: con `_speed` de
> 1.99 m/s, 10 cm se cruzan en ~100 ms (6 fotogramas), y el clamp de `_maxJointAcceleration` (720 °/s²)
> ya consume ~42 ms solo en bajar una articulacion de 60 a 30 °/s. Por eso el umbral por defecto paso
> a 30 cm y la transicion a lineal. **Si vuelve a "no notarse", el sospechoso es la duracion de la
> ventana, no el mecanismo.**

### Bloqueo de descenso con la garra cerrada

Segundo mecanismo que altera `deltaWorld` antes de la IK, junto al clamp de workspace. Con
`GripperController.IsGripperClosed`, el paso hacia abajo se recorta al hueco restante:

```csharp
bool isCarryingPayload = _gripperController.IsHoldingObject;
float allowedDescent = Mathf.Max(0f, _proximitySensor.Distance - ProximitySlowdownSettings.GetDescentMargin(isCarryingPayload));
if (deltaWorld.y < -allowedDescent) { deltaWorld.y = -allowedDescent; IsDescentBlocked = true; }
```

Se aplica en **coordenadas de mundo y antes de `WorldVectorToFrame`**, porque la componente vertical
del input se compone como `Vector3.up * rawY`, tambien en mundo. Subir nunca se limita.

**Hay dos margenes, y la separacion es el punto entero del mecanismo (2026-08-16).** Cada uno resuelve
una maniobra opuesta:

| Situacion | Ajuste | Default | Para que |
|---|---|---|---|
| Garra cerrada **vacia** | `DescentMarginMeters` | 5 cm | Que el gripper no se estrelle contra el suelo al bajar a recoger. |
| Garra **con pieza** | `CarryDescentMarginMeters` | 5 mm | Poder **apoyar** la pieza. El sensor ya descuenta cuanto sobresale (`GetPayloadExtent`), asi que el hueco medido es el que queda bajo la pieza y tiene que poder llegar casi a cero. |

Es la misma logica que ya separaba `DescentMarginMeters` del umbral de frenado, un nivel mas abajo:
atar el bloqueo al umbral de 30 cm impediria acercarse a la pieza, y atar el margen con pieza al de la
garra vacia impide depositarla, porque el brazo se frena con la pieza a 5 cm de la superficie y hay que
soltarla desde ahi. `GetDescentMargin(bool)` e `IsDescentBlockEnabledFor(bool)` son el unico punto donde
se elige, para que el bloqueo y su HUD no puedan discrepar. Cualquiera de los dos en `0` desactiva el
bloqueo en esa situacion.

### Piso duro (limite geometrico, 2026-08-16)

`JoystickAdapter.ApplyHardFloorLimit()` impide que el gripper —o la pieza que lleve— baje por debajo de
`_hardFloorWorldY` (+ `_hardFloorClearance`). Cuarto mecanismo que altera `deltaWorld` antes de la IK.

**Por que hace falta si ya existen el bloqueo de descenso y el veto de colision:** esos dos son
**reactivos**. El bloqueo necesita `_proximitySensor.HasHit` y el veto necesita golpear un collider. Los
prefabs del entorno vienen con `addColliders: 0`, asi que mientras no se ejecute
`Tools > Entorno > Generar colliders faltantes` **el suelo no existe para las queries de fisica**: no hay
hit, no hay impacto, ninguno de los dos actua y el brazo lo atraviesa. El clamp de workspace tampoco lo
evita, porque su `_minHeight` (-0.2 por defecto) esta en el **frame del robot**, no en mundo.

Este limite es puramente geometrico y no depende del entorno. Se mide sobre los AABB reales de
`GripperController.TryGetGripperBounds()` y `TryGetPayloadBounds()`, de modo que no hay offsets que
calibrar; si no hubiera ningun collider donde medir, cae al TCP como referencia (conservador: la punta de
los dedos queda por debajo). Se ejecuta **despues** de `ApplyDescentLimit()`, que resetea
`IsDescentBlocked` al entrar, para que el aviso del HUD sobreviva cuando quien frena es el piso.

> [!NOTE]
> `_hardFloorWorldY` debe coincidir con `GrabbableSafetyGuard.minimumWorldY` y
> `GripperController.minimumReleaseWorldY`. Los tres describen la misma cota del suelo y hoy viven por
> separado; si se cambia el nivel del suelo hay que tocar los tres.

### Anticolision con el entorno (veto de movimiento)

> [!IMPORTANT]
> **Los colliders NO detienen el brazo por si solos.** El robot se mueve asignando angulos articulares
> via `MechanicalGroup.SetJoints()`, no con Rigidbody ni fuerzas, y Unity no resuelve la penetracion de
> objetos movidos por transform: reporta contactos y triggers, pero no frena nada. Ademas, el prefab del
> KUKA no tiene ni un solo collider ni Rigidbody. La unica forma de detener el brazo respetando la
> arquitectura es un **veto por software** sobre la trayectoria cartesiana.

`JoystickAdapter.ApplyCollisionVeto()` barre el volumen aproximado del gripper (`SphereCastNonAlloc`,
radio `_collisionProbeRadius`) a lo largo de `deltaWorld` y recorta el paso al hueco libre menos
`_collisionClearance`. Tercer mecanismo que altera `deltaWorld`, junto al clamp de workspace y al
bloqueo de descenso, y por la misma razon: antes de la IK, nunca por joint.

**Segundo barrido para la pieza agarrada (2026-08-16).** Al agarrarla, la pieza pasa a colgar del robot
y `IsObstacle()` la descarta, de modo que el barrido de arriba solo representa el volumen del gripper:
lateralmente se la podia empotrar contra el entorno. Si `_includePayloadInCollisionVeto` esta activo y
`GripperController.TryGetPayloadBounds()` devuelve un AABB, se lanza un `BoxCastNonAlloc` con ese volumen
en la misma direccion y se toma **el mas restrictivo de los dos**. Se usa el AABB real y no un radio de
esfera mayor a proposito: inflar `_collisionProbeRadius` penalizaria tambien al gripper vacio. El filtrado
de impactos es compartido (`NearestObstacleDistance`), asi que ambos barridos aplican exactamente los
mismos descartes.

Reglas de diseno:

- **Solapamiento en el origen se ignora** (`hit.distance <= 1e-4`). Si se tratara como obstaculo, un
  gripper que ya penetro geometria quedaria congelado sin forma de salir. Vale igual para el barrido de
  la pieza: una pieza apoyada sobre una mesa arranca solapando y no debe bloquear el movimiento.
- **Las superficies horizontales se ignoran** (`Dot(hit.normal, Vector3.up) > _floorNormalThreshold`).
  El suelo lo gobierna el bloqueo de descenso, que es preciso y descuenta la pieza transportada. Vetarlo
  aqui impediria bajar a recoger piezas, porque el radio del barrido frenaria el gripper a ~12 cm del
  piso.
- **`_obstacleMask` por defecto es solo la layer `Entorno` (6)**, que hasta ahora estaba definida y sin
  usar. Mientras no se ejecute `Tools > Entorno > Generar colliders faltantes`, nada esta en esa layer,
  el veto no actua y el comportamiento es identico al anterior. Es deliberado: la feature se activa
  cuando el entorno esta preparado, no antes.
- **Alcance conocido**: se vigilan el volumen del gripper y el de la pieza agarrada. Los eslabones altos
  del brazo (codo, antebrazo) pueden seguir atravesando geometria, porque no tienen colliders y vigilarlos
  exigiria un barrido por eslabon. Queda fuera de este mecanismo.

Los colliders del entorno se generan con `Assets/Editor/EnvironmentColliderTool.cs`. No se pueden
obtener activando "Generate Colliders" en el importador de los FBX: la escena instancia los `.prefab`
derivados del asset, no los FBX, y esos prefabs son assets distintos sin colliders.

> [!IMPORTANT]
> La herramienta opera sobre las **escenas cargadas**, y el entorno vive en `Map_v2`, no en `Planta`.
> Ejecutarla con `Map_v2` sin cargar no da error: informa de los pocos objetos que encontro y deja el
> entorno intacto. Ademas solo marca las escenas como dirty, **no las guarda**: sin un `Ctrl+S` sobre
> `Map_v2` los colliders existen en la sesion actual del Editor y se pierden al cerrar.
>
> `Tools > Entorno > Diagnosticar anticolision` imprime el estado real —escenas cargadas, mallas con
> collider y en layer `Entorno` por escena, y si las mascaras del veto y del sensor cubren esa layer—
> para no tener que deducirlo.

Reglas de diseno que hay que respetar si se toca esto:

- **Solo el sensor inferior frena.** En escena conviven 5 `GripperDistanceSensor`: el inferior (`DistanceSensor`, sobre el eje de agarre) y 4 laterales que vienen del prefab `OnRobot_RG2_Holder`. Los laterales tienen `detectionMask` = todas las layers y `maxDistance` 0.4 m, asi que verian el suelo permanentemente y dejarian el brazo clavado al 50%. El sensor que frena se marca con `contributesToSpeedReduction`; `JoystickAdapter.Awake()` busca el primero que lo tenga activo si el campo `_proximitySensor` quedo sin asignar.
- **El estado vive en el sensor**, no en el adapter. `ProximityFactor` (continuo) alimenta el multiplicador; `IsWithinSlowdownRange` (booleano con histeresis `slowdownReleaseFactor`) alimenta solo el HUD, para que el indicador no parpadee en el borde del umbral. La geometria vive en el sensor y el **piso** del frenado en el adapter, que es quien decide cuanto frenar.
- **El sensor ve el entorno, no solo las piezas.** `detectionMask` = layer 0 (`Default`) + layer 3 (`Manipulable`). Ampliarla no reintroduce autodeteccion porque `CanDetect()` sigue descartando todo lo que cuelgue de la raiz del robot. Es imprescindible para que la lectura siga siendo util con la garra cerrada y para que el bloqueo de descenso proteja del suelo y no solo de otros cubos. Como contrapartida, `IsGripDistanceSafe` tuvo que acotarse a colliders con tag `Agarrable`: sin eso, "Seguro para agarrar" aparecia al acercarse al piso.
- **Con una pieza agarrada se mide el hueco bajo la pieza, no bajo el sensor.** El objeto agarrado se vuelve hijo del robot (`GripperController.GrabObject()` hace `SetParent`) y por tanto `CanDetect()` lo descarta; el sensor mide hasta la superficie de mas abajo y le resta cuanto sobresale la pieza (`GetPayloadExtent`). Sin esa resta, el bloqueo de descenso dejaria hundir la pieza en el suelo.
- **El umbral no vive en ninguno de los dos.** Esta en la clase estatica `ProximitySlowdownSettings` (persistida en `PlayerPrefs`, editable desde el menu de pausa) precisamente para evitar una dependencia circular: el adapter lee la distancia del sensor y el sensor lee el umbral. Con el umbral en `0` el frenado queda desactivado.
- **No escalar `_velocity` en `Update()`.** Rompería el umbral `_velocity.sqrMagnitude < MotionInputEpsilon` de `FixedUpdate()` (dispararia `EndMotion()` espurio) y no afectaria al modo J6 exclusivo, que relee los ejes crudos.

### Modos de deteccion de `GripperDistanceSensor`

El script soporta dos modos, seleccionables **por instancia** con `detectionMode`:

| Modo | Como mide | Quien lo usa |
|---|---|---|
| `Cone` (default) | N `Physics.OverlapSphereNonAlloc` escalonadas que aproximan un cono | Los 4 sensores laterales del prefab |
| `SphereCast` | `Physics.SphereCastNonAlloc` de radio `castRadius` sobre el eje (o `RaycastNonAlloc` si el radio es 0) | El sensor inferior de escena |

El default es `Cone` a proposito: el YAML del prefab no tiene la clave nueva, asi que los laterales conservan su comportamiento historico sin tocar el prefab. En ambos modos `Distance` es la **distancia euclidea al punto de impacto**, para que la lectura en mm no salte al cambiar de modo.

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

### Ciclo de agarre y suelta (2026-08-16)

**Archivos:** `Assets/Scripts/GripperController.cs`, `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`.

El agarre se confirma solo si, **durante una orden explicita de cierre** (`isClosing`), la misma pieza toca las caras internas de **ambos** dedos (`HasOpposingInnerContacts`). Tocar un objeto con la garra ya cerrada nunca lo adhiere. Al confirmar, la pieza pasa a cinematica y se emparenta al `graspPoint`.

Si **varias** piezas cumplen el criterio a la vez, se agarra la mas cercana al `graspPoint`. Antes se tomaba la primera del diccionario, y el orden de iteracion de un `Dictionary` no esta definido: con dos piezas entre los dedos, cual se llevaba era arbitrario y no reproducible.

`SetParent` preserva la pose mundial pero **no** la escala, asi que si el `graspPoint` arrastra una escala distinta de 1 la pieza se deforma al agarrarla. `PreserveWorldScale()` corrige la escala local para mantener la mundial, avisa por consola (bajo modo debug) de que la jerarquia deberia revisarse, y al soltar se restaura la `localScale` exacta que la pieza tenia antes.

**La suelta es diferida, no inmediata.** `ToggleGrip()` a abierto solo marca `isReleasing`; la devolucion real a la fisica ocurre en `FixedUpdate()` (`UpdatePendingRelease`). Motivo: si la pieza vuelve a ser dinamica con los dedos todavia cerrados encima, Unity resuelve esa penetracion y la pieza salta o sale disparada de costado.

La condicion (`HaveFingersClearedPayload`) es un **incremento** de apertura, `releaseOpeningDelta` (0-1, default `0.2`), medido desde la apertura que los dedos tenian al ordenar la suelta — **no** un umbral absoluto. Al agarrar, `StopMotion()` deja los dedos detenidos apoyados sobre la pieza, asi que una pieza ancha arranca la suelta con `OpeningFraction` ya alta y un umbral absoluto se cumpliria en el primer tick, que es justo lo que hay que evitar. Como caso limite (pieza casi tan ancha como la carrera del RG2, donde ese incremento no cabe) tambien basta con `IsInPosition` y apertura mayor que la inicial. Un `releaseTimeout` (default `1 s`) fuerza la suelta con warning si la animacion no avanza, para que la pieza nunca quede pegada. Si se vuelve a cerrar antes de que se cumpla la condicion, la suelta pendiente se cancela y el agarre se mantiene.

Mientras dura la suelta pendiente, `IsHoldingObject` sigue en `true`: la inercia y la penalizacion de velocidad por payload se mantienen hasta que la pieza realmente se libera. `IsGripperClosed`, en cambio, pasa a `false` de inmediato (refleja la intencion del operario y libera el bloqueo de descenso).

**Velocidad al soltar.** Por defecto la pieza se libera con velocidad cero: cae en vertical, que es lo mas predecible para formacion. Con `inheritReleaseVelocity` activo hereda la velocidad del `graspPoint` (estimada por diferencia de posicion entre ticks de fisica en `TrackGraspVelocity()`), acotada por `maxInheritedReleaseSpeed`.

**Masa del gripper.** `RefreshGripperMass()` reasigna `gripperRigidbody.mass = gripperBaseMass + GrabbedMass` (con `gripperBaseMass` cacheado en `Awake()`), en vez de acumular `mass +=` / `mass -=`, que derivaba si un agarre y su suelta no se emparejaban. `originalMass` y el `isKinematic` previo de la pieza se guardan al agarrar y se restauran tal cual al soltar; si la pieza no tiene `Rigidbody`, `originalMass` se pone a `0` explicitamente (antes conservaba la masa de la pieza anterior y falseaba `jEff` y `payloadSpeedMultiplier`).

**Interfaz publica de `Ctrl_OnRobotRG2_Custom`** usada por `GripperController`: `OpeningFraction` (0 = cerrado, 1 = abierto, normalizada contra el angulo de `s_max` del polinomio del RG2), `IsInPosition` y `StopMotion()`. `in_position` es `public` solo bajo `#if UNITY_EDITOR`, asi que escribirlo directamente rompia la compilacion de cualquier build standalone; `StopMotion()` existe para encapsularlo.

---

## 6. Resets de estado

| Evento | Accion actual |
|---|---|
| Modo camara activo | No se mueve el brazo. `_velocity` queda en cero. |
| Volver a modo robot | Remapea ejes, resetea PIDs/velocidades, recaptura orientacion. |
| Input suprimido | `_velocity = Vector3.zero`, reset de PIDs/velocidades, UI en cero. |
| Joystick en reposo | Resetea PIDs/velocidades, marca la orientación como no capturada para forzar recaptura desde la pose real física actual, y retorna. No hay target activo (salvo si `_resettingJ6` está activo). |
| Inicio de recorrido | Resetea PIDs/velocidades y captura la orientacion TCP actual antes de IK. |
| IK invalida | Limpia UI y no aplica nuevos joints en ese tick. |
| Doble clic en gripper / Cuadrado PS4 | Activa el reseteo suave de J6 al cero físico real de 17.7° (`_resettingJ6 = true`). |
| Finalización de reseteo J6 | Cuando J6 y el robot físico alcanzan los 17.7° (error < 0.1°), se limpia `_resettingJ6` y se recaptura la orientación TCP de referencia. |

---

| Script | Rol | Relacion con PID/IK |
|---|---|---|
| `GripperController.cs` | Logica de agarre/suelta | Independiente |
| `Ctrl_OnRobot_RG2_Custom.cs` | Animacion de dedos del gripper y detección de doble clic para resetear J6 | Llama a `ResetJ6ToZero()` en doble clic |
| `GripperDistanceSensor.cs` | Sensor de distancia del gripper (modo `Cone` en los laterales, `SphereCast` en el inferior) | **Entrada de la cadena de control**: el sensor inferior expone `IsWithinSlowdownRange`, que `JoystickAdapter` usa como `proximitySpeedMultiplier` |
| `ProximitySlowdownSettings.cs` | Umbral de frenado y los **dos** margenes de bloqueo de descenso, garra vacia y con pieza (estatico, persistido en `PlayerPrefs`, editables desde el menu de pausa) | Parametros de la asistencia por proximidad |
| `GripperViewSettings.cs` | Largo de las guias perpendiculares de la gripper camera (estatico, `PlayerPrefs`, menu de pausa) | UI General |
| `DebugSettings.cs` | Modo debug global: enciende/apaga en caliente los logs de diagnostico (estatico, `PlayerPrefs`, menu de pausa). Lo consultan `GripperController`, `GripperTriggerForwarder` y `GrabbableSafetyGuard`; sus flags `[SerializeField]` locales siguen actuando como override por componente | Independiente de la cadena de control |
| `GripperStatusOverlay.cs` | Aviso sobre la vista del gripper: "DESCENSO BLOQUEADO" / "VELOCIDAD n%". Se autoinstancia, patron de `J6OverlayController` | Lee `JoystickAdapter.IsDescentBlocked` y `ProximitySpeedScale` |
| `GripperTopCameraFollow.cs` | Camara superior del gripper | Independiente |
| `GripperTriggerForwarder.cs` | Reenvio de triggers | Independiente |
| `JoystickVibrationHidOutput.cs` | Vibracion del joystick | Independiente |
| `AxisUIController.cs` | UI de ejes | Independiente |
| `JointStatePublisher.cs` | Publicacion de estado articular | Independiente |
| `RobotTest.cs` | Pruebas/manual | Independiente |
| `J6OverlayController.cs` | Dibuja un dial superpuesto en Canvas (derecha) cuando el modo J6 exclusivo está activo | UI de J6 Exclusivo |
| `LeftLayoutManager.cs` | Reposiciona por código `Input Info`/`J1`-`J6`/`SafetyInfoOperator` y delega a `ControlGuidePanel` el cambio de texto PS4/VR2. Reparenta `DistanceSensorValue` dentro de `CameraGripperView` (franja inferior, con banda oscura `DistanceValueBackdrop` detrás para legibilidad). | UI General |
| `PidActionsPanel.cs` | Panel estático (`PID_Section`, hijo de `InfoPanel_Gripper`) con las 6 filas fijas de acción PID por joint; expone `SetJointAction`/`SetExtraRow` para agregar filas nuevas clonando una plantilla inactiva solo si hace falta | UI de Acciones de Control (PID) |
| `ControlGuidePanel.cs` | Panel estático (`Guide_Section`, hijo de `InfoPanel_Gripper`) con los ítems fijos de la guía de controles PS4/VR2; expone `AddOrUpdateItem` para agregar comandos nuevos clonando una plantilla inactiva solo si hace falta | UI de Guía de Controles |

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
