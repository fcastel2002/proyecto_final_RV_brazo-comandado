#include <stdio.h>
#include "pico/stdlib.h"
#include "joystick.h"
#include "tusb.h"
#include "hid_device.h"
#include "usbd.h"

int main()
{
    stdio_init_all();
    tusb_init();
    Joystick joystick_xy(26,27);
    Joystick joystick_z(28, 0);
    const float conv_factor = 3.3f / (1<<12);
    joystick_xy.init();
    joystick_z.init();
    while (true) {
        tud_task();
        uint16_t coords_xy[2];  // Array C-style
        uint16_t coords_z[2];
        joystick_xy.read_coordinate(coords_xy);
        joystick_z.read_coordinate(coords_z);

        float x = coords_xy[0] * conv_factor;
        float y = coords_xy[1] * conv_factor;
        float z = coords_z[0] * conv_factor;

        //printf("X: %.2f, Y: %.2f, Z: %.2f\n", x, y, z);
        if(tud_hid_ready()){
            int16_t report_x = coords_xy[0];
            int16_t report_y = coords_xy[1];
            int16_t report_z = coords_z[0];

            uint8_t report[6];
            report[0] = report_x & 0xFF;
            report[1] = (report_x >> 8) & 0xFF;
            report[2] = report_y & 0xFF;
            report[3] = (report_y >> 8) & 0xFF;
            report[4] = report_z & 0xFF;
            report[5] = (report_z >> 8) & 0xFF;

            tud_hid_report(0, report, sizeof(report));
        }
        sleep_ms(20);
    }
}