using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LeftLayoutManager : MonoBehaviour
{
    private InputProfileSwitcher _profileSwitcher;
    private GameObject _helpPanel;
    private TextMeshProUGUI _helpText;
    private bool _arranged = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        GameObject go = new GameObject("LeftLayoutManagerInstance");
        go.AddComponent<LeftLayoutManager>();
        DontDestroyOnLoad(go);
    }

    private void Start()
    {
        _profileSwitcher = FindFirstObjectByType<InputProfileSwitcher>();
        if (_profileSwitcher != null)
        {
            _profileSwitcher.ActiveProfileChanged += OnProfileChanged;
        }
    }

    private void OnDestroy()
    {
        if (_profileSwitcher != null)
        {
            _profileSwitcher.ActiveProfileChanged -= OnProfileChanged;
        }
    }

    private void Update()
    {
        if (!_arranged)
        {
            TryArrangeGUI();
        }
    }

    private void OnProfileChanged(InputProfileSwitcher.InputProfileKind profile)
    {
        UpdateHelpText();
    }

    private void TryArrangeGUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject cameraView = GameObject.Find("CameraGripperView");
        if (cameraView == null) return;

        GameObject inputInfo = GameObject.Find("Input Info");
        GameObject safetyInfo = GameObject.Find("SafetyInfoOperator");
        GameObject distanceSensor = GameObject.Find("DistanceSensorValue");

        // Reposicionar 'Input Info'
        if (inputInfo != null)
        {
            RectTransform rt = inputInfo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(280f, 130f);
            rt.anchoredPosition = new Vector2(20f, -20f);
        }

        // Reposicionar 'J1' a 'J6'
        string[] jointNames = { "J1", "J2", "J3", "J4", "J5", "J6" };
        for (int i = 0; i < jointNames.Length; i++)
        {
            GameObject jointObj = GameObject.Find(jointNames[i]);
            if (jointObj != null)
            {
                RectTransform rt = jointObj.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(200f, 30f);
                rt.anchoredPosition = new Vector2(20f, -160f - i * 28f);

                TextMeshProUGUI tmp = jointObj.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.alignment = TextAlignmentOptions.MidlineLeft;
                    tmp.fontSize = 16;
                }
            }
        }

        // Reposicionar 'CameraGripperView'
        if (cameraView != null)
        {
            RectTransform rt = cameraView.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(280f, 280f);
            rt.anchoredPosition = new Vector2(20f, 20f);
        }

        // Reposicionar 'SafetyInfoOperator'
        if (safetyInfo != null)
        {
            RectTransform rt = safetyInfo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(280f, 30f);
            rt.anchoredPosition = new Vector2(20f, 305f);

            TextMeshProUGUI tmp = safetyInfo.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.fontSize = 15;
            }
        }

        // Reposicionar 'DistanceSensorValue'
        if (distanceSensor != null)
        {
            RectTransform rt = distanceSensor.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(280f, 30f);
            rt.anchoredPosition = new Vector2(20f, 335f);

            TextMeshProUGUI tmp = distanceSensor.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.fontSize = 15;
            }
        }

        CreateHelpPanel(canvas);
        UpdateHelpText();
        _arranged = true;
    }

    private void CreateHelpPanel(Canvas canvas)
    {
        // Crear contenedor del panel de ayuda
        _helpPanel = new GameObject("Left_Help_Panel");
        _helpPanel.transform.SetParent(canvas.transform, false);

        RectTransform rt = _helpPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 250f);
        rt.anchoredPosition = new Vector2(20f, 40f);

        // Fondo premium oscuro y semi-translúcido
        Image bgImg = _helpPanel.AddComponent<Image>();
        bgImg.color = new Color(0.08f, 0.09f, 0.11f, 0.92f);

        // Borde fino
        Outline outline = _helpPanel.AddComponent<Outline>();
        outline.effectColor = new Color(0.28f, 0.39f, 0.52f, 0.5f);
        outline.effectDistance = new Vector2(1f, 1f);

        // Texto de ayuda
        GameObject textObj = new GameObject("Help_Text");
        textObj.transform.SetParent(_helpPanel.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(15f, 15f);
        textRt.offsetMax = new Vector2(-15f, -15f);

        _helpText = textObj.AddComponent<TextMeshProUGUI>();
        _helpText.alignment = TextAlignmentOptions.TopLeft;
        _helpText.fontSize = 13f;
        _helpText.color = Color.white;
        _helpText.lineSpacing = 15f;
    }

    private void UpdateHelpText()
    {
        if (_helpText == null) return;

        InputProfileSwitcher.InputProfileKind activeProfile = InputProfileSwitcher.InputProfileKind.PS4;
        if (_profileSwitcher != null)
        {
            activeProfile = _profileSwitcher.ActiveProfile;
        }

        string profileName = activeProfile == InputProfileSwitcher.InputProfileKind.PS4 ? "PS4" : "VR-2";
        
        string text = $"<b><color=#F4B400>GUÍA DE CONTROLES ({profileName})</color></b>\n\n";

        if (activeProfile == InputProfileSwitcher.InputProfileKind.PS4)
        {
            text += "• <b>Mover TCP (X/Z):</b> Stick Izquierdo\n" +
                    "• <b>Mover TCP (Y):</b> Stick Derecho\n" +
                    "• <b>Rotar J6:</b> L1 (Anti-hor) / R1 (Horario)\n" +
                    "• <b>Homing J6:</b> Botón Cuadrado\n" +
                    "• <b>Abrir/Cerrar Garra:</b> R2 / L2\n" +
                    "• <b>Hold Garra (1s):</b> Homing J6\n" +
                    "• <b>Cámara/Modo:</b> L3 (Doble click: Modo J6)";
        }
        else
        {
            text += "• <b>Mover TCP (X/Z):</b> Palanca Principal\n" +
                    "• <b>Mover TCP (Y):</b> Palanca Altura\n" +
                    "• <b>Abrir/Cerrar Garra:</b> Trigger Gripper\n" +
                    "• <b>Hold Gripper (1s):</b> Homing J6\n" +
                    "• <b>Cámara/Modo:</b> Botón Menú / Toggle";
        }

        _helpText.text = text;
    }
}
