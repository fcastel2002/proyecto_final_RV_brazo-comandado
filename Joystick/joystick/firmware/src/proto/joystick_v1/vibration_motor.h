#pragma once
#ifndef VIBRATION_MOTOR_H
#define VIBRATION_MOTOR_H

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

#define VIBRATION_MOTOR_GPIO 15
#define VIBRATION_REPORT_ON_MASK 0x01

void vibration_motor_init(void);
void vibration_motor_set(bool enabled);

#ifdef __cplusplus
}
#endif

#endif // VIBRATION_MOTOR_H
