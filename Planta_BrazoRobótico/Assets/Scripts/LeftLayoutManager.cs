using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LeftLayoutManager : MonoBehaviour
{
    private InputProfileSwitcher _profileSwitcher;
    private ControlGuidePanel _guidePanel;
    private RectTransform _guiaHorizontal;
    private RectTransform _guiaVertical;
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

        GripperViewSettings.GuideLengthChanged += ApplyGuideLength;
    }

    private void OnDestroy()
    {
        if (_profileSwitcher != null)
        {
            _profileSwitcher.ActiveProfileChanged -= OnProfileChanged;
        }

        GripperViewSettings.GuideLengthChanged -= ApplyGuideLength;
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

        // 'DistanceSensorValue' pasa a mostrarse DENTRO de la vista de la gripper camera, en una
        // franja inferior, para que el operario lea la distancia sin apartar la mirada del recuadro.
        if (distanceSensor != null)
        {
            RectTransform rt = distanceSensor.GetComponent<RectTransform>();
            rt.SetParent(cameraView.transform, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(6f, 6f);
            rt.offsetMax = new Vector2(-6f, 32f);

            EnsureDistanceBackdrop(cameraView.transform, rt);

            TextMeshProUGUI tmp = distanceSensor.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.alignment = TextAlignmentOptions.Bottom;
                tmp.fontSize = 18;
            }
        }

        ApplyGuideLength();

        _guidePanel = FindFirstObjectByType<ControlGuidePanel>();
        UpdateHelpText();
        _arranged = true;
    }

    /// <summary>
    /// Ajusta el largo de las guías perpendiculares de la gripper camera. Venían fijas a 512 px en
    /// la escena sobre un recuadro de ~270, por lo que desbordaban; al anclarlas relativas al padre
    /// escalan solas con el recuadro y el operario puede regular su largo desde el menú de pausa.
    /// El grosor y el color no se tocan.
    /// </summary>
    private void ApplyGuideLength()
    {
        // Se cachean porque GameObject.Find no encuentra objetos inactivos: una vez ocultas las
        // guías, sin la referencia guardada no habría forma de volver a mostrarlas.
        if (_guiaHorizontal == null)
        {
            GameObject found = GameObject.Find("GuiaHorizontal");
            if (found != null) _guiaHorizontal = found.GetComponent<RectTransform>();
        }

        if (_guiaVertical == null)
        {
            GameObject found = GameObject.Find("GuiaVertical");
            if (found != null) _guiaVertical = found.GetComponent<RectTransform>();
        }

        float factor = GripperViewSettings.GuideLengthFactor;
        float half = factor * 0.5f;
        bool visible = factor > 0f;

        if (_guiaHorizontal != null)
        {
            _guiaHorizontal.anchorMin = new Vector2(0.5f - half, 0.5f);
            _guiaHorizontal.anchorMax = new Vector2(0.5f + half, 0.5f);
            _guiaHorizontal.pivot = new Vector2(0.5f, 0.5f);
            _guiaHorizontal.anchoredPosition = Vector2.zero;
            _guiaHorizontal.sizeDelta = new Vector2(0f, _guiaHorizontal.sizeDelta.y);
            _guiaHorizontal.gameObject.SetActive(visible);
        }

        if (_guiaVertical != null)
        {
            _guiaVertical.anchorMin = new Vector2(0.5f, 0.5f - half);
            _guiaVertical.anchorMax = new Vector2(0.5f, 0.5f + half);
            _guiaVertical.pivot = new Vector2(0.5f, 0.5f);
            _guiaVertical.anchoredPosition = Vector2.zero;
            _guiaVertical.sizeDelta = new Vector2(_guiaVertical.sizeDelta.x, 0f);
            _guiaVertical.gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// Banda oscura detrás del valor de distancia. Sin ella el texto es ilegible cuando la
    /// cámara del gripper enfoca una superficie clara.
    /// </summary>
    private static void EnsureDistanceBackdrop(Transform cameraView, RectTransform textRect)
    {
        const string backdropName = "DistanceValueBackdrop";
        if (cameraView.Find(backdropName) != null) return;

        GameObject backdrop = new GameObject(backdropName, typeof(RectTransform));
        backdrop.layer = cameraView.gameObject.layer;

        RectTransform rt = backdrop.GetComponent<RectTransform>();
        rt.SetParent(cameraView, false);
        rt.anchorMin = textRect.anchorMin;
        rt.anchorMax = textRect.anchorMax;
        rt.pivot = textRect.pivot;
        rt.offsetMin = textRect.offsetMin;
        rt.offsetMax = textRect.offsetMax;

        Image image = backdrop.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.55f);
        image.raycastTarget = false;

        // Justo antes del texto en la jerarquía => se dibuja detrás de él.
        rt.SetSiblingIndex(textRect.GetSiblingIndex());
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
