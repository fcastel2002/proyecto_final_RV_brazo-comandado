# Original User Request

## Initial Request — 2026-06-30T18:13:00Z

Modificar la cadena de control y la interfaz de usuario en Unity para el brazo robótico: hacer opcional la rotación circular del TCP desde el menú de pausa, mejorar la interfaz del modo J6 superponiéndola a la cámara del gripper, reducir la sensibilidad de J6 y añadir un reseteo rápido de J6 (cancelando la acción normal) mediante doble clic.

Working directory: d:\06. Proyectos\proyecto_final_RV_brazo-comandado\Planta_BrazoRobótico
Integrity mode: development

## Requirements

### R1. Modo de Orientación del TCP (Menú de Pausa)
Hacer que el comportamiento actual (donde el TCP gira para mantenerse recto respecto al eslabón 1 del robot) sea opcional. El modo por defecto debe ser mantener la orientación exacta original del TCP. Añadir un control (botón/toggle) en el **menú de pausa** del juego para alternar entre estos dos modos.

### R2. Interfaz del Modo J6 Exclusivo (Superposición)
El modo J6 exclusivo ya no tendrá la GUI separada actual (`J6HUDController`). La nueva interfaz debe superponerse directamente sobre la vista de la `GripperCamera`. Debe consistir en un dial traslúcido (que no tape excesivamente la visión de la cámara) que actúe como un gráfico de torta (barrido), mostrando marcas/diales de referencia y el ángulo actual de J6.

### R3. Sensibilidad del Modo J6
En el modo J6 exclusivo, reducir la sensibilidad de la entrada para que la rotación angular de J6 sea **4 veces más lenta** que la entrada directa del joystick, facilitando así un posicionamiento fino y preciso.

### R4. Reseteo de J6 con Doble Clic del Gripper
Implementar una detección de doble clic en el botón del Gripper (el gatillo).
- Al hacer un clic simple, el gripper se abre o cierra como de costumbre.
- Al hacer doble clic rápido, se debe **cancelar o revertir** la acción de apertura/cierre que habría provocado el primer clic, y en su lugar, J6 debe volver a su posición de 0° de forma controlada (suavemente, no instantáneo).

## Verification Resources
Existe una suite de ejecución en batchmode (`ControlDiagnosticBatch.cs` y `ControlDiagnosticRunner.cs`) que puede usarse como referencia para levantar la simulación sin gráficos (`-nographics`) y verificar telemetría en JSON (`Logs/control_diagnostics_log.json`).

## Acceptance Criteria

### Verificación Funcional Lógica (Automatizable)
- [ ] Existe un script de prueba (ej. `ControlDiagnostic...`) o un test de PlayMode que inyecte un doble clic en la acción del Gripper y confirme que J6 se mueve hacia 0° y que el estado final del gripper se mantiene inalterado.
- [ ] Existe un test que verifique que al inyectar movimiento en el eje en modo J6, la velocidad del target de J6 es una cuarta parte (0.25x) del valor original o del stick puro.

### Verificación de UI y Comportamiento Manual
- [ ] En Play Mode, el menú de pausa incluye un botón/toggle claramente visible para alternar la orientación del TCP.
- [ ] Al entrar en el modo J6 exclusivo, la nueva GUI en formato dial de torta traslúcido aparece dibujada sobre la cámara del gripper.
- [ ] Al ejecutar en batch mode sin interfaz gráfica, el proyecto sigue compilando sin errores (`exit code 0`).
