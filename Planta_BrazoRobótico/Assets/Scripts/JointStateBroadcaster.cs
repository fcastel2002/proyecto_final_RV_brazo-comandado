using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Preliy.Flange;
using Debug = UnityEngine.Debug;

/// <summary>
/// Lee los ángulos de las 6 articulaciones del robot (Preliy Flange) y los
/// transmite por UDP como "q1,q2,q3,q4,q5,q6\n" en grados, a una tasa configurable.
/// Independiente de JointStatePublisher.cs (ROS): mismo patrón de lectura,
/// transporte distinto.
///
/// Funciona como servidor UDP con suscripción dinámica: no hay un destino fijo
/// configurado de antemano. Cualquier cliente que envíe un datagrama a _serverPort
/// queda registrado como suscriptor y recibe el estado articular en cada envío.
///
/// Cómo usar:
///   1. Agregar este componente a cualquier GameObject de la escena.
///   2. Asignar el campo "Controller" en el Inspector con el componente Controller del robot.
///   3. Configurar el puerto local del servidor (por defecto 25001).
///   4. Desde el cliente, enviar cualquier datagrama (ej. "HELLO") a ese puerto para suscribirse,
///      y repetirlo periódicamente como keep-alive si la limpieza automática está habilitada.
/// </summary>
public class JointStateBroadcaster : MonoBehaviour
{
    [Header("Preliy Flange")]
    [Tooltip("Arrastrá aquí el componente Controller del robot.")]
    [SerializeField] private Controller _controller;

    [Header("UDP")]
    [Tooltip("Activa/desactiva el envío sin quitar el componente.")]
    [SerializeField] private bool _enableBroadcast = true;

    [Tooltip("Puerto UDP local donde Unity escucha suscripciones y desde donde responde con el estado articular.")]
    [SerializeField] private int _serverPort = 25001;

    [Tooltip("Frecuencia de envío en Hz.")]
    [SerializeField] [Range(1f, 100f)] private float _sendHz = 25f;

    [Header("Suscripción")]
    [Tooltip("Habilita la limpieza automática de suscriptores inactivos. Modificable en runtime.")]
    [SerializeField] private bool _autoCleanupEnabled = false;

    [Tooltip("Tiempo sin recibir un paquete de un suscriptor antes de removerlo (solo si _autoCleanupEnabled está activo).")]
    [SerializeField] private float _subscriberTimeoutSeconds = 15f;

    private UdpClient _udpClient;
    private float _sendInterval;
    private float _timeSinceLastSend;
    private readonly StringBuilder _messageBuilder = new StringBuilder(64);

    // El callback de recepción corre en un hilo de threadpool: Time.time no es
    // válido ahí, por eso el "last seen" se mide con un Stopwatch (seguro entre hilos).
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly Dictionary<IPEndPoint, double> _subscribers = new Dictionary<IPEndPoint, double>();
    private readonly object _subscribersLock = new object();
    private volatile bool _isClosing;

    private void Start()
    {
        _udpClient = new UdpClient(_serverPort);
        _stopwatch.Restart();
        _isClosing = false;

        _sendInterval = 1f / Mathf.Max(_sendHz, 1f);
        _timeSinceLastSend = 0f;

        if (_controller == null)
            Debug.LogError("[JointStateBroadcaster] Controller no asignado en el Inspector.");

        BeginListening(_udpClient);
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

        List<IPEndPoint> targets;
        lock (_subscribersLock)
        {
            if (_autoCleanupEnabled)
                RemoveInactiveSubscribers_NoLock();

            targets = new List<IPEndPoint>(_subscribers.Keys);
        }

        foreach (var subscriber in targets)
        {
            try
            {
                _udpClient.Send(datagram, datagram.Length, subscriber);
            }
            catch (SocketException)
            {
                // Un suscriptor caído (ej. puerto ICMP inalcanzable) no debe frenar el envío a los demás.
            }
        }
    }

    /// <summary>Debe llamarse con _subscribersLock ya tomado.</summary>
    private void RemoveInactiveSubscribers_NoLock()
    {
        double now = _stopwatch.Elapsed.TotalSeconds;
        List<IPEndPoint> expired = null;

        foreach (var kvp in _subscribers)
        {
            if (now - kvp.Value > _subscriberTimeoutSeconds)
            {
                expired ??= new List<IPEndPoint>();
                expired.Add(kvp.Key);
            }
        }

        if (expired == null) return;
        foreach (var endPoint in expired)
            _subscribers.Remove(endPoint);
    }

    private void BeginListening(UdpClient client)
    {
        if (_isClosing || client == null) return;

        try
        {
            client.BeginReceive(OnReceive, client);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private void OnReceive(IAsyncResult asyncResult)
    {
        if (_isClosing) return;

        var client = (UdpClient)asyncResult.AsyncState;
        IPEndPoint remoteEndPoint = null;

        try
        {
            client.EndReceive(asyncResult, ref remoteEndPoint);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (SocketException)
        {
            BeginListening(client);
            return;
        }

        double now = _stopwatch.Elapsed.TotalSeconds;
        lock (_subscribersLock)
        {
            _subscribers[remoteEndPoint] = now;
        }

        BeginListening(client);
    }

    /// <summary>Copia segura de los endpoints actualmente suscriptos, para consumo desde otros MonoBehaviours (ej. panel de UI).</summary>
    public List<IPEndPoint> GetActiveSubscribers()
    {
        lock (_subscribersLock)
        {
            var result = new List<IPEndPoint>(_subscribers.Count);
            foreach (var endPoint in _subscribers.Keys)
                result.Add(new IPEndPoint(endPoint.Address, endPoint.Port));
            return result;
        }
    }

    private void CloseSocket()
    {
        _isClosing = true;
        if (_udpClient == null) return;
        _udpClient.Close();
        _udpClient = null;
    }

    private void OnDestroy() => CloseSocket();

    private void OnApplicationQuit() => CloseSocket();
}
