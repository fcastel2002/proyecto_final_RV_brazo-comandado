using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Aviso de estado sobre la vista de la gripper camera: informa al operario de POR QUE el brazo se
/// esta comportando distinto de lo que pide el joystick.
///
/// Sin este aviso, un brazo que frena solo o que deja de bajar se percibe como un fallo del mando.
/// Muestra "DESCENSO BLOQUEADO" (prioritario) o "VELOCIDAD n%", y se oculta cuando no hay nada que
/// comunicar para no ensuciar el recuadro.
///
/// Se autoinstancia y construye su UI en runtime, mismo patron que J6OverlayController.
/// </summary>
public class GripperStatusOverlay : MonoBehaviour
{
    private static readonly Color BlockedColor = new Color(1f, 0.35f, 0.30f, 1f);
    private static readonly Color SlowdownColor = new Color(1f, 0.69f, 0.13f, 1f);

    private JoystickAdapter _adapter;
    private GameObject _chipRoot;
    private TextMeshProUGUI _chipText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        GameObject go = new GameObject("GripperStatusOverlayInstance");
        go.AddComponent<GripperStatusOverlay>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        if (_chipRoot == null)
        {
            TryBuildUi();
            return;
        }

        if (_adapter == null)
        {
            _adapter = FindFirstObjectByType<JoystickAdapter>();
            if (_adapter == null)
            {
                SetChipVisible(false);
                return;
            }
        }

        if (_adapter.IsMotionBlocked)
        {
            ShowChip("OBSTACULO", BlockedColor);
            return;
        }

        if (_adapter.IsDescentBlocked)
        {
            ShowChip("DESCENSO BLOQUEADO", BlockedColor);
            return;
        }

        // Por debajo de este margen el frenado no es distinguible y el aviso solo parpadearia.
        if (_adapter.ProximitySpeedScale < 0.99f)
        {
            ShowChip($"VELOCIDAD {_adapter.ProximitySpeedScale * 100f:F0}%", SlowdownColor);
            return;
        }

        SetChipVisible(false);
    }

    private void ShowChip(string message, Color color)
    {
        SetChipVisible(true);
        _chipText.text = message;
        _chipText.color = color;
    }

    private void SetChipVisible(bool visible)
    {
        if (_chipRoot != null && _chipRoot.activeSelf != visible)
            _chipRoot.SetActive(visible);
    }

    private void TryBuildUi()
    {
        GameObject cameraViewObj = GameObject.Find("CameraGripperView");
        if (cameraViewObj == null) return;

        // Franja superior: la inferior la ocupa DistanceSensorValue.
        _chipRoot = new GameObject("GripperStatusChip", typeof(RectTransform));
        _chipRoot.layer = cameraViewObj.layer;

        RectTransform rootRect = _chipRoot.GetComponent<RectTransform>();
        rootRect.SetParent(cameraViewObj.transform, false);
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.offsetMin = new Vector2(6f, -30f);
        rootRect.offsetMax = new Vector2(-6f, -6f);

        Image background = _chipRoot.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.55f);
        background.raycastTarget = false;

        GameObject textObj = new GameObject("Label", typeof(RectTransform));
        textObj.layer = cameraViewObj.layer;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.SetParent(_chipRoot.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        _chipText = textObj.AddComponent<TextMeshProUGUI>();
        _chipText.text = string.Empty;
        _chipText.alignment = TextAlignmentOptions.Center;
        _chipText.fontSize = 14;
        _chipText.fontStyle = FontStyles.Bold;
        _chipText.color = SlowdownColor;
        _chipText.raycastTarget = false;

        _chipRoot.SetActive(false);
    }
}
