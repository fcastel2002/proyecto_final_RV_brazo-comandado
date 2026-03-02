/**
 * @file main.cpp
 * @brief ESP8266 SoftAP — UDP receiver for 6-DOF joint telemetry.
 *
 * Receives a binary packet of 6 x float (little-endian, 24 bytes total)
 * over UDP port 5000 and prints the decoded values on Serial at 115200 baud.
 */

#include <ESP8266WiFi.h>
#include <WiFiUdp.h>

// ---------------------------------------------------------------------------
// Build-time constants (overridable via platformio.ini build_flags)
// ---------------------------------------------------------------------------
#ifndef WIFI_SSID
#define WIFI_SSID "RobotVR_AP"
#endif

#ifndef WIFI_PASS
#define WIFI_PASS "12345678"
#endif

#ifndef UDP_PORT
#define UDP_PORT 5000
#endif

#ifndef NUM_JOINTS
#define NUM_JOINTS 6
#endif

// ---------------------------------------------------------------------------
// Expected payload size: NUM_JOINTS * sizeof(float)
// ---------------------------------------------------------------------------
static constexpr size_t EXPECTED_PAYLOAD = NUM_JOINTS * sizeof(float);

// ---------------------------------------------------------------------------
// Globals
// ---------------------------------------------------------------------------
WiFiUDP udp;

// Static IP configuration for the AP interface
static const IPAddress apIP(192, 168, 4, 1);
static const IPAddress gateway(192, 168, 4, 1);
static const IPAddress subnet(255, 255, 255, 0);

// Aligned receive buffer to avoid unaligned-access faults
static union {
    uint8_t raw[EXPECTED_PAYLOAD];
    float   joints[NUM_JOINTS];
} __attribute__((aligned(4))) rxBuf;

// ---------------------------------------------------------------------------
// setup()
// ---------------------------------------------------------------------------
void setup() {
    Serial.begin(115200);
    delay(100);
    Serial.println();
    Serial.println(F("== RobotVR ESP8266 UDP Receiver =="));

    // ---- WiFi SoftAP ---------------------------------------------------
    WiFi.mode(WIFI_AP);
    WiFi.softAPConfig(apIP, gateway, subnet);

    const bool ok = WiFi.softAP(WIFI_SSID, WIFI_PASS);
    if (!ok) {
        Serial.println(F("[ERROR] Failed to start SoftAP"));
        while (true) { delay(1000); }
    }

    Serial.print(F("[INFO]  SSID : ")); Serial.println(WIFI_SSID);
    Serial.print(F("[INFO]  IP   : ")); Serial.println(WiFi.softAPIP());
    Serial.print(F("[INFO]  PORT : ")); Serial.println(UDP_PORT);

    // ---- UDP listener --------------------------------------------------
    udp.begin(UDP_PORT);
    Serial.println(F("[INFO]  UDP listener started."));
}

// ---------------------------------------------------------------------------
// loop()
// ---------------------------------------------------------------------------
void loop() {
    const int packetSize = udp.parsePacket();
    if (packetSize <= 0) {
        return; // No packet available
    }

    // Validate payload length
    if (static_cast<size_t>(packetSize) != EXPECTED_PAYLOAD) {
        // Drain the unexpected packet
        while (udp.available()) { udp.read(); }
        Serial.printf("[WARN] Bad packet size: %d (expected %u)\n",
                      packetSize, EXPECTED_PAYLOAD);
        return;
    }

    // Read directly into the aligned union buffer
    const int bytesRead = udp.read(rxBuf.raw, EXPECTED_PAYLOAD);
    if (static_cast<size_t>(bytesRead) != EXPECTED_PAYLOAD) {
        Serial.println(F("[WARN] Incomplete read"));
        return;
    }

    // ---- Print decoded joint values ------------------------------------
    Serial.print(F("Joints [rad]: "));
    for (uint8_t i = 0; i < NUM_JOINTS; ++i) {
        Serial.print(rxBuf.joints[i], 4);   // 4 decimal places
        if (i < NUM_JOINTS - 1) {
            Serial.print(F(" | "));
        }
    }
    Serial.println();
}
