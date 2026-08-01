# AGENTS.md

Instrucciones para Codex y otros agentes que trabajen en este repositorio.

## Alcance

Este archivo aplica a todo el repositorio, salvo que exista un `AGENTS.md` más específico en un subdirectorio.

## Índice obligatorio de codebase-memory

- Antes de explorar o modificar código, ejecuta `index_repository` sobre la raíz del repositorio en modo `full` y usa el nombre de proyecto devuelto por la herramienta.
- Para descubrir código usa, en este orden, `search_graph`, `trace_path`, `get_code_snippet`, `query_graph` y `get_architecture`. Recurre a búsquedas de texto solo para literales, archivos no indexables o cuando el grafo resulte insuficiente.
- Después de cualquier conjunto coherente de cambios en el repositorio, vuelve a ejecutar `index_repository` en modo `full` antes de validar o entregar el trabajo. No des por finalizada una tarea con el índice desactualizado.
- Si la indexación falla, informa el error y no presentes como vigente información obtenida de un índice anterior.

## Contexto del proyecto

- Proyecto: sistema de teleoperación y simulación en realidad virtual para formación con un brazo robótico manipulador.
- Proyecto Unity principal: `Planta_BrazoRobótico/`. Respeta la tilde en `Robótico`; no renombres el directorio a `Robotico`.
- Código Unity principal: `Planta_BrazoRobótico/Assets/Scripts/`.
- Versión Unity esperada: `6000.3.11f1`.
- Dependencias Unity importantes: Preliy Flange, Unity Input System, TextMeshPro, URP y ROS-TCP-Connector.
- Firmware joystick RP2040/Pico: `Joystick/joystick/firmware/src/proto/joystick_v1/`.
- Firmware ESP8266/PlatformIO: `FirmwareESP8266/`.
- Bridge ROS 2 a UDP: `FirmwareESP8266/ros2_bridge/`.
- Informe/documentación: `Informe/`.

## Regla principal antes de modificar control Unity

Antes de tocar la cadena de control del brazo, lee y respeta:

`Planta_BrazoRobótico/Assets/Scripts/_ARQUITECTURA_CONTROL.md`

Si modificas comportamiento, nombres o responsabilidades de la arquitectura descrita ahí, actualiza también ese documento en el mismo cambio.

Para cambios, pruebas o ajustes de parámetros sobre la cadena de control, registra también la prueba y el resultado en:

`Planta_BrazoRobótico/Assets/Scripts/_REGISTRO_PRUEBAS_CONTROL.md`

El registro debe incluir el síntoma, el cambio probado, parámetros/archivos afectados, feedback observado por el usuario y la decisión posterior.

## Arquitectura de control que no debe romperse

La cadena principal es:

1. `JoystickAdapter.Update()` lee entradas del PS4/Input System con `_moveX`, `_moveY`, `_moveZ` mediante `ReadAxis()`, incluyendo calibración e inversión.
2. El remapeo de ejes depende de cámara/efector para que el movimiento sea intuitivo al volver del modo cámara.
3. `JoystickAdapter.FixedUpdate()` integra velocidad cartesiana a una nueva posición TCP y preserva orientación con `_fixedTcpOrientation`.
4. La pose objetivo se resuelve con `Preliy.Flange` usando `Solver.ComputeInverse()`.
5. `JoystickAdapter.ApplyPID()` convierte el `JointTarget` IK en movimiento articular suavizado.
6. `RobotDynamics.ComputeEffectiveInertia()` calcula `J_eff` por articulación.
7. `JointPID.Compute()` calcula torque virtual por articulación.
8. El resultado final se aplica con `_controller.MechanicalGroup.SetJoints(new JointTarget(qNew), notify: true)`.

Puntos críticos:

- Flange recibe ángulos articulares, no torques físicos de Unity.
- El brazo principal no debe moverse mediante `Rigidbody`, fuerzas ni impulsos de Unity Physics.
- Mantén unidades consistentes: posiciones en metros, ángulos Flange en grados, ROS en radianes, inercia en kg·m², velocidad articular en °/s.
- Para errores angulares usa `Mathf.DeltaAngle` o una lógica equivalente que respete wraparound.
- Si `solution.IsValid == false`, no fuerces movimiento articular.
- Al entrar en modo cámara o quedar el joystick en reposo, no acumules velocidad residual.
- Conserva el desacople entre control del brazo, gripper, sensores, ROS y háptica salvo que la tarea pida integrarlos explícitamente.

## Scripts Unity por rol

Núcleo PID/IK:

- `JoystickAdapter.cs`: orquesta input → pose cartesiana → IK → PID → `SetJoints`.
- `JointPID.cs`: PID por articulación, anti-windup y reset de estado interno.
- `RobotDynamics.cs`: masas, centros de masa e inercia efectiva por articulación.

Independientes o periféricos:

- `GripperController.cs`: agarre/suelta de objetos.
- `Ctrl_OnRobot_RG2_Custom.cs`: animación de dedos del gripper.
- `GripperDistanceSensor.cs`, `GripperTopCameraFollow.cs`, `GripperTriggerForwarder.cs`: soporte del gripper.
- `JoystickVibrationHidOutput.cs`: salida HID para vibración; está orientado a Windows y debe conservar sus guards de plataforma.
- `JointStatePublisher.cs`: publica `/joint_states`; convierte grados de Flange a radianes para ROS.
- `AxisUIController.cs`, `GameManager.cs`, `RobotTest.cs`: UI, configuración o pruebas.

## Convenciones de C# / Unity

- Mantén el estilo existente: clases en namespace global, campos privados con `[SerializeField]`, comentarios en español cuando aporten contexto.
- Usa `Awake`, `OnEnable`, `OnDisable`, `Start`, `Update` y `FixedUpdate` con sus responsabilidades habituales: input en `Update`, movimiento/estado fijo en `FixedUpdate`.
- Habilita y deshabilita `InputActionReference.action` en el ciclo de vida; evita suscripciones duplicadas.
- Antes de usar `_controller`, verifica `null` y, cuando corresponda, `_controller.IsValid.Value`.
- Evita cambios masivos de formato o renombres de campos serializados; pueden romper referencias del Inspector.
- No cambies GUIDs de `.meta` ni recrees assets sin necesidad.
- Evita asignaciones innecesarias dentro de `Update`/`FixedUpdate` en código sensible a rendimiento.

## ROS, firmware y háptica

- `JointStatePublisher` publica nombres `joint_1` a `joint_6`; cualquier cambio debe coincidir con URDF/nodos ROS.
- ROS usa radianes; Flange/Unity en esta arquitectura trabaja con grados para joints.
- `FirmwareESP8266/platformio.ini` define `WIFI_SSID`, `WIFI_PASS`, `UDP_PORT=5000` y `NUM_JOINTS=6`. No cambies estos valores sin justificarlo.
- El firmware Pico usa CMake, Pico SDK y TinyUSB. Si agregas fuentes, actualiza `CMakeLists.txt`.
- El bridge ROS 2 expone el ejecutable `joint_state_udp_bridge`.
- La vibración HID debe fallar de forma segura cuando el dispositivo no está presente o la plataforma no es Windows.

## Archivos que normalmente no debes editar

No modifiques salvo que la tarea lo pida explícitamente:

- `Planta_BrazoRobótico/Library/`, `Temp/`, `Obj/`, `Build*/`, `Logs/`, `UserSettings/`.
- `.vs/`, `.plastic/`, archivos de IDE, cachés y backups.
- Binarios grandes, modelos CAD, texturas, escenas, prefabs y materiales si el cambio puede hacerse en scripts/documentación.
- `ProjectSettings/` y `Packages/` salvo cambios deliberados de versión, paquete o configuración Unity.

## Validación recomendada

Cuando el entorno local tenga las herramientas instaladas, usa los comandos mínimos que apliquen al cambio.

Unity, importación/compilación básica:

```bash
Unity -batchmode -nographics -quit -projectPath "Planta_BrazoRobótico" -logFile -
```

Unity Test Framework, si existen tests relevantes:

```bash
Unity -batchmode -nographics -quit -projectPath "Planta_BrazoRobótico" -runTests -testPlatform EditMode -logFile -
```

Firmware ESP8266:

```bash
cd FirmwareESP8266
pio run
```

Firmware RP2040/Pico:

```bash
cd Joystick/joystick/firmware/src/proto/joystick_v1
cmake -S . -B build
cmake --build build
```

Bridge ROS 2:

```bash
cd FirmwareESP8266/ros2_bridge
colcon build --packages-select joint_state_udp_bridge
```

Si no puedes ejecutar validaciones por falta de Unity, Pico SDK, PlatformIO, ROS 2 o hardware, indícalo claramente en el resumen final.

## Expectativas para cambios de Codex

- SIEMPRE debes correr validaciones y compilar el código localmente (ej. Unity en batchmode) para asegurar que no hay errores de compilación antes de presentarle cualquier cambio o feature al usuario.
- Haz cambios pequeños, enfocados y revisables.
- Explica qué archivos tocaste y por qué.
- Reporta validaciones ejecutadas y validaciones no ejecutadas.
- Para cambios en control del brazo, incluye una nota explícita de compatibilidad con `_ARQUITECTURA_CONTROL.md`.
- No mezcles refactors, limpieza de assets y cambios funcionales en el mismo parche salvo que sea necesario.
