using UnityEngine;
using TMPro;

public class LeftLayoutManager : MonoBehaviour
{
    private InputProfileSwitcher _profileSwitcher;
    private ControlGuidePanel _guidePanel;
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

        GameObject safetyInfo = GameObject.Find("SafetyInfoOperator");
        GameObject distanceSensor = GameObject.Find("DistanceSensorValue");

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

        // 'CameraGripperView' ya no se reposiciona por código: ahora es hijo estático
        // de InfoPanel_Gripper y su lugar lo controla el Vertical Layout Group del panel.

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

        _guidePanel = FindFirstObjectByType<ControlGuidePanel>();
        UpdateHelpText();
        _arranged = true;
    }

    private void UpdateHelpText()
    {
        if (_guidePanel == null) return;

        InputProfileSwitcher.InputProfileKind activeProfile = InputProfileSwitcher.InputProfileKind.PS4;
        if (_profileSwitcher != null)
        {
            activeProfile = _profileSwitcher.ActiveProfile;
        }

        _guidePanel.SetProfile(activeProfile);
    }
}
