import time
import hid

VID = 0xCAFE
PID = 0x4004

dev = hid.device()
dev.open(VID, PID)

# Como no usas Report ID, hidapi en Windows espera un 0 inicial.
def motor(on: bool):
    data = [0x00, 0x01 if on else 0x00]
    written = dev.write(data)
    print(f"Enviados {written}/{len(data)} bytes")

print("Motor ON")
motor(True)
time.sleep(1)

print("Motor OFF")
motor(False)

dev.close()
