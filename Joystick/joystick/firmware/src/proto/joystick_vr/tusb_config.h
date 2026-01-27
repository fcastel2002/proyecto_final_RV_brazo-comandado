// src/tusb_config.h
#pragma once

// Soporte dispositivo (ajustá clases según tu caso)
#define CFG_TUSB_MCU         OPT_MCU_RP2040     // debe coincidir con tu compile_def
//#define CFG_TUSB_OS          OPT_OS_NONE

// Tamaños de memoria
#define CFG_TUD_ENDPOINT0_SIZE 64

// Habilitá clases que uses
#define CFG_TUD_CDC         1
#define CFG_TUD_MSC         0
#define CFG_TUD_HID         1
#define CFG_TUD_MIDI        0
#define CFG_TUD_VENDOR      0

// CDC buffers de ejemplo
#define CFG_TUD_CDC_RX_BUFSIZE 256
#define CFG_TUD_CDC_TX_BUFSIZE 256
