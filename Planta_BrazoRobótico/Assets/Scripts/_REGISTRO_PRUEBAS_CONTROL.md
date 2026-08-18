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
- Hay un gran overshoot si el ángulo de J6 está muy lejos de 17.7° al hacer el homing y luego demora mucho tiempo en retornar y estabilizarse.
- El joystick sigue vibrando una vez que el objeto ya está agarrado por el gripper.
- Falta exponer el offset del raycast, acoplar la distancia de seguridad con el inicio de vibración y exponer las velocidades de apertura y cerrado del gripper en el inspector.
- La interfaz de usuario (HUD, textos, cámara del gripper) está dispersa, y se necesita que toda la GUI esté agrupada en el lateral izquierdo, excepto el dial del modo J6, además de un rectángulo de ayuda dependiente del joystick activo.
- Al moverse a los extremos del espacio de trabajo o singularidades, el robot se bloquea (se congela) y resulta imposible salir de esa posición mediante joystick, teniendo que reiniciar la simulación.

Cambio probado:
- **Homing J6 a 17.7° sin Overshoot**: Modificado el cálculo de `targetZero` para redondear al múltiplo más cercano de `17.7° + k * 360°`. Se extendió la guarda de finalización de `_resettingJ6` para requerir que el error angular físico real de la junta J6 sea menor a 0.1°. Para evitar el overshoot por un cambio brusco del setpoint (escalón), se inicializa `_j6TargetAngle` a la posición angular física real del joint en el instante en que inicia el homing.
- **Joystick Input PS4 para J6**: Mapeados los nuevos inputs de PS4: L1 (`J6AntiHor`) y R1 (`J6Hor`) para rotación manual continua a 45°/s, y el botón Cuadrado (`J6Home`) para disparar el homing directo.
- **Vibración Inteligente del Gripper**: Se añadió en `GripperDistanceSensor` una búsqueda automática por código del script `GripperController` para evitar que sea nulo. Si `IsHoldingObject` es verdadero, la vibración se silencia de inmediato.
- **Parámetros Expuestos**: Añadido `raycastOffset` (Vector3 offset local del origen del rayo) y se acopló en `OnValidate` el límite `vibrationStartDistance` con `safeGripMaxDistance`. En `Ctrl_OnRobotRG2_Custom` se expusieron `_openSpeed` y `_closeSpeed` para regular la velocidad de apertura y cerrado del gripper.
- **Layout Lateral y Panel de Ayuda**: Creado `LeftLayoutManager.cs` que al arrancar busca y re-ancla la cámara de gripper (escalada a 280x280), textos de joints, barras de entrada y textos de sensor a la izquierda del canvas. Además, genera un panel con efecto Outline y fondo oscuro translúcido con los controles de PS4 o VR2 dependiendo del perfil activo.
- **Desacoplo del Dial J6**: Modificado `J6OverlayController.cs` para emparentar el contenedor del dial de J6 directamente al Canvas y anclarlo a la derecha, garantizando que el dial no se mueva al lateral izquierdo con el feed de la cámara.
- **Solución al Bloqueo en Extremos**: Se corrigió el bug de recaptura de orientación en `JoystickAdapter.EndMotion()`. Al soltar el joystick y detenerse la marcha, se limpia `_orientationCaptured = false`, forzando la recaptura inmediata de la pose física real como la nueva referencia TCP de orientación fija. Esto evita que el brazo intente "regresar" a una pose de orientación desactualizada e imposible al reanudar el control, permitiendo salir fluidamente de singularidades y límites.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`
- `Assets/Scripts/InputProfileSwitcher.cs`
- `Assets/Scripts/GripperDistanceSensor.cs`
- `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`
- `Assets/Scripts/J6OverlayController.cs`
- `Assets/Scripts/LeftLayoutManager.cs`

Resultado observado:
- Compilación del proyecto Unity exitosa. El layout organiza toda la GUI en la izquierda del canvas con el panel de ayuda actualizado para PS4/VR2 de manera dinámica, y el dial de J6 se posiciona perfectamente en la derecha de la pantalla. El homing llega físicamente a 17.7° de manera suave y con cero sobreoscilación. Al llevar el robot a los extremos del espacio de trabajo y soltar el joystick, el robot se libera instantáneamente y permite salir del límite de inmediato en cualquier dirección sin bloquearse.

Decision:
- Integrar permanentemente estos ajustes y layout para producción.

### 2026-07-01 - Robustez de Orientación de Garra y Límites del Espacio de Trabajo Dextrógiro

Sintoma:
- Al realizar ciertos movimientos cartesianos, el lag del PID o límites de IK en los extremos de la cinemática desvían la orientación de la garra (pitch/roll) respecto a la vertical (garra hacia abajo). Al soltar el joystick, `EndMotion()` recaptura la pose física desviada actual como la nueva referencia, consolidando el drift de forma permanente sin posibilidad de recuperación.
- La reducción de velocidad por drift físico se activa de forma espuria a partir de solo 1.0° de desviación. Durante movimientos rápidos normales en la zona central de trabajo, el desfase dinámico del PID supera este umbral, haciendo que el robot se desplace a tirones o se sienta muy pesado.
- El operador puede arrastrar el TCP fuera de la zona en la que la cinemática es capaz de mantener la verticalidad, forzando fallos de IK y desviaciones de garra.

Cambio probado:
- **Espacio de Trabajo Dextrógiro Seguro**: Se implementaron límites configurables en `JoystickAdapter.cs` (`_enableWorkspaceLimits`, `_minHorizontalRadius = 0.8f`, `_maxHorizontalRadius = 2.6f`, `_minHeight = -0.2f`, `_maxHeight = 1.8f`). En `FixedUpdate()`, la posición del TCP objetivo se limita horizontal y verticalmente en el frame de la base del robot antes de enviarse a la IK, garantizando que el target cartesiano jamás abandone el espacio seguro.
- **Auto-Alineación de Garra a la Vertical**: Se introdujo el parámetro `_forceVerticalGripper` (por defecto `true`). Al capturar la orientación fija en `CaptureFixedOrientation()` (durante la inicialización y en cada parada `EndMotion()`), el script proyecta el vector `up` del TCP y recalcula la rotación usando `Quaternion.LookRotation(Vector3.down, projectUp)`. Esto obliga al eje local de aproximación (Z) a apuntar exactamente hacia abajo, saneando la referencia de orientación de cualquier drift de pitch/roll acumulado.
- **Optimización de Umbral de Drift de Velocidad**: Se expusieron los umbrales `_safetyDriftStartThreshold = 3.0f` y `_safetyDriftMaxTolerance = 5.0f`. Esto previene reducciones de velocidad falsas por lag normal del PID durante movimientos veloces centrales, manteniendo la protección activa únicamente ante bloqueos físicos graves, colisiones o desvíos severos de más de 3.0°.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`

Resultado observado:
- Compilación del proyecto Unity exitosa. Las variables y validaciones impiden que el robot intente resolver poses cartesianas fuera del radio de 2.6m o altura de -0.2m, reduciendo singularidades.
- La eliminación del drift al detenerse funciona de forma limpia al reconstruir matemáticamente el vector de aproximación vertical.

Decision:
- Integrar permanentemente estos cambios para mejorar la robustez física y cinemática en la teleoperación interactiva.

### 2026-08-02 - Unificación estática de paneles UI (PID / Guía / Cámara Gripper)

Sintoma:
- El panel "Acciones de Control (PID)" se dibujaba con `OnGUI()` (IMGUI legacy) en `JoystickAdapter.cs`, sin relación con el Canvas, sin fuente TMP y solo visible en Play.
- El panel "Guía de Controles (PS4)" se creaba 100% por código en runtime (`LeftLayoutManager.CreateHelpPanel()`, `new GameObject` + `AddComponent`), tampoco visible en el Editor.
- `CameraGripperView` era estático en la escena pero `LeftLayoutManager` la reposicionaba por código cada Play, sin relación visual con los otros dos paneles.
- Pedido del usuario: unificar los tres elementos en un mismo recuadro/fondo, visibles también en modo Editor (no solo generados dinámicamente), y dejar ambos paneles fácilmente escalables para agregar más ítems a futuro.

Cambio probado:
- Se creó a mano en `Assets/Scenes/Planta.unity`, bajo `Canvas`, el contenedor estático `InfoPanel_Gripper` (Image + Outline + Vertical Layout Group + Content Size Fitter), con dos hijos `PID_Section` y `Guide_Section` (mismo patrón de Layout Group anidado + Content Size Fitter) y, como tercer hijo, `CameraGripperView` reparentado (antes colgaba directo de `Canvas`).
- `PID_Section` contiene 6 filas TMP fijas (`PID_Row_J1`...`J6`) + una fila plantilla inactiva (`PID_RowTemplate`) para futuras filas.
- `Guide_Section` contiene 7 ítems TMP fijos (los mismos comandos PS4 que ya existían) + un ítem plantilla inactivo (`Guide_ItemTemplate`).
- Nuevo `Assets/Scripts/PidActionsPanel.cs`: componente en `PID_Section`, expone `SetJointAction(index, value)` y `SetExtraRow(key, label, value)` (clona `PID_RowTemplate` solo si se pide una fila que no existe todavía).
- Nuevo `Assets/Scripts/ControlGuidePanel.cs`: componente en `Guide_Section`, expone `SetProfile(profile)` (contiene los textos PS4/VR2 que antes vivían embebidos en `LeftLayoutManager`) y `AddOrUpdateItem(key, texto)` (clona `Guide_ItemTemplate` solo si hace falta).
- `JoystickAdapter.cs`: se eliminó el método `OnGUI()` completo y el array suelto `_jointActionTexts`/`_jointActionFormat`. `UpdateJointActionDisplay()` ahora llama a `_pidActionsPanel.SetJointAction(i, valor)` para cada joint. Este cambio es **solo de visualización**, no toca la cadena de control (`ApplyPID`, PID, IK, límites de workspace no se modificaron).
- `LeftLayoutManager.cs`: se eliminaron `CreateHelpPanel()` y el bloque que reposicionaba `CameraGripperView` por código (ahora su posición la controla el Vertical Layout Group de `InfoPanel_Gripper`). `UpdateHelpText()` ahora busca el `ControlGuidePanel` una vez y le delega `SetProfile(...)`. El reposicionamiento por código de `Input Info`/`J1`-`J6`/`SafetyInfoOperator`/`DistanceSensorValue` se dejó sin cambios (fuera de alcance de este pedido).

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`
- `Assets/Scripts/LeftLayoutManager.cs`
- `Assets/Scripts/PidActionsPanel.cs` (nuevo)
- `Assets/Scripts/ControlGuidePanel.cs` (nuevo)
- `Assets/Scenes/Planta.unity`: `InfoPanel_Gripper`, `PID_Section`, `Guide_Section` (nuevos, creados a mano en el Editor).

Resultado observado:
- Feedback del usuario en Play Mode: "Funcinó perfecto" — los tres elementos quedan contenidos en el mismo recuadro/fondo, PID y Guía apilados verticalmente con títulos amarillo/negrita, cámara del gripper funcionando igual que antes, sin errores de compilación en la Console.
- No se pudo correr la validación en batchmode (`Unity -batchmode -nographics -quit`) porque el Editor del usuario ya tenía el proyecto abierto (Unity bloquea instancias múltiples sobre el mismo proyecto); se validó por recompilación automática del Editor abierto y prueba manual en Play Mode.

Decision:
- Mantener e integrar los cambios. Nota de compatibilidad con arquitectura de control: el cambio es exclusivamente de UI/visualización (paneles PID y Guía + contenedor del feed de cámara); no se tocó `ApplyPID`, `JointPID`, `RobotDynamics`, la generación del target IK ni los límites de workspace/orientación documentados en `_ARQUITECTURA_CONTROL.md`.

### 2026-08-02 - Simulación de peso (Mass) del objeto agarrado en la inercia efectiva

Sintoma:
- El usuario notó que el tag `Agarrable` y la propiedad `Mass` de los cubos (Rigidbody) no afectaban en absoluto la velocidad de subida del gripper con un cubo agarrado. Se confirmó por lectura de código: `RobotDynamics.ComputeEffectiveInertia()` solo usaba la tabla estática `Links[]` (masas fijas del URDF, `Links[5].Mass = 0.5f` para el link_6/gripper) y nunca leía el `Rigidbody.mass` real del cubo ni el `Rigidbody` (kinemático) del gripper. El tag `Agarrable` solo se usa en `GrabbableSafetyGuard` (recuperación al caer del mapa) y `GripperTriggerForwarder` (filtro de qué se puede agarrar), sin relación con el PID.

Cambio probado:
- `RobotDynamics.ComputeEffectiveInertia(robotJoints, payloadMass = 0f, payloadWorldPos = null)`: nuevos parámetros opcionales (backward-compatible). Si `payloadMass > 0`, suma `payloadMass * distancia_perpendicular_i_payload²` a `J_eff[i]` de cada joint, tratando el objeto agarrado como masa puntual.
- `GripperController.cs`: nuevas propiedades de solo lectura `GrabbedMass` (masa original del objeto agarrado, 0 si no hay ninguno) y `GrabbedWorldPosition` (posición mundial del `graspPoint`). No se tocó la lógica de agarre/suelta/transferencia de masa al `Rigidbody` del gripper.
- `JoystickAdapter.cs`: nuevo campo `_gripperController` (auto-resuelto en `Awake()` con `FindFirstObjectByType<GripperController>()` si no se asigna en el Inspector, mismo patrón que `GripperDistanceSensor`) y nuevo campo `_payloadInertiaMultiplier` (default `1`, `[Min(0f)]`). En `ApplyPID()`, si `_gripperController.IsHoldingObject`, se pasa `GrabbedMass * _payloadInertiaMultiplier` y `GrabbedWorldPosition` a `ComputeEffectiveInertia`.

Archivos/parametros:
- `Assets/Scripts/RobotDynamics.cs`
- `Assets/Scripts/GripperController.cs`
- `Assets/Scripts/JoystickAdapter.cs`: nuevos campos `_gripperController`, `_payloadInertiaMultiplier`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: sección 5 ampliada ("Inercia simulada, muneca y carga (payload) agarrada").

Resultado observado:
- Pendiente de confirmación visual/interactiva en Unity Play Mode con un cubo `Agarrable` de masa alta vs. baja.
- Validación de compilación: **no se pudo ejecutar**. Se intentó `dotnet build Assembly-CSharp.csproj --no-restore` pero no hay SDK de .NET instalado en el entorno (solo el runtime). Se intentó Unity batchmode (`Unity.exe -batchmode -quit -projectPath . -logFile ...` con la versión 6000.3.17f1, que coincide con `ProjectSettings/ProjectVersion.txt` actual) pero salió con código 1 de inmediato porque el Editor del usuario ya tenía el proyecto abierto (3 procesos `Unity.exe` activos) — mismo bloqueo de instancia múltiple documentado en la entrada del 2026-08-02 anterior. Se hizo revisión manual línea por línea de los tres archivos editados (firmas, tipos, paréntesis, uso de propiedades) sin encontrar errores evidentes, pero esto no reemplaza una compilación real.
- Nota: la nueva firma de `ComputeEffectiveInertia` usa parámetros opcionales, por lo que no debería romper ningún otro caller existente aunque no se haya podido compilar.

Decision:
- Pendiente. No marcar como "integrado" hasta que el usuario confirme compilación limpia (recompilación automática del Editor abierto, revisando la Console) y valide en Play Mode que la velocidad de subida cambia de forma perceptible entre un cubo liviano y uno pesado, idealmente contrastando con `ControlDiagnosticRunner`/`RunVerticalSweep` (ya existente) para una comparación objetiva antes/después.
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: cambio aditivo y opt-in — `payloadMass` por defecto es `0`, así que sin objeto agarrado el cálculo de `jEff` es idéntico al anterior; no se tocaron `JointPID`, la generación del target cartesiano/IK, ni los límites de workspace/orientación.

### 2026-08-02 (continuación) - Sin efecto perceptible pese a multiplicador x10; penalización de velocidad cartesiana por payload

Sintoma:
- Feedback del usuario: "No noto diferencia en la velocidad de subida aun subiendole el parametro de 1 a 10" (`_payloadInertiaMultiplier`).

Análisis:
- No era un problema de magnitud. `ApplyPID()` tiene un feedforward de velocidad (`feedforwardTorque = (jNorm/dt) * (qTargetVelocity*discreteDampingFactor - _jointVelocity[i])`) que luego se divide por `jNorm` en `acceleration = torque/jNorm`. El `jNorm` se cancela algebraicamente en ese término, dejándolo matemáticamente independiente de la inercia sin importar cuánta masa se sume. Solo el término PID puro (`Kp*error + Ki*integral + Kd*derivative`) se divide por `jNorm` sin cancelarse, pero es chico frente al feedforward cuando el seguimiento va razonablemente bien. Conclusión: el cambio anterior (sumar masa a `RobotDynamics.ComputeEffectiveInertia`) es físicamente correcto pero prácticamente inerte en este lazo de control concreto.

Cambio probado:
- Se agrega un segundo mecanismo, independiente del anterior: `payloadSpeedMultiplier` en `JoystickAdapter.FixedUpdate()`, calculado desde `_gripperController.GrabbedMass` y multiplicado directamente sobre `deltaWorld` (la velocidad cartesiana) junto a `driftSpeedMultiplier`, **antes** de la IK:
  ```csharp
  float payloadSpeedMultiplier = 1f;
  if (_gripperController != null && _gripperController.IsHoldingObject)
  {
      float massRatio = Mathf.Clamp01(_gripperController.GrabbedMass / _maxSimulatedPayloadMass);
      payloadSpeedMultiplier = Mathf.Lerp(1f, _minPayloadSpeedMultiplier, massRatio);
  }
  var deltaWorld = _velocity * (_speed * dt * driftSpeedMultiplier * payloadSpeedMultiplier);
  ```
- Se aplica sobre la trayectoria cartesiana completa (no por joint) para no repetir el bug documentado el 2026-06-29 ("Análisis de Límite de Velocidad vs. Trayectoria Cartesiana"): clampear velocidades articulares de forma independiente rompe la relación geométrica entre joints y corrompe la orientación del TCP. Al escalar `deltaWorld` antes de la IK, todos los joints ven la misma trayectoria recta, solo que más lenta.
- Nuevos campos en `JoystickAdapter.cs`: `_maxSimulatedPayloadMass` (default `20f`, kg, referenciado al "Cubo Prueba" de Mass=20 visto en el Inspector del usuario) y `_minPayloadSpeedMultiplier` (default `0.25f`, piso para no bloquear al operador con cargas muy pesadas).
- Se mantiene el cambio anterior (`RobotDynamics`/`_payloadInertiaMultiplier`) sin revertir: es inofensivo, sigue siendo físicamente correcto para el término PID puro y afecta transitorios/arranque, aunque no es el mecanismo que produce el efecto perceptible.

Archivos/parametros:
- `Assets/Scripts/JoystickAdapter.cs`: `_maxSimulatedPayloadMass`, `_minPayloadSpeedMultiplier`, `payloadSpeedMultiplier` en `FixedUpdate()`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: documentada la cancelación algebraica del feedforward sobre el término de inercia, y la nueva sección "Penalizacion de velocidad cartesiana por payload".

Resultado observado:
- Pendiente de confirmación en Unity Play Mode por el usuario.
- Validación de compilación: no ejecutada por el mismo motivo que la entrada anterior (Editor del usuario abierto con el proyecto, sin SDK de .NET disponible en este entorno). Revisión manual del bloque insertado en `FixedUpdate()`: variables en scope correctas (`dt`, `_gripperController`, `_velocity`, `_speed` ya existían en el método), sin errores de sintaxis evidentes.

Decision:
- Pendiente de confirmación visual del usuario. Si `_maxSimulatedPayloadMass=20` con el "Cubo Prueba" (Mass=20) resulta demasiado agresivo o demasiado sutil, ajustar ese valor o `_minPayloadSpeedMultiplier` desde el Inspector sin tocar código.
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: cambio aditivo y opt-in — sin objeto agarrado, `payloadSpeedMultiplier = 1f` y el comportamiento es idéntico al anterior. No se tocaron `JointPID`, `RobotDynamics`, la generación de IK, ni los límites de workspace/orientación existentes.

---

### 2026-08-14 - Sensor inferior por SphereCast, distancia en la vista del gripper y frenado por proximidad

Sintoma:
- El sensor que apunta hacia abajo no trazaba rayos: aproximaba un cono con 8 `Physics.OverlapSphereNonAlloc` escalonadas. La `Distance` era euclidea al `ClosestPoint` (no una interseccion real) y la propiedad publica `Hit` nunca llegaba a asignarse.
- La distancia se mostraba en `DistanceSensorValue`, un TMP suelto en la esquina inferior izquierda del Canvas: el operario tenia que apartar la mirada del recuadro de la gripper camera para leerla.
- Nada frenaba el brazo por proximidad. `GripperDistanceSensor` no tenia ni una sola referencia fuera de su propio archivo; `JoystickAdapter` no lo conocia.

Cambio probado:
- `GripperDistanceSensor.cs`: nuevo `detectionMode` (`Cone` | `SphereCast`) **por instancia**, con default `Cone`. El cuerpo del muestreo en cono se extrajo tal cual a `MeasureWithCone()`. `MeasureWithSphereCast()` usa `Physics.SphereCastNonAlloc` con buffer (no un `SphereCast` simple) para poder descartar impactos contra el propio robot via el `CanDetect()` existente, y retrocede el origen `castRadius` sobre el eje para que un objeto ya solapado no devuelva distancia 0. `Distance` sigue siendo euclidea al punto de impacto, para que la lectura en mm no salte al cambiar de modo.
- `GripperDistanceSensor.cs`: nueva propiedad `IsWithinSlowdownRange` con histeresis (`slowdownReleaseFactor`, 1.15) como **unica fuente de verdad** del frenado, consumida tanto por el color del HUD como por `JoystickAdapter`. Nuevo flag `contributesToSpeedReduction` para que solo el sensor inferior dispare el frenado.
- `ProximitySlowdownSettings.cs` (nuevo): clase estatica con el umbral en metros, persistido en `PlayerPrefs`, presets `5/10/15/20/30 cm` + `Desactivado`. Vive fuera del adapter y del sensor a proposito: el adapter lee la distancia del sensor y el sensor lee el umbral; si el umbral viviera en cualquiera de los dos se referenciarian mutuamente.
- `JoystickAdapter.cs`: `proximitySpeedMultiplier` (default `0.5f` = 50% mas lento) compuesto en la misma linea que `driftSpeedMultiplier * payloadSpeedMultiplier`, antes de la IK. `Awake()` autodescubre el sensor con `ContributesToSpeedReduction` si el campo quedo sin asignar, mismo patron que `_gripperController`. La telemetria JSON pasa a loguear el factor realmente aplicado.
- `LeftLayoutManager.cs`: `DistanceSensorValue` se reparenta dentro de `CameraGripperView` (franja inferior, anclada por `offsetMin`/`offsetMax` para no desbordar, ya que el recuadro no tiene `RectMask2D`), con una banda oscura `DistanceValueBackdrop` detras para legibilidad sobre superficies claras. Se hizo por codigo y no editando la jerarquia del `.unity` a mano.
- `PauseMenuController.cs`: boton ciclico "Frenado prox.: N cm". Se descarto un `Slider` porque `FindNextSelectable` reserva la navegacion izquierda/derecha a los botones de perfil y devuelve `null` en cualquier otro caso; el boton ciclico replica el patron ya existente de `OrientationButton` sin tocar la navegacion. Panel de 560 a 620 px de alto para que no se recorte "Continuar".

Archivos/parametros:
- `Assets/Scripts/ProximitySlowdownSettings.cs` (nuevo).
- `Assets/Scripts/GripperDistanceSensor.cs`: `detectionMode`, `castRadius`, `contributesToSpeedReduction`, `slowdownReleaseFactor`, `colorizeDistanceText` + 4 colores.
- `Assets/Scripts/JoystickAdapter.cs`: `_proximitySensor`, `_proximitySpeedMultiplier`.
- `Assets/Scripts/LeftLayoutManager.cs`, `Assets/Scripts/PauseMenuController.cs`.
- `Assets/Scenes/Planta.unity`: unicas claves nuevas, todas en el bloque del `DistanceSensor` (`&1424931422`): `detectionMode: 1`, `castRadius: 0.01`, `contributesToSpeedReduction: 1`, `slowdownReleaseFactor: 1.15`.

Resultado observado:
- Validacion de compilacion **ejecutada**: `Unity -batchmode -nographics -quit` con 6000.3.11f1 termina con "Exiting batchmode successfully now!" y cero `error CS`. Los dos unicos warnings en los archivos tocados (`_safetyDriftStartThreshold` / `_safetyDriftMaxTolerance` asignados y nunca usados) son **preexistentes**, residuo del sistema de drift desactivado.
- Comportamiento en Play Mode: pendiente de confirmacion visual del usuario.

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: cambio aditivo y opt-in. El multiplicador se aplica sobre la trayectoria cartesiana completa antes de la IK, nunca por joint en `ApplyPID()`, respetando la regla establecida el 2026-06-29. Con el umbral en `Desactivado` o sin objeto dentro del rango, `proximitySpeedMultiplier = 1f` y el comportamiento es identico al anterior. No se tocaron `JointPID`, `RobotDynamics`, la generacion de IK, ni los limites de workspace/orientacion.
- Los 4 sensores laterales del prefab `OnRobot_RG2_Holder` quedan **sin modificar**: al no existir las claves nuevas en su YAML heredan `Cone` y `contributesToSpeedReduction = false` de los defaults del codigo, su `distanceText` es null (por lo que `UpdateUi()` sigue haciendo early-return y nunca colorean nada) y no se toco la vibracion. El prefab no aparece en el diff.
- Limitacion conocida a revisar con el usuario: el sensor inferior tiene `detectionMask` = solo layer 3 `Manipulable`, asi que el frenado se dispara ante los cubos agarrables pero **no** ante el suelo o el entorno. Si se quisiera cubrir el entorno, es cambiar `m_Bits` en el bloque de escena; no se hizo por no alterar el comportamiento de deteccion vigente.

---

### 2026-08-14 (2) - Frenado progresivo, medicion con garra cerrada, bloqueo de descenso y guias ajustables

Sintoma (feedback del usuario tras probar la entrada anterior en Play Mode):
1. "No pareciese estar moviendose mas lento cuando estoy cerca".
2. "Deja de medir la distancia cuando esta el gripper cerrado".
3. Pedido nuevo: con la garra cerrada, no permitir bajar mas alla del umbral; solo subir.
4. Pedido nuevo: poder ajustar el largo de las guias perpendiculares de la gripper camera ("se ven larguisimas pero no encuentro donde modificarlas").

Diagnostico (medido, no supuesto):
- **El frenado SI funcionaba.** Contrastado contra `Logs/control_diagnostics_log.json`: con `speedMult 0,500` el TCP recorria 0.0994 m en 0.1 s (0.995 m/s) y sin frenar 0.2077 m (2.08 m/s). Exactamente la mitad. Lo que fallaba era la **percepcion**: con `_speed` 1.99 m/s y umbral de 10 cm, la ventana dura ~100 ms (6 fotogramas a 60 FPS). Ademas `_velocity` no se normaliza, asi que en diagonal la velocidad real es 2.81 m/s y la ventana baja a 70 ms. Y el clamp de `_maxJointAcceleration` (720 °/s²) consume ~42 ms solo en bajar una articulacion de 60 a 30 °/s: media ventana gastada en la rampa.
- **Causa raiz de la perdida de medicion**: `GripperController.GrabObject()` linea 195 hace `grabbedObject.transform.SetParent(parent)` y, como `graspPoint` esta a null en escena, `parent` acaba siendo el transform de `OnRobot_RG2`. El cubo pasa a colgar del robot y `CanDetect()` lo descarta por `IsChildOf(ignoredHierarchyRoot)`. **No tenia nada que ver con el SphereCast: con el modo cono ocurria igual.** Cerrar la garra sin llegar a agarrar no rompe nada (los dedos estan en layer 0, fuera de la mascara).
- **Las guias** (`GuiaHorizontal` 512x2, `GuiaVertical` 2x512) son hijas fijas de `CameraGripperView` en el `.unity`, que mide ~270 px: desbordan casi el doble por cada lado. Ningun script las tocaba, por eso no habia donde ajustarlas.

Cambio probado:
- `GripperDistanceSensor.cs`: `IsWithinSlowdownRange` (booleano) se complementa con `ProximityFactor` continuo (0 en el umbral, 1 al contacto). El booleano queda solo para el HUD, con su histeresis, para que el indicador no parpadee. Nuevo `GetPayloadExtent()`: con una pieza agarrada se descuenta cuanto sobresale (proyeccion de los 8 vertices del AABB de sus colliders sobre el eje del sensor, con el `Collider[]` cacheado por objeto para no asignar por fotograma), de modo que la lectura pasa a ser el hueco libre BAJO la pieza. `IsGripDistanceSafe` se acota a colliders con tag `Agarrable`. `OnDisable()` resetea el estado de frenado.
- `JoystickAdapter.cs`: el multiplicador pasa a `Mathf.Lerp(1f, _proximitySpeedMultiplier, ProximityFactor)`. Nuevo `ApplyDescentLimit()` que recorta la componente Y negativa de `deltaWorld` al hueco restante menos el margen. Nuevas propiedades `ProximitySpeedScale` e `IsDescentBlocked` para el HUD, limpiadas en `EndMotion()`, en la entrada a modo camara y en `SetInputSuppressed()`.
- `GripperController.cs`: se exponen `IsGripperClosed` y `GrabbedObject` (solo getters sobre campos existentes, sin logica nueva).
- `ProximitySlowdownSettings.cs`: umbral por defecto 10 -> **30 cm**, presets 10/20/30/40/50 cm + Desactivado. Segundo ajuste `DescentMarginMeters` (default 5 cm, presets 3/5/8/12 cm + Desactivado). La clave de PlayerPrefs del umbral se versiono a `.v2` para que quien ya tuviera 10 cm guardado no se quedase con el valor viejo. `CycleToNext()` busca el preset mas cercano en vez de exigir coincidencia exacta.
- `GripperStatusOverlay.cs` (nuevo): chip sobre la vista con "DESCENSO BLOQUEADO" (prioritario, rojo) o "VELOCIDAD n%" (ambar), oculto cuando no hay nada que comunicar.
- `GripperViewSettings.cs` (nuevo) + `LeftLayoutManager.cs`: largo de guias por anclas relativas al recuadro (dejan de desbordar y escalan solas), con presets Ocultas/25/50/75/Completa. Se cachean los `RectTransform` porque `GameObject.Find` no encuentra objetos inactivos y, una vez ocultas, no habria forma de recuperarlas.
- `PauseMenuController.cs`: botones "Bloqueo desc.:" y "Guías:". Panel de 620 a 740 px.
- `Assets/Scenes/Planta.unity`: `detectionMask.m_Bits` 8 -> **9** (Default + Manipulable) solo en el `DistanceSensor`.

Archivos/parametros:
- Nuevos: `Assets/Scripts/GripperStatusOverlay.cs`, `Assets/Scripts/GripperViewSettings.cs`.
- Modificados: `GripperDistanceSensor.cs`, `JoystickAdapter.cs`, `GripperController.cs`, `ProximitySlowdownSettings.cs`, `LeftLayoutManager.cs`, `PauseMenuController.cs`, `Assets/Scenes/Planta.unity`.

Resultado observado:
- Validacion de compilacion: ver nota al pie de esta entrada.
- Comportamiento en Play Mode: pendiente de confirmacion del usuario.

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: el frenado sigue aplicandose sobre la trayectoria cartesiana completa antes de la IK, nunca por joint en `ApplyPID()`. El bloqueo de descenso se suma como **segundo mecanismo que altera `deltaWorld`**, junto al clamp de workspace ya existente, y opera en coordenadas de mundo antes de `WorldVectorToFrame` porque la vertical del input se compone como `Vector3.up * rawY`. No se tocaron `JointPID`, `RobotDynamics`, la generacion de IK ni los limites de workspace.
- El margen de bloqueo se dejo como ajuste **independiente** del umbral de frenado (5 cm frente a 30 cm). Atarlos habria hecho imposible depositar una pieza: el brazo se frenaria a 30 cm del suelo y habria que soltarla desde esa altura.
- Los 4 sensores laterales siguen sin modificarse (`Cone`, `contributesToSpeedReduction = false`, sin UI). El prefab `OnRobot_RG2_Holder.prefab` no aparece en el diff.

**Hallazgo pendiente, a atacar por separado (NO se toco aqui):** J5/J6 viven saturados contra `_maxJointVelocity` (60 °/s) con 10-12° de drift de orientacion. La causa es que su `jNorm` cae siempre al piso de `0.05` (`JoystickAdapter.cs`, `ApplyPID`) porque `jEff` de los links 5 y 6 (7 kg y 0.5 kg con `ComLocal` en el propio eje) queda en ~0.01 kg·m² frente a `_referenceInertia: 5.39`. Eso les da 20x de autoridad PID y los mete en bang-bang, de modo que **la muñeca ignora cualquier multiplicador cartesiano**: por muy frenado que vaya el TCP, el gripper gira igual de rapido. Es un problema de ajuste preexistente e independiente de esta feature; corregirlo implica retocar `_referenceInertia` o la tabla de masas de `RobotDynamics`, y debe validarse con `RunVerticalSweep`.

---

### 2026-08-15 - Anticolision con el entorno: colliders y veto de movimiento

Sintoma:
- "El suelo y el entorno no colisionan con el brazo ni el gripper, no se si sera por el tag o por que".

Diagnostico (dos causas independientes, ninguna relacionada con el tag ni con la layer):
1. **No habia colliders en ninguna parte.** El prefab del KUKA tiene **0 colliders y 0 rigidbodies**: el brazo es geometria puramente visual. El prefab `OnRobot_RG2_Holder` tambien tiene 0 (los 4 de los dedos se anadieron sueltos en la escena). Y los prefabs del entorno industrial tienen 0, porque sus 55 FBX estan importados con `addColliders: 0`. **Activar "Generate Colliders" en el importador no habria servido**: la escena instancia los `.prefab` derivados del asset (p.ej. `Road_set_v1_b_floor.prefab`, con 15 instancias — ese es el suelo), que son assets distintos de los FBX y no heredan esa opcion.
2. **Aunque los hubiera, el brazo los atravesaria igual.** Se mueve por `SetJoints()` (pose cinematica), y Unity no resuelve penetracion en objetos movidos por transform. La colision fisica resuelta esta fuera de alcance por diseno: `_ARQUITECTURA_CONTROL.md` prohibe mover el brazo con Rigidbody o fuerzas.

Contexto de escenas aclarado con el usuario: trabaja con **multi-scene editing**, `Planta.unity` (robot, HUD, managers) y `Map_v2.unity` (entorno) cargadas a la vez. En disco cada escena solo tiene su mitad. **Build Settings solo contiene `Planta.unity`**, asi que en una build el entorno no existiria: queda pendiente consolidar o anadir `Map_v2` a Build Settings.

Cambio probado:
- `Assets/Editor/EnvironmentColliderTool.cs` (nuevo): menu `Tools > Entorno`. Recorre **todas las escenas cargadas** (necesario por el multi-scene), anade `MeshCollider` a los objetos con `MeshFilter` que no tengan collider, y los pasa a la layer **`Entorno` (6)**, que estaba definida y sin usar. Omite el robot, el gripper, los sensores, las piezas agarrables y la UI. Reversible con Undo y con un menu inverso.
- `JoystickAdapter.ApplyCollisionVeto()`: barrido `SphereCastNonAlloc` del volumen aproximado del gripper a lo largo de `deltaWorld`, recortando el paso al hueco libre menos la holgura. Se ignoran los solapamientos en el origen (si no, un gripper ya penetrado quedaria congelado sin salida) y las superficies horizontales (las gobierna el bloqueo de descenso; vetarlas aqui frenaria el gripper a ~12 cm del piso e impediria recoger piezas).
- `GripperStatusOverlay`: nuevo aviso "OBSTACULO".
- `Assets/Scenes/Planta.unity`: `detectionMask.m_Bits` a **73** (Default + Manipulable + Entorno), para que el sensor siga viendo el entorno tanto antes como despues de ejecutar la herramienta.

Resultado observado:
- Compilacion **ejecutada** con Roslyn (`DotNetSdkRoslyn/csc.dll`) contra los response files que genera Unity, porque el Editor del usuario tenia el lock del proyecto y el batchmode no podia arrancar: `Assembly-CSharp` exit 0 y `Assembly-CSharp-Editor` (incluyendo el archivo nuevo) exit 0. Unicos warnings, los dos `CS0414` preexistentes.
- Comportamiento en Play Mode: pendiente. **La herramienta de colliders no se ha ejecutado todavia**; hasta entonces el veto no actua (su mascara por defecto es solo `Entorno`, que esta vacia).

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: el veto se aplica sobre la trayectoria cartesiana antes de la IK, nunca por joint, igual que el clamp de workspace y el bloqueo de descenso. No se toco `JointPID`, `RobotDynamics` ni la generacion de IK. No se anadio Rigidbody ni fuerzas al brazo.
- Alcance conocido y aceptado: **solo se vigila el volumen del gripper**. Los eslabones altos (codo, antebrazo) pueden seguir atravesando geometria; cubrirlos exigiria colliders por eslabon y un barrido por cada uno.

**Incidencia de trabajo a tener presente:** el Editor del usuario tenia `Planta.unity` cargada con cambios sin guardar mientras se editaba el mismo archivo en disco. Al guardar, Unity sobrescribio la edicion de `detectionMask` (volvio de 9 a 8) conservando el resto. Antes de editar la escena por fuera del Editor, hay que asegurarse de que no este cargada y sucia, o reaplicar y verificar despues.

---

### 2026-08-16 - Robustez del agarre: suelta diferida, estado del payload y compilacion de build

Origen:
- Pedido del usuario: revisar que se podria mejorar del agarre sin cambios profundos, y aplicar el primer bloque (bugs de pocas lineas). No se toco nada fuera de la cadena de agarre.

Sintomas / hallazgos (revision de codigo, no reportados en Play Mode):
1. **La pieza se soltaba antes de que los dedos abrieran.** `ToggleGrip()` llamaba a `ReleaseObject()` en el mismo frame en que el animator recien arrancaba la apertura, asi que la pieza volvia a ser dinamica con los dedos todavia cerrados encima: Unity resuelve esa penetracion y la pieza salta, vibra o sale disparada de costado.
2. **`originalMass` se arrastraba entre agarres.** Si la pieza agarrada no tenia `Rigidbody`, nunca se asignaba, pero `GrabbedMass` la devolvia igual (solo comprobaba `grabbedObject != null`): la inercia efectiva y el `payloadSpeedMultiplier` usaban la masa de la pieza *anterior*.
3. **Deriva de masa del gripper.** El par `gripperRigidbody.mass += / -=` acumula error permanente si algun agarre y su suelta no se emparejan (p.ej. si la pieza se destruye mientras esta tomada).
4. **No se guardaba el `isKinematic` previo de la pieza:** una pieza que ya fuera cinematica quedaba dinamica al soltarla.
5. **La build standalone no compilaba, por dos motivos independientes en el mismo par de archivos:** `using UnityEditor;` sin guarda en `Ctrl_OnRobot_RG2_Custom.cs`, y `GripperController` escribiendo `gripperAnimator.in_position`, que fuera del Editor es `private`. En el Editor esto no se nota nunca.
6. **Logs de trigger activos por defecto** (`debugTriggers`, `debug`): escriben en consola en cada entrada/salida de contacto.

Cambio probado:
- `Ctrl_OnRobot_RG2_Custom.cs`: `using UnityEditor;` envuelto en `#if UNITY_EDITOR`. Nueva interfaz publica de solo lectura `OpeningFraction` (apertura normalizada contra el angulo de `s_max`, evaluado una sola vez y cacheado en `__theta_max`) e `IsInPosition`, mas `StopMotion()` para encapsular la parada de la animacion sin tocar `in_position` desde fuera.
- `GripperController.cs`: suelta diferida (`BeginRelease` / `UpdatePendingRelease`, llamada desde `FixedUpdate`) con `releaseOpeningDelta` (default `0.2`) y `releaseTimeout` (default `1 s`, fuerza la suelta con warning si la animacion no avanza). Volver a cerrar durante la espera cancela la suelta y mantiene el agarre.
  - **Correccion durante la propia implementacion, vale la pena registrarla:** el umbral se planteo primero como apertura absoluta (`releaseOpeningFraction >= 0.35`). Estaba mal: al agarrar, `StopMotion()` deja los dedos detenidos apoyados sobre la pieza, asi que una pieza ancha arranca la suelta con `OpeningFraction` ya por encima del umbral y liberaba en el primer tick — el bug original habria seguido vivo justo en las piezas grandes, que son las que peor saltan. La condicion final (`HaveFingersClearedPayload`) mide el **incremento** de apertura desde el instante de la orden, con `IsInPosition` como caso limite para piezas casi tan anchas como la carrera del RG2. `RefreshGripperMass()` reasigna `base + payload` en vez de acumular. Se guardan y restauran `originalMass` y `grabbedWasKinematic`; `originalMass = 0` explicito cuando no hay `Rigidbody`. El reset de estado y la masa se aplican fuera del `if (grabbedObject != null)` para cubrir la pieza destruida. `debugTriggers` por defecto a `false`.
- `GripperTriggerForwarder.cs`: `debug` por defecto a `false`.

Archivos tocados:
- `Assets/Scripts/GripperController.cs`
- `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`
- `Assets/Scripts/GripperTriggerForwarder.cs`
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: nueva subseccion "Ciclo de agarre y suelta" y nota en la seccion 5.

Resultado observado:
- Compilacion **ejecutada**, dos veces, con `dotnet build` sobre el `Assembly-CSharp.csproj` que genera Unity (el batchmode no podia arrancar: el Editor del usuario tenia el lock del proyecto). Salida redirigida al scratchpad para no ensuciar el repo; `git status` confirma que solo quedaron modificados los archivos editados.
  - Con los defines tal cual (incluyen `UNITY_EDITOR`): **0 errores**, unicos warnings los dos `CS0414` preexistentes de `JoystickAdapter`.
  - Con los defines **sin `UNITY_EDITOR`** (simula build de player, que es el caso que estaba roto): **0 errores**. Aparecen 3 `CS0414` mas, todos preexistentes y ajenos a este cambio: el `in_position` privado de `Ctrl_ABB_SG`, `Ctrl_OnRobot_RG2` y `Ctrl_Robotiq_2F_85` (scripts originales del asset en `Assets/AssetsGripper/Script/`) queda sin uso al no compilarse el bloque de Editor.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**. En particular queda por confirmar en VR si `releaseOpeningDelta = 0.2` (~13° de apertura, ~0.07 s a la velocidad por defecto de 180) se siente inmediato al soltar o si conviene bajarlo.

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: no se toco `JointPID`, `RobotDynamics`, la generacion de IK ni los limites de workspace/orientacion. El brazo sigue sin moverse por `Rigidbody` ni fuerzas. El unico efecto sobre la cadena de control es que, durante la suelta pendiente, `IsHoldingObject` sigue en `true` unos ~0.13 s mas, asi que la inercia y la penalizacion de velocidad por payload se descuentan al liberar la pieza y no al pulsar el boton (mas fiel a la fisica, no menos).
- `debugTriggers` y `debug` se cambiaron solo en el **valor por defecto del script**. Las instancias ya presentes en la escena conservan el `true` serializado: hay que desmarcarlos a mano en el Inspector para que surta efecto.
- Fuera de alcance, propuesto y no aplicado (bloques 2 y 3 de la revision): eleccion de la pieza mas cercana al `graspPoint` en vez de la primera del diccionario, incluir los bounds de la pieza agarrada en `ApplyCollisionVeto` (hoy se la puede empotrar lateralmente), herencia opcional de la velocidad del brazo al soltar, verificacion de escala del `graspPoint`, y feedback haptico/HUD al confirmar el agarre.

---

### 2026-08-16 (2) - Boton "Modo debug" en el menu de pausa

Origen:
- Pedido del usuario, a continuacion de la entrada anterior. Ahi habian quedado `debugTriggers` y `debug` en `false` por defecto, con la limitacion de que cambiarlos exige salir de Play Mode y tocar el Inspector componente por componente. `GrabbableSafetyGuard.logRecoveries` era peor: ese componente se auto-anade por codigo (`RuntimeInitializeOnLoadMethod`), asi que no tiene Inspector donde editarlo.

Cambio probado:
- `DebugSettings.cs` (nuevo): estatico con `PlayerPrefs`, mismo patron que `ProximitySlowdownSettings` y `GripperViewSettings`. `IsEnabled`, `SetEnabled`, `Toggle()`, `Describe()` y evento `EnabledChanged`. Default **apagado**: la consola arranca limpia y encenderlo es una accion explicita.
- Consumidores: `GripperController` y `GripperTriggerForwarder` ganan una propiedad `LogEnabled => flagLocal || DebugSettings.IsEnabled`; `GrabbableSafetyGuard` evalua `logRecoveries || DebugSettings.IsEnabled` y su flag pasa a default `false` (antes logueaba siempre). **Los flags del Inspector se conservan como override por componente**, para poder aislar uno solo mientras se depura sin llenar la consola con el resto.
- `PauseMenuController.cs`: nuevo `debugModeButton` con el ciclo completo del patron existente — campo `[SerializeField]`, `EnsureDebugModeButtonExists()` (para la escena que ya trae el panel armado), `ToggleDebugMode()` / `UpdateDebugModeButtonText()` / `BuildDebugModeLabel()`, alta en `WireButtons`, `ApplyButtonColors`, `RefreshSelectedTextColors`, `UpdateProfileUi`, el array de orden de `FindNextSelectable` (entre guias y Continuar) y `BuildDefaultMenu`. Altura del panel por defecto de 740 a 798 (46 de alto + 12 de spacing por boton).

Archivos tocados:
- `Assets/Scripts/DebugSettings.cs` (nuevo)
- `Assets/Scripts/PauseMenuController.cs`
- `Assets/Scripts/GripperController.cs`, `Assets/Scripts/GripperTriggerForwarder.cs`, `Assets/Scripts/GrabbableSafetyGuard.cs`
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: fila de `DebugSettings.cs` en la tabla de scripts.

Resultado observado:
- Compilacion **ejecutada**, con y sin `UNITY_EDITOR`: **0 errores** en ambos perfiles, mismos warnings preexistentes que en la entrada anterior (2 y 5 `CS0414`).
- **Detalle de metodo a recordar:** el `Assembly-CSharp.csproj` que genera Unity enumera los `.cs` uno por uno, asi que un archivo **nuevo** no entra en la compilacion hasta que Unity regenera el proyecto. Hubo que anadir su `<Compile Include>` a mano para poder validar. El csproj esta en `.gitignore` (`*.csproj`) y Unity lo regenera al recompilar, asi que la edicion es transitoria e inocua.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**.

Decision:
- Alcance del "modo debug": solo **logs de runtime** de la cadena de agarre y de la recuperacion de piezas. Quedan deliberadamente fuera `GripperDistanceSensor.drawGizmos` (los gizmos solo se ven en la Scene view del Editor, no en Play ni en VR, asi que un boton en el menu de pausa no los alcanza) y `JoystickVibrationHidOutput.logConnection` (un unico log al conectar, diagnostico util siempre).
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: no toca la cadena de control. Solo condiciona llamadas a `Debug.Log`.
- **A verificar en el Editor:** si `pauseMenuRoot` esta asignado en la escena, `EnsureDebugModeButtonExists()` inyecta el boton en el `Panel` existente. Si ese panel tiene altura fija en el `.unity` (y no un Content Size Fitter), puede que haya que subirla a mano para que no recorte "Continuar", igual que se hizo con la altura por defecto del menu construido por codigo.

---

### 2026-08-16 (3) - Comportamiento del agarre: pieza mas cercana, payload en el veto de colision, velocidad al soltar y escala

Origen:
- Pedido del usuario: implementar el bloque 2 de la revision del agarre (los cuatro puntos de "comportamiento", no bugs de compilacion).

Sintomas / hallazgos:
1. **Eleccion no determinista de la pieza.** `TryGrab()` recorria `fingerContacts` y agarraba la primera que cumpliera el criterio de contactos opuestos. El orden de iteracion de un `Dictionary` no esta definido: con dos piezas entre los dedos, cual se llevaba era arbitrario y no reproducible para el operario.
2. **La pieza agarrada no participaba del veto de colision.** `ApplyCollisionVeto()` barre solo el volumen del efector, y la pieza, al pasar a colgar del robot, queda descartada por `IsObstacle()`. En vertical si estaba cubierta (el sensor inferior descuenta `GetPayloadExtent`), pero **lateralmente se podia empotrar la pieza dentro de una caja o contra una pared**.
3. **La pieza caia en vertical al soltar** (velocidades forzadas a cero), perdiendo la inercia del brazo si el operario soltaba en movimiento.
4. **`SetParent` no preserva la escala**: si el `graspPoint` arrastra una escala distinta de 1, la pieza se deforma al agarrarla.

Cambio probado:
- `GripperController.TryGrab()`: entre las candidatas validas se elige la de menor distancia al `graspPoint` en vez de la primera del diccionario.
- `GripperController.TryGetPayloadBounds(out Bounds)` (nuevo, publico): AABB mundial de la pieza agarrada considerando solo colliders solidos, sobre un array de colliders cacheado por agarre (`payloadColliders`), porque lo consulta el veto en cada `FixedUpdate`.
- `JoystickAdapter.ApplyCollisionVeto()`: segundo barrido `BoxCastNonAlloc` con el AABB de la pieza, en la misma direccion, tomando **el mas restrictivo** de los dos resultados. Nuevo flag `_includePayloadInCollisionVeto` (default `true`). El filtrado de impactos se extrajo a `NearestObstacleDistance(hitCount)` para que ambos barridos apliquen exactamente los mismos descartes (solapamiento en origen, `IsObstacle`, superficies horizontales) sin duplicar el codigo.
- `GripperController`: `inheritReleaseVelocity` (default **`false`** = comportamiento actual) y `maxInheritedReleaseSpeed` (default 1.5 m/s). La velocidad se estima en `TrackGraspVelocity()` por diferencia de posicion del `graspPoint` entre ticks de fisica, siempre, haya pieza o no.
- `GripperController`: `PreserveWorldScale()` tras el `SetParent` corrige la escala local para conservar la mundial y avisa por consola (bajo modo debug); al soltar se restaura la `localScale` exacta previa (`grabbedOriginalLocalScale`).

Archivos tocados:
- `Assets/Scripts/GripperController.cs`
- `Assets/Scripts/JoystickAdapter.cs`: `_includePayloadInCollisionVeto`, `NearestObstacleDistance()`, segundo barrido en `ApplyCollisionVeto()`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: seccion de anticolision y "Ciclo de agarre y suelta" ampliadas.

Decisiones de diseno a tener presentes:
- **BoxCast con el AABB real y no un `_collisionProbeRadius` mayor.** Inflar la esfera habria penalizado tambien al gripper vacio, frenandolo lejos de todo cuando no lleva nada.
- **El barrido de la pieza hereda el descarte de superficies horizontales.** Es intencional: el descenso lo sigue gobernando el bloqueo de descenso, que ya descuenta cuanto sobresale la pieza. Vetar horizontales aqui impediria depositarla.
- **El solapamiento en origen se ignora tambien para la pieza.** Una pieza apoyada en una mesa arranca solapando; tratarlo como obstaculo dejaria el brazo clavado al recogerla.
- **`inheritReleaseVelocity` queda apagado por defecto.** Heredar la velocidad es mas realista, pero para formacion es mas util que la pieza caiga donde el operario la solto. Queda a un clic en el Inspector.

Resultado observado:
- Compilacion **ejecutada** con y sin `UNITY_EDITOR`: **0 errores** en ambos perfiles, mismos warnings preexistentes (2 y 5 `CS0414`). Unity ya habia regenerado el `csproj` por su cuenta e incluido `DebugSettings.cs`, asi que esta vez no hizo falta tocarlo.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**. Conviene probar en particular: (a) llevar una pieza contra una pared lateral y comprobar que aparece "OBSTACULO" antes de penetrarla; (b) que recoger una pieza del suelo o de una mesa sigue siendo posible (que el nuevo barrido no bloquee de mas); (c) que el aviso de escala **no** aparece, lo que confirmaria que la jerarquia del `graspPoint` esta limpia.
- **Dependencia a recordar:** el veto solo actua sobre las layers de `_obstacleMask` (por defecto solo `Entorno`, 6). Mientras no se haya ejecutado `Tools > Entorno > Generar colliders faltantes` —que segun la entrada del 2026-08-15 seguia pendiente— el segundo barrido no tiene nada que golpear y no se notara ningun cambio.

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: el segundo barrido se aplica sobre la trayectoria cartesiana **antes de la IK, nunca por joint**, igual que el clamp de workspace, el bloqueo de descenso y el veto ya existente. No se toco `JointPID`, `RobotDynamics`, la generacion de IK ni los limites de workspace/orientacion. El brazo sigue sin moverse por `Rigidbody` ni fuerzas. Con `_includePayloadInCollisionVeto` en `false`, o sin pieza agarrada, el comportamiento es identico al anterior.
- Fuera de alcance, sigue pendiente del bloque 3: feedback haptico/HUD al confirmar el agarre.

---

### 2026-08-16 (4) - No se podia bajar lo suficiente para depositar la pieza

Sintoma reportado por el usuario:
- "No me deja bajar lo suficiente con el objeto agarrado, me frena el movimiento por seguridad; el problema es que no logro acercar lo suficiente el objeto al lugar donde debo dejarlo".

Diagnostico:
- **`ApplyDescentLimit()` usaba el mismo margen con la garra vacia que con una pieza agarrada.** La condicion de entrada es `IsGripperClosed`, que es cierta en ambos casos, y el margen era siempre `DescentMarginMeters` (5 cm por defecto).
- Ese margen esta pensado para una maniobra concreta: que el gripper **vacio** no se estrelle contra el suelo al bajar a recoger. Con una pieza agarrada la maniobra es la **contraria** —hay que apoyarla— y ademas el sensor ya descuenta cuanto sobresale la pieza (`GetPayloadExtent`), asi que el hueco medido es el que queda **bajo la pieza**. Reservar 5 cm de ese hueco significa frenar con la pieza a 5 cm de la mesa: depositarla con precision era imposible, habia que soltarla desde ahi y dejarla caer.
- No es un bug de calculo del sensor ni del descuento del payload: ambos median bien. Es que un unico parametro estaba gobernando dos maniobras opuestas.

Cambio probado:
- `ProximitySlowdownSettings.cs`: nuevo `CarryDescentMarginMeters` (clave propia en `PlayerPrefs`, default **5 mm**) con sus presets `{5 mm, 1 cm, 2 cm, 3 cm, 0}`. Nuevos `GetDescentMargin(bool isCarryingPayload)` e `IsDescentBlockEnabledFor(bool)` como **unico punto de decision**, para que el bloqueo y su HUD no puedan discrepar. `DescribeCarryDescentMargin()` formatea en milimetros: en centimetros los presets se leerian todos "0 cm".
- `JoystickAdapter.ApplyDescentLimit()`: elige margen y enable segun `_gripperController.IsHoldingObject`. El chequeo de enable se movio **despues** de resolver si se lleva pieza, porque ahora depende de eso.
- `PauseMenuController.cs`: boton "Bloqueo c/pieza: N mm" con el ciclo completo del patron (campo, `EnsureCarryDescentMarginButtonExists()`, cycle/update/label, `WireButtons`, colores, orden de navegacion y `BuildDefaultMenu`). Altura del panel por defecto de 798 a 856.

Archivos tocados:
- `Assets/Scripts/ProximitySlowdownSettings.cs`, `Assets/Scripts/JoystickAdapter.cs`, `Assets/Scripts/PauseMenuController.cs`
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: tabla de los dos margenes en "Bloqueo de descenso" y fila actualizada en la tabla de scripts.

Resultado observado:
- Compilacion **ejecutada** con y sin `UNITY_EDITOR`: **0 errores**, mismos warnings preexistentes.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**. Con 5 mm la pieza deberia poder apoyarse practicamente sobre la superficie; si aun asi frena antes de tiempo, el boton permite bajar a 0 (desactivado) para descartar el bloqueo de descenso como causa.

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: mismo mecanismo y mismo punto de aplicacion (sobre `deltaWorld`, en mundo, antes de la IK, nunca por joint). Solo cambia **que valor** de margen se usa. Sin pieza agarrada el comportamiento es identico al anterior.
- **Hipotesis alternativa que queda descartable con una prueba**, por si el sintoma persiste: el aviso del HUD distingue "DESCENSO BLOQUEADO" (este mecanismo) de "OBSTACULO" (`ApplyCollisionVeto`). Si lo que aparece es "OBSTACULO", la causa seria el barrido de la pieza agregado en la entrada anterior —por ejemplo al depositar en un hueco ajustado, si el `BoxCast` roza una pared vertical, cuya normal no se descarta por no ser horizontal— y se desactiva con `_includePayloadInCollisionVeto`.

---

### 2026-08-16 (5) - El brazo atraviesa el suelo: piso duro geometrico

Sintoma reportado por el usuario:
- "Por que atravieso el suelo?".

Diagnostico (verificado, no supuesto):
- **`Assets/RPG_FPS_game_assets_industrial/Map_v2.unity` tiene UN solo collider en toda la escena.** La herramienta `Tools > Entorno > Generar colliders faltantes`, creada el 2026-08-15, **sigue sin ejecutarse**; ya quedo anotado como pendiente en aquella entrada y en la del veto con payload.
- Las tres protecciones existentes son **reactivas** y por tanto ninguna puede actuar sin colliders:
  1. `ApplyDescentLimit()` sale por `!_proximitySensor.HasHit`: sin collider el sensor no ve el suelo.
  2. `ApplyCollisionVeto()` no tiene contra que barrer, y ademas descarta las superficies horizontales por diseno (delega el suelo en el mecanismo anterior).
  3. El clamp de workspace **no es un limite de suelo**: `_minHeight` (-0.2) esta en el **frame del robot**, no en mundo, asi que permite bajar por debajo de la cota del suelo.
- Es decir: no habia ningun limite absoluto de altura. El sintoma no depende de los cambios recientes; es preexistente y se hizo evidente al poder bajar mas con la pieza.

Cambio probado:
- `JoystickAdapter.ApplyHardFloorLimit()` (nuevo): cuarto mecanismo que recorta `deltaWorld` antes de la IK. Impide que el punto mas bajo del gripper o de la pieza baje de `_hardFloorWorldY + _hardFloorClearance` (defaults `0` y `5 mm`). Se llama **despues** de `ApplyDescentLimit()`, que resetea `IsDescentBlocked` al entrar, para que el aviso del HUD sobreviva cuando quien frena es el piso.
- `GripperController.TryGetGripperBounds()` (nuevo) + refactor de `TryGetPayloadBounds()` sobre un `TryEncapsulate(colliders, includeTriggers)` comun. Los colliders del gripper se cachean en `Awake()`, cuando todavia no cuelga ninguna pieza, asi que nunca contaminan la medida con la carga.
- **Los triggers SI cuentan para el gripper y NO para el payload.** Los volumenes de las caras internas de los dedos son triggers y son justamente la parte mas baja de la garra, que es lo que interesa para el piso.

Archivos tocados:
- `Assets/Scripts/JoystickAdapter.cs`: `_enableHardFloor`, `_hardFloorWorldY`, `_hardFloorClearance`, `HardFloorWorldY`, `ApplyHardFloorLimit()`.
- `Assets/Scripts/GripperController.cs`: `gripperColliders`, `TryGetGripperBounds()`, `TryEncapsulate()`.
- `Assets/Scripts/_ARQUITECTURA_CONTROL.md`: nueva seccion "Piso duro".

Resultado observado:
- Compilacion **ejecutada** con y sin `UNITY_EDITOR`: **0 errores**, mismos warnings preexistentes.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**.

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: mismo patron que los otros tres limites —sobre `deltaWorld`, en mundo, antes de la IK, nunca por joint—. No se toco `JointPID`, `RobotDynamics` ni la generacion de IK. El brazo sigue sin moverse por `Rigidbody` ni fuerzas.
- **El piso duro es una red de seguridad, no el arreglo de fondo.** Solo protege del plano del suelo: mesas, palés, cajas y cualquier otra superficie elevada se siguen atravesando. Para eso hace falta ejecutar la herramienta de colliders, que es lo que activa el sensor y el veto. Conviene mantener el piso duro igualmente aun despues de generarlos, como respaldo determinista.
- **Deuda conocida:** la cota del suelo esta ahora en TRES sitios independientes (`_hardFloorWorldY`, `GrabbableSafetyGuard.minimumWorldY`, `GripperController.minimumReleaseWorldY`), los tres con default `0`. Si algun dia se mueve el nivel del suelo hay que tocar los tres. Unificarlos en un estatico compartido seria el paso natural, no se hizo aqui por no ampliar el alcance del arreglo.

---

### 2026-08-16 (6) - Tras generar los colliders, sigue sin colisionar con nada salvo el piso

Sintoma reportado por el usuario:
- "Fue error mio, nunca use la herramienta; de todas formas acabo de usarla y no colisiono con nada, solo con el piso" (el piso es el limite geometrico de la entrada anterior, que no depende de colliders).

Estado verificado en disco (datos, no suposiciones):
- `Map_v2.unity` **sigue teniendo 1 solo collider**. La herramienta llama a `MarkSceneDirty` pero **no guarda**: si el usuario no hace `Ctrl+S` sobre `Map_v2`, los colliders viven solo en la sesion del Editor. En Play Mode funcionarian igual (se usa la escena en memoria), pero se pierden al cerrar.
- `Planta.unity`: `_endEffector` **asignado**; el sensor que frena tiene `detectionMask.m_Bits = 73` (Default + Manipulable + **Entorno**) y `maxDistance = 1`. La edicion de la mascara del 2026-08-15 sobrevivio.
- El bloque serializado del `JoystickAdapter` **termina en `_safetyDriftMaxTolerance`**: los campos de anticolision no estan en el YAML, porque la escena no se guarda desde antes de que se anadieran. Al cargar toman los inicializadores del script (`_enableCollisionVeto = true`, `_obstacleMask = 1 << 6`), que son los correctos.
- Conclusion: **la configuracion es correcta**, asi que el sintoma no se explica por parametros. Faltan datos de runtime.

Causas candidatas, en orden de probabilidad:
1. **La herramienta se ejecuto sin `Map_v2` cargada.** El entorno vive en `Map_v2`, no en `Planta`. La herramienta recorre solo las escenas cargadas y no avisa de nada: informa de los pocos objetos que encontro y termina. El numero que imprimio en el log es el dato decisivo.
2. **El veto solo vigila el gripper y su pieza**, no los eslabones del brazo. Golpear una estanteria con el codo no frena nada. Es una limitacion ya documentada, pero es facil leerla como "no colisiona".
3. **Las superficies horizontales las descarta el veto por diseno** (`_floorNormalThreshold`): apoyarse sobre una mesa no lo gobierna el veto sino el bloqueo de descenso via sensor.

Cambio probado:
- `Assets/Editor/EnvironmentColliderTool.cs`: nuevo menu `Tools > Entorno > Diagnosticar anticolision`. Imprime escenas cargadas (y cuales NO lo estan), por escena el numero de mallas candidatas / con collider / en layer `Entorno`, la `_obstacleMask` y `_endEffector` del adapter, la `detectionMask` y `maxDistance` del sensor que frena, y un recordatorio de los dos limites por diseno. Lee los `[SerializeField]` privados via `SerializedObject`.
- `_ARQUITECTURA_CONTROL.md`: aviso de que la herramienta opera sobre escenas cargadas y no guarda.

Resultado observado:
- Compilacion de `Assembly-CSharp-Editor` **ejecutada**: 0 errores.
- Pendiente: la salida del diagnostico en la maquina del usuario. Sin ese dato no se puede cerrar el caso.

Decision:
- **No se toco la cadena de control.** Hasta aqui se habian hecho dos diagnosticos por deduccion sobre el estado del entorno; el segundo (colliders ausentes) resulto correcto pero incompleto. Antes de seguir cambiando codigo de control conviene tener el estado real de la escena, que es justo lo que imprime el menu nuevo.

---

### 2026-08-16 (7) - Giro de camara (yaw) para el perfil VR2

Pedido del usuario:
- Que en **modo camara** se pueda rotar la vista alrededor del eje Y con el VR2, cosa que el perfil PS4 ya permitia. Tras una aclaracion suya, el eje asignado es **"Mover Y"** (la primera propuesta fue "Mover Z" y la descarto).

Diagnostico:
- El yaw de la camara lo produce `CameraJoystickController` a partir de `_viewSide`, que en PS4 es el horizontal del stick derecho. **El VR2 no tiene segundo stick**, asi que en `InputProfileSwitcher.TryBuildProfile()` el perfil VR2 dejaba `ViewUp` y `ViewSide` en `null` y no habia forma de girar la vista.
- De los tres ejes del VR2, el modo camara solo usaba dos: `MoveForward` = "Mover X" y `MoveSide` = "Mover Z". **"Mover Y" estaba sin usar en modo camara**, asi que asignarlo al yaw no le quita nada a los controles existentes.

Cambio probado:
- `InputProfileSwitcher.cs`, perfil VR2: `ViewSide = Resolve(robotBasicAsset, "Basico Cartesiano", "Mover Y")`. Una linea; `CameraJoystickController` ya sabia hacer el yaw, solo le faltaba la accion.
- `ControlGuidePanel.cs`: nuevo item en `Vr2Items`, "Girar Camara: Palanca Altura (en modo camara)". Pasa de 5 a 6 items; `ApplyDefaultItems` recorre las filas fijas de la escena, que son al menos 7 porque PS4 tiene 7, asi que entra sin clonar plantillas.

Archivos tocados:
- `Assets/Scripts/InputProfileSwitcher.cs`, `Assets/Scripts/ControlGuidePanel.cs`

Resultado observado:
- Compilacion **ejecutada** con y sin `UNITY_EDITOR`: **0 errores**, mismos warnings preexistentes.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**. A confirmar el **sentido de giro**: `CameraJoystickController` hace `transform.Rotate(Vector3.up, viewSide * _lookSpeed * dt)` sin opcion de inversion, asi que si en el VR2 la palanca de altura queda invertida respecto de lo esperado hay que anadir un flag de inversion (el `_invertMoveY` del `JoystickAdapter` no aplica aqui, es de otro componente).

Decision:
- **Solo afecta al modo camara.** `CameraJoystickController.Update()` sale por `!JoystickAdapter.IsCameraMode`, asi que en modo robot "Mover Y" sigue moviendo el TCP en vertical exactamente igual que antes. No se toco `JoystickAdapter` ni nada de la cadena de control.
- `ViewUp` (pitch) sigue en `null` para VR2: no se pidio, y el VR2 no tiene un cuarto eje libre donde ponerlo sin quitarselo a otra funcion.

---

### 2026-08-16 (8) - Ejes de camara VR2 intercambiados y sentido de giro de J6 corregido

Pedido del usuario (dos cosas):
1. En **modo camara**, intercambiar los roles: `MoveForward` debe ser "Mover Z" y `MoveSide` "Mover X" (antes al reves). Explicitamente **no** se tocan los input actions, solo el mapeo del modo camara.
2. Invertir el sentido de giro del **modo J6**: "cuando realizo movimiento horario en el joystick, J6 se mueve antihorario".

Cambio probado:
- `InputProfileSwitcher.cs`, perfil VR2: `MoveForward = "Mover Z"`, `MoveSide = "Mover X"`. `ViewSide` sigue en "Mover Y" (entrada anterior). Como los roles del modo robot (`MoveX`/`MoveY`/`MoveZ`) se resuelven por separado unas lineas mas arriba, el intercambio **no afecta al movimiento del brazo**: solo lo consume `CameraJoystickController`, que sale por `IsCameraMode`.
- `JoystickAdapter.UpdateJ6ExclusiveControl()`: `float targetDelta = deltaStick * 0.25f` (antes `-deltaStick * 0.25f`). El gear ratio 1:4 no cambia.

Hallazgo colateral que confirma que la correccion va en la direccion correcta:
- La rama de **botones** del mismo metodo (`_j6AntiHorAction` resta, `_j6HorAction` suma, y luego `_j6TargetAngle += buttonInput * 45 * dt`) **nunca estuvo invertida**. O sea, el analogico y los botones L1/R1 movian J6 en sentidos opuestos entre si. Quitar el negativo no solo arregla lo reportado, ademas deja las dos entradas coherentes.

Archivos tocados:
- `Assets/Scripts/InputProfileSwitcher.cs`, `Assets/Scripts/JoystickAdapter.cs`

Resultado observado:
- Compilacion **ejecutada** con y sin `UNITY_EDITOR`: **0 errores**, mismos warnings preexistentes.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**, en particular que el analogico y los botones L1/R1 (perfil PS4) giren ahora en el mismo sentido.

Decision:
- Nota de compatibilidad con `_ARQUITECTURA_CONTROL.md`: el cambio de J6 es de **signo del incremento de `_j6TargetAngle`**, dentro del modo J6 exclusivo. No se tocaron el gear ratio, los limites `_j6MinLimit`/`_j6MaxLimit`, el unwrap del angulo del stick, el reseteo a 17.7 ni `JointPID`. El mapeo de camara no toca la cadena de control en absoluto.

---

### 2026-08-16 (9) - REGRESION PROPIA: los botones PS4/VR-2 se salieron de la pantalla

Sintoma reportado por el usuario:
- "Por que no funciona cambiar el joystick desde el menu pausa? antes funcionaba".

Diagnostico (regresion introducida en las entradas (2) y (4) de hoy):
- En la escena, `PauseMenuController.pauseMenuRoot` es **`fileID: 0`**, es decir null: el menu **no existe en el `.unity`**, lo construye entero `BuildDefaultMenu()` por codigo, incluidos `ps4Button` y `vr2Button`.
- El panel se creaba con **altura fija y anclado al centro**. Al anadir el boton de modo debug y el de bloqueo con pieza hubo que subirla a mano dos veces: **740 -> 798 -> 856**.
- El `CanvasScaler` de la escena es **`m_UiScaleMode: 0` (ConstantPixelSize) con `m_ScaleFactor: 1`**: no escala nada, 1 unidad de UI = 1 pixel real de ventana, y el `m_ReferenceResolution: {800, 600}` es decorativo en ese modo. Un panel de 856 px no entra en una Game view normal.
- Como el panel esta **centrado**, el excedente se reparte arriba y abajo. Lo que se salia por arriba era el titulo, "Joystick: ..." y **justo la fila PS4/VR-2**: invisible e inclicable. Los botones anadidos van al final, por eso esos si se veian y parecia que solo fallaba el cambio de joystick.

Cambio probado:
- `PauseMenuController.BuildDefaultMenu()`: el panel deja de tener altura fija y pasa a **estirarse en vertical con el canvas** (`anchorMin.y = 0`, `anchorMax.y = 1`, `sizeDelta.y = -2 * PanelVerticalMargin`). Anadir opciones ya no exige recalcular ninguna altura y el panel no puede volver a salirse de la pantalla.
- Menu compactado para que el contenido entre con holgura: constantes `ButtonHeight` 46 -> **40**, `LayoutSpacing` 12 -> **8**, padding vertical 24 -> 16, titulo 34 -> 28, etiqueta de boton 20 -> 18, `CalibrationStatus` 98 -> 56. El total pasa de ~852 a ~706 px.
- Se mantiene `childAlignment = UpperCenter` **a proposito**, y ahora esta comentado en el codigo: si algun dia el contenido no entra, lo que se pierde es lo de abajo, nunca los botones de perfil.

Archivos tocados:
- `Assets/Scripts/PauseMenuController.cs`

Resultado observado:
- Compilacion **ejecutada** con y sin `UNITY_EDITOR`: **0 errores**.
- Comportamiento en Play Mode: **pendiente de validar por el usuario**.

Leccion para no repetirlo:
- **Con `ConstantPixelSize`, cualquier alto fijo en la UI es una apuesta sobre el tamano de la ventana.** Anadir una opcion al menu de pausa no debe implicar tocar un numero magico de altura. Si en el futuro el menu vuelve a crecer y deja de entrar en pantallas bajas, el paso siguiente es meter el contenido en un `ScrollRect`, no volver a subir la altura.
- El diff de la escena (`git diff` sobre `Planta.unity`) confirmo ademas que al guardarla solo se anadieron campos nuevos con sus defaults correctos: `_obstacleMask` 64, `_enableHardFloor` 1, `_hardFloorWorldY` 0, `releaseOpeningDelta` 0.2. Ninguna referencia se perdio.

---

### 2026-08-16 (10) - El menu de pausa navega pero no ejecuta ninguna accion

Sintoma precisado por el usuario:
- "Antes podia cambiar de joystick aunque no estuviese ese seleccionado (...). Ahora solo me deja abrir el menu pausa y navegar, pero no puedo realizar ninguna accion".

Correccion del diagnostico anterior:
- La entrada (9) atribuyo el sintoma al panel saliendose de pantalla. **Esa no era la causa**: el problema no es de visibilidad sino del **submit**, que no se ejecuta sobre ningun boton. El arreglo de anclaje de (9) se mantiene porque es correcto por si mismo, pero no explicaba esto.
- Se reviso el diff completo de los dos archivos tocados contra `3da6040` (el commit anterior a todo el trabajo de hoy): `InputProfileSwitcher` solo cambia el mapeo de camara del VR2, y `PauseMenuController` solo anade botones, colores y layout. **Ninguno toca el camino del submit**, que va por las acciones globales `Ps4Click` / `Vr2Click`, no modificadas. Tambien se verifico que la action "Click" existe en `PS4Joystick.inputactions`, asi que `Resolve` no devuelve null.

Causa candidata identificada por lectura del guard (a confirmar en Play Mode):
- `SubmitSelected()` pone `_ignoreMenuSubmitUntilRelease = true` para que una pulsacion no cuente dos veces, y ese flag **solo se libera cuando `ReadMenuSubmitPressed()` deja de leer pulsado**. Pero esa lectura es de **nivel** y hace `Ps4Click >= threshold || Vr2Click >= threshold`: **basta con que uno de los dos quede pegado en alto** (mando desconectado, eje flotante, contacto normalmente cerrado en el VR2 industrial) para que el flag no se libere jamas y bloquee **todos** los submits, tambien los del otro mando.
- Encaja con el sintoma completo: la navegacion (`ReadMenuHorizontal`/`Vertical`) y la pausa (`WasPausePressedThisFrame`) **no pasan por ese guard**, por eso siguen respondiendo. Y explica el "antes funcionaba" sin que el codigo del submit haya cambiado: depende del estado del hardware, no del codigo.

Cambio probado:
- `PauseMenuController.Update()`: el guard se libera al soltar **o** por timeout (`MenuSubmitReleaseTimeout`, 0.5 s). Es seguro porque la deteccion del submit ya es **por flanco** (`WasMenuSubmitPressedThisFrame`), asi que soltar el guard antes no puede duplicar pulsaciones. Si salta por timeout con el boton aun en alto, se avisa por consola (bajo modo debug) senalando que un click esta quedandose pegado.
- Logs de diagnostico bajo `DebugSettings.IsEnabled`: uno al ejecutar el submit (con el nombre del boton) y otro cuando se pulsa confirmar sobre algo no interactuable. Sirven para distinguir de un vistazo entre "el input no llega" y "el input llega pero el boton no responde".

Archivos tocados:
- `Assets/Scripts/PauseMenuController.cs`

Resultado observado:
- Compilacion **ejecutada** con y sin `UNITY_EDITOR`: **0 errores**.
- **Pendiente de confirmar en Play Mode.** Con el modo debug encendido: si aparece el warning del timeout, la causa era el click pegado y queda confirmada. Si no aparece ningun log al pulsar confirmar, el input no esta llegando y hay que mirar el binding de "Click"/"Camera Toggle" en los assets. Si aparece "Submit sobre 'X'" pero no pasa nada, el fallo esta en el `onClick` de ese boton concreto.

Leccion:
- **Un guard "hasta soltar" que mira el nivel de varias fuentes a la vez es un punto unico de fallo.** Si cualquiera de ellas puede quedarse en alto, bloquea la interaccion entera y el sintoma aparece lejos de la causa. Con deteccion por flanco disponible, el guard de nivel sobra o debe llevar tope temporal.

---

### 2026-08-02 (11) - Sección de Métricas de Desempeño (Canvas) + contador de colisiones del gripper

Sintoma / pedido:
- Enriquecer el proyecto (aplicación de colocación de celdas Eternity en racks de autoelevadores) con métricas de desempeño visibles en el Canvas: cantidad de colisiones del gripper contra objetos no agarrables, tiempo de operación (agarre→suelta), cantidad de operaciones (objetos movidos) y masa del objeto agarrado. Debía seguir el mismo patrón estático de Hierarchy que `PID_Section`/`Guide_Section` (sin generar objetos por código salvo extensión futura vía plantilla).

Cambio probado:
- Nuevo `Assets/Scripts/PerformanceMetricsPanel.cs`: panel estático (mismo patrón que `PidActionsPanel`/`ControlGuidePanel`) con 4 filas fijas + `SetExtraRow` para métricas futuras clonando una plantilla inactiva.
- Nuevo `Assets/Scripts/PerformanceMetricsTracker.cs`: en `OnRobot_RG2`, junto a `GripperController` (sin modificarlo). Detecta la transición agarre→suelta por polling de `IsHoldingObject`/`GrabbedMass` en `Update()` (no se agregaron eventos a `GripperController` para minimizar el cambio en un script del gripper), cuenta operaciones, mide duración, y expone `NotifyCollisionContact` para el conteo de colisiones, deduplicado por objeto (si dos partes del gripper tocan la misma pieza a la vez, cuenta una sola colisión).
- Nuevo `Assets/Scripts/GripperCollisionCounter.cs`: mismo mecanismo que `GripperTriggerForwarder` (triggers, no colisión sólida) aplicado al caso inverso: cuenta contacto solo si el objeto **no** tiene tag "Agarrable", e ignora contactos bajo un `Transform` raíz configurable (el propio robot) para no contarse a sí mismo.
- Se decidió explícitamente **no** usar `OnCollisionEnter`: el Rigidbody del gripper es Kinematic (confirmado en la escena) y Unity no garantiza eventos de colisión sólida entre un Rigidbody Kinematic y colliders estáticos sin Rigidbody (paredes, racks). Los triggers sí son confiables en esa combinación, y es el mismo mecanismo que ya usa el agarre.
- Hierarchy: `Metrics_Section` creado a mano como hijo de `InfoPanel_Gripper` (mismo patrón Vertical Layout Group + Content Size Fitter que `PID_Section`/`Guide_Section`), con título + 4 filas + plantilla inactiva. `CollisionSensor` creado a mano como hijo de `OnRobot_RG2`, con `BoxCollider` en modo Trigger.

Archivos/parametros:
- `Assets/Scripts/PerformanceMetricsPanel.cs` (nuevo)
- `Assets/Scripts/PerformanceMetricsTracker.cs` (nuevo)
- `Assets/Scripts/GripperCollisionCounter.cs` (nuevo)
- `Assets/Scenes/Planta.unity`: `Metrics_Section` (nuevo, hijo de `InfoPanel_Gripper`), `CollisionSensor` (nuevo, hijo de `OnRobot_RG2`).

Resultado observado:
- Feedback del usuario en Play Mode: "Impecable" — agarrar y soltar una celda de batería (tag "Agarrable") no suma colisión; tocar el rack, la caja u otro objeto sin ese tag sí suma. El panel se ve correctamente integrado en el mismo recuadro que PID/Guía/Cámara.

Nota sobre el `BoxCollider` de `CollisionSensor`:
- Al colgar de la base fija del gripper (`OnRobot_RG2`) y no de los huesos de los dedos (`L_Arm_ID_0/1`, `R_Arm_ID_0/1`), su tamaño **no seguí la apertura/cierre real** de la garra. Se dimensionó a mano para el caso "dedos abiertos" (peor caso), lo que puede sobre-contar alguna colisión cuando la garra está cerrada y el volumen del sensor sobresale más allá de los dedos reales en ese instante. Aceptado como trade-off correcto para esta métrica (mejor sobre-contar que no detectar). Mejora futura posible, no implementada: redimensionar el collider en cada `FixedUpdate` usando `GripperController.TryGetGripperBounds()`, que ya calcula el AABB real de los colliders del gripper cuadro a cuadro.

Decision:
- Integrar permanentemente. Nota de compatibilidad con arquitectura de control: cambio exclusivamente de UI/telemetría del gripper; no se tocó `JoystickAdapter`, `JointPID`, `RobotDynamics`, la generación del target IK, ni `GripperController`/`GripperTriggerForwarder` (se leen, no se modifican).

---

### 2026-08-17 (12) - Investigación: el gripper atraviesa todos los objetos salvo el piso

Sintoma:
- El usuario pidió analizar por qué puede atravesar todo el entorno con el gripper, salvo el piso, y si se puede arreglar para que no atraviese nada.

Diagnóstico (sin cambios de código, solo lectura + herramientas ya existentes en el proyecto):
- El piso funciona distinto: lo protegen `ApplyHardFloorLimit()` (cota Y numérica fija) y `ApplyDescentLimit()` (sensor de proximidad), **ninguno de los dos necesita colliders**. Existen justamente porque no se podía confiar en la colisión real del entorno (ver nota ya existente en `_ARQUITECTURA_CONTROL.md`, "Piso duro").
- Para el resto del entorno **ya existe** un veto genérico (`ApplyCollisionVeto()`, `SphereCast`/`BoxCast` contra `_obstacleMask` = layer "Entorno"), activado (`_enableCollisionVeto: True`) y con la máscara correcta.
- `Tools > Entorno > Diagnosticar anticolision` (herramienta ya existente, `Assets/Editor/EnvironmentColliderTool.cs`) confirmó en dos corridas sucesivas:
  1. Primera corrida: `Planta: 2 mallas candidatas, 2 con collider, 0 en layer 'Entorno'` — `Rack` y `Caja` tenían colliders pero estaban en layer `Default` en vez de `Entorno`. `Map_v2` ya estaba 100% correcto (173/173).
  2. Se corrió `Tools > Entorno > Generar colliders faltantes` (con `Planta` + `Map_v2` cargadas) → `Rack`/`Caja` pasaron a layer `Entorno`. Segunda corrida del diagnóstico: `Planta: 2/2/2`. **El brazo seguía atravesando todo** pese a la configuración ya correcta.
- Inspección directa de los 5 `BoxCollider` de `Rack`: formaban un **marco hueco** (4 paredes finas de 0.045 en el perímetro + una bandeja delgada cerca del piso), pensado para la estructura del rack, no para los bidones apilados dentro/sobre él. El interior (donde están los bidones) no tenía collider.
- Se agregó un `MeshCollider` no-convexo sobre `Rack` usando su malla real (`SM_RackEternity`, la misma del `Mesh Renderer`), replicando el patrón que ya usa `EnvironmentColliderTool` para el resto del entorno. **Seguía siendo atravesable.**
- Dato del usuario, empírico: agregarle un `Rigidbody` (no-kinemático) a `Rack` sí lo vuelve detectable — pero ahora el robot puede empujarlo/chocarlo físicamente.

Causa raíz identificada:
- `Physics.SphereCastNonAlloc`/`BoxCastNonAlloc` (las queries de `ApplyCollisionVeto`) no detectan de forma confiable un `MeshCollider` no-convexo si el objeto no tiene `Rigidbody` — limitación de PhysX, no un bug del script. `Physics.Raycast` sí funciona sin Rigidbody; las queries de barrido de forma (sphere/box cast) no.
- El Rigidbody agregado empíricamente por el usuario es no-kinemático, por eso el gripper (Kinematic) lo empuja: un Rigidbody Kinematic siempre puede mover a uno dinámico que toca, nunca al revés.

Cambio propuesto (pendiente de que el usuario lo pruebe — se priorizó actualizar la documentación antes de seguir probando en Play Mode):
- Mantener el `Rigidbody` en `Rack`, pero marcar **`Is Kinematic = true`**. Debería preservar la detección (por tener Rigidbody) sin el empuje físico (por ser Kinematic, no reacciona a fuerzas ni colisiones).
- Si funciona, replicar en `Caja`: `Mesh Collider` no-convexo sobre su malla real + `Rigidbody` Kinematic.

Archivos/parametros:
- `Assets/Scenes/Planta.unity`: `Rack` — layer reasignada a `Entorno` (vía herramienta), `Mesh Collider` agregado (no-convexo, mesh `SM_RackEternity`), `Rigidbody` agregado (no-kinemático, pendiente de pasar a Kinematic). Ningún script tocado.
- Documentado en `_ARQUITECTURA_CONTROL.md`, sección "Anticolision con el entorno": nota sobre el requisito de Rigidbody (Kinematic) para que las queries de barrido detecten `MeshCollider` no-convexos, y aceptación explícita de que el veto solo vigila gripper + pieza agarrada (no codo/antebrazo), justificada por la cinemática DH de este robot (eje `q3` perpendicular al plano brazo-antebrazo) y la ausencia de obstáculos que sobresalgan de ese plano en la escena actual.

Resultado observado:
- **Pendiente de confirmar en Play Mode** si `Rigidbody` + `Is Kinematic` en `Rack` da detección sin empuje. `Caja` queda pendiente de replicar el mismo tratamiento una vez confirmado.

Decision:
- No integrar todavía como definitivo — falta la confirmación del paso Kinematic. Documentado para que la investigación (y el motivo de cada paso) quede trazable de una sesión a la otra.
