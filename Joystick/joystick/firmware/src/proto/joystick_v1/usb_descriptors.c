#include "tusb.h"
#include <string.h>

#define VENDOR_ID 0xCAFE
#define PRODUCT_ID 0x4004
#define DEVICE_BCD 0x0100

#define ITF_NUM_HID 0
#define ITF_NUM_TOTAL 1

#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN)
#define CONFIG_NUM 1
#define CONFIG_STR_INDEX 0
#define CONFIG_ATTRIBUTES (TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP)
#define CONFIG_POWER_MA 100

static const uint8_t hid_report_descriptor[] = {
    TUD_HID_REPORT_DESC_GAMEPAD()
};

static const tusb_desc_device_t device_descriptor = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = VENDOR_ID,
    .idProduct = PRODUCT_ID,
    .bcdDevice = DEVICE_BCD,
    .iManufacturer = 0x01,
    .iProduct = 0x02,
    .iSerialNumber = 0x03,
    .bNumConfigurations = CONFIG_NUM
};

static const uint8_t configuration_descriptor[] = {
    TUD_CONFIG_DESCRIPTOR(CONFIG_NUM, ITF_NUM_TOTAL, CONFIG_STR_INDEX,
                          CONFIG_TOTAL_LEN, CONFIG_ATTRIBUTES, CONFIG_POWER_MA),

    TUD_HID_DESCRIPTOR(ITF_NUM_HID, 0, HID_ITF_PROTOCOL_NONE,
                       sizeof(hid_report_descriptor), 0x81,
                       CFG_TUD_HID_EP_BUFSIZE, 10),
};

static const char* const string_descriptors[] = {
    (const char[]){0x09, 0x04},
    "GrupoVRobotics",
    "Joystick VR-v2",
    "00001",
};

static uint16_t string_buf[32];

const uint8_t* tud_descriptor_device_cb(void) {
    return (const uint8_t*)&device_descriptor;
}

const uint8_t* tud_descriptor_configuration_cb(uint8_t index) {
    (void)index;
    return configuration_descriptor;
}

const uint8_t* tud_hid_descriptor_report_cb(uint8_t instance) {
    (void)instance;
    return hid_report_descriptor;
}

const uint16_t* tud_descriptor_string_cb(uint8_t index, uint16_t langid) {
    (void)langid;
    uint8_t chr_count;

    if (index == 0) {
        memcpy(&string_buf[1], string_descriptors[0], 2);
        chr_count = 1;
    } else {
        if (index >= sizeof(string_descriptors) / sizeof(string_descriptors[0])) {
            return NULL;
        }

        const char* str = string_descriptors[index];
        chr_count = (uint8_t)strlen(str);
        if (chr_count > 31) {
            chr_count = 31;
        }

        for (uint8_t i = 0; i < chr_count; i++) {
            string_buf[1 + i] = str[i];
        }
    }

    string_buf[0] = (uint16_t)((TUSB_DESC_STRING << 8) | (chr_count * 2 + 2));
    return string_buf;
}

uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                               uint8_t* buffer, uint16_t reqlen) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)reqlen;
    return 0;
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                           uint8_t const* buffer, uint16_t bufsize) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)bufsize;
}
