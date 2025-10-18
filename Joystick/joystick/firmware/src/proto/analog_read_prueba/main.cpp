    #include <stdio.h>
    #include "pico/stdlib.h"
    #include "hardware/gpio.h"
    #include "hardware/adc.h"
    #include "joystick.h"

    int main()
    {
        
        stdio_init_all();

        adc_init();
        adc_gpio_init(26); // GPIO 26 is ADC0
        adc_select_input(0); // Select ADC0 (GPIO 26)
        while (true) {
        const float conversion_factor = 3.3f / (1 << 12); // 12-bit ADC
        uint16_t result = adc_read(); // Read the ADC value

        }

    }
