#!/usr/bin/env python3
"""
udp_joint_receiver.py

Cliente UDP que se suscribe al servidor de Unity (JointStateBroadcaster.cs,
arquitectura de servidor con suscripcion dinamica) y muestra por consola los
angulos articulares del robot.

ARQUITECTURA (Opcion B - servidor con suscripcion dinamica):
    - Unity escucha en un puerto fijo (_serverPort) y YA NO tiene un destino
      fijo configurado en el Inspector. Cualquier cliente que le mande un
      datagrama a ese puerto queda registrado como suscriptor y empieza a
      recibir el estado articular.
    - Este script:
        1. Pregunta por consola la IP y el puerto donde escucha Unity.
        2. Manda un datagrama de suscripcion ("HELLO") a esa direccion.
        3. Repite ese datagrama periodicamente como keep-alive (por si
           Unity tiene habilitado _autoCleanupEnabled, que remueve
           suscriptores inactivos).
        4. Escucha en el mismo socket las respuestas de Unity.

CONTRATO DE PROTOCOLO (mensaje de datos, no confundir con la suscripcion):
    - Un mensaje ASCII por paquete, terminado en '\n'
    - Contenido: "q1,q2,q3,q4,q5,q6"  -> 6 valores en GRADOS
"""

import argparse
import socket
import threading
from datetime import datetime


def _disable_udp_connreset(sock):
    """En Windows, si un datagrama enviado a Unity no encuentra a nadie
    escuchando (por ej. se detuvo el modo Run), el SO devuelve un ICMP
    "Port Unreachable" que Winsock traduce en un WSAECONNRESET (10054)
    en la SIGUIENTE recv() de este socket UDP, aunque UDP no tenga
    conexion. MATLAB (udpport) ya neutraliza esto internamente; en
    Python hay que desactivarlo a mano con este ioctl. No existe en
    Linux/Mac, de ahi el hasattr."""
    if hasattr(socket, "SIO_UDP_CONNRESET"):
        try:
            sock.ioctl(socket.SIO_UDP_CONNRESET, False)
        except OSError:
            pass


def parse_args():
    parser = argparse.ArgumentParser(
        description="Cliente UDP con suscripcion dinamica (Unity -> Python)."
    )
    parser.add_argument(
        "--keepalive-interval",
        type=float,
        default=5.0,
        help="Segundos entre cada datagrama de keep-alive (default: 5.0).",
    )
    parser.add_argument(
        "--bufsize",
        type=int,
        default=1024,
        help="Tamano del buffer de recepcion en bytes (default: 1024).",
    )
    parser.add_argument(
        "--recv-timeout",
        type=float,
        default=0.5,
        help="Timeout de recvfrom en segundos, para poder revisar el "
             "keep-alive sin bloquear indefinidamente (default: 0.5).",
    )
    return parser.parse_args()


def ask_target():
    ip = input("IP de la aplicacion Unity (servidor UDP): ").strip()
    while True:
        port_str = input("Puerto UDP del servidor Unity: ").strip()
        try:
            port = int(port_str)
            break
        except ValueError:
            print("Puerto invalido, ingresa un numero entero.")
    return ip, port


def main():
    args = parse_args()
    target_ip, target_port = ask_target()
    target = (target_ip, target_port)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    _disable_udp_connreset(sock)
    sock.settimeout(args.recv_timeout)

    stop_event = threading.Event()

    def send_subscription():
        try:
            sock.sendto(b"HELLO", target)
        except OSError as e:
            print(f"[WARN] No se pudo enviar la suscripcion: {e}")

    def keepalive_loop():
        while not stop_event.wait(args.keepalive_interval):
            send_subscription()

    print(f"Suscribiendose a {target_ip}:{target_port} ...")
    send_subscription()

    keepalive_thread = threading.Thread(target=keepalive_loop, daemon=True)
    keepalive_thread.start()

    print("Escuchando datos entrantes... (Ctrl+C para detener)\n")

    try:
        while True:
            try:
                data, addr = sock.recvfrom(args.bufsize)
            except socket.timeout:
                continue
            except ConnectionResetError:
                # Unity dejo de escuchar (se detuvo el modo Run) y el
                # ultimo HELLO/keep-alive volvio como ICMP Port
                # Unreachable. No es un error fatal: seguimos
                # reintentando el keep-alive hasta que Unity vuelva.
                continue

            # Debug: ver el paquete tal cual llega, sin parsear.
            print(f"[RAW] bytes={data!r}")

            raw = data.decode("ascii", errors="replace").strip()
            print(f"[RAW] decoded={raw!r}")
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
        stop_event.set()
        sock.close()


if __name__ == "__main__":
    main()