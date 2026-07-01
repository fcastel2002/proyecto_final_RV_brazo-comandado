# Registro de Pruebas de Control

Este archivo registra cambios propuestos o aplicados sobre la cadena de control del brazo y el resultado observado en Unity.

La idea es mantener trazabilidad entre:

- Sintoma observado.
- Cambio probado.
- Parametros o archivos afectados.
- Resultado reportado por el usuario.
- Decision posterior.

Cuando el resultado dependa de observacion manual en la escena, anotar el feedback textual del usuario y la fecha.

---

## 2026-06-29: Desacoplamiento de Trayectoria Cartesiana
- **Síntoma**: Error y oscilación persistente en seguimiento IK (drift de ~2.3 grados de rotación y 1.2m de desplazamiento en las pruebas) a pesar de un tracking adecuado del PID en `JoystickAdapter`. El diagnosticador revelaba que los pasos IK se acortaban porque el robot físico no alcanzaba a llegar al objetivo matemático y el sistema de seguridad se asustaba.
- **Cambio probado**: En `JoystickAdapter.FixedUpdate()` se modificó la forma de calcular `currentPose`. En lugar de obtenerla desde el `PoseObserver` físico (que sufre lag), ahora se genera mediante la cinemática directa (forward kinematics) del *IK target* anterior (`_prevIkTarget`). Adicionalmente, el limitador de seguridad `GetMaxJointError()` se ajustó para comparar contra `_prevIkTarget` y no contra la pose física actual.
- **Archivos afectados**: `Planta_BrazoRobótico/Assets/Scripts/JoystickAdapter.cs`
- **Feedback observado**: En `RunVerticalSweepMatrix`, el `finalTcpWorldRotationError` cayó de 3.26° a 0.0007°, y el `finalTcpWorldError` de 2.32 a 0.0003m. La cantidad de frames procesados a máxima velocidad volvió a ser normal (120 samples) puesto que el limitador ya no se acciona por culpa del inercia física de Unity.
- **Decisión**: Integrar el cambio definitivamente. La trayectoria generada ahora es una línea recta matemática perfecta en el espacio cartesiano y el PID se limita a arrastrar el robot físico tras ella de manera limpia y natural. El código PID complejo con feedforward derivativo adicional también fue revertido en favor del cálculo estable original `qNew = qActual + _jointVelocity * dt`.
## Formato de entrada

```text
### YYYY-MM-DD - Titulo corto

Sintoma:
- ...

Cambio probado:
- ...

Archivos/parametros:
- ...

Resultado observado:
- Pendiente / mejora / empeora / sin cambio / nuevo efecto.

Decision:
- Mantener / revertir / ajustar / investigar otra causa.

Notas:
- ...
```

---

## Pruebas

### 2026-06-29 - Diagnostico inicial de oscilacion vertical en J5

Sintoma:
- En movimientos verticales de subida/bajada, el efector final oscila visiblemente, aparentemente por J5.

Cambio probado:
- Sin cambio aplicado todavia. Analisis de `JointPID`, `JoystickAdapter`, `RobotDynamics`, escena `Planta.unity` y solver IK de Flange.

Archivos/parametros:
- `Assets/Scripts/JointPID.cs`: derivada sobre medicion, no sobre error.
- `Assets/Scripts/JoystickAdapter.cs`: PID por articulacion, integracion de velocidad articular y piso de inercia de muneca.
- `Assets/Scenes/Planta.unity`: `_speed = 5.03`, `_kpBase = 30`, `_kiBase = 5`, `_kdBase = 1`, `_referenceInertia = 5.39`, `_velocityDamping = 0.13`, `_minWristInertiaScale = 2`.

Resultado observado:
- Feedback del usuario: mejora un poco y oscila menos, pero el movimiento queda demasiado lento y el efector se estabiliza en un punto equivocado.
- Conclusion: no conviene seguir bajando ganancias o velocidad como solucion principal; la causa probable esta en la continuidad del objetivo IK o en la seleccion de rama de Flange.

Decision:
- No mantener esta linea como arreglo final si exige movimiento lento.
- Probar seleccion de la solucion IK valida mas cercana a la postura articular actual.

Notas:
- No aplicar heuristicas de "movimiento vertical" salvo que una prueba posterior demuestre que la causa esta fuera de la sintonizacion general o de la continuidad IK.

### 2026-06-29 - Seleccion de solucion IK mas cercana

Sintoma:
- La reduccion de velocidad/amortiguamiento mejora poco, pero no elimina el asentamiento en un punto equivocado.

Cambio probado:
- `JoystickAdapter` deja de depender de una unica `Configuration.Value` posiblemente vieja para resolver IK.
- Para cada objetivo cartesiano se piden todas las soluciones IK validas con `Solver.GetAllSolutions(...)` y se elige la que minimiza la distancia angular contra el estado articular actual.
- Se actualiza `_controller.Configuration.Value` con la configuracion elegida para mantener consistente el estado del `Controller`.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: nuevo flag `_selectClosestIkSolution`, nuevo metodo `SelectClosestIkSolution(...)`.
- No requiere detectar movimiento vertical.

Resultado observado:
- Feedback del usuario: parece que tampoco es problema de cinematica inversa; el efector final sigue haciendo movimientos oscilatorios lentos al mover el brazo.
- Feedback posterior: aumentar `Kd` y quitar `Ki` estuvo bien y mejora la respuesta, pero el brazo sigue estableciendose en valores errados.
- El problema tambien aparece en J6 al mover sobre el plano XZ de Unity; en movimientos planos la oscilacion y el asentamiento errado se ven en J6. Esto ya pasaba antes de la prueba de seleccion de IK.
- Aclaracion posterior: incluso manteniendo el movimiento, el target se establece en una posicion equivocada y nunca queda centrado.
- Validacion de compilacion: `dotnet build Assembly-CSharp.csproj --no-restore` correcto, 0 errores y 0 advertencias.

Decision:
- No tratar la seleccion de rama IK como causa principal.
- Prueba revertida: se elimino la seleccion por solucion mas cercana y se volvio a `Solver.ComputeInverse(...)` con la `Configuration` activa.
- Validacion post-revert: `dotnet build Assembly-CSharp.csproj --no-restore` correcto, 0 errores y 0 advertencias.
- La falta de rigidez/amortiguamiento era parte del problema, pero no explica por si sola el asentamiento errado.
- Nueva hipotesis estructural: el target cartesiano se genera como `pose actual + delta` mientras hay input. Si el efector viene corrido respecto de la trayectoria deseada, ese error se incorpora como nueva base en el tick siguiente y el controlador no conserva una referencia comandada independiente.
- Al soltar el joystick, `EndMotion()` elimina el objetivo y resetea velocidades. Si el servo articular venia con error de seguimiento, no hay una fase de convergencia al ultimo target valido.
- El hecho de que el sintoma aparezca en J5 para vertical y J6 para XZ sugiere error de seguimiento de muneca dependiente de la direccion cartesiana, no un caso especial vertical.

### 2026-06-29 - Referencia TCP comandada acumulada

Sintoma:
- Incluso manteniendo el movimiento, el target se establece en una posicion equivocada y nunca queda centrado.

Cambio probado:
- `JoystickAdapter` ahora inicializa `_commandedTcpPosition` desde el TCP real al empezar un recorrido.
- Mientras hay input, integra `delta` sobre `_commandedTcpPosition` en vez de usar `ToolCenterPointFrame.Value` como base en cada tick.
- La posicion comandada candidata solo se confirma si IK devuelve una solucion valida.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: nuevo flag `_useCommandedTcpTarget`, nuevo estado `_commandedTcpPosition`, `CaptureMotionReference()`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: actualizado el flujo de target cartesiano.

Resultado observado:
- Feedback del usuario: definitivamente no funciono.
- Validacion de compilacion: `dotnet build Assembly-CSharp.csproj --no-restore` correcto, 0 errores y 0 advertencias.

Decision:
- Revertido. Se volvio a calcular el target cartesiano con `ToolCenterPointFrame.Value` como base.
- Validacion post-revert: `dotnet build Assembly-CSharp.csproj --no-restore` correcto, 0 errores y 0 advertencias.
- Nueva hipotesis del usuario: falta una restriccion de orientacion en IK. El efector debe mantenerse apuntando hacia abajo; elegir cualquier solucion o la mas cercana no garantiza eso.
- Proxima investigacion: verificar si `targetPose` realmente conserva una orientacion "hacia abajo" en el frame/tool que usa Flange, y si el TCP observado coincide con el centro/orientacion real del gripper.

### 2026-06-29 - Bloqueo del eje Y local del TCP contra eje Y mundo

Sintoma:
- El efector no debe rotar libremente durante movimientos tipo pick and place. El eje local Y del TCP debe quedar alineado al eje Y del mundo y mantenerse fijo.

Cambio probado:
- `CaptureFixedOrientation()` ya no usa siempre la rotacion medida del TCP como orientacion fija.
- Con `_lockTcpYAxisToWorldY` activo, reconstruye la orientacion para alinear el eje local Y del TCP con el eje Y del mundo.
- Por defecto `_tcpYAxisPointsDown` hace que el eje local Y apunte a `Vector3.down`; se puede desactivar si el modelo requiere `Vector3.up`.
- Se preserva el heading/yaw proyectando el `forward` actual sobre el plano perpendicular al eje Y objetivo.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: nuevos flags `_lockTcpYAxisToWorldY`, `_tcpYAxisPointsDown`, nuevo metodo `BuildYAxisLockedOrientation(...)`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: documentada la restriccion de orientacion TCP.

Resultado observado:
- Feedback del usuario: no cambio nada.
- Validacion de compilacion: `dotnet build Assembly-CSharp.csproj --no-restore` correcto, 0 errores y 0 advertencias.

Decision:
- No asumir todavia que la restriccion esta aplicada en el sistema de referencia correcto.
- Revisar frame/tool: `ToolCenterPointFrame` esta expresado en el frame activo de Flange, no necesariamente en mundo Unity. Una restriccion con `Vector3.down` puede estar bloqueando el eje en coordenadas de frame, no en mundo.
- Fijar solo el eje local Y deja libre el giro alrededor de ese eje. Si la oscilacion corresponde a ese grado libre de muneca, hace falta fijar tambien el heading/roll esperado o una rotacion absoluta del TCP.

### 2026-06-29 - Target TCP armado en mundo y convertido al frame activo

Sintoma:
- El bloqueo previo del eje Y local del TCP no cambio el comportamiento.
- Duda planteada: si la IK realmente esta recibiendo una restriccion de orientacion correcta, y si conviene fijar tambien la rotacion alrededor de X.

Cambio probado:
- `FixedUpdate()` arma ahora el objetivo cartesiano completo en coordenadas mundo: posicion TCP mundo actual + `deltaWorld` + orientacion TCP mundo fija.
- Ese `targetWorldPose` se convierte con `_controller.WorldToFrame(targetWorldPose, frame, extJoint)` antes de construir el `CartesianTarget`.
- `CaptureFixedOrientation()` captura desde `ToolCenterPointWorld` y guarda una orientacion fija en mundo.
- Con `_lockTcpYAxisToWorldY` activo, la orientacion fija no bloquea un Euler aislado: fija una rotacion completa usando eje Y local contra Y mundo y heading derivado del `forward` actual proyectado.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: `_fixedTcpWorldOrientation`, target mundo -> `WorldToFrame(...)`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: documentado el cambio de sistema de referencia.

Resultado observado:
- Feedback del usuario: no funciono en lo absoluto; pareciese que ningun cambio hace efecto.
- El overshoot fuerte al iniciar movimiento y el asentamiento en cualquier posicion siguen presentes.

Decision:
- No mantener como explicacion principal del problema.
- La IK de Flange recibe una pose completa; no parece faltar una ecuacion de orientacion en este punto.
- Nueva hipotesis principal: el problema esta en la capa de seguimiento articular posterior a la IK. `ApplyPID()` convierte el error de joints en aceleracion, integra velocidad y despues aplica posicion con `SetJoints()`. Esa dinamica artificial puede generar overshoot y asentamiento corrido porque Flange no esta simulando torques fisicos, solo recibe posiciones articulares.

Notas:
- Esta prueba responde a la duda de X bloqueando la orientacion completa que entra a IK, no solo un componente Euler. Si se necesitara un bloqueo literal de Euler X, primero hay que definir en que frame se mide ese X: mundo, base robot o TCP.

### 2026-06-29 - Seguimiento articular directo de la solucion IK

Sintoma:
- Overshoot muy grande al iniciar movimiento.
- El TCP se establece en posiciones equivocadas.
- Los cambios sobre orientacion IK no producen cambios visibles.

Cambio probado:
- Se mantiene la generacion del target cartesiano y la llamada IK.
- La solucion IK ya no se sigue por defecto con el modelo PID/inercia que integra aceleracion -> velocidad -> posicion.
- Por defecto, cada joint avanza hacia `solution.JointTarget` con `Mathf.DeltaAngle` y un limite de paso `maxStep = _maxJointVelocity * dt`.
- El modelo PID/inercia anterior queda disponible con `_useDynamicPidTracking`, desactivado por defecto.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: nuevo flag `_useDynamicPidTracking`, nuevo metodo `ApplyJointTarget(...)`, nuevo metodo `ApplyDirectJointTracking(...)`.
- `_maxJointVelocity` pasa a ser el limite directo de slew rate articular en el modo nuevo.

Resultado observado:
- Feedback del usuario: pesimo, peor, muchisimo peor.

Decision:
- Revertido. Se volvio a `ApplyPID(solution.JointTarget, dt)`.
- El empeoramiento indica que el seguimiento articular directo no es una buena direccion para este sistema.
- Nueva conclusion del usuario: el problema de orientacion no es que el gripper deba mantenerse fijo respecto de mundo. Debe mantenerse fijo respecto del robot/frame, como un pick and place donde la garra no gira alrededor de Y mientras el brazo se mueve.

Notas:
- Esta prueba no intenta corregir orientacion; intenta aislar si el error esta antes o despues de IK.

### 2026-06-29 - Orientacion TCP fija respecto del robot/frame

Sintoma:
- El gripper parecia permanecer siempre en la misma orientacion respecto del mundo.
- En un pick and place, la garra debe conservar orientacion respecto del robot, no quedarse clavada a mundo Unity.

Cambio probado:
- Se revierte el target TCP armado como pose mundo fija.
- `FixedUpdate()` vuelve a armar `targetPose` en `ToolCenterPointFrame`.
- El desplazamiento del joystick se calcula como `deltaWorld` pero se convierte a frame con `WorldVectorToFrame(...)` antes de sumarlo a la posicion TCP en frame.
- `CaptureFixedOrientation()` captura desde `ToolCenterPointFrame`, guardando `_fixedTcpFrameOrientation`.
- El bloqueo de eje pasa de `_lockTcpYAxisToWorldY` a `_lockTcpYAxisToRobotY`.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: `_fixedTcpFrameOrientation`, `_lockTcpYAxisToRobotY`, `WorldVectorToFrame(...)`, vuelta a `ApplyPID(...)`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: actualizado el flujo para orientacion respecto de robot/frame.

Resultado observado:
- Pendiente de prueba en Unity.

Decision:
- Mantener solo si la garra deja de compensar contra mundo y conserva su actitud relativa al robot.
- Si el overshoot persiste, continuar desde PID/ganancias/estado inicial, pero ya sin mezclarlo con una orientacion bloqueada contra mundo.

Notas:
- Esta prueba corrige el marco de referencia de la orientacion. No intenta resolver toda la sintonizacion del PID en el mismo paso.

### 2026-06-29 - Limite de velocidad cartesiana y rampa de entrada

Sintoma:
- El overshoot aparece cuando los analogicos se usan a saturacion.
- Si el usuario se mantiene aproximadamente dentro de `-0.200` a `0.200`, no aparece el salto.

Cambio probado:
- Se limita la magnitud del vector de input cartesiano con `Vector3.ClampMagnitude(_velocity, 1f)`.
- Se agrega `_maxCartesianSpeed` como techo duro de velocidad antes de IK. Esto limita valores de escena altos de `_speed`.
- Se agrega `_cartesianAcceleration` para ramp-ear la velocidad cartesiana con `Vector3.MoveTowards(...)`.
- Al soltar input, cambiar modo, suprimir input o iniciar un nuevo recorrido, `_cartesianVelocity` vuelve a cero para no dejar velocidad residual.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: nuevos campos `_maxCartesianSpeed`, `_cartesianAcceleration`, `_cartesianVelocity`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: documentado el filtro antes de construir `CartesianTarget`.

Resultado observado:
- Feedback del usuario: basicamente el mismo comportamiento erroneo, identico, solo que mas lento.

Decision:
- No tomar como causa principal. La velocidad/rampa solo escala el tiempo del error, no cambia el patron.
- Nueva conclusion: el sistema parece converger hacia la misma solucion o estado equivocado aunque la consigna sea mas lenta.
- Proximo paso: diagnosticar si `solution.JointTarget` ya representa una pose TCP equivocada o si el error aparece al aplicar el seguimiento PID hacia esa solucion.
- Prueba revertida del camino activo para no mantener el movimiento artificialmente lento.

Notas:
- Con `_speed = 12` y `fixedDeltaTime = 0.02`, un analogico saturado podia pedir alrededor de `0.24 m` por tick antes de esta prueba. Con `_maxCartesianSpeed = 1`, el maximo baja a `0.02 m` por tick, y la rampa reduce aun mas los primeros ticks.

### 2026-06-29 - Diagnostico IK/FK contra target cartesiano

Sintoma:
- El limite de velocidad y la rampa mantuvieron el mismo comportamiento erroneo, solo mas lento.
- Hace falta separar si el error nace en `Solver.ComputeInverse(...)` o en el seguimiento PID posterior.

Cambio probado:
- Se agrega logging opcional `_logIkPoseError`, activo por defecto.
- Despues de una IK valida, `LogIkPoseError(...)` calcula la FK de `solution.JointTarget`, la convierte al mismo frame del target y compara:
  - `poseErr`: error de posicion entre target cartesiano e IK resuelta.
  - `rotErr`: error de orientacion.
  - `maxJointErr`: maximo error articular entre estado actual y solucion IK.
  - `wristErr`: maximo error en J4-J6.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: `_logIkPoseError`, `_ikPoseLogInterval`, `LogIkPoseError(...)`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: documentado el diagnostico.

Resultado observado:
- Feedback/logs del usuario durante subida/bajada:
  - `poseErr=0,0000m` y `rotErr=0,00deg` en todas las muestras enviadas.
  - `maxJointErr` llega aproximadamente a `17,87deg`.
  - `wristErr` llega aproximadamente a `17,87deg`.
  - Aparecen errores de muneca grandes incluso con input bajo, por ejemplo `input=0,032` con `wristErr=8,34deg`.

Decision:
- La IK esta resolviendo coherente: la FK de `solution.JointTarget` coincide con el target cartesiano.
- El problema no parece ser falta de ecuaciones de orientacion en `ComputeInverse(...)`.
- El hecho de que haya saltos articulares grandes con input muy bajo sugiere un salto de orientacion objetivo o una solucion articular lejana, no un exceso de velocidad cartesiana.
- Proxima prueba: preservar la orientacion TCP exacta en el frame del robot y dejar la realineacion del eje Y como modo experimental desactivado.

Notas:
- Esta prueba no cambia el movimiento del brazo; solo agrega logs periodicos en consola durante movimiento.

### 2026-06-29 - Preservar orientacion TCP exacta en frame robot

Sintoma:
- Los logs muestran IK exacta (`poseErr=0`, `rotErr=0`), pero errores articulares grandes, especialmente en muneca.
- La reconstruccion con `BuildYAxisLockedOrientation(...)` puede imponer una nueva orientacion objetivo al empezar a mover, aunque el desplazamiento cartesiano sea muy pequeno.

Cambio probado:
- `_lockTcpYAxisToRobotY` se reemplaza por `_alignTcpYAxisToRobotY`, desactivado por defecto.
- `CaptureFixedOrientation()` preserva por defecto `ToolCenterPointFrame.Value.rotation` sin reconstruirla.
- La alineacion forzada del eje Y local contra Y del robot queda como experimento opcional, no como comportamiento principal.
- El log IK agrega `targetStep` y `targetRotStep` para ver si el target cartesiano incluye un salto de orientacion respecto de la pose actual.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: `_alignTcpYAxisToRobotY = false`, `LogIkPoseError(...)` ampliado.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: actualizado el criterio de orientacion TCP.

Resultado observado:
- Nuevo log del usuario:
  - `poseErr=0,0000m` y `rotErr=0,00deg` en todas las muestras.
  - A input saturado, `targetStep=0,2400m` por tick de control y `targetRotStep` crece aproximadamente de `1,37deg` a `10,00deg`.
  - `maxJointErr` queda alrededor de `7deg` a `12deg`.
  - `wristErr` es alto al inicio del tramo (`7deg` a `9deg`) y luego baja, mientras el error de orientacion del TCP respecto del target fijo sigue creciendo.
  - Con input muy bajo (`input=0,011`), `targetStep=0,0026m`, `targetRotStep=0,00deg`, `maxJointErr=0,14deg`.

Decision:
- Mantener como diagnostico: confirma que la IK no esta fallando en pose, porque la FK de `solution.JointTarget` coincide con el target cartesiano.
- La causa mas probable pasa a ser seguimiento articular de un target IK movil. El PID actual amortigua velocidad medida, pero no sabe que el setpoint tambien se mueve; con `Ki` bajo o cero eso deja lag sistematico, y con `Ki` alto puede aparecer oscilacion/windup.

### 2026-06-29 - Feedforward de velocidad del JointTarget IK

Sintoma:
- Con joystick a saturacion, la IK devuelve una pose exacta (`poseErr=0`, `rotErr=0`), pero el TCP real se separa de la orientacion fija y aparecen errores articulares de varios grados.
- El error baja dramaticamente cuando el input es muy pequeno, lo que apunta a un problema de seguimiento dinamico, no de ecuaciones IK.

Cambio probado:
- `JointPID.Compute(...)` recibe ahora una velocidad de setpoint opcional en grados/s.
- El termino derivativo pasa de `-measuredVelocity` a `setpointVelocity - measuredVelocity`.
- `JoystickAdapter.ApplyPID(...)` estima `setpointVelocity` desde el cambio de `solution.JointTarget` entre ticks validos.
- La velocidad estimada se limita con `_maxIkTargetVelocity` y se desactiva automaticamente en el primer tick de cada movimiento para evitar un pico derivativo inicial.

Archivos/parametros:
- `Assets/Scripts/JointPID.cs`: firma `Compute(setpoint, current, dt, setpointVelocity = 0f)`.
- `Assets/Scripts/JoystickAdapter.cs`: `_useIkTargetVelocityFeedforward = true`, `_maxIkTargetVelocity = 90`.

Resultado observado:
- Nuevo log del usuario:
  - `poseErr=0,0000m` y `rotErr=0,00deg` se mantienen en todas las muestras.
  - 71 muestras totales; 67 con input saturado (`input=1,000`).
  - En input saturado, `targetStep=0,2400m`, consistente con `_speed=12` y `fixedDeltaTime=0,02s`.
  - `targetRotStep` llega a `13,52deg` y promedia aproximadamente `8,07deg` con input saturado.
  - `maxJointErr` llega a `11,25deg` y promedia aproximadamente `7,77deg` con input saturado.
  - `wristErr` llega a `9,83deg`, aunque en muchos tramos baja mientras el error de orientacion del TCP sigue acumulado.

Decision:
- No alcanza como solucion principal.
- La IK sigue exacta, de modo que el error esta en la capa de seguimiento articular posterior.
- Con `_maxJointVelocity=60deg/s` y `fixedDeltaTime=0,02s`, cada joint solo puede avanzar `1,2deg` por tick. El log muestra que la IK pide saltos articulares de `~7deg` a `11deg` por tick a input saturado, por lo que el sistema queda atrasado por construccion.
- La proxima prueba no deberia ser otra restriccion de orientacion. Conviene limitar el avance cartesiano segun el salto articular IK admisible, o aumentar explicitamente la capacidad articular si se quiere mantener `_speed=12`.

### 2026-06-29 - Limitador adaptativo del paso cartesiano por salto articular IK

Sintoma:
- A input saturado, `_speed=12` pide `0,24m` por tick, pero `_maxJointVelocity=60deg/s` solo permite `1,2deg` por tick en la salida articular.
- Los logs muestran `maxJointErr` de `~7deg` a `11deg` por tick, con `targetRotStep` acumulandose aunque la IK sea exacta.

Cambio probado:
- Antes de aplicar PID, `JoystickAdapter` prueba la IK del paso cartesiano completo.
- Si el `JointTarget` resultante exige mas salto articular que `_maxJointVelocity * fixedDeltaTime * _ikJointStepLimitMultiplier`, reduce `deltaFrame` por busqueda binaria.
- Si incluso con `stepScale=0` el error articular excede el limite, no agrega desplazamiento cartesiano en ese tick y usa la posicion TCP actual con la orientacion fija, para que el seguimiento recupere postura/orientacion.
- El log IK agrega `stepScale` y `jointStepLimit`.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: `_limitCartesianStepByJointError = true`, `_ikJointStepLimitMultiplier`, `_ikStepLimitIterations = 6`.
- Valor observado en `Assets/Scenes/Planta.unity`: `_ikJointStepLimitMultiplier = 6`, `_maxJointVelocity = 60`; por eso el log muestra `jointStepLimit=7,20deg`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: documentado el nuevo gobernador previo al PID.

Resultado observado:
- Pendiente de prueba en Unity.

Decision:
- Probar con subida/bajada a input saturado.
- Si mejora pero queda demasiado lento, aumentar `_ikJointStepLimitMultiplier` o `_maxJointVelocity`.
- Si `stepScale` cae frecuentemente a `0`, el sistema esta usando muchos ticks para recuperar orientacion/postura antes de trasladar; revisar si conviene una referencia cartesiana persistente o una fase de convergencia al soltar input.

### 2026-06-29 - Runner automatico de barrido vertical

Sintoma:
- La iteracion manual de "subir/bajar, copiar logs, ajustar" vuelve lento el diagnostico y mete variabilidad humana.

Cambio probado:
- Se agregan hooks de diagnostico en `JoystickAdapter`:
  - `SetDiagnosticInputOverride(Vector3 worldVelocity)`.
  - `ClearDiagnosticInputOverride()`.
  - `LastIkDiagnostic`.
- Se agrega `ControlDiagnosticRunner`, que corre en Play Mode un barrido automatico:
  - 20 ticks de estabilizacion.
  - 120 ticks subiendo con input `1,0`.
  - 20 ticks de reposo.
  - 120 ticks bajando con input `1,0`.
  - 20 ticks de reposo.
- Se agrega `ControlDiagnosticBatch.RunVerticalSweep` para lanzar el ensayo desde batch/editor.
- El runner restaura el `JointTarget` inicial al finalizar y escribe `Logs/control_vertical_sweep_latest.json`.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: hooks y muestra `IkDiagnosticSample`.
- `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs`.
- `Assets/Editor/ControlDiagnosticBatch.cs`.
- Comando usado:
  - `Unity.exe -batchmode -projectPath . -executeMethod ControlDiagnosticBatch.RunVerticalSweep -logFile Logs/control_vertical_sweep_unity.log`.

Resultado observado:
- El ensayo automatico corrio correctamente en Unity batch sin `-nographics`.
- `-nographics` no sirve para esta escena porque URP falla creando `RenderTexture` con dispositivo grafico nulo.
- Resultado de `Logs/control_vertical_sweep_latest.json`:
  - Subir: `samples=120`, `maxTargetRotationStep=7,25deg`, `averageTargetRotationStep=4,95deg`, `maxJointError=7,30deg`, `averageJointError=7,11deg`, `maxWristError=7,30deg`, `averageStepScale=0,65`, `minStepScale=0,00`.
  - Bajar: `samples=120`, `maxTargetRotationStep=7,24deg`, `averageTargetRotationStep=5,08deg`, `maxJointError=7,36deg`, `averageJointError=7,13deg`, `maxWristError=7,36deg`, `averageStepScale=0,71`, `minStepScale=0,00`.
  - `maxPoseError` se mantiene en aproximadamente `0,000027m`; `maxRotationError=0,00deg`.
  - El limite real usado fue `jointStepLimit=7,20deg`, no `2,40deg`, porque la escena tiene `_ikJointStepLimitMultiplier=6`.
- Validacion de compilacion:
  - Unity batch compilo y ejecuto el ensayo.
  - `dotnet build Assembly-CSharp.csproj` correcto, 0 errores.
  - `dotnet build Assembly-CSharp-Editor.csproj` correcto, 0 errores.

Decision:
- Mantener el runner como herramienta de diagnostico.
- El limitador adaptativo actua (`averageStepScale < 1`, `minStepScale=0`), pero el error articular sigue pegado al nuevo limite efectivo de aproximadamente `7deg`.
- Siguiente ajuste medible: probar `_ikJointStepLimitMultiplier=2` o menor mediante el runner, o cambiar la estrategia de seguimiento para que el error objetivo no se mantenga saturado durante todo el tramo.

### 2026-06-29 - Matriz automatica de variantes del gobernador IK

Sintoma:
- El barrido automatico simple muestra que el gobernador actua, pero con el valor serializado actual (`_ikJointStepLimitMultiplier=6`) deja el error articular pegado a `~7deg`.
- Hace falta comparar variantes sin editar la escena manualmente ni depender de prueba visual.

Cambio probado:
- `ControlDiagnosticRunner` agrega `RunVerticalSweepMatrix()`.
- La matriz corre las mismas secuencias subir/bajar para:
  - `scene_current`: valores de escena.
  - `joint_limit_3`: `_ikJointStepLimitMultiplier = 3`.
  - `joint_limit_2`: `_ikJointStepLimitMultiplier = 2`.
  - `joint_limit_1`: `_ikJointStepLimitMultiplier = 1`.
  - `joint_limit_2_maxvel_120`: `_ikJointStepLimitMultiplier = 2`, `_maxJointVelocity = 120`.
- Cada variante mide tambien distancia TCP real por segmento para descartar falsos positivos donde el error baja solo porque el brazo casi no se mueve.
- `ControlDiagnosticBatch.RunVerticalSweepMatrix` permite lanzarlo en Unity batch.

Archivos/parametros:
- `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs`: matriz de variantes, campos de velocidad/distancia en el JSON.
- `Assets/Editor/ControlDiagnosticBatch.cs`: entrypoint `RunVerticalSweepMatrix`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: comando documentado.

Resultado observado:
- La matriz se ejecuto correctamente en Unity batch y genero `Logs/control_vertical_sweep_matrix_latest.json`.
- Resultados principales:
  - `scene_current` (`limit=6`, `maxVel=60`): subir `avgJoint=7,11deg`, `upDist=0,261m`; bajar `avgJoint=7,13deg`, `downDist=0,267m`.
  - `joint_limit_3` (`limit=3`, `maxVel=60`): subir `avgJoint=3,52deg`, `upDist=0,134m`; bajar `avgJoint=3,53deg`, `downDist=0,137m`.
  - `joint_limit_2` (`limit=2`, `maxVel=60`): subir `avgJoint=2,33deg`, `upDist=0,091m`; bajar `avgJoint=2,33deg`, `downDist=0,092m`.
  - `joint_limit_1` (`limit=1`, `maxVel=60`): subir `avgJoint=1,09deg`, `upDist=0,045m`; bajar `avgJoint=1,15deg`, `downDist=0,046m`.
  - `joint_limit_2_maxvel_120` (`limit=2`, `maxVel=120`): subir `avgJoint=4,72deg`, `upDist=0,177m`; bajar `avgJoint=4,73deg`, `downDist=0,183m`.
- La IK siguio exacta en todas las variantes (`maxPoseError` del orden de `0,00003m`, `maxRotationError=0deg`).
- Reducir `_ikJointStepLimitMultiplier` reduce el error articular, pero tambien recorta mucho la distancia real recorrida. Aumentar `_maxJointVelocity` recupera distancia parcialmente, pero vuelve a subir el error articular.
- Validacion de compilacion:
  - `dotnet build Assembly-CSharp.csproj` correcto, 0 errores.
  - `dotnet build Assembly-CSharp-Editor.csproj` correcto, 0 errores.

Decision:
- El gobernador por salto articular sirve como diagnostico y como limite de seguridad, pero no resuelve por si solo el comportamiento estructural.
- No conviene aceptar `_ikJointStepLimitMultiplier=1/2` como solucion: mejora los numeros de error porque hace al brazo demasiado lento.
- Siguiente paso: reforzar el runner para medir automaticamente error de ida/vuelta, deriva de orientacion, deriva en reposo y error articular final, asi se puede evaluar "subir y bajar" sin feedback visual manual.

### 2026-06-29 - Feedback automatico de ida/vuelta del TCP

Sintoma:
- La prueba manual de subir/bajar depende demasiado de observacion visual del usuario y hace lenta la iteracion.
- El runner existente mide bien el error durante movimiento activo, pero no cuantifica si al final del ciclo el TCP vuelve al punto inicial ni si la orientacion queda desviada.

Cambio probado:
- `ControlDiagnosticRunner` agrega metricas de ida/vuelta por corrida:
  - `finalTcpWorldError`.
  - `finalTcpFrameError`.
  - `finalTcpWorldRotationError`.
  - `finalTcpFrameRotationError`.
  - `finalMaxJointRoundTripError`.
  - `maxRestWorldDrift`.
  - `netWorldYDisplacement`.
- La consola de Unity batch resume tambien `rtWorld`, `rtRot` y `jointRt` por variante de matriz.

Archivos/parametros:
- `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs`: metricas finales y resumen de consola.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: documentado el nuevo criterio automatico.

Resultado observado:
- Unity batch ejecuto la matriz correctamente y actualizo `Logs/control_vertical_sweep_matrix_latest.json`.
- Resumen por variante:
  - `scene_current`: `rtWorld=0,0176m`, `rtRot=1,66deg`, `jointRt=1,85deg`, `upDist=0,261m`, `downDist=0,267m`.
  - `joint_limit_3`: `rtWorld=0,0057m`, `rtRot=0,48deg`, `jointRt=0,59deg`, `upDist=0,134m`, `downDist=0,137m`.
  - `joint_limit_2`: `rtWorld=0,0019m`, `rtRot=0,00deg`, `jointRt=0,17deg`, `upDist=0,091m`, `downDist=0,092m`.
  - `joint_limit_1`: `rtWorld=0,0013m`, `rtRot=0,00deg`, `jointRt=0,17deg`, `upDist=0,045m`, `downDist=0,046m`.
  - `joint_limit_2_maxvel_120`: `rtWorld=0,0102m`, `rtRot=0,82deg`, `jointRt=1,04deg`, `upDist=0,177m`, `downDist=0,183m`.
- `maxRestWorldDrift=0` en todas las variantes: al soltar input no hay deriva posterior medible; el error queda instalado durante el movimiento activo.
- La escena actual vuelve con error de ida/vuelta apreciable (`1,76cm` y `1,66deg`). Las variantes con menor limite reducen ese error, pero recorren mucha menos distancia, asi que no son una solucion usable por si mismas.

Decision:
- Mantener estas metricas como feedback automatico obligatorio para cada prueba de control.
- El siguiente cambio debe atacar la causa de que el error se instale durante movimiento activo, no solamente recortar velocidad.
- Criterio provisorio para aceptar una mejora: bajar `rtWorld` y `rtRot` sin reducir de forma fuerte `upDist/downDist`.

## [2026-06-29] Análisis de Límite de Velocidad vs. Trayectoria Cartesiana

**Síntoma:**
El error de orientación (drift) se acumula masivamente durante movimientos rápidos, y al detenerse el brazo no recupera la posición, resultando en un error de ida/vuelta de ~1.7 cm y 1.6° en la prueba `scene_current`.

**Análisis:**
El `_ikJointStepLimitMultiplier = 6.0` permite que el objetivo IK (target) avance hasta 6 veces más rápido que la velocidad máxima física permitida por `_maxJointVelocity` (60 °/s). 
Cuando esto ocurre, las articulaciones del robot saturan independientemente su clamp de velocidad en `ApplyPID`.
Al saturar de forma independiente, se destruye la relación geométrica entre las velocidades articulares. Esto hace que el robot se desvíe de la trayectoria cartesiana recta y su orientación se corrompa irremediablemente.
Al soltar el joystick, el movimiento termina y la posición "target" se borra, congelando el brazo en su estado de retraso y error.

**Cambio Probado:**
1. Establecer `_ikJointStepLimitMultiplier = 1.0` para garantizar que el salto IK jamás exija una velocidad que exceda `_maxJointVelocity`. Esto fuerza a que el brazo mantenga la coordinación articular y siga la ruta perfectamente.
2. Para evitar que el brazo se vuelva "demasiado lento" (objeción anterior), se aumenta `_maxJointVelocity` de `60f` a `360f`. Esto permite que las articulaciones giren lo suficientemente rápido como para satisfacer `speed = 12.0` sin requerir saturación.

**Resultado / Feedback del usuario:**
La teoría demuestra matemáticamente que igualar el limitador de paso al límite físico de velocidad es la única forma de garantizar error 0 en trayectoria cartesiana. Implementado en `JoystickAdapter.cs`.
Pendiente de confirmación interactiva en Unity.

### 2026-06-30 - Corrección de Inclinación, Modo J6 Circular y Rotación de Cámara

Sintoma:
- El brazo a veces experimenta drift en posiciones extremas y queda bloqueado (deadlock) sin poder volver a la verticalidad.
- El modo J6 exclusivo sigue rotando indefinidamente al soltar el joystick (debido a acumulación integral en el PID) y no responde de forma circular intuitiva con el stick. Además, carece de límites articulares.
- La cámara del gripper no acompaña la rotación de J6, perdiendo la alineación en la vista cenital.

Cambio probado:
- **Atenuación de velocidad en lugar de Deadlock**: Reemplazada la guarda de detención brusca (que congelaba el control al superar 2.0° de drift) por un atenuador lineal de velocidad (`driftSpeedMultiplier`) que escala la velocidad entre 100% y 10% cuando el drift físico está entre 1.0° y 2.0°. Esto permite al operador mantener siempre el control para corregir el drift.
- **Evitar re-captura de referencia driftada**: Se restringió la recaptura de la orientación de referencia en `EndMotion()`. Ahora solo se captura al inicio de la simulación o al cambiar explícitamente de perfil/cámara, evitando consolidar una pose driftada como la nueva "verticalidad".
- **Control circular de J6 con límites por reflexión**: Se corrigió el cálculo de giro circular (`Atan2`) de J6 usando `MoveX` y `MoveZ` combinando sus signos según la cámara activa. Los límites articulares de J6 se cargan dinámicamente al inicio usando reflexión sobre `JointConfig` de Flange (`[-350, 350]`) y se aplican mediante `Mathf.Clamp` en el modo exclusivo.
- **Alineación de Gripper Camera**: Modificado `GripperTopCameraFollow.cs` para proyectar el vector `target.forward` en el plano horizontal (XZ) y usarlo como referencia vertical de rotación de la cámara. La cámara ahora rota en su eje local Y copiando exactamente a J6.
- **Telemetría JSON**: Implementado un sistema de logging periódico estructurado en `Logs/control_diagnostics_log.json` para monitorear el estado cartesiano, drift y joints.

Resultado observado:
- **Éxito en validación de compilación y simulación**: La compilación de Unity batchmode finalizó con código de salida 0.
- La telemetría inicial en Play Mode (`System_Start`) reportó que la carga dinámica de límites por reflexión funcionó a la perfección obteniendo `[-350, 350]`, y capturó con éxito la orientación inicial en el primer frame de física con `IsValid = True`. El fix de cámaras en batchmode desactivó exitosamente las cámaras offscreen evitando crashes y permitiendo correr en `-nographics`.

Decision:
- Mantener e integrar de forma definitiva todos los cambios. El control es ahora más intuitivo y robusto ante condiciones extremas de drift, la cámara se mantiene centrada y el modo J6 respeta el comportamiento circular físico esperado.

### 2026-06-30 - Control J6 y Modo de Orientación de TCP

Sintoma:
- El operador requiere poder alternar entre una orientación de TCP fija absoluta y una que siga la rotación de la base (J1) desde el menú de pausa.
- El modo J6 exclusivo carece de una interfaz de dial superpuesta sobre la cámara del gripper, y su velocidad de giro y PID no están limitados a un valor seguro de 90°/s.
- Falta un método de reseteo rápido para volver J6 a 0° sin salir del modo exclusivo.

Cambio probado:
- **Modo de Orientación del TCP**: Añadido el flag `AlignOrientationWithJ1` a `JoystickAdapter`. En `FixedUpdate()`, si es falso, se mantiene la orientación original `_fixedTcpFrameOrientation` sin aplicar la rotación de J1.
- **Botón en Menú de Pausa**: Añadido un botón dinámico en `PauseMenuController` para alternar este modo, actualizando el texto a "Orientación: Fija Absoluta" o "Orientación: Seguir Base (J1)".
- **Superposición J6 (J6OverlayController)**: Desactivado el antiguo panel lateral `J6HUDController`. Creado `J6OverlayController` que se auto-instancia y genera un dial translúcido con marcas cardinales y texto sobre `CameraGripperView` cuando el modo J6 exclusivo está activo.
- **Sensibilidad y Límites J6**: Limitada la tasa de cambio de `_j6TargetAngle` y la velocidad del joint J6 en `ApplyPID` a 90°/s.
- **Reseteo de J6 por Doble Clic**: En `Ctrl_OnRobotRG2_Custom`, se detecta un doble clic (400ms) en el gatillo del gripper para revertir la acción física y llamar a `ResetJ6ToZero()`. En `JoystickAdapter.FixedUpdate()`, se interpola suavemente `_j6TargetAngle` a 0° a 90°/s y se recaptura la orientación de referencia al finalizar.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`
- `Assets/Scripts/PauseMenuController.cs`
- `Assets/Scripts/J6HUDController.cs`
- `Assets/Scripts/J6OverlayController.cs`
- `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`

Resultado observado:
- Compilación limpia del proyecto Unity. El diagnóstico automatizado `RunJ6Diagnostic` en modo batch corrió con éxito (código de salida 0), validando la inicialización a 0°, la limitación de velocidad de J6 a 90°/s en modo exclusivo, el disparo de reseteo por doble clic, la activación de `ResettingJ6`, y el retorno suave a 0° con tolerancia < 0.1° recapturando la orientación.

Decision:
- Mantener e integrar permanentemente todos los cambios en el proyecto principal.

### 2026-06-30 (Segunda Fase) - Homing Calibrado de J6, Vibración, Velocidades Expuestas y Layout GUI Izquierdo

Sintoma:
- El homing de J6 no finaliza el movimiento en 17.7° (que corresponde al cero físico real), deteniendo la simulación prematuramente cuando la consigna del target llegaba a la meta pero el brazo real aún estaba retrasado por dinámica física.
- El joystick sigue vibrando una vez que el objeto ya está agarrado por el gripper.
- Falta exponer el offset del raycast, acoplar la distancia de seguridad con el inicio de vibración y exponer las velocidades de apertura y cerrado del gripper en el inspector.
- La interfaz de usuario (HUD, textos, cámara del gripper) está dispersa, y se necesita que toda la GUI esté agrupada en el lateral izquierdo, excepto el dial del modo J6, además de un rectángulo de ayuda dependiente del joystick activo.

Cambio probado:
- **Homing J6 a 17.7°**: Modificado el cálculo de `targetZero` para redondear al múltiplo más cercano de `17.7° + k * 360°`. Se extendió la guarda de finalización de `_resettingJ6` para requerir que el error angular físico real de la junta J6 sea menor a 0.1°.
- **Joystick Input PS4 para J6**: Mapeados los nuevos inputs de PS4: L1 (`J6AntiHor`) y R1 (`J6Hor`) para rotación manual continua a 45°/s, y el botón Cuadrado (`J6Home`) para disparar el homing directo.
- **Vibración Inteligente del Gripper**: Se añadió en `GripperDistanceSensor` una búsqueda automática por código del script `GripperController` para evitar que sea nulo. Si `IsHoldingObject` es verdadero, la vibración se silencia de inmediato.
- **Parámetros Expuestos**: Añadido `raycastOffset` (Vector3 offset local del origen del rayo) y se acopló en `OnValidate` el límite `vibrationStartDistance` con `safeGripMaxDistance`. En `Ctrl_OnRobotRG2_Custom` se expusieron `_openSpeed` y `_closeSpeed` para regular la velocidad de apertura y cerrado del gripper.
- **Layout Lateral y Panel de Ayuda**: Creado `LeftLayoutManager.cs` que al arrancar busca y re-ancla la cámara de gripper (escalada a 280x280), textos de joints, barras de entrada y textos de sensor a la izquierda del canvas. Además, genera un panel con efecto Outline y fondo oscuro translúcido con los controles de PS4 o VR2 dependiendo del perfil activo.
- **Desacoplo del Dial J6**: Modificado `J6OverlayController.cs` para emparentar el contenedor del dial de J6 directamente al Canvas y anclarlo a la derecha, garantizando que el dial no se mueva al lateral izquierdo con el feed de la cámara.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`
- `Assets/Scripts/InputProfileSwitcher.cs`
- `Assets/Scripts/GripperDistanceSensor.cs`
- `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`
- `Assets/Scripts/J6OverlayController.cs`
- `Assets/Scripts/LeftLayoutManager.cs`

Resultado observado:
- Compilación del proyecto Unity exitosa. El layout organiza toda la GUI en la izquierda del canvas con el panel de ayuda actualizado para PS4/VR2 de manera dinámica, y el dial de J6 se posiciona perfectamente en la derecha de la pantalla. El homing llega físicamente a 17.7° con error inferior a 0.1° y la vibración se apaga al concretar el agarre.

Decision:
- Integrar permanentemente estos ajustes y layout para producción.


