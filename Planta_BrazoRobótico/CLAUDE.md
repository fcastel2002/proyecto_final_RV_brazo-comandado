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
Implementar dinámica del robot:
- Joystick analógico → consignas de velocidad en ejes TCP (proporcional al desplazamiento)
- Integración de velocidad → consignas de posición TCP
- IK de Flange → ángulos articulares objetivo
- Controladores PID por articulación (q1–q6)
- Masas realistas en eslabones para simular inercia
- Efecto esperado: cargas más pesadas generan respuesta más lenta, 
  compensada por acción integral del PID (especialmente q2 y q3)

## Hardware de entrada
PS4 controller vía Unity Input System

## Notas importantes
- El eje de remapeo dinámico de joystick ya está implementado (JoystickAdapter.cs)
- No modificar la lógica de cambio de modo cámara/robot (L3)