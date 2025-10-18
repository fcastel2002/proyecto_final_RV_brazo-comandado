#include <stdio.h>
#include "pico/stdlib.h"
#include "joystick.h"

int main()
{
    stdio_init_all();
    Joystick joystick_xy(26,27);
    Joystick joystick_z(28, 0);
    const float conv_factor = 3.3f / (1<<12);
    joystick_xy.init();
    joystick_z.init();
    while (true) {
        auto coords_xy = joystick_xy.read_coordinate();
        auto coords_z = joystick_z.read_coordinate();

        float x = coords_xy[0];
        float y = coords_xy[1];
        float z = coords_z[0] ;

        printf("X: %.2f, Y: %.2f, Z: %.2f\n", x, y, z);
        sleep_ms(100);
    }
}
