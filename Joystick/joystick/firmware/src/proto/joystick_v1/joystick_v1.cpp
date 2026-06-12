
#include <stdio.h>
#include "pico/stdlib.h"

extern "C" {
#include "bsp/board.h"
#include "tusb.h"
}

#include "joystick_analog.h"
#include "vibration_motor.h"

static constexpr uint SWITCH_LEFT_GPIO = 16;
static constexpr uint SWITCH_RIGHT_GPIO = 17;
static constexpr uint16_t CENTERED_AXIS_ADC = 2048;

static void init_switch(uint gpio) {
  gpio_init(gpio);
  gpio_set_dir(gpio, GPIO_IN);
  gpio_pull_up(gpio);
}

static bool switch_pressed(uint gpio) {
  return !gpio_get(gpio);
}

static int8_t adc_to_axis(uint16_t v) {
  if (v > 4095) v = 4095;
  int32_t s = ((int32_t)v - 2048) * 127 / 2047; // centra en 0
  if (s < -127) s = -127;
  if (s > 127)  s = 127;
  return (int8_t)s;
}

static void send_hid_report(uint16_t x_adc, uint16_t y_adc, uint16_t z_adc,
                            uint16_t rz_adc, uint32_t buttons) {
  if (!tud_hid_ready()) return;

  hid_gamepad_report_t r{};
  r.x  = adc_to_axis(x_adc);
  r.y  = adc_to_axis(y_adc);
  r.z  = adc_to_axis(z_adc);
  r.rz = adc_to_axis(rz_adc);
  r.hat = 0x08;   // centrado
  r.buttons = buttons;

  tud_hid_report(0, &r, sizeof r);  // Report ID 0 (coincide con el macro GAMEPAD)
}
int main() {
    board_init();
    tusb_init();
    vibration_motor_init();
    init_switch(SWITCH_LEFT_GPIO);
    init_switch(SWITCH_RIGHT_GPIO);

    Joystick joystick_xy(27, 28);
    Joystick joystick_z(26, 0);
    joystick_xy.init();
    joystick_z.init();

    while (true) {
        tud_task();

        uint16_t coords_xy[2];
        uint16_t coords_z[2];
        joystick_xy.read_coordinate(coords_xy);
        joystick_z.read_coordinate(coords_z);

        uint32_t buttons = 0;
        if (switch_pressed(SWITCH_LEFT_GPIO)) {
            buttons |= GAMEPAD_BUTTON_0;
        }
        if (switch_pressed(SWITCH_RIGHT_GPIO)) {
            buttons |= GAMEPAD_BUTTON_1;
        }

        send_hid_report(coords_xy[0], coords_xy[1], coords_z[0],
                        CENTERED_AXIS_ADC, buttons);

        board_led_write(tud_mounted());
        sleep_ms(10);
    }
}
