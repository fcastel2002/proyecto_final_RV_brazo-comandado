using System.Collections.Generic;
using System.Net;
using TMPro;
using UnityEngine;

/// <summary>
/// Panel estático (`Clients_Section`, hijo de `InfoPanel_Gripper`) que lista las IPs suscriptas a
/// <see cref="JointStateBroadcaster"/>. La cantidad de clientes conectados es inherentemente variable,
/// así que las filas de IP sí se clonan por código — pero de una plantilla propia e inactiva
/// (<see cref="_rowTemplate"/>), nunca de un objeto ajeno en uso como hacía la versión anterior.
/// </summary>
public class ConnectedClientsPanel : MonoBehaviour
{
    private const float RefreshInterval = 0.5f;

    [Header("Filas base (fijas en el Editor)")]
    [SerializeField] private TextMeshProUGUI _emptyRow;

    [Header("Plantilla para filas de IP")]
    [Tooltip("Fila TMP inactiva usada como plantilla para clonar una fila por cada cliente conectado.")]
    [SerializeField] private TextMeshProUGUI _rowTemplate;

    private JointStateBroadcaster _broadcaster;
    private float _timeSinceRefresh;

    private readonly Dictionary<string, TextMeshProUGUI> _rows = new Dictionary<string, TextMeshProUGUI>();
    private readonly List<string> _staleKeys = new List<string>();

    private void Start()
    {
        _broadcaster = FindFirstObjectByType<JointStateBroadcaster>();
        RefreshRows();
    }

    private void Update()
    {
        if (_broadcaster == null)
        {
            _broadcaster = FindFirstObjectByType<JointStateBroadcaster>();
            return;
        }

        _timeSinceRefresh += Time.deltaTime;
        if (_timeSinceRefresh < RefreshInterval) return;
        _timeSinceRefresh = 0f;

        RefreshRows();
    }

    private void RefreshRows()
    {
        List<IPEndPoint> subscribers = _broadcaster != null
            ? _broadcaster.GetActiveSubscribers()
            : new List<IPEndPoint>();

        _staleKeys.Clear();
        _staleKeys.AddRange(_rows.Keys);

        foreach (IPEndPoint endPoint in subscribers)
        {
            string key = endPoint.ToString();
            _staleKeys.Remove(key);

            if (!_rows.TryGetValue(key, out TextMeshProUGUI row))
            {
                row = GetOrCreateRow(key);
            }

            if (row != null)
                row.text = endPoint.Address.ToString();
        }

        foreach (string staleKey in _staleKeys)
        {
            if (_rows.TryGetValue(staleKey, out TextMeshProUGUI row) && row != null)
                Destroy(row.gameObject);
            _rows.Remove(staleKey);
        }

        if (_emptyRow != null)
            _emptyRow.gameObject.SetActive(_rows.Count == 0);
    }

    private TextMeshProUGUI GetOrCreateRow(string key)
    {
        if (_rowTemplate == null) return null;

        TextMeshProUGUI clone = Instantiate(_rowTemplate, _rowTemplate.transform.parent);
        clone.name = $"Clients_Row_{key}";
        clone.gameObject.SetActive(true);
        _rows[key] = clone;
        return clone;
    }
}
