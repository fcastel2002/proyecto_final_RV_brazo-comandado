#include "joystick_analog.h"

void Joystick::init(){
    adc_init();
    adc_gpio_init(gpio_q1_);
    if (gpio_q2_ != 0) {
        adc_gpio_init(gpio_q2_);
    }
}

void Joystick::read_coordinate(uint16_t coords[2]){  // Cambia a array C-style
    adc_select_input(gpio_q1_ - 26);
    uint16_t lectura_q1 = adc_read();
    uint16_t lectura_q2 = 0;

    lectura_q1 = apply_filter(lectura_q1, filter_buffer_q1_, filter_index_q1, prev_reading_q1_);
    
    if(gpio_q2_ != 0){
        adc_select_input(gpio_q2_ - 26);
        lectura_q2 = adc_read();
        lectura_q2 = apply_filter(lectura_q2, filter_buffer_q2_, filter_index_q2, prev_reading_q2_);
    } else{
        lectura_q2 = 0;
    }

    coords[0] = lectura_q1;
    coords[1] = lectura_q2;
}

uint16_t Joystick::apply_filter(uint16_t lectura, uint16_t buffer[], int& index, uint16_t& prev_filtered) {  // Cambia a array C-style
    buffer[index] = lectura;
    
    uint32_t sum = 0;
    for (int i = 0; i < filter_size; i++) {
        sum += buffer[i];
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
    
    prev_filtered = filtered;
    
    index = (index + 1) % filter_size;
    return filtered;
}