#!/usr/bin/env python3
"""
udp_robot_viewer.py

Cliente UDP con suscripcion dinamica al servidor de Unity
(JointStateBroadcaster.cs), igual que udp_joint_receiver.py, pero en vez
de imprimir por consola visualiza el robot en 3D usando el Robotics
Toolbox de Peter Corke para Python (roboticstoolbox-python) y su backend
'pyplot' (matplotlib) -- el mismo "stick figure" (esferas + cilindros)
que dibuja robot_udp_plot.m en MATLAB, porque es el mismo motor de
graficado portado a Python.

Nota: el backend 'swift' (three.js) se descarto para este visor. Swift
solo puede renderizar mallas reales (STL/URDF); el DHRobot que arma
este script es puramente cinematico (sin geometria), asi que con Swift
unicamente se veian los ejes del origen y nada mas. 'pyplot' dibuja el
robot a partir de la cinematica directa, igual que MATLAB, y ademas
permite superponer texto (panel de angulos) directamente en la figura.

Pensado para correr en la PC de escritorio, donde no hay restricciones
de compilacion como en Termux/Android: numpy, scipy y spatialmath
instalan sin drama via pip.

Reutiliza la MISMA tabla DH y la MISMA formula de calibracion mecanica
(JointConfig.Offset/Factor) que ya validamos en:
    - robot_udp_plot.m          (MATLAB, Robotics Toolbox)
    - udp_joint_dashboard.py    (celular, Three.js embebido)

Instalacion:
    pip install roboticstoolbox-python
    (instala numpy, scipy, spatialmath-python y matplotlib como
    dependencias automaticamente)

Uso:
    python udp_robot_viewer.py
    (pregunta la IP/puerto de Unity por consola, igual que los otros scripts)

Al arrancar se abre una ventana de matplotlib con el robot y un panel
con los 6 angulos articulares actuales (en grados, tal como llegan de
Unity).
"""

import argparse
import socket
import time

import numpy as np
import roboticstoolbox as rtb
from spatialmath import SE3

# ---------------------------------------------------------------------------
# Definicion del robot (DH confirmado por Claude Code contra el prefab
# KUKA_KR210_R3100-2 de Preliy Flange)
# ---------------------------------------------------------------------------
# (alpha, a, d, theta_offset_DH) -- convencion estandar (no modified),
# igual que en robot_udp_plot.m
DH_PARAMS = [
    (-np.pi / 2, 0.33,  0.645, 0.0),         # Joint_1
    (0.0,        1.35,  0.0,   -np.pi / 2),  # Joint_2
    (-np.pi / 2, 0.115, 0.0,   0.0),         # Joint_3
    (-np.pi / 2, 0.0,   1.42,  0.0),         # Joint_4
    (np.pi / 2,  0.0,   0.0,   0.0),         # Joint_5
    (0.0,        0.0,   0.24,  0.0),         # Joint_6
]

# Calibracion mecanica JointConfig (aparte del DH), confirmada por Claude Code:
#   adjusted_deg = (q_unity_deg + Offset) * Factor
#   theta_rad    = deg2rad(adjusted_deg) + theta_offset_DH  (esto ultimo va
#                  en el 'offset' de cada RevoluteDH, no hace falta sumarlo aca)
CONFIG_OFFSET_DEG = np.array([0.0, 90.0, -90.0, 0.0, 0.0, 0.0])
CONFIG_FACTOR = np.array([1.0, 1.0, 1.0, 1.0, -1.0, 1.0])

# ---------------------------------------------------------------------------
# Gripper (OnRobot RG2 montado en el TCP) -- representacion parametrica, no
# un calco del mesh: el prefab de Unity tiene una jerarquia de escalas FBX
# anidadas (x347.49, x0.001, x999.99994) demasiado inconsistente para
# reconstruir cotas exactas sin abrir el Editor. Lo que SI esta confirmado:
#   - Carrera total (stroke) 0-100mm, ver Ctrl_OnRobotRG2_Custom.cs (s_max).
#   - Se agarra a la muneca (mismo frame que ya usa el 'tool' del DHRobot).
# Cuerpo y largo de dedo son valores representativos, ajustables aca.
# Cotas ajustadas a la ficha tecnica real del RG2 (~195-236mm base-a-dedo);
# antes se veia desproporcionadamente chico contra el resto de los eslabones.
GRIPPER_BODY_LENGTH_M = 0.09
GRIPPER_FINGER_LENGTH_M = 0.11
GRIPPER_MAX_HALF_STROKE_M = 0.05  # mitad de los 100mm de carrera total


def build_robot():
    links = [
        rtb.RevoluteDH(alpha=alpha, a=a, d=d, offset=theta_offset)
        for (alpha, a, d, theta_offset) in DH_PARAMS
    ]
    # Frame "Flange": rotacion fija de 180 grados al final del TCP (a=0,d=0).
    robot = rtb.DHRobot(links, name="KR210_sim", tool=SE3.Rz(np.pi))
    return robot


def unity_deg_to_q_rad(q_unity_deg):
    """Misma formula usada en MATLAB y en el dashboard del celular."""
    q_unity_deg = np.asarray(q_unity_deg, dtype=float)
    adjusted_deg = (q_unity_deg + CONFIG_OFFSET_DEG) * CONFIG_FACTOR
    return np.deg2rad(adjusted_deg)


def gripper_segments(robot, q_rad, opening_fraction):
    """Devuelve 3 segmentos (body, dedo izq, dedo der) como pares de puntos
    mundo ((x0,y0,z0), (x1,y1,z1)), colgando del TCP (mismo frame 'tool'
    que ya usa el DHRobot, con el Rz(pi) de Flange incluido)."""
    t_tcp = robot.fkine(q_rad)
    half_gap = np.clip(opening_fraction, 0.0, 1.0) * GRIPPER_MAX_HALF_STROKE_M

    def world_point(x, y, z):
        return np.asarray(t_tcp * np.array([x, y, z]), dtype=float).flatten()

    origin = world_point(0.0, 0.0, 0.0)
    body_end = world_point(0.0, 0.0, GRIPPER_BODY_LENGTH_M)
    left_root = world_point(-half_gap, 0.0, GRIPPER_BODY_LENGTH_M)
    left_tip = world_point(-half_gap, 0.0, GRIPPER_BODY_LENGTH_M + GRIPPER_FINGER_LENGTH_M)
    right_root = world_point(half_gap, 0.0, GRIPPER_BODY_LENGTH_M)
    right_tip = world_point(half_gap, 0.0, GRIPPER_BODY_LENGTH_M + GRIPPER_FINGER_LENGTH_M)

    return [
        (origin, body_end),
        (left_root, left_tip),
        (right_root, right_tip),
    ]


# ---------------------------------------------------------------------------
# Cliente UDP (mismo protocolo de suscripcion/keep-alive de siempre)
# ---------------------------------------------------------------------------
def ask_target():
    ip = input("IP de la aplicacion Unity (servidor UDP): ").strip()
    while True:
        port_str = input("Puerto UDP del servidor Unity: ").strip()
        try:
            return ip, int(port_str)
        except ValueError:
            print("Puerto invalido, ingresa un numero entero.")


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


def receive_latest(sock, bufsize):
    """Espera el proximo datagrama y descarta cualquier backlog mas viejo
    que ya haya llegado mientras tanto, quedandose solo con el ultimo.

    Mismo criterio que robot_udp_plot.m: si el render tarda mas que el
    intervalo de envio de Unity, el buffer del SO va acumulando paquetes
    y sin este drenado el visor se ve cada vez mas retrasado respecto de
    Unity. Devuelve (data, addr, discarded).
    """
    data, addr = sock.recvfrom(bufsize)  # bloqueante, respeta sock.settimeout()

    discarded = 0
    original_timeout = sock.gettimeout()
    sock.settimeout(0.0)
    try:
        while True:
            data, addr = sock.recvfrom(bufsize)
            discarded += 1
    except OSError:
        pass
    finally:
        sock.settimeout(original_timeout)

    return data, addr, discarded


def parse_args():
    parser = argparse.ArgumentParser(
        description="Visor 3D (Robotics Toolbox + Swift) del estado articular del robot."
    )
    parser.add_argument("--keepalive-interval", type=float, default=5.0,
                         help="Segundos entre cada datagrama de keep-alive (default: 5.0).")
    parser.add_argument("--bufsize", type=int, default=1024,
                         help="Tamano del buffer de recepcion UDP (default: 1024).")
    parser.add_argument("--recv-timeout", type=float, default=0.5,
                         help="Timeout de recvfrom en segundos (default: 0.5).")
    parser.add_argument("--verbose", action="store_true",
                         help="Imprime cada paquete crudo recibido (debug).")
    return parser.parse_args()


def main():
    args = parse_args()
    target_ip, target_port = ask_target()
    target = (target_ip, target_port)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    _disable_udp_connreset(sock)
    sock.settimeout(args.recv_timeout)

    def send_subscription():
        try:
            sock.sendto(b"HELLO", target)
        except OSError as e:
            print(f"[WARN] No se pudo enviar la suscripcion: {e}")

    print(f"Suscribiendose a {target_ip}:{target_port} ...")
    send_subscription()
    last_keepalive = time.monotonic()

    robot = build_robot()

    print("Abriendo visor 3D (matplotlib)...")
    # Limites fijos porque el alcance del KR210 (~2.9 m) no entra en el
    # autoscale por defecto de PyPlot (+-0.5 m).
    env = robot.plot(
        robot.q,
        backend="pyplot",
        block=False,
        limits=[-2.5, 2.5, -2.5, 2.5, 0.0, 3.0],
    )

    # Panel de texto con los angulos articulares actuales (grados, tal
    # como llegan de Unity, antes de aplicar Offset/Factor).
    joint_text = env.fig.text(
        0.02, 0.98, "", va="top", ha="left", family="monospace", fontsize=9
    )

    def format_panel(q_unity_deg):
        lines = ["Articulaciones (deg):"]
        lines += [f"  J{i}: {v:7.2f}" for i, v in enumerate(q_unity_deg, start=1)]
        return "\n".join(lines)

    joint_text.set_text(format_panel([0.0] * 6))

    # Segmentos del gripper (body + 2 dedos), colgados del TCP. Se crean una
    # sola vez y se reposicionan en cada paquete, igual que joint_text.
    gripper_lines = [
        env.ax.plot([], [], [], color="black", linewidth=5)[0] for _ in range(3)
    ]

    def update_gripper_visual(q_rad, opening_fraction):
        segments = gripper_segments(robot, q_rad, opening_fraction)
        for line, (p0, p1) in zip(gripper_lines, segments):
            xs, ys, zs = zip(p0, p1)
            line.set_data_3d(xs, ys, zs)

    update_gripper_visual(robot.q, 0.0)

    print("Escuchando datos entrantes... (Ctrl+C para detener)\n")

    try:
        while True:
            if time.monotonic() - last_keepalive >= args.keepalive_interval:
                send_subscription()
                last_keepalive = time.monotonic()

            try:
                data, addr, discarded = receive_latest(sock, args.bufsize)
            except socket.timeout:
                env.step(0.05)  # sigue refrescando la ventana aunque no llegue nada
                continue
            except ConnectionResetError:
                # Unity dejo de escuchar (se detuvo el modo Run) y el
                # ultimo HELLO/keep-alive volvio como ICMP Port
                # Unreachable. No es un error fatal: seguimos
                # reintentando el keep-alive hasta que Unity vuelva.
                env.step(0.05)
                continue

            raw = data.decode("ascii", errors="replace").strip()
            if args.verbose:
                extra = f" ({discarded} descartados)" if discarded else ""
                print(f"[RAW] {raw!r} desde {addr[0]}:{addr[1]}{extra}")

            if not raw:
                continue

            parts = raw.split(",")
            if len(parts) not in (6, 7):
                continue

            try:
                values = [float(p) for p in parts]
            except ValueError:
                continue

            q_unity_deg = values[:6]
            # Paquetes viejos (sin 7mo campo) no traen estado de gripper:
            # se asume cerrado, que es el estado inicial real en Unity
            # (GripperController.isGripperClosed arranca en true).
            gripper_opening = values[6] if len(values) == 7 else 0.0

            q_rad = unity_deg_to_q_rad(q_unity_deg)
            robot.q = q_rad
            joint_text.set_text(format_panel(q_unity_deg))
            update_gripper_visual(q_rad, gripper_opening)
            env.step(0.05)

    except KeyboardInterrupt:
        print("\nDetenido por el usuario.")
    finally:
        sock.close()
        try:
            env.close()
        except Exception:
            pass


if __name__ == "__main__":
    main()