# Arquitectura de Control — Planta BrazoRobótico
**Fecha:** 2026-06-20  
**Branch:** FisicaEslabones  
**Robot:** KUKA KR210 R3100-2 | **Librería IK:** Flange 1.0.11

---

## Diagrama de flujo completo

```
PS4 Controller (Input System)
        │
        │  _moveX / _moveY / _moveZ  (InputActionReference)
        ▼
┌─────────────────────────────────────────────────────┐
│  JoystickAdapter.Update()  [L134-140]               │
│  rawX/Y/Z → _velocity (Vector3, mundo, remapeado)   │
└─────────────────────────────────────────────────────┘
        │
        │  _velocity * _speed * fixedDeltaTime  [L159]
        ▼
┌─────────────────────────────────────────────────────┐
│  JoystickAdapter.FixedUpdate()  [L143-178]          │
│  delta → targetPose = TRS(pos + delta,              │
│                           _fixedTcpOrientation,     │
│                           one)           [L162]      │
└─────────────────────────────────────────────────────┘
        │
        │  CartesianTarget(targetPose, cfg, extJoint)
        ▼
┌─────────────────────────────────────────────────────┐
│  Flange: Solver.ComputeInverse()  [L170]            │
│  → solution.JointTarget  (q1..q6 objetivo, °)       │
└─────────────────────────────────────────────────────┘
        │
        │  solution.JointTarget + robotJoints actuales
        ▼
┌─────────────────────────────────────────────────────┐
│  JoystickAdapter.ApplyPID()  [L206-234]             │
│                                                     │
│  RobotDynamics.ComputeEffectiveInertia()  [L209]    │
│    → jEff[i]  (kg·m² por articulación)             │
│                                                     │
│  Para i = 0..5:                                     │
│    error = qTarget[i] - qActual[i]                  │
│    torque  = JointPID[i].Compute(...)  [L218]       │
│    jNorm   = jEff[i] / _referenceInertia  [L222]    │
│    vel    += (torque / jNorm) * dt  [L223]          │
│    vel    -= _velocityDamping * vel * dt  [L226]    │
│    vel     = Clamp(vel, ±_maxJointVelocity)  [L227] │
│    qNew[i] = qActual + vel * dt  [L229]             │
└─────────────────────────────────────────────────────┘
        │
        │  qNew[0..5]  (ángulos finales, °)
        ▼
┌─────────────────────────────────────────────────────┐
│  MechanicalGroup.SetJoints(JointTarget(qNew))       │
│  [JoystickAdapter.cs:233]                           │
│  Flange NO recibe torques — solo ángulos            │
└─────────────────────────────────────────────────────┘
```

---

## 1. Punto de entrada de input

**Archivo:** `Assets/Scripts/JoystickAdapter.cs`

| InputActionReference | Campo Inspector | Eje lógico |
|---|---|---|
| `_moveX` | Stick derecho X | Desplazamiento lateral (mundo) |
| `_moveY` | Stick izquierdo Y | Desplazamiento vertical |
| `_moveZ` | Stick derecho Y | Desplazamiento frontal (hacia robot) |
| `_modoCamara` | L3 (LS click) | Toggle modo cámara / modo robot |

Lectura de ejes — `Update()`:
```csharp
// JoystickAdapter.cs:134-140
float rawX = _moveX?.action.ReadValue<float>() ?? 0f;
float rawY = _moveY?.action.ReadValue<float>() ?? 0f;
float rawZ = _moveZ?.action.ReadValue<float>() ?? 0f;

_velocity = _dirX * (rawX * _signX)
          + Vector3.up * rawY
          + _dirZ * (rawZ * _signZ);
```

`_dirX`, `_dirZ`, `_signX`, `_signZ` son calculados por `RemapAxesFromCamera()` al volver al modo robot, de modo que MoveZ siempre apunta "desde la cámara hacia el efector" independientemente de la orientación del brazo.

---

## 2. Cadena velocidad → posición → IK

**Archivo:** `Assets/Scripts/JoystickAdapter.cs`, método `FixedUpdate()` (L143-178)

**a) Integración de velocidad a delta de posición:**
```csharp
// JoystickAdapter.cs:159-161
var delta = _velocity * (_speed * Time.fixedDeltaTime);
var currentPose = _controller.PoseObserver.ToolCenterPointFrame.Value;
Vector3 currentPos = (Vector3)currentPose.GetColumn(3);
```

**b) Reconstrucción de `targetPose` (posición + orientación fija):**
```csharp
// JoystickAdapter.cs:162
var targetPose = Matrix4x4.TRS(currentPos + delta, _fixedTcpOrientation, Vector3.one);
```
`_fixedTcpOrientation` se captura en `CaptureFixedOrientation()` (L181-185) al inicio y al volver del modo cámara. Garantiza que el TCP no gire mientras se traslada.

**c) Llamada a IK:**
```csharp
// JoystickAdapter.cs:169-170
var target = new CartesianTarget(targetPose, configuration, extJoint);
var solution = _controller.Solver.ComputeInverse(target, tool, frame);
```
`ComputeInverse` pertenece a `Preliy.Flange`. Si `solution.IsValid == false` (singularidad o fuera de workspace) se aborta el tick sin mover los joints (L172-175).

---

## 3. Cálculo de inercia simulada

**Archivo:** `Assets/Scripts/RobotDynamics.cs`

**Firma:**
```csharp
// RobotDynamics.cs:44
public static float[] ComputeEffectiveInertia(IReadOnlyList<TransformJoint> robotJoints)
```

- **Input:** lista de los 6 `TransformJoint` de Flange (poses en mundo en tiempo real).  
- **Output:** `float[6]` — `jEff[i]` en kg·m², calculado como  
  `J_eff[i] = Σ_{j≥i} m_j · d_ij²`  
  donde `d_ij` es la distancia perpendicular del eje del joint `i` al CoM del link `j` en coordenadas mundo (RobotDynamics.cs:48-66).

**Datos de masa:** tabla estática `RobotDynamics.Links[]` (RobotDynamics.cs:25-33) con masas URDF del KUKA KR210.

**Uso en ApplyPID (JoystickAdapter.cs:222-223):**
```csharp
float jNorm = Mathf.Max(jEff[i] / _referenceInertia, 0.001f);
_jointVelocity[i] += (torque / jNorm) * dt;
```

**Inspector de JoystickAdapter:**
| Campo | Variable | Rol |
|---|---|---|
| Reference Inertia | `_referenceInertia` (L51) | Denominador de normalización (kg·m²). Con valor = J_eff real → ganancia PID neutra. |
| Velocity Damping | `_velocityDamping` (L53) | Fricción viscosa (s⁻¹). Evita aceleración indefinida (L226). |
| Max Joint Velocity | `_maxJointVelocity` (L55) | Clamp de seguridad cinemático (°/s) (L227). |

---

## 4. PID por articulación

**Archivo:** `Assets/Scripts/JointPID.cs`

**Firma de Compute():**
```csharp
// JointPID.cs:32
public float Compute(float setpoint, float current, float dt)
```
- `setpoint`, `current` en grados; `dt` en segundos.  
- Retorna torque virtual en °/s² (a inercia de referencia).

**Fórmula completa (JointPID.cs:34-38):**
```csharp
float error = setpoint - current;
_integral = Mathf.Clamp(_integral + error * dt, -MaxIntegral, MaxIntegral);
float derivative = dt > 1e-6f ? (error - _prevError) / dt : 0f;
_prevError = error;
return Kp * error + Ki * _integral + Kd * derivative;
```

**Cadena dinámica en ApplyPID (JoystickAdapter.cs:218-229):**
```
torque  = Kp·e + Ki·∫e·dt + Kd·(de/dt)          [JointPID.cs:38]
accel   = torque / jNorm                          [JoystickAdapter.cs:223]
vel    += accel * dt  →  -= damping*vel*dt  →  Clamp(±maxVel)
qNew    = qActual + vel * dt                      [JoystickAdapter.cs:229]
```

**Anti-windup:** `Mathf.Clamp(_integral, -MaxIntegral, MaxIntegral)` — JointPID.cs:35.  
Campo Inspector `MaxIntegral` (JointPID.cs:15, default 200 °·s).

**Resets de `_jointVelocity` y PIDs:**

| Evento | Ubicación | Acción |
|---|---|---|
| `Start()` | JoystickAdapter.cs:100-108 | `InitPIDs()` crea instancias nuevas (integral=0, prevError=0) |
| Volver a modo robot (L3) | JoystickAdapter.cs:119 → `ResetPIDs()` L195-203 | `pid.Reset()` en todos + `Array.Clear(_jointVelocity)` |
| Joystick en reposo | JoystickAdapter.cs:150-151 | `Array.Clear(_jointVelocity)` sin tocar integrales |

---

## 5. Aplicación final

**Línea exacta donde se llama SetJoints():**
```csharp
// JoystickAdapter.cs:233
_controller.MechanicalGroup.SetJoints(new JointTarget(qNew), notify: true);
```

`qNew` es un `float[6]` de ángulos articulares en grados, resultado directo de la simulación dinámica (paso 4). Flange interpreta esto como posición articular objetivo y mueve los joints directamente — no recibe fuerzas, torques ni impulsos físicos de Unity. El motor de física de Unity no participa en el movimiento del brazo principal.

---

## 6. Scripts NO relacionados con la cadena de control PID/IK

### GripperController — `Assets/Scripts/GripperController.cs`

Gestiona la lógica de agarre/suelta del gripper RG2. Completamente independiente:
- Entrada: `ToggleGrip()` llamado desde `Ctrl_OnRobot_RG2_Custom` (no desde JoystickAdapter).
- No lee inputs de ejes, no accede a `_controller`, no modifica joints del brazo.
- Opera sobre `Rigidbody` del gripper y objetos con tag `"Agarrable"`.
- No interactúa con `JointPID`, `RobotDynamics` ni `SetJoints`.

### Ctrl_OnRobot_RG2_Custom — `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`

Anima los dedos del gripper (rotación de los brazos R/L_Arm_ID_*). Completamente independiente:
- Entrada: `_toggleGripAction` (InputActionReference propio, no compartido con JoystickAdapter).
- Usa su propio `FixedUpdate()` con máquina de estados (ctrl_state 0/1/2).
- Opera sobre `Rigidbody.MoveRotation()` de las partes del gripper.
- No accede a `_controller`, no llama `SetJoints`, no interactúa con PID ni IK.

---

## Resumen de archivos por rol

| Script | Rol | Relación con cadena PID/IK |
|---|---|---|
| `JoystickAdapter.cs` | Orquestador principal: input → IK → PID → SetJoints | **Núcleo** |
| `JointPID.cs` | Controlador PID por articulación | **Núcleo** |
| `RobotDynamics.cs` | Cálculo de inercia efectiva por articulación | **Núcleo** |
| `GameManager.cs` | Solo configura targetFPS en Awake | Independiente |
| `GripperController.cs` | Grab/release de objetos | Independiente |
| `Ctrl_OnRobot_RG2_Custom.cs` | Animación dedos gripper | Independiente |
| `GripperDistanceSensor.cs` | Sensor de distancia del gripper | Independiente |
| `GripperTopCameraFollow.cs` | Cámara top del gripper | Independiente |
| `GripperTriggerForwarder.cs` | Reenvía triggers al GripperController | Independiente |
| `JoystickVibrationHidOutput.cs` | Vibración del joystick PS4 | Independiente |
| `AxisUIController.cs` | UI de visualización de ejes | Independiente |
| `JointStatePublisher.cs` | Publicación de estado articular | Independiente |
| `RobotTest.cs` | Script de prueba | Independiente |
