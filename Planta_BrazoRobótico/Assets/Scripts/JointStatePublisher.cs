using System;
using UnityEngine;
using Preliy.Flange;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;

/// <summary>
/// Lee los ángulos de las 6 articulaciones del robot (Preliy Flange)
/// y los publica en el topic ROS 2 /joint_states a una tasa configurable.
///
/// Cómo usar:
///   1. Agregar este componente a cualquier GameObject de la escena (p.ej. el mismo
///      que tiene el Controller, o un GameObject vacío "ROS Bridge").
///   2. Asignar el campo "Controller" en el Inspector con el componente Controller del robot.
///   3. Asegurarse de que ROS Settings (Robotics > ROS Settings) tenga la IP/puerto
///      del ROS-TCP-Endpoint correcto (por defecto 127.0.0.1:10000).
/// </summary>
public class JointStatePublisher : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Preliy Flange")]
    [Tooltip("Arrastrá aquí el componente Controller del robot.")]
    [SerializeField] private Controller _controller;

    [Header("ROS 2")]
    [Tooltip("Nombre del topic al que se publicarán los joint states.")]
    [SerializeField] private string _topicName = "/joint_states";

    [Tooltip("Frecuencia de publicación en Hz (máximo recomendado: 50).")]
    [SerializeField] [Range(1f, 50f)] private float _publishHz = 50f;

    [Tooltip("Nombres de las articulaciones (deben coincidir con el URDF / nodo ROS).")]
    [SerializeField] private string[] _jointNames = { "joint_1", "joint_2", "joint_3",
                                                       "joint_4", "joint_5", "joint_6" };

    // ── Privados ──────────────────────────────────────────────────────────────

    private ROSConnection _ros;
    private float _publishInterval;
    private float _timeSinceLastPublish;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        // Registrar el publisher con ROS-TCP-Connector
        _ros = ROSConnection.GetOrCreateInstance();
        _ros.RegisterPublisher<JointStateMsg>(_topicName);

        _publishInterval = 1f / Mathf.Max(_publishHz, 1f);
        _timeSinceLastPublish = 0f;

        if (_controller == null)
            Debug.LogError("[JointStatePublisher] Controller no asignado en el Inspector.");
    }

    private void Update()
    {
        if (_controller == null) return;

        _timeSinceLastPublish += Time.deltaTime;
        if (_timeSinceLastPublish < _publishInterval) return;
        _timeSinceLastPublish = 0f;

        PublishJointState();
    }

    // ── Publicación ───────────────────────────────────────────────────────────

    private void PublishJointState()
    {
        // Obtener los ángulos actuales del solver de Preliy Flange
        // JointTarget.Value contiene un array con los ángulos en GRADOS (primeros 6 son robot joints)
        var jointTarget = _controller.MechanicalGroup.JointState;

        // Convertir a array de doubles en radianes (ROS usa radianes)
        // Los primeros 6 valores del array son las articulaciones del robot (J1-J6)
        double[] positions = new double[]
        {
            DegToRad(jointTarget.Value[0]),  // J1
            DegToRad(jointTarget.Value[1]),  // J2
            DegToRad(jointTarget.Value[2]),  // J3
            DegToRad(jointTarget.Value[3]),  // J4
            DegToRad(jointTarget.Value[4]),  // J5
            DegToRad(jointTarget.Value[5]),  // J6
        };

        // Construir el mensaje ROS 2 JointState
        var msg = new JointStateMsg
        {
            header = new HeaderMsg
            {
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg
                {
                    sec  = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    nanosec = (uint)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1000) * 1_000_000)
                },
                frame_id = "base_link"
            },
            name     = _jointNames,
            position = positions,
            velocity = new double[6],   // ceros (no se usa)
            effort   = new double[6]    // ceros (no se usa)
        };

        _ros.Publish(_topicName, msg);
    }

    // ── Utilidades ────────────────────────────────────────────────────────────

    private static double DegToRad(float degrees) => degrees * System.Math.PI / 180.0;
}
