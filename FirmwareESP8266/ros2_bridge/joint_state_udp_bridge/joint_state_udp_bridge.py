#!/usr/bin/env python3
"""
joint_state_udp_bridge.py

ROS 2 node that subscribes to /joint_states (sensor_msgs/JointState),
packs the `position` array as 6 little-endian floats using struct,
and sends the 24-byte binary payload via UDP to the ESP8266 SoftAP.

Rate-limited: at most one UDP send per MIN_PERIOD_S seconds.
"""

import socket
import struct
import time

import rclpy
from rclpy.node import Node
from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy
from sensor_msgs.msg import JointState

# ── Configuration ──────────────────────────────────────────────────────────
ESP_IP: str = "192.168.4.1"
ESP_PORT: int = 5000
NUM_JOINTS: int = 6
SEND_HZ: float = 50.0                # maximum send rate  (Hz)
MIN_PERIOD_S: float = 1.0 / SEND_HZ  # minimum interval   (s)
STRUCT_FMT: str = f"<{NUM_JOINTS}f"  # little-endian, 6 floats
PAYLOAD_SIZE: int = struct.calcsize(STRUCT_FMT)  # 24 bytes


class JointStateUdpBridge(Node):
    """Translates /joint_states → binary UDP datagrams."""

    def __init__(self) -> None:
        super().__init__("joint_state_udp_bridge")

        # ── Parameters (overridable at launch) ─────────────────────────────
        self.declare_parameter("esp_ip", ESP_IP)
        self.declare_parameter("esp_port", ESP_PORT)
        self.declare_parameter("send_hz", SEND_HZ)
        self.declare_parameter("num_joints", NUM_JOINTS)

        self._esp_ip: str = self.get_parameter("esp_ip").value
        self._esp_port: int = self.get_parameter("esp_port").value
        self._num_joints: int = self.get_parameter("num_joints").value

        send_hz: float = self.get_parameter("send_hz").value
        self._min_period: float = 1.0 / max(send_hz, 1.0)
        self._fmt: str = f"<{self._num_joints}f"

        # ── UDP socket (non-blocking) ─────────────────────────────────────
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.setblocking(False)

        # ── Rate-limit state ──────────────────────────────────────────────
        self._last_send_time: float = 0.0

        # ── QoS: keep only the latest message to avoid queue backlog ──────
        qos = QoSProfile(
            reliability=ReliabilityPolicy.BEST_EFFORT,
            history=HistoryPolicy.KEEP_LAST,
            depth=1,
        )

        self._sub = self.create_subscription(
            JointState,
            "/joint_states",
            self._on_joint_state,
            qos,
        )

        self.get_logger().info(
            f"Bridge ready → UDP {self._esp_ip}:{self._esp_port}  "
            f"({self._num_joints} joints, ≤{send_hz:.0f} Hz)"
        )

    # ── Callback ──────────────────────────────────────────────────────────
    def _on_joint_state(self, msg: JointState) -> None:
        """Pack position[] and send via UDP, respecting rate limit."""
        now = time.monotonic()
        if (now - self._last_send_time) < self._min_period:
            return  # drop — within rate-limit window

        positions = list(msg.position)

        # Pad with 0.0 or truncate to the expected joint count
        if len(positions) < self._num_joints:
            positions.extend([0.0] * (self._num_joints - len(positions)))
        elif len(positions) > self._num_joints:
            positions = positions[: self._num_joints]

        payload: bytes = struct.pack(self._fmt, *positions)

        try:
            self._sock.sendto(payload, (self._esp_ip, self._esp_port))
            self._last_send_time = now
        except OSError as exc:
            self.get_logger().warn(f"UDP send failed: {exc}", throttle_duration_sec=2.0)

    # ── Cleanup ───────────────────────────────────────────────────────────
    def destroy_node(self) -> None:
        self._sock.close()
        super().destroy_node()


# ── Entry point ───────────────────────────────────────────────────────────
def main(args=None) -> None:
    rclpy.init(args=args)
    node = JointStateUdpBridge()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
