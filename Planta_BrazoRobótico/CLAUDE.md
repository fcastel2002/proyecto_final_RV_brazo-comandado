# Planta_BrazoRobótico — Contexto del Proyecto

## Descripción
Entorno VR de entrenamiento para teleoperación de brazo robótico industrial 
(KUKA KR210 R3100-2) en Unity 6.3 LTS. Proyecto compartido con Francisco Castel.

## Librería principal
Flange 1.0.11 (com.preliy.flange) — instalada desde Git URL.
Código fuente en: Library/PackageCache/com.preliy.flange@34040b32179a/

## Scripts del proyecto
Assets/Scripts/ — scripts propios del equipo

## Objetivo actual
La dinámica del robot está implementada. Flujo vigente:
- Joystick analógico → `_velocity` (Vector3, ejes TCP remapeados desde cámara)
- Integración de velocidad → delta de posición TCP → `targetPose` (orientación fija)
- IK de Flange (`ComputeInverse`) → ángulos articulares objetivo (q1–q6)
- PID por articulación (`JointPID.Compute`) → torque virtual
- Inercia simulada (`RobotDynamics.ComputeEffectiveInertia`, masas URDF reales) →
  normaliza la aceleración: joints más pesados responden más lento
- `SetJoints()` recibe solo ángulos finales; Flange NO recibe torques físicos

Ver arquitectura detallada con referencias exactas archivo:línea en:
`Assets/Scripts/_ARQUITECTURA_CONTROL.md`

## Hardware de entrada
PS4 controller vía Unity Input System

## Notas importantes
- El eje de remapeo dinámico de joystick ya está implementado (JoystickAdapter.cs)
- No modificar la lógica de cambio de modo cámara/robot (L3)