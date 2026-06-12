#include "vibration_motor.h"

#include "hardware/gpio.h"

void vibration_motor_init(void) {
    gpio_init(VIBRATION_MOTOR_GPIO);
    gpio_set_dir(VIBRATION_MOTOR_GPIO, GPIO_OUT);
    gpio_put(VIBRATION_MOTOR_GPIO, 0);
}

void vibration_motor_set(bool enabled) {
    gpio_put(VIBRATION_MOTOR_GPIO, enabled ? 1 : 0);
}
