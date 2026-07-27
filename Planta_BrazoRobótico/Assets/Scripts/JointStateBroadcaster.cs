using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Preliy.Flange;

/// <summary>
/// Lee los ángulos de las 6 articulaciones del robot (Preliy Flange) y los
/// transmite por UDP como "q1,q2,q3,q4,q5,q6\n" en grados, a una tasa configurable.
/// Independiente de JointStatePublisher.cs (ROS): mismo patrón de lectura,
/// transporte distinto.
///
/// Cómo usar:
///   1. Agregar este componente a cualquier GameObject de la escena.
///   2. Asignar el campo "Controller" en el Inspector con el componente Controller del robot.
///   3. Configurar IP/puerto destino (por defecto 127.0.0.1:25001).
/// </summary>
public class JointStateBroadcaster : MonoBehaviour
{
    [Header("Preliy Flange")]
    [Tooltip("Arrastrá aquí el componente Controller del robot.")]
    [SerializeField] private Controller _controller;

    [Header("UDP")]
    [Tooltip("Activa/desactiva el envío sin quitar el componente.")]
    [SerializeField] private bool _enableBroadcast = true;

    [Tooltip("IP destino del paquete UDP (se va a reemplazar por la IP de Tailscale).")]
    [SerializeField] private string _targetIp = "127.0.0.1";

    [Tooltip("Puerto UDP destino.")]
    [SerializeField] private int _targetPort = 25001;

    [Tooltip("Frecuencia de envío en Hz.")]
    [SerializeField] [Range(1f, 100f)] private float _sendHz = 25f;

    private UdpClient _udpClient;
    private IPEndPoint _endPoint;
    private float _sendInterval;
    private float _timeSinceLastSend;
    private readonly StringBuilder _messageBuilder = new StringBuilder(64);

    private void Start()
    {
        _udpClient = new UdpClient();
        _endPoint = new IPEndPoint(IPAddress.Parse(_targetIp), _targetPort);

        _sendInterval = 1f / Mathf.Max(_sendHz, 1f);
        _timeSinceLastSend = 0f;

        if (_controller == null)
            Debug.LogError("[JointStateBroadcaster] Controller no asignado en el Inspector.");
    }

    private void Update()
    {
        if (!_enableBroadcast || _controller == null) return;

        _timeSinceLastSend += Time.deltaTime;
        if (_timeSinceLastSend < _sendInterval) return;
        _timeSinceLastSend = 0f;

        SendJointState();
    }

    private void SendJointState()
    {
        // Mismo array que JointStatePublisher: JointState.Value[0..5] en GRADOS.
        var jointState = _controller.MechanicalGroup.JointState;

        _messageBuilder.Clear();
        _messageBuilder.Append(jointState.Value[0].ToString("F2", CultureInfo.InvariantCulture));
        _messageBuilder.Append(',');
        _messageBuilder.Append(jointState.Value[1].ToString("F2", CultureInfo.InvariantCulture));
        _messageBuilder.Append(',');
        _messageBuilder.Append(jointState.Value[2].ToString("F2", CultureInfo.InvariantCulture));
        _messageBuilder.Append(',');
        _messageBuilder.Append(jointState.Value[3].ToString("F2", CultureInfo.InvariantCulture));
        _messageBuilder.Append(',');
        _messageBuilder.Append(jointState.Value[4].ToString("F2", CultureInfo.InvariantCulture));
        _messageBuilder.Append(',');
        _messageBuilder.Append(jointState.Value[5].ToString("F2", CultureInfo.InvariantCulture));
        _messageBuilder.Append('\n');

        byte[] datagram = Encoding.ASCII.GetBytes(_messageBuilder.ToString());
        _udpClient.Send(datagram, datagram.Length, _endPoint);
    }

    private void CloseSocket()
    {
        if (_udpClient == null) return;
        _udpClient.Close();
        _udpClient = null;
    }

    private void OnDestroy() => CloseSocket();

    private void OnApplicationQuit() => CloseSocket();
}
