#!/usr/bin/env python3
"""
Monitor estilo "Tester Electrónico" con gráficas en tiempo real
Muestra historial de señales como un osciloscopio
"""
import pygame
from collections import deque

class OscilloscopeMonitor:
    """Monitor con gráficas en tiempo real estilo osciloscopio"""
    
    def __init__(self):
        pygame.init()
        pygame.joystick.init()
        
        if pygame.joystick.get_count() == 0:
            raise Exception("⚠ No se detectó ningún joystick")
        
        self.joystick = pygame.joystick.Joystick(0)
        self.joystick.init()
        
        # Ventana
        self.width = 1400
        self.height = 800
        self.screen = pygame.display.set_mode((self.width, self.height))
        pygame.display.set_caption(f"Oscilloscope Monitor - {self.joystick.get_name()}")
        
        # Fuentes
        self.font_title = pygame.font.Font(None, 36)
        self.font_normal = pygame.font.Font(None, 24)
        self.font_small = pygame.font.Font(None, 18)
        
        self.clock = pygame.time.Clock()
        
        # Colores estilo osciloscopio
        self.BG = (10, 15, 20)
        self.GRID = (30, 40, 50)
        self.TEXT = (0, 255, 200)
        self.WHITE = (255, 255, 255)
        
        # Colores para cada eje
        self.colors = [
            (0, 255, 100),   # Verde
            (255, 100, 0),   # Naranja
            (100, 150, 255), # Azul
            (255, 255, 0),   # Amarillo
            (255, 0, 200),   # Magenta
            (0, 255, 255),   # Cyan
        ]
        
        # Historial de datos (para gráficas)
        self.history_length = 300  # 5 segundos a 60fps
        self.axis_history = [deque(maxlen=self.history_length) for _ in range(6)]
        self.button_history = [deque(maxlen=self.history_length) for _ in range(16)]
        
        print(f"✓ Conectado: {self.joystick.get_name()}")
        print(f"  Ejes: {self.joystick.get_numaxes()}")
        print(f"  Botones: {self.joystick.get_numbuttons()}")
    
    def draw_grid(self, x, y, width, height):
        """Dibuja grid de fondo"""
        # Líneas horizontales
        for i in range(5):
            y_pos = y + int(height * i / 4)
            pygame.draw.line(self.screen, self.GRID, 
                           (x, y_pos), (x + width, y_pos), 1)
        
        # Líneas verticales
        for i in range(11):
            x_pos = x + int(width * i / 10)
            pygame.draw.line(self.screen, self.GRID, 
                           (x_pos, y), (x_pos, y + height), 1)
    
    def draw_waveform(self, x, y, width, height, history, color, label, value):
        """Dibuja forma de onda"""
        # Fondo
        pygame.draw.rect(self.screen, (20, 25, 30), (x, y, width, height))
        self.draw_grid(x, y, width, height)
        
        # Borde
        pygame.draw.rect(self.screen, self.GRID, (x, y, width, height), 2)
        
        # Línea central (0)
        center_y = y + height // 2
        pygame.draw.line(self.screen, (50, 60, 70), 
                        (x, center_y), (x + width, center_y), 1)
        
        # Dibujar forma de onda
        if len(history) > 1:
            points = []
            for i, val in enumerate(history):
                px = x + int(i * width / self.history_length)
                # Mapear valor de -1..1 a altura del gráfico
                py = center_y - int(val * (height // 2 - 10))
                points.append((px, py))
            
            if len(points) > 1:
                pygame.draw.lines(self.screen, color, False, points, 2)
        
        # Label y valor actual
        label_surf = self.font_small.render(label, True, color)
        self.screen.blit(label_surf, (x + 5, y + 5))
        
        value_surf = self.font_normal.render(f"{value:+.3f}", True, color)
        self.screen.blit(value_surf, (x + width - 80, y + 5))
        
        # Escala
        scale_top = self.font_small.render("+1.0", True, self.GRID)
        self.screen.blit(scale_top, (x + 5, y + 25))
        
        scale_bot = self.font_small.render("-1.0", True, self.GRID)
        self.screen.blit(scale_bot, (x + 5, y + height - 20))
    
    def draw_button_timeline(self, x, y, width, height, button_histories, buttons):
        """Dibuja timeline de botones"""
        # Fondo
        pygame.draw.rect(self.screen, (20, 25, 30), (x, y, width, height))
        
        # Título
        title = self.font_normal.render("BOTONES (Timeline)", True, self.TEXT)
        self.screen.blit(title, (x + 5, y + 5))
        
        # Timeline para cada botón
        num_buttons = min(16, len(buttons))
        button_height = (height - 40) // num_buttons
        
        for btn_id in range(num_buttons):
            btn_y = y + 35 + btn_id * button_height
            
            # Label
            label = self.font_small.render(f"B{btn_id}", True, 
                                          self.colors[btn_id % len(self.colors)])
            self.screen.blit(label, (x + 5, btn_y + 2))
            
            # Timeline
            if len(button_histories[btn_id]) > 0:
                for i, pressed in enumerate(button_histories[btn_id]):
                    if pressed:
                        px = x + 40 + int(i * (width - 45) / self.history_length)
                        color = self.colors[btn_id % len(self.colors)]
                        pygame.draw.line(self.screen, color, 
                                       (px, btn_y), (px, btn_y + button_height - 2), 2)
            
            # Estado actual
            if buttons[btn_id]:
                pygame.draw.circle(self.screen, (0, 255, 0), 
                                 (x + width - 15, btn_y + button_height // 2), 5)
        
        # Borde
        pygame.draw.rect(self.screen, self.GRID, (x, y, width, height), 2)
    
    def draw_current_values(self, x, y, axes, buttons):
        """Panel de valores actuales"""
        # Título
        title = self.font_title.render("VALORES ACTUALES", True, self.TEXT)
        self.screen.blit(title, (x, y))
        
        y_offset = y + 40
        
        # Ejes
        for i, val in enumerate(axes[:6]):
            axis_names = ["L-StickX", "L-StickY", "R-StickX", "R-StickY", "L2", "R2"]
            name = axis_names[i] if i < len(axis_names) else f"Axis{i}"
            
            color = self.colors[i % len(self.colors)]
            
            # Nombre
            text = self.font_normal.render(name, True, color)
            self.screen.blit(text, (x, y_offset))
            
            # Valor
            val_text = self.font_normal.render(f"{val:+.3f}", True, self.WHITE)
            self.screen.blit(val_text, (x + 150, y_offset))
            
            # Barra
            bar_width = 200
            bar_x = x + 250
            bar_y = y_offset + 5
            
            pygame.draw.rect(self.screen, self.GRID, (bar_x, bar_y, bar_width, 15), 1)
            
            # Relleno
            fill_width = int(((val + 1) / 2) * bar_width)
            pygame.draw.rect(self.screen, color, (bar_x + 1, bar_y + 1, fill_width, 13))
            
            y_offset += 35
        
        # Botones presionados
        y_offset += 20
        pressed_text = self.font_normal.render("Botones presionados:", True, self.TEXT)
        self.screen.blit(pressed_text, (x, y_offset))
        
        y_offset += 30
        pressed = [str(i) for i, b in enumerate(buttons) if b]
        if pressed:
            btn_text = ", ".join(pressed)
            text = self.font_normal.render(btn_text, True, (0, 255, 100))
            self.screen.blit(text, (x, y_offset))
        else:
            text = self.font_normal.render("Ninguno", True, self.GRID)
            self.screen.blit(text, (x, y_offset))
    
    def run(self):
        """Loop principal"""
        running = True
        
        while running:
            self.clock.tick(60)
            print
            # Verificar conexión con el joystick
            if not pygame.joystick.get_count() > 0:
                print("⚠ Joystick desconectado, intentando reconectar...")
                pygame.joystick.quit()
                pygame.joystick.init()
                self.joystick = None
                
                if pygame.joystick.get_count() > 0:
                    self.joystick = pygame.joystick.Joystick(0)
                    self.joystick.init()
                    print("✓ Joystick reconectado")
                else:
                    print("✗ No se pudo reconectar el joystick")
                    continue
                    running = False
            
            try:
                for event in pygame.event.get():
                    if event.type == pygame.QUIT:
                        running = False
                    elif event.type == pygame.KEYDOWN:
                        if event.key == pygame.K_ESCAPE:
                            running = False
            except:
                continue

            pygame.event.pump()
            
            # Leer datos
            num_axes = self.joystick.get_numaxes()
            axes = [self.joystick.get_axis(i) for i in range(num_axes)]
            
            # Asegurar que tenemos 6 ejes (rellenar con 0)
            while len(axes) < 6:
                axes.append(0.0)
            
            buttons = [self.joystick.get_button(i) 
                      for i in range(self.joystick.get_numbuttons())]
            
            # Guardar en historial
            for i in range(6):
                self.axis_history[i].append(axes[i])
            
            for i in range(len(buttons)):
                if i < 16:
                    self.button_history[i].append(buttons[i])
            
            # Dibujar
            self.screen.fill(self.BG)
            
            # Título principal
            title = self.font_title.render("OSCILLOSCOPE MONITOR", True, self.TEXT)
            self.screen.blit(title, (20, 20))
            
            # Nombre del joystick
            name = self.font_small.render(self.joystick.get_name(), True, self.GRID)
            self.screen.blit(name, (20, 55))
            
            # FPS
            fps = self.font_normal.render(f"{int(self.clock.get_fps())} FPS", 
                                        True, self.GRID)
            self.screen.blit(fps, (1300, 25))
            
            # Gráficas de ejes (2 columnas)
            waveform_width = 400
            waveform_height = 100
            
            axis_names = ["L-Stick X", "L-Stick Y", "R-Stick X", 
                         "R-Stick Y", "L2", "R2"]
            
            for i in range(6):
                col = i % 2
                row = i // 2
                
                x = 20 + col * (waveform_width + 20)
                y = 90 + row * (waveform_height + 20)
                
                name = axis_names[i] if i < len(axis_names) else f"Axis {i}"
                
                self.draw_waveform(x, y, waveform_width, waveform_height,
                                 self.axis_history[i], 
                                 self.colors[i % len(self.colors)],
                                 name, axes[i])
            
            # Timeline de botones (abajo)
            self.draw_button_timeline(20, 480, 840, 280, 
                                     self.button_history, buttons)
            
            # Panel de valores actuales (derecha)
            self.draw_current_values(900, 90, axes, buttons)
            
            # Instrucciones
            inst = self.font_small.render("ESC para salir | Muestra últimos 5 segundos", 
                                        True, self.GRID)
            self.screen.blit(inst, (20, 770))
            
            pygame.display.flip()
        
        pygame.quit()

def main():
    try:
        monitor = OscilloscopeMonitor()
        monitor.run()
    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()
        pygame.quit()

if __name__ == "__main__":
    main()
