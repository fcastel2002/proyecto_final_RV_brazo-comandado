using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PidActionsPanel : MonoBehaviour
{
    [Header("Filas base (fijas en el Editor)")]
    [SerializeField] private TextMeshProUGUI[] _jointRows = new TextMeshProUGUI[6];
    [SerializeField] private string _valueFormat = "F3";

    [Header("Plantilla para filas adicionales")]
    [Tooltip("Fila TMP inactiva usada como plantilla al agregar por código un dato nuevo (ej. coordenadas del gripper) que no exista como fila fija.")]
    [SerializeField] private TextMeshProUGUI _rowTemplate;

    private readonly Dictionary<string, TextMeshProUGUI> _extraRows = new Dictionary<string, TextMeshProUGUI>();

    public void SetJointAction(int jointIndex, float value)
    {
        if (_jointRows == null || jointIndex < 0 || jointIndex >= _jointRows.Length) return;

        TextMeshProUGUI row = _jointRows[jointIndex];
        if (row == null) return;

        row.text = $"Joint {jointIndex + 1}: {value.ToString(_valueFormat)} °/s²";
    }

    public void SetExtraRow(string key, string label, string value)
    {
        TextMeshProUGUI row = GetOrCreateExtraRow(key);
        if (row != null)
            row.text = $"{label}: {value}";
    }

    private TextMeshProUGUI GetOrCreateExtraRow(string key)
    {
        if (_extraRows.TryGetValue(key, out var existing) && existing != null)
            return existing;

        if (_rowTemplate == null) return null;

        TextMeshProUGUI clone = Instantiate(_rowTemplate, _rowTemplate.transform.parent);
        clone.name = $"PID_Row_{key}";
        clone.gameObject.SetActive(true);
        _extraRows[key] = clone;
        return clone;
    }
}
