#!/usr/bin/env python3
"""
udp_robot_dashboard.py

Cliente UDP con suscripcion dinamica al servidor de Unity
(JointStateBroadcaster.cs). Sirve un dashboard web local con:
  - Gauges circulares por articulacion (lectura numerica rapida).
  - Una vista 3D del robot (Three.js, corre en el navegador) que calcula
    la cinematica directa con la MISMA tabla DH y formula de
    Offset/Factor que ya usamos en robot_udp_plot.m (MATLAB) y en
    udp_robot_viewer.py (matplotlib).

Version de escritorio de Clients/Android-Termux/udp_joint_dashboard.py.
El render 3D lo hace el navegador via WebGL (Three.js), no este script,
asi que no depende de roboticstoolbox-python/Swift ni de compilar nada:
solo Flask.

Igual que en robot_udp_plot.m (MATLAB), el hilo de red drena el socket
y se queda solo con el ULTIMO paquete disponible en cada vuelta, para
que un backlog de UDP no se traduzca en delay acumulado en el dashboard.

Instalacion:
    pip install flask

Uso:
    python udp_robot_dashboard.py
    (pregunta la IP/puerto de Unity por consola, igual que los otros scripts)

Despues abrir en el navegador:
    http://127.0.0.1:8080
"""

import argparse
import logging
import socket
import threading
import time

from flask import Flask, jsonify, render_template_string

# ---------------------------------------------------------------------------
# Estado compartido entre el hilo de red y el servidor web
# ---------------------------------------------------------------------------
_state_lock = threading.Lock()
_state = {
    "q": [0.0] * 6,
    "last_packet_time": 0.0,
    "packet_count": 0,
    "target_ip": "",
    "target_port": 0,
}

GAUGE_MIN_DEG = -180.0
GAUGE_MAX_DEG = 180.0


def ask_target():
    ip = input("IP de la aplicacion Unity (servidor UDP): ").strip()
    while True:
        port_str = input("Puerto UDP del servidor Unity: ").strip()
        try:
            port = int(port_str)
            return ip, port
        except ValueError:
            print("Puerto invalido, ingresa un numero entero.")


def receive_latest(sock, bufsize):
    """Espera el proximo datagrama y descarta cualquier backlog mas viejo
    que ya haya llegado mientras tanto, quedandose solo con el ultimo
    (mismo criterio que robot_udp_plot.m)."""
    data = sock.recv(bufsize)  # bloqueante, respeta sock.settimeout()

    original_timeout = sock.gettimeout()
    sock.settimeout(0.0)
    try:
        while True:
            data = sock.recv(bufsize)
    except OSError:
        pass
    finally:
        sock.settimeout(original_timeout)

    return data


def udp_worker(target_ip, target_port, keepalive_interval, bufsize, recv_timeout):
    """Hilo de fondo: suscripcion + keep-alive + recepcion de paquetes."""
    target = (target_ip, target_port)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(recv_timeout)

    def send_subscription():
        try:
            sock.sendto(b"HELLO", target)
        except OSError:
            pass

    send_subscription()
    last_keepalive = time.monotonic()

    with _state_lock:
        _state["target_ip"] = target_ip
        _state["target_port"] = target_port

    while True:
        if time.monotonic() - last_keepalive >= keepalive_interval:
            send_subscription()
            last_keepalive = time.monotonic()

        try:
            data = receive_latest(sock, bufsize)
        except socket.timeout:
            continue

        raw = data.decode("ascii", errors="replace").strip()
        if not raw:
            continue

        parts = raw.split(",")
        if len(parts) != 6:
            continue

        try:
            q = [float(p) for p in parts]
        except ValueError:
            continue

        with _state_lock:
            _state["q"] = q
            _state["last_packet_time"] = time.monotonic()
            _state["packet_count"] += 1


# ---------------------------------------------------------------------------
# Servidor web
# ---------------------------------------------------------------------------
app = Flask(__name__)

PAGE_HTML = """
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Estado articular - KUKA KR210</title>
<script src="https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js"></script>
<style>
  :root { color-scheme: dark; }
  body {
    margin: 0; padding: 16px;
    background: #14161a; color: #e6e6e6;
    font-family: -apple-system, Roboto, Arial, sans-serif;
  }
  h1 { font-size: 1.1rem; font-weight: 600; margin: 0 0 4px 0; }
  .subtitle { color: #8a8f98; font-size: 0.85rem; margin-bottom: 12px; }
  .status {
    display: inline-block; padding: 3px 10px; border-radius: 999px;
    font-size: 0.75rem; font-weight: 600; margin-bottom: 14px;
  }
  .status.ok { background: #1f4d2e; color: #6fdc8c; }
  .status.stale { background: #4d3a1f; color: #f0b25c; }
  .status.dead { background: #4d1f1f; color: #f06c6c; }

  #viewer3d {
    width: 100%; height: 520px; border-radius: 14px; overflow: hidden;
    background: #0c0d10; margin-bottom: 18px; touch-action: none;
    position: relative;
  }
  #viewer3d canvas { display: block; }
  .viewer-hint {
    position: absolute; bottom: 8px; left: 12px;
    font-size: 0.7rem; color: #565b66; pointer-events: none;
  }

  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
    gap: 14px;
  }
  .card {
    background: #1c1f26; border-radius: 14px; padding: 14px;
    display: flex; flex-direction: column; align-items: center;
  }
  .card .label { font-size: 0.8rem; color: #8a8f98; margin-bottom: 8px; }
  .gauge {
    width: 92px; height: 92px; border-radius: 50%;
    display: flex; align-items: center; justify-content: center;
    background: conic-gradient(#4fc3f7 var(--pct, 0%), #2a2e37 0);
    transition: background 0.08s linear;
  }
  .gauge-inner {
    width: 72px; height: 72px; border-radius: 50%;
    background: #1c1f26;
    display: flex; align-items: center; justify-content: center;
    font-variant-numeric: tabular-nums;
    font-size: 0.95rem; font-weight: 600;
  }
  .footer { margin-top: 18px; color: #565b66; font-size: 0.75rem; }
</style>
</head>
<body>
  <h1>Estado articular &mdash; KUKA KR210</h1>
  <div class="subtitle" id="target-info">Conectando...</div>
  <div class="status stale" id="status-badge">esperando datos</div>

  <div id="viewer3d">
    <div class="viewer-hint">arrastrar: rotar &middot; rueda: zoom</div>
  </div>

  <div class="grid" id="grid"></div>

  <div class="footer" id="packet-info"></div>

<script>
// ---------------------------------------------------------------------
// Gauges (lectura numerica rapida)
// ---------------------------------------------------------------------
const GAUGE_MIN = -180.0;
const GAUGE_MAX = 180.0;

const grid = document.getElementById('grid');
for (let i = 0; i < 6; i++) {
  const card = document.createElement('div');
  card.className = 'card';
  card.innerHTML = `
    <div class="label">J${i + 1}</div>
    <div class="gauge" id="gauge-${i}">
      <div class="gauge-inner" id="value-${i}">0.0&deg;</div>
    </div>`;
  grid.appendChild(card);
}

function pctFor(value) {
  const clamped = Math.min(GAUGE_MAX, Math.max(GAUGE_MIN, value));
  return ((clamped - GAUGE_MIN) / (GAUGE_MAX - GAUGE_MIN)) * 100;
}

// ---------------------------------------------------------------------
// Cinematica directa (misma tabla DH y formula que robot_udp_plot.m)
// ---------------------------------------------------------------------
// [alpha(rad), a(m), d(m), theta_offset_DH(rad)]
const DH = [
  [-Math.PI / 2, 0.33,  0.645, 0            ],  // Joint_1
  [0,            1.35,  0,     -Math.PI / 2 ],  // Joint_2
  [-Math.PI / 2, 0.115, 0,     0            ],  // Joint_3
  [-Math.PI / 2, 0,     1.42,  0            ],  // Joint_4
  [Math.PI / 2,  0,     0,     0            ],  // Joint_5
  [0,            0,     0.24,  0            ],  // Joint_6
];
const CONFIG_OFFSET_DEG = [0, 90, -90, 0, 0, 0];
const CONFIG_FACTOR     = [1, 1, 1, 1, -1, 1];

function computeJointPositions(qDeg) {
  let T = new THREE.Matrix4(); // identidad = frame Base
  const points = [new THREE.Vector3(0, 0, 0)];

  for (let i = 0; i < 6; i++) {
    const adjustedDeg = (qDeg[i] + CONFIG_OFFSET_DEG[i]) * CONFIG_FACTOR[i];
    const [alpha, a, d, thetaOffset] = DH[i];
    const theta = (adjustedDeg * Math.PI) / 180 + thetaOffset;

    const Rz = new THREE.Matrix4().makeRotationZ(theta);
    const Tz = new THREE.Matrix4().makeTranslation(0, 0, d);
    const Tx = new THREE.Matrix4().makeTranslation(a, 0, 0);
    const Rx = new THREE.Matrix4().makeRotationX(alpha);

    const A = new THREE.Matrix4().multiply(Rz).multiply(Tz).multiply(Tx).multiply(Rx);
    T = T.clone().multiply(A);
    points.push(new THREE.Vector3().setFromMatrixPosition(T));
  }
  return points;
}

// ---------------------------------------------------------------------
// Escena Three.js
// ---------------------------------------------------------------------
const container = document.getElementById('viewer3d');
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0c0d10);

const camera = new THREE.PerspectiveCamera(50, container.clientWidth / container.clientHeight, 0.01, 100);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(container.clientWidth, container.clientHeight);
container.insertBefore(renderer.domElement, container.firstChild);

scene.add(new THREE.AmbientLight(0xffffff, 0.6));
const dirLight = new THREE.DirectionalLight(0xffffff, 0.8);
dirLight.position.set(2, 2, 3);
scene.add(dirLight);

const grid3d = new THREE.GridHelper(4, 8, 0x333844, 0x22252c);
grid3d.rotation.x = Math.PI / 2; // el grid nace en el plano XZ; lo llevamos al plano XY
scene.add(grid3d);

const robotGroup = new THREE.Group();
scene.add(robotGroup);

function createLink(p1, p2, radius, color) {
  const dir = new THREE.Vector3().subVectors(p2, p1);
  const length = dir.length();
  if (length < 1e-6) return null;

  const geometry = new THREE.CylinderGeometry(radius, radius, length, 12);
  const material = new THREE.MeshStandardMaterial({ color });
  const mesh = new THREE.Mesh(geometry, material);

  mesh.position.copy(p1).addScaledVector(dir, 0.5);
  const axis = new THREE.Vector3(0, 1, 0);
  mesh.quaternion.copy(new THREE.Quaternion().setFromUnitVectors(axis, dir.clone().normalize()));
  return mesh;
}

function updateRobotVisual(qDeg) {
  while (robotGroup.children.length) {
    robotGroup.remove(robotGroup.children[0]);
  }

  const points = computeJointPositions(qDeg);
  const jointColor = 0xff6a13; // naranja KUKA
  const linkColor = 0x2b2f38;

  points.forEach((p, idx) => {
    const sphere = new THREE.Mesh(
      new THREE.SphereGeometry(idx === 0 ? 0.05 : 0.045, 14, 14),
      new THREE.MeshStandardMaterial({ color: jointColor })
    );
    sphere.position.copy(p);
    robotGroup.add(sphere);
  });

  for (let i = 0; i < points.length - 1; i++) {
    const link = createLink(points[i], points[i + 1], 0.035, linkColor);
    if (link) robotGroup.add(link);
  }
}

updateRobotVisual([0, 0, 0, 0, 0, 0]);

// ---------------------------------------------------------------------
// Camara orbital manual (mouse), sin dependencias extra
// ---------------------------------------------------------------------
const target = new THREE.Vector3(0, 0, 1.0);
const camState = { radius: 4.5, theta: Math.PI / 4, phi: Math.PI / 3 };

function updateCameraFromState() {
  const x = target.x + camState.radius * Math.sin(camState.phi) * Math.cos(camState.theta);
  const y = target.y + camState.radius * Math.sin(camState.phi) * Math.sin(camState.theta);
  const z = target.z + camState.radius * Math.cos(camState.phi);
  camera.position.set(x, y, z);
  camera.up.set(0, 0, 1);
  camera.lookAt(target);
}
updateCameraFromState();

let dragging = false, lastX = 0, lastY = 0;
renderer.domElement.addEventListener('pointerdown', (e) => {
  dragging = true; lastX = e.clientX; lastY = e.clientY;
  renderer.domElement.setPointerCapture(e.pointerId);
});
renderer.domElement.addEventListener('pointerup', () => { dragging = false; });
renderer.domElement.addEventListener('pointercancel', () => { dragging = false; });
renderer.domElement.addEventListener('pointermove', (e) => {
  if (!dragging) return;
  const dx = e.clientX - lastX, dy = e.clientY - lastY;
  lastX = e.clientX; lastY = e.clientY;
  camState.theta -= dx * 0.006;
  camState.phi = Math.min(Math.PI - 0.05, Math.max(0.05, camState.phi - dy * 0.006));
  updateCameraFromState();
});
renderer.domElement.addEventListener('wheel', (e) => {
  camState.radius = Math.min(10, Math.max(1.2, camState.radius + e.deltaY * 0.002));
  updateCameraFromState();
  e.preventDefault();
}, { passive: false });

function onResize() {
  const w = container.clientWidth, h = container.clientHeight;
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  renderer.setSize(w, h);
}
window.addEventListener('resize', onResize);

function animate() {
  requestAnimationFrame(animate);
  renderer.render(scene, camera);
}
animate();

// ---------------------------------------------------------------------
// Polling del backend (mismo endpoint que ya usaban los gauges)
// ---------------------------------------------------------------------
async function poll() {
  try {
    const res = await fetch('/api/state', { cache: 'no-store' });
    const data = await res.json();

    document.getElementById('target-info').textContent =
      `Suscripto a ${data.target_ip}:${data.target_port}`;

    for (let i = 0; i < 6; i++) {
      const v = data.q[i];
      document.getElementById(`gauge-${i}`).style.setProperty('--pct', pctFor(v) + '%');
      document.getElementById(`value-${i}`).innerHTML = v.toFixed(1) + '&deg;';
    }
    updateRobotVisual(data.q);

    const badge = document.getElementById('status-badge');
    if (data.age_seconds < 1.0) {
      badge.className = 'status ok';
      badge.textContent = 'recibiendo datos';
    } else if (data.age_seconds < 5.0) {
      badge.className = 'status stale';
      badge.textContent = `sin datos hace ${data.age_seconds.toFixed(1)}s`;
    } else {
      badge.className = 'status dead';
      badge.textContent = 'sin conexion';
    }

    document.getElementById('packet-info').textContent =
      `Paquetes recibidos: ${data.packet_count}`;
  } catch (e) {
    // silencioso: reintenta en el proximo poll
  }
}

setInterval(poll, 100);
poll();
</script>
</body>
</html>
"""


@app.route("/")
def index():
    return render_template_string(PAGE_HTML)


@app.route("/api/state")
def api_state():
    with _state_lock:
        q = list(_state["q"])
        last_packet_time = _state["last_packet_time"]
        packet_count = _state["packet_count"]
        target_ip = _state["target_ip"]
        target_port = _state["target_port"]

    age = time.monotonic() - last_packet_time if last_packet_time > 0 else 999.0

    return jsonify({
        "q": q,
        "age_seconds": age,
        "packet_count": packet_count,
        "target_ip": target_ip,
        "target_port": target_port,
    })


def parse_args():
    parser = argparse.ArgumentParser(
        description="Dashboard web (gauges + vista 3D) para el estado articular del robot."
    )
    parser.add_argument("--web-port", type=int, default=8080,
                         help="Puerto del dashboard web local (default: 8080).")
    parser.add_argument("--keepalive-interval", type=float, default=5.0,
                         help="Segundos entre cada datagrama de keep-alive (default: 5.0).")
    parser.add_argument("--bufsize", type=int, default=1024,
                         help="Tamano del buffer de recepcion UDP (default: 1024).")
    parser.add_argument("--recv-timeout", type=float, default=0.5,
                         help="Timeout de recvfrom en segundos (default: 0.5).")
    return parser.parse_args()


def main():
    args = parse_args()
    target_ip, target_port = ask_target()

    worker = threading.Thread(
        target=udp_worker,
        args=(target_ip, target_port, args.keepalive_interval, args.bufsize, args.recv_timeout),
        daemon=True,
    )
    worker.start()

    logging.getLogger("werkzeug").setLevel(logging.ERROR)

    print(f"\nSuscribiendose a {target_ip}:{target_port} ...")
    print(f"Dashboard listo. Abri en el navegador:")
    print(f"    http://127.0.0.1:{args.web_port}\n")

    app.run(host="127.0.0.1", port=args.web_port, debug=False, use_reloader=False)


if __name__ == "__main__":
    main()
