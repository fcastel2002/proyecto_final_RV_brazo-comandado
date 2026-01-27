#pragma once
#ifndef JOYSTICK_ANALOG_H
#define JOYSTICK_ANALOG_H

#include <stdint.h>  // Usa stdint.h en lugar de cstdint
#include "hardware/adc.h"

class Joystick{
    public:
        Joystick(int gpio_q1, int gpio_q2){
            gpio_q1_ = gpio_q1;
            gpio_q2_ = gpio_q2;
            // Inicializa buffers a 0 (opcional, pero bueno para claridad)
            for(int i = 0; i < filter_size; i++){
                filter_buffer_q1_[i] = 0;
                filter_buffer_q2_[i] = 0;
            }
        }
        void init();
        
        void read_coordinate(uint16_t coords[2]);  // Cambia a array C-style
    private:
        int gpio_q1_;
        int gpio_q2_;
        uint16_t prev_reading_q1_ = 0;
        uint16_t prev_reading_q2_ = 0;
        static const int filter_size = 4;
        static const uint16_t dead_zone_threshold = 10;
        uint16_t filter_buffer_q1_[filter_size];  // Array C-style
        uint16_t filter_buffer_q2_[filter_size];
        int filter_index_q1 = 0;
        int filter_index_q2 = 0;
        uint16_t apply_filter(uint16_t lectura, uint16_t buffer[], int& index, uint16_t& prev_filtered);  // Cambia a array C-style
        
};

#endif // JOYSTICK_ANALOG_H