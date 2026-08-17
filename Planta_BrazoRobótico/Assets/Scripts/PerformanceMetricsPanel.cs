using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PerformanceMetricsPanel : MonoBehaviour
{
    [Header("Filas base (fijas en el Editor)")]
    [SerializeField] private TextMeshProUGUI _collisionsRow;
    [SerializeField] private TextMeshProUGUI _operationTimeRow;
    [SerializeField] private TextMeshProUGUI _operationCountRow;
    [SerializeField] private TextMeshProUGUI _grabbedMassRow;

    [Header("Plantilla para filas adicionales")]
    [Tooltip("Fila TMP inactiva usada como plantilla al agregar por código una métrica nueva que no exista como fila fija.")]
    [SerializeField] private TextMeshProUGUI _rowTemplate;

    private readonly Dictionary<string, TextMeshProUGUI> _extraRows = new Dictionary<string, TextMeshProUGUI>();

    public void SetCollisionCount(int count)
    {
        SetRow(_collisionsRow, "Colisiones", count.ToString());
    }

    public void SetOperationCount(int count)
    {
        SetRow(_operationCountRow, "Operaciones", count.ToString());
    }

    public void SetOperationTime(float seconds)
    {
        SetRow(_operationTimeRow, "Tiempo Operación", $"{seconds:F1} s");
    }

    public void SetGrabbedMass(float? mass)
    {
        string value = mass.HasValue ? $"{mass.Value:F2} kg" : "-";
        SetRow(_grabbedMassRow, "Masa Agarrada", value);
    }

    public void SetExtraRow(string key, string label, string value)
    {
        TextMeshProUGUI row = GetOrCreateExtraRow(key);
        if (row != null)
            row.text = $"{label}: {value}";
    }

    private static void SetRow(TextMeshProUGUI row, string label, string value)
    {
        if (row == null) return;
        row.text = $"{label}: {value}";
    }

    private TextMeshProUGUI GetOrCreateExtraRow(string key)
    {
        if (_extraRows.TryGetValue(key, out var existing) && existing != null)
            return existing;

        if (_rowTemplate == null) return null;

        TextMeshProUGUI clone = Instantiate(_rowTemplate, _rowTemplate.transform.parent);
        clone.name = $"Metrics_Row_{key}";
        clone.gameObject.SetActive(true);
        _extraRows[key] = clone;
        return clone;
    }
}
