using System.Collections.Generic;
using System.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Agrega una sección "CLIENTES CONECTADOS" al panel InfoPanel_Gripper existente en la escena
/// (el mismo que muestra ACCIONES DE CONTROL (PID) y GUÍA DE CONTROLES (PS4)), listando las IPs
/// suscriptas a JointStateBroadcaster. El título y las filas se clonan de PID_Title/PID_Row_J1
/// para heredar exactamente su estilo (fuente, color, tamaño) sin hardcodear referencias de asset.
/// </summary>
public class ConnectedClientsPanel : MonoBehaviour
{
    private const float RefreshInterval = 0.5f;
    private const string PanelName = "InfoPanel_Gripper";
    private const string TitleTemplatePath = "PID_Section/PID_Title";
    private const string RowTemplatePath = "PID_Section/PID_Row_J1";

    private JointStateBroadcaster _broadcaster;
    private Transform _rowsContainer;
    private TextMeshProUGUI _rowTemplate;
    private TextMeshProUGUI _emptyRow;
    private float _timeSinceRefresh;
    private bool _built;

    private readonly Dictionary<string, TextMeshProUGUI> _rows = new Dictionary<string, TextMeshProUGUI>();
    private readonly List<string> _staleKeys = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        GameObject go = new GameObject("ConnectedClientsPanelInstance");
        go.AddComponent<ConnectedClientsPanel>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        if (!_built)
        {
            TryBuildSection();
            return;
        }

        _timeSinceRefresh += Time.deltaTime;
        if (_timeSinceRefresh < RefreshInterval) return;
        _timeSinceRefresh = 0f;

        RefreshRows();
    }

    private void TryBuildSection()
    {
        _broadcaster = FindFirstObjectByType<JointStateBroadcaster>();
        GameObject infoPanel = GameObject.Find(PanelName);
        if (_broadcaster == null || infoPanel == null) return;

        Transform titleTemplate = infoPanel.transform.Find(TitleTemplatePath);
        Transform rowTemplate = infoPanel.transform.Find(RowTemplatePath);
        if (titleTemplate == null || rowTemplate == null) return;

        BuildSection(infoPanel.transform, titleTemplate.GetComponent<TextMeshProUGUI>(), rowTemplate.GetComponent<TextMeshProUGUI>());
        _built = true;
    }

    private void BuildSection(Transform infoPanelTransform, TextMeshProUGUI titleTemplate, TextMeshProUGUI rowTemplate)
    {
        GameObject section = new GameObject("Clients_Section", typeof(RectTransform));
        section.transform.SetParent(infoPanelTransform, false);
        section.layer = infoPanelTransform.gameObject.layer;

        ContentSizeFitter fitter = section.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = Instantiate(titleTemplate, section.transform);
        title.name = "Clients_Title";
        title.text = "CLIENTES CONECTADOS";
        title.gameObject.SetActive(true);

        _rowTemplate = rowTemplate;
        _rowsContainer = section.transform;

        _emptyRow = Instantiate(rowTemplate, section.transform);
        _emptyRow.name = "Clients_EmptyRow";
        _emptyRow.text = "Sin clientes conectados";
        _emptyRow.gameObject.SetActive(true);

        // Se ubica justo después de Guide_Section para no alterar el orden PID -> Guía -> Cámara.
        Transform guideSection = infoPanelTransform.Find("Guide_Section");
        if (guideSection != null)
            section.transform.SetSiblingIndex(guideSection.GetSiblingIndex() + 1);

        RefreshRows();
    }

    private void RefreshRows()
    {
        List<IPEndPoint> subscribers = _broadcaster.GetActiveSubscribers();

        _staleKeys.Clear();
        _staleKeys.AddRange(_rows.Keys);

        foreach (IPEndPoint endPoint in subscribers)
        {
            string key = endPoint.ToString();
            _staleKeys.Remove(key);

            if (!_rows.TryGetValue(key, out TextMeshProUGUI row))
            {
                row = Instantiate(_rowTemplate, _rowsContainer);
                row.name = $"Clients_Row_{key}";
                row.gameObject.SetActive(true);
                _rows[key] = row;
            }

            row.text = endPoint.Address.ToString();
        }

        foreach (string staleKey in _staleKeys)
        {
            if (_rows.TryGetValue(staleKey, out TextMeshProUGUI row) && row != null)
                Destroy(row.gameObject);
            _rows.Remove(staleKey);
        }

        _emptyRow.gameObject.SetActive(_rows.Count == 0);
    }
}
