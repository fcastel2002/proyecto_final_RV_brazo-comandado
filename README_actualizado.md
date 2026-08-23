# Entrenamiento en entorno virtual para la teleoperación segura de brazos robóticos

Proyecto Final de la asignatura **Realidad Virtual** — Facultad de Ingeniería, UNCuyo (Ing. Mecatrónica).

Entorno de **Realidad Virtual (Unity)** + **hardware propio (joystick RP2040)** para entrenar operarios en la teleoperación de un brazo robótico industrial (KUKA KR210), pensado para el proceso de ensamble manual de baterías de plomo-ácido de la empresa AUTOELEC.

**Alumnos:** Quiroga, Juan Ignacio (13889) · Castel, Francisco (13784)
**Profesores:** Ing. Javier Rosenstein · Ing. Carlos Tomba
**Año:** 2026

---

## Índice

- [El problema y la motivación](#el-problema-y-la-motivación)
- [Objetivos y alcance](#objetivos-y-alcance)
- [Arquitectura general](#arquitectura-general)
- [Estructura del repositorio](#estructura-del-repositorio)
- [Simulación Unity](#simulación-unity)
- [Hardware: joystick RP2040](#hardware-joystick-rp2040)
- [Comunicación UDP y clientes externos](#comunicación-udp-y-clientes-externos)
- [Puente ROS 2](#puente-ros-2)
- [Instalación y uso](#instalación-y-uso)
- [Validación / build](#validación--build)
- [Documentación adicional](#documentación-adicional)
- [Estado del proyecto](#estado-del-proyecto)
- [Licencia](#licencia)

---

## El problema y la motivación

AUTOELEC ensambla manualmente paquetes de baterías de plomo-ácido para autoelevadores: vasos de 20 a 100 kg que un operario debe levantar y ubicar dentro de un rack metálico, con posturas cada vez más forzadas a medida que el rack se completa. Es una tarea con riesgo ergonómico y de accidentes considerable.

Un entorno de RV permite entrenar la teleoperación del manipulador que reemplazaría esa tarea manual **sin riesgo físico ni costo de errores** (piezas dañadas, paradas de producción), reconfigurar libremente los escenarios de entrenamiento, e instrumentar métricas objetivas de desempeño (tiempo por tarea, colisiones, reintentos). El joystick físico usado en VR es el mismo que se usaría para operar el robot real, para que la destreza aprendida transfiera directamente.

## Objetivos y alcance

**Objetivo general:** desarrollar un entorno VR y un esquema de control para entrenar y luego teleoperar un brazo robótico mediante un joystick propio, priorizando seguridad, usabilidad y desempeño.

Incluye:
- Simulador del brazo en Unity con interacción XR y cinemática inversa (Flange).
- Mapeo de los controles del joystick a los grados de libertad del robot.
- Mecanismos de seguridad: límites de espacio de trabajo, frenado por proximidad, bloqueo de descenso, piso duro, anticolisión con el entorno.
- Herramientas de monitoreo externo por UDP (Python, MATLAB, Android/Termux) del estado articular en tiempo real.
- Prototipo funcional del joystick RP2040 como dispositivo HID estándar, con retroalimentación háptica (vibración).

Fuera de alcance de esta versión: háptica proporcional a fuerza de contacto, *motion planning* autónomo, integración física directa con el robot electrohidráulico real, y certificación normativa industrial.

## Arquitectura general

```
PS4 Controller / Joystick RP2040 (HID)
        │
        ▼
Unity Input System
        │
        ▼
JoystickAdapter (velocidad cartesiana → pose objetivo IK)
        │
        ▼
Preliy Flange — Solver.ComputeInverse()  (KUKA KR210 R3100-2)
        │
        ▼
JointPID + RobotDynamics (inercia efectiva por articulación)
        │
        ▼
MechanicalGroup.SetJoints()  (ángulos articulares, no física de Unity)
        │
        ├──► GripperController (agarre/suelta, sensores de proximidad)
        ├──► JointStatePublisher ──► ROS-TCP-Connector (/joint_states, radianes)
        └──► JointStateBroadcaster ──► UDP ──► Clientes externos (Python/MATLAB/Android)
```

El robot **nunca se mueve por `Rigidbody`, fuerzas ni impulsos de Unity Physics**: Flange recibe ángulos articulares en grados. La cadena completa, con todas las capas de seguridad (frenado por proximidad, bloqueo de descenso, piso duro, veto de colisión) y el detalle de cada mecanismo probado, está documentada en profundidad en [`_ARQUITECTURA_CONTROL.md`](Planta_BrazoRobótico/Assets/Scripts/_ARQUITECTURA_CONTROL.md).

## Estructura del repositorio

```
Planta_BrazoRobótico/     Proyecto Unity 6000.3.11f1 (simulación principal)
  Assets/Scripts/         Código C# — control, gripper, sensores, UI, ROS, HID
    _ARQUITECTURA_CONTROL.md        Cadena de control real, capa por capa
    _REGISTRO_PRUEBAS_CONTROL.md    Historial de pruebas y ajustes de parámetros
Joystick/joystick/
  firmware/               Firmware C/C++ del joystick (RP2040 / Pico SDK + TinyUSB)
  mechanics/              Diseño mecánico (SolidWorks, STEP, STL, gcode de impresión)
FirmwareESP8266/          Firmware ESP8266 (PlatformIO) para enlace UDP alternativo
  ros2_bridge/            Paquete ROS 2 (Colcon): puente joint_state ↔ UDP
Clients/
  Python/                 Cliente y dashboards UDP (udp_client.py, udp_robot_dashboard.py, udp_robot_viewer.py)
  MATLAB/                 Recepción y graficado UDP (robot_udp_plot.m, udp_joint_receiver.m) + Robotics/Spatial Math Toolbox
  Android-Termux/         Dashboard UDP para correr en Termux (Android)
docs/Informe/             Informe final (PDF/LaTeX), presentación HTML y capturas de código
AGENTS.md / CLAUDE.md     Instrucciones para agentes de IA que trabajen en este repo
```

## Simulación Unity

Desarrollada en **Unity 6000.3.11f1** con el paquete de robótica **Preliy Flange** (`com.preliy.flange`), que provee el modelo articulado del **KUKA KR210 R3100-2** y un solver de cinemática inversa analítico. El joystick (RP2040 o un gamepad PS4) se integra vía **Unity Input System**.

Puntos destacados de la implementación (ver `_ARQUITECTURA_CONTROL.md` para el detalle técnico):

- **Control por articulación:** PID con anti-windup, feedforward de velocidad del *setpoint* IK e inercia efectiva calculada por articulación (`RobotDynamics`), incluyendo el efecto de la carga agarrada.
- **Modos Robot / Cámara:** los mismos analógicos mueven el TCP del robot o la cámara *first-person*, con remapeo geométrico de ejes según la orientación del operador.
- **Capas de seguridad independientes**, todas aplicadas sobre la trayectoria cartesiana antes de la IK (nunca por articulación): límites de espacio de trabajo, frenado progresivo por proximidad, bloqueo de descenso con la garra cerrada, piso duro geométrico y veto de colisión con el entorno (incluyendo la pieza transportada).
- **Gripper (`GripperController` + `Ctrl_OnRobot_RG2_Custom`):** ciclo de agarre/suelta con confirmación por contacto en ambos dedos, suelta diferida para evitar que la física expulse la pieza, y herencia opcional de velocidad al soltar.
- **Interfaz en pantalla (Canvas):** acciones de control PID por articulación, guía de comandos del joystick, cámara embebida en el gripper, métricas de desempeño (colisiones, tiempo y cantidad de operaciones) y panel de clientes UDP conectados.
- **Publicación ROS:** `JointStatePublisher` publica `/joint_states` convirtiendo de grados (Flange) a radianes (ROS), vía ROS-TCP-Connector.
- **Diagnóstico automático:** `ControlDiagnosticRunner` + `ControlDiagnosticBatch` corren barridos verticales en batch mode y comparan variantes de parámetros de control, con reportes JSON en `Logs/`.

## Hardware: joystick RP2040

Controlador de dos ejes analógicos con carcasa impresa en 3D, construido sobre **Raspberry Pi Pico**. El firmware (C/C++ sobre el Pico SDK) usa **TinyUSB** para exponerse como un **HID USB estándar** (gamepad), reconocido por Unity sin drivers adicionales, e implementa **recepción de comandos de vibración** desde Unity como retroalimentación háptica de proximidad.

El diseño mecánico (SolidWorks, STEP/STL, gcode de impresión) y las distintas revisiones del prototipo (v1 a v3) están en `Joystick/joystick/mechanics/`.

## Comunicación UDP y clientes externos

`JointStateBroadcaster` (Unity) sirve un socket UDP con suscripción dinámica de clientes y transmite `q1..q6,gripper` en grados a una frecuencia configurable. Se incluyen clientes de referencia listos para usar:

- **Python** (`Clients/Python/`): cliente simple, visor y dashboard con gráficos en tiempo real.
- **MATLAB** (`Clients/MATLAB/`): recepción y graficado (`robot_udp_plot.m`, `udp_joint_receiver.m`), con soporte de Robotics/Spatial Math Toolbox.
- **Android/Termux** (`Clients/Android-Termux/`): dashboard UDP pensado para correr en un teléfono vía Termux.

## Puente ROS 2

`FirmwareESP8266/ros2_bridge/` es un paquete ROS 2 (Colcon) que expone el ejecutable `joint_state_udp_bridge`, alternativa de enlace UDP↔ROS cuando no se usa directamente ROS-TCP-Connector desde Unity. `FirmwareESP8266/` (PlatformIO) es el firmware para un enlace UDP basado en ESP8266.

## Instalación y uso

1. **Simulación (Unity)**
   - Abrir `Planta_BrazoRobótico/` con **Unity Hub** (versión `6000.3.11f1`).
   - Abrir la escena principal (`Planta.unity`) y presionar Play.
2. **Joystick (RP2040)**
   - Compilar el firmware en `Joystick/joystick/firmware/src/proto/joystick_v1/` con CMake + Pico SDK.
   - Flashear el `.uf2` resultante en la Raspberry Pi Pico.
   - Conectar por USB: el sistema operativo lo reconoce como HID (`cafe-4004-Joystick VR`), sin drivers.
3. **Monitoreo externo (opcional)**
   - Ejecutar cualquiera de los clientes en `Clients/` apuntando al puerto UDP publicado por `JointStateBroadcaster` (default `25001`).
4. **Puente ROS 2 / ESP8266 (opcional, en desarrollo)**
   - Compilar y correr `FirmwareESP8266/ros2_bridge/` para integrar el estado articular con un grafo ROS 2.

## Validación / build

```bash
# Unity, en batch mode (no usar -nographics: URP falla creando RenderTexture sin dispositivo gráfico)
Unity -batchmode -projectPath "Planta_BrazoRobótico" -quit -logFile -

# Firmware ESP8266 (PlatformIO)
cd FirmwareESP8266 && pio run

# Firmware joystick RP2040 (CMake + Pico SDK)
cd Joystick/joystick/firmware/src/proto/joystick_v1
cmake -S . -B build && cmake --build build

# Puente ROS 2 (Colcon)
cd FirmwareESP8266/ros2_bridge
colcon build --packages-select joint_state_udp_bridge
```

## Documentación adicional

- [`docs/Informe/Informe_Proyecto_RV_2026-Castel-Quiroga.pdf`](docs/Informe/Informe_Proyecto_RV_2026-Castel-Quiroga.pdf) — informe completo del proyecto.
- [`docs/Informe/presentacion/index.html`](docs/Informe/presentacion/index.html) — presentación HTML autocontenida (24 diapositivas, con animaciones y notas del orador; ver [`LEEME.md`](docs/Informe/presentacion/LEEME.md)).
- [`Planta_BrazoRobótico/Assets/Scripts/_ARQUITECTURA_CONTROL.md`](Planta_BrazoRobótico/Assets/Scripts/_ARQUITECTURA_CONTROL.md) — cadena de control real, capa por capa, con el código relevante.
- [`Planta_BrazoRobótico/Assets/Scripts/_REGISTRO_PRUEBAS_CONTROL.md`](Planta_BrazoRobótico/Assets/Scripts/_REGISTRO_PRUEBAS_CONTROL.md) — historial de síntomas, cambios probados y decisiones sobre el control.
- [`AGENTS.md`](AGENTS.md) / [`CLAUDE.md`](CLAUDE.md) — instrucciones para agentes de IA que trabajen en este repositorio.

## Estado del proyecto

- [x] Definición de arquitectura general.
- [x] Desarrollo del joystick en RP2040 (prototipo funcional v1–v3, HID + vibración).
- [x] Simulación en Unity: control cartesiano/PID, capas de seguridad, gripper, métricas, UI.
- [x] Comunicación UDP con clientes externos (Python, MATLAB, Android/Termux).
- [x] Publicación de estado articular a ROS 2 (`/joint_states`) y puente UDP↔ROS 2.
- [ ] Integración física directa con el robot electrohidráulico real (trabajo futuro).
- [ ] Retroalimentación háptica proporcional a la fuerza de contacto (trabajo futuro).

## Licencia

Este proyecto se distribuye bajo la licencia **Apache 2.0**. Consulta el archivo [`LICENSE`](LICENSE) para más detalles.
