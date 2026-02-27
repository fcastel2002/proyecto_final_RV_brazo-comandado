using UnityEngine;
using TMPro;

/// <summary>
/// Dibuja los ejes X (rojo), Y (verde), Z (azul) del transform en el Game View
/// usando LineRenderers. Adjuntar al Joint_6 o a cualquier GameObject.
///
/// Requisitos:
///   - TextMeshPro en el proyecto (para las etiquetas X, Y, Z)
///   - Un material con shader Unlit/Color (se crea automáticamente si no se asigna)
/// </summary>
public class AxisRenderer : MonoBehaviour
{
    [Header("Geometría")]
    [Tooltip("Longitud de cada eje en metros")]
    [SerializeField] private float _length = 0.5f;

    [Tooltip("Grosor de la línea en metros")]
    [SerializeField] private float _width = 0.025f;

    [Header("Opciones")]
    [Tooltip("true = ejes locales del transform | false = ejes globales del mundo")]
    [SerializeField] private bool _localAxes = true;

    [Tooltip("Mostrar etiquetas X / Y / Z")]
    [SerializeField] private bool _showLabels = true;

    [Tooltip("Tamaño de las etiquetas")]
    [SerializeField] private float _labelSize = 0.015f;

    // Referencias internas
    private LineRenderer _lrX, _lrY, _lrZ;
    private Transform _labelX, _labelY, _labelZ;

    private void Awake()
    {
        _lrX = CreateAxis("Axis_X", Color.red);
        _lrY = CreateAxis("Axis_Y", Color.green);
        _lrZ = CreateAxis("Axis_Z", Color.blue);

        if (_showLabels)
        {
            _labelX = CreateLabel("Label_X", "X", Color.red);
            _labelY = CreateLabel("Label_Y", "Y", Color.green);
            _labelZ = CreateLabel("Label_Z", "Z", Color.blue);
        }
    }

    private void LateUpdate()
    {
        Vector3 origin = transform.position;

        Vector3 axisX = (_localAxes ? transform.right : Vector3.right) * _length;
        Vector3 axisY = (_localAxes ? transform.up : Vector3.up) * _length;
        Vector3 axisZ = (_localAxes ? transform.forward : Vector3.forward) * _length;

        SetLine(_lrX, origin, origin + axisX);
        SetLine(_lrY, origin, origin + axisY);
        SetLine(_lrZ, origin, origin + axisZ);

        if (_showLabels)
        {
            PositionLabel(_labelX, origin + axisX);
            PositionLabel(_labelY, origin + axisY);
            PositionLabel(_labelZ, origin + axisZ);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private LineRenderer CreateAxis(string goName, Color color)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.startWidth = _width;
        lr.endWidth = _width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // Material Unlit para que el color sea exacto sin que afecte la iluminación
        lr.material = new Material(Shader.Find("Unlit/Color")) { color = color };

        return lr;
    }

    private void SetLine(LineRenderer lr, Vector3 start, Vector3 end)
    {
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
    }

    private Transform CreateLabel(string goName, string text, Color color)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = _labelSize * 200f;   // TMP usa puntos, escalar acorde
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;

        // Que la etiqueta siempre mire a la cámara
        go.AddComponent<FaceCamera>();

        return go.transform;
    }

    private void PositionLabel(Transform label, Vector3 worldPos)
    {
        // Un poco más allá del extremo del eje para no solaparse
        label.position = worldPos + (worldPos - transform.position).normalized * (_length * 0.15f);
    }
}

/// <summary>
/// Hace que el GameObject mire siempre hacia la cámara principal (Billboard).
/// </summary>
public class FaceCamera : MonoBehaviour
{
    private Camera _cam;

    private void Start() => _cam = Camera.main;

    private void LateUpdate()
    {
        if (_cam == null) return;
        transform.rotation = _cam.transform.rotation;
    }
}