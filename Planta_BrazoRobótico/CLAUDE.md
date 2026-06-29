# Instrucciones Generales para Agentes (CLAUDE.md)

*Este archivo refleja las directrices maestras definidas en `AGENTS.md`.*

## Contexto del Proyecto
- **Proyecto:** Sistema de teleoperación y simulación en realidad virtual para formación con un brazo robótico manipulador.
- **Estructura Principal:**
  - `Planta_BrazoRobótico/` (Proyecto Unity 6000.3.11f1). Respeta la tilde en el nombre.
  - `Planta_BrazoRobótico/Assets/Scripts/` (Código Unity principal).
  - `Joystick/joystick/firmware/src/proto/joystick_v1/` (Firmware RP2040/Pico, CMake).
  - `FirmwareESP8266/` (Firmware ESP8266, PlatformIO).
  - `FirmwareESP8266/ros2_bridge/` (Bridge ROS 2 a UDP, Colcon).
  - `Informe/` (Documentación).
- **Dependencias Unity Clave:** Preliy Flange, Unity Input System, TextMeshPro, URP y ROS-TCP-Connector.

## Reglas Críticas de Arquitectura de Control
Antes de modificar la cadena de control del brazo en Unity, **es obligatorio** leer y respetar:
1. `Planta_BrazoRobótico/Assets/Scripts/_ARQUITECTURA_CONTROL.md`
2. `Planta_BrazoRobótico/Assets/Scripts/_REGISTRO_PRUEBAS_CONTROL.md`

Si modificas comportamiento, nombres o responsabilidades, actualiza `_ARQUITECTURA_CONTROL.md`.
Si realizas pruebas o ajustes de parámetros, registra el síntoma, cambio, feedback y decisión en `_REGISTRO_PRUEBAS_CONTROL.md`.

**Puntos Clave del Control:**
- Flange recibe **ángulos articulares** (grados), no torques físicos de Unity Physics.
- El brazo no debe moverse mediante `Rigidbody`, fuerzas ni impulsos de Unity Physics.
- Mantén la consistencia de unidades: metros (posición), grados (Flange), radianes (ROS), kg·m² (inercia), °/s (velocidad articular).
- Si la solución IK es inválida (`solution.IsValid == false`), no fuerces el movimiento.
- No acumules velocidad residual al entrar en modo cámara o soltar el joystick.

## Archivos Críticos
- `JoystickAdapter.cs`: Orquesta el input → velocidad cartesiana → pose IK → PID → inercia → `SetJoints`.
- `JointPID.cs`: PID por articulación, anti-windup.
- `RobotDynamics.cs`: Cálculos de masa e inercia efectiva.
- `JointStatePublisher.cs`: Publica `/joint_states` (convierte grados a radianes para ROS).
- `JoystickVibrationHidOutput.cs`: Salida HID para vibración (solo Windows).

## Convenciones
- Clases en namespace global, campos privados con `[SerializeField]`.
- Input en `Update`, física y movimiento en `FixedUpdate`.
- Verifica `_controller` y `_controller.IsValid.Value` antes de usarlos.
- Evita renombres masivos o cambios de formato que rompan referencias del Inspector. No cambies GUIDs en `.meta`.

## Comandos de Validación
- Unity (Batchmode): `Unity -batchmode -nographics -quit -projectPath "Planta_BrazoRobótico" -logFile -`
- ESP8266: `cd FirmwareESP8266 && pio run`
- RP2040: `cd Joystick/joystick/firmware/src/proto/joystick_v1 && cmake -S . -B build && cmake --build build`
- ROS 2 Bridge: `cd FirmwareESP8266/ros2_bridge && colcon build --packages-select joint_state_udp_bridge`

## Expectativas para Cambios
- SIEMPRE debes correr validaciones y compilar el código localmente (ej. Unity en batchmode) para asegurar que no hay errores de compilación antes de presentarle cualquier cambio o feature al usuario.
- Haz cambios pequeños, enfocados y revisables.
- Explica qué archivos tocaste y por qué.
- Reporta validaciones ejecutadas y validaciones no ejecutadas.
- Para cambios en control del brazo, incluye una nota explícita de compatibilidad con `_ARQUITECTURA_CONTROL.md`.
- No mezcles refactors, limpieza de assets y cambios funcionales en el mismo parche salvo que sea necesario.