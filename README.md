# Sistema de teleoperación y simulación en realidad virtual para formación de operarios en robótica manipuladora

# &nbsp;

Este repositorio contiene el código y los recursos necesarios para el desarrollo de un **entorno de entrenamiento para operarios de brazos robóticos**, combinando simulación en Unity y hardware basado en un joystick con microcontrolador RP2040.

---
## 🔧 Tecnologías Utilizadas

- **Unity** (motor gráfico)  
- **C++ sobre RP2040** (firmware del joystick)  
- **Arduino (u otro controlador)** para integración futura con el brazo físico  
- **Serial/UART o protocolos de comunicación** para enlace entre dispositivos  

---

## ⚙️ Instalación y Uso

1. **Simulación (Unity)**  
   - Abrir el directorio `/UnityProject` con Unity Hub.  
   - Ejecutar la escena principal para iniciar el entorno virtual.  

2. **Joystick (RP2040)**  
   - Compilar el código del directorio `/JoystickFirmware` en el entorno de desarrollo C++ adecuado.  
   - Flashear el firmware en el microcontrolador RP2040.  

3. **Conexión con el robot real (opcional, en desarrollo)**  
   - Configurar la comunicación con Arduino u otro controlador.  
   - Asegurar la sincronización de sensores para la retroalimentación en el entorno virtual.  

---

## 🚧 Estado del Proyecto

Actualmente en fase de desarrollo inicial:
- [x] Definición de arquitectura general.  
- [ ] Desarrollo del joystick en RP2040.  
- [ ] Implementación de la simulación en Unity.  
- [ ] Integración con brazos robóticos reales.  

---

## 🤝 Contribuir

1. Haz un fork del repositorio.  
2. Crea una nueva rama (`git checkout -b feature/nueva-funcionalidad`).  
3. Realiza tus cambios y haz commit (`git commit -m 'Agregada nueva funcionalidad'`).  
4. Sube la rama (`git push origin feature/nueva-funcionalidad`).  
5. Abre un Pull Request.  

---

## 📄 Licencia

Este proyecto se distribuye bajo la licencia **[MIT]** (o la que elijas).  
Consulta el archivo `LICENSE` para más detalles.


