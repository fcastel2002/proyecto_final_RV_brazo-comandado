#include "joystick.h"
#include <stdio.h>

#include "pico/stdlib.h"
#include "hardware/adc.h" // Include the ADC header

void Joystick::init(){
    adc_init();
    adc_gpio_init(gpio_q1_);
    adc_gpio_init(gpio_q2_);
}

std::array<uint16_t,2> Joystick::read_coordinate(){
    std::array<uint16_t,2> coords;
    adc_select_input(gpio_q1_ - 26); // Select the first joystick axis GPIO28
    uint16_t lectura_q1 = adc_read();
    uint16_t lectura_q2 = 0;

    lectura_q1 = apply_filter(lectura_q1, filter_buffer_q1_, filter_index_q1, prev_reading_q1_);
        if(gpio_q2_ != 0){
        adc_select_input(gpio_q2_ - 26); // Select the second joystick axis GPIO27
        lectura_q2 = adc_read();
        lectura_q2 = apply_filter(lectura_q2, filter_buffer_q2_, filter_index_q2, prev_reading_q2_);
    } else{
        lectura_q2 = 0;
    }

    prev_reading_q1_ = lectura_q1;
    prev_reading_q2_ = lectura_q2;
    coords[0] = lectura_q1;
    coords[1] = lectura_q2;
    
    
    return coords;
}
uint16_t Joystick::apply_filter(uint16_t lectura, std::array<uint16_t, filter_size>& buffer, int& index, uint16_t prev_filtered) {
    // Actualizar buffer con la nueva lectura
    buffer[index] = lectura;
    
    // Calcular media móvil
    uint32_t sum = 0;
    for (uint16_t val : buffer) {
        sum += val;
    }
    uint16_t filtered = sum / filter_size;
        if(filtered > prev_filtered){
        if((filtered - prev_filtered) < dead_zone_threshold){
            filtered = prev_filtered;
        }
    } else {
        if((prev_filtered - filtered) < dead_zone_threshold){
            filtered = prev_filtered;
        }
    }
    

    
    // Avanzar índice
    index = (index + 1) % filter_size;
    return filtered;
}
