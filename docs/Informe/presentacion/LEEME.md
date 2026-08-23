# Presentación — Proyecto Final de Realidad Virtual

Diapositivas en HTML autónomo, generadas a partir de `../InformeFinal.tex` y sus figuras.

## Cómo abrirla

Doble clic en `index.html`. Se abre en cualquier navegador moderno (Chrome, Edge, Firefox).
No necesita conexión a internet ni servidor: las imágenes se leen de `../Img/`.

> Importante: **no muevas `index.html` fuera de esta carpeta** sin llevarte también `../Img/`,
> porque las rutas de las imágenes son relativas.

Para proyectar, abrí el archivo y presioná **F** (pantalla completa).

## Atajos de teclado

| Tecla | Acción |
|---|---|
| `→` `↓` `Espacio` `AvPág` | Avanzar (revela los elementos de a uno) |
| `←` `↑` `RePág` | Retroceder |
| `N` / `P` | Diapositiva siguiente / anterior completa |
| `Inicio` / `Fin` | Primera / última diapositiva |
| `1`…`9` | Saltar a esa diapositiva |
| `O` o `Esc` | Vista general de las 24 diapositivas |
| `S` | Mostrar/ocultar **notas del orador** |
| `F` | Pantalla completa |
| `T` | Reiniciar el cronómetro |
| `?` | Ayuda con todos los atajos |
| Clic en una imagen | Ampliarla a pantalla completa |

También funciona con gestos de deslizamiento en pantallas táctiles.

## Estructura (24 diapositivas, ~15 min)

1. Portada
2. El problema: ensamble manual en AUTOELEC
3. Riesgos de la operación manual
4. Motivación del entorno VR
5. Objetivos y alcance
6. **Arquitectura del sistema** (diagrama animado)
7. Hardware: joystick RPi Pico
8. HID bidireccional
9. Unity + Flange (KUKA KR210)
10. Interfaz en pantalla (Canvas)
11. Mapeo de comandos y modos
12. **Remapeo geométrico** (animación interactiva: la cámara orbita y los versores se recalculan)
13. Del joystick a la consigna cartesiana
14. Control PID por articulación
15. Inercia efectiva
16. **Capas de seguridad** (animación: descenso frenado por proximidad)
17. Gripper: lógica de agarre
18. Sensores de distancia y háptica
19. **Comunicación UDP** (diagrama animado)
20. Clientes de referencia
21. Métricas de desempeño
22. Autoría procedural de contenido
23. Conclusiones
24. Cierre

Las **notas del orador** (tecla `S`) traen, para cada diapositiva, el punto que conviene
remarcar al exponer.

## Imágenes agregadas

Estas tres se extrajeron del PDF compilado del informe porque el `.tex` las referencia
pero no estaban en el repositorio, y quedaron guardadas en `../Img/`:

- `joystick_top_inside.jpg` — interior del joystick
- `joystick_top_full.jpg` — exterior del prototipo
- `logo_fing_uncu.png` — logo institucional (recortado de la portada)

## Editar el contenido

Todo vive en `index.html`. Cada diapositiva es un `<section class="slide" data-title="...">`.
Los elementos con clase `frag` se revelan de a uno al avanzar. Las notas del orador van en
`<div class="notes-src">` al final de cada sección. La paleta y la tipografía están en las
variables CSS de `:root`, al principio del archivo.
