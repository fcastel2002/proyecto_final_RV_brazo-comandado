#pragma once
#ifndef JOYSTICK
#define JOYSTICK
#include <cstdint>
const int JOYSTICK_X_PIN = 26; // GPIO 26 for X-axis
const int JOYSTICK_Y_PIN = 27; // GPIO 27 for Y-axis
const int JOYSTICK_Z_PIN = 28; // GPIO 28 for Z-axis 

typedef struct{
    uint16_t x;
    uint16_t y;

} joystick_xy_state_t;

typedef struct{
    uint16_t z;
} joystick_z_state_t;

#endif