from setuptools import setup, find_packages

package_name = "joint_state_udp_bridge"

setup(
    name=package_name,
    version="1.0.0",
    packages=find_packages(),
    install_requires=["setuptools"],
    zip_safe=True,
    maintainer="Francisco",
    maintainer_email="todo@todo.com",
    description="ROS 2 bridge: /joint_states → UDP binary to ESP8266",
    license="Apache-2.0",
    entry_points={
        "console_scripts": [
            "joint_state_udp_bridge = joint_state_udp_bridge.joint_state_udp_bridge:main",
        ],
    },
)
