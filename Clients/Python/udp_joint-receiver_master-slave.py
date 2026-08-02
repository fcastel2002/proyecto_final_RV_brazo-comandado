#!/usr/bin/env python3
"""
udp_joint_receiver.py

Receptor UDP que imprime por consola los angulos articulares del robot
enviados desde Unity (JointStateBroadcaster.cs).

CONTRATO DE PROTOCOLO (debe coincidir con lo que envia Unity):
    - Transporte: UDP
    - Un mensaje ASCII por paquete, terminado en '\n'
    - Contenido: "q1,q2,q3,q4,q5,q6"  -> 6 valores en GRADOS, separados
      por coma, mismo orden que MechanicalGroup.JointState.Value

Uso tipico (esta PC = "computadora de escritorio", receptora via Tailscale):
    1. Confirmar la IP de Tailscale de esta PC:
           tailscale ip -4
    2. En Unity (notebook), en el componente JointStateBroadcaster:
           Target Ip   -> la IP de Tailscale de ESTA PC (la que corre este script)
           Target Port -> el mismo puerto que le pases a --port aca abajo
    3. Correr este script en la PC de escritorio:
           python udp_joint_receiver.py --port 25001
    4. Entrar en Play mode en Unity y mover el robot con el joystick.

Por defecto escucha en 0.0.0.0 (todas las interfaces de red de esta PC,
incluida la de Tailscale), asi que no hace falta tocar --host salvo que
quieras restringirlo a una interfaz especifica.
"""

import argparse
import socket
from datetime import datetime


def parse_args():
    parser = argparse.ArgumentParser(
        description="Receptor UDP de angulos articulares (Unity -> Python)."
    )
    parser.add_argument(
        "--host",
        default="0.0.0.0",
        help="IP local en la que escuchar (default: 0.0.0.0, todas las interfaces).",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=25001,
        help="Puerto UDP local (default: 25001). Debe coincidir con el "
             "'Target Port' configurado en JointStateBroadcaster en Unity.",
    )
    parser.add_argument(
        "--bufsize",
        type=int,
        default=1024,
        help="Tamano del buffer de recepcion en bytes (default: 1024).",
    )
    return parser.parse_args()


def main():
    args = parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((args.host, args.port))

    print(f"Escuchando UDP en {args.host}:{args.port} ... (Ctrl+C para detener)\n")

    try:
        while True:
            data, addr = sock.recvfrom(args.bufsize)

            raw = data.decode("ascii", errors="replace").strip()
            if not raw:
                continue

            parts = raw.split(",")
            if len(parts) != 6:
                print(f"[WARN] Paquete con {len(parts)} campos (se esperaban 6) "
                      f"desde {addr[0]}:{addr[1]}: \"{raw}\"")
                continue

            try:
                q = [float(p) for p in parts]
            except ValueError:
                print(f"[WARN] Paquete mal formado desde {addr[0]}:{addr[1]}: \"{raw}\"")
                continue

            ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
            valores = "  ".join(f"{v:8.2f}" for v in q)
            print(f"[{ts}] ({addr[0]}:{addr[1]}) q = [{valores}] deg")

    except KeyboardInterrupt:
        print("\nDetenido por el usuario.")
    finally:
        sock.close()


if __name__ == "__main__":
    main()