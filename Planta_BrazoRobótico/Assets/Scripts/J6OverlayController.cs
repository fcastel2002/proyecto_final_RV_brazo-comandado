using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Preliy.Flange;

public class J6OverlayController : MonoBehaviour
{
    private GameObject _overlayContainer;
    private RectTransform _dialPointer;
    private TextMeshProUGUI _angleText;
    private Controller _controller;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        GameObject go = new GameObject("J6OverlayControllerInstance");
        go.AddComponent<J6OverlayController>();
        DontDestroyOnLoad(go);
    }

    private void Start()
    {
        _controller = FindFirstObjectByType<Controller>();
    }

    private void Update()
    {
        if (_overlayContainer == null)
        {
            FindAndBuildUI();
        }

        bool isExclusive = JoystickAdapter.IsJ6ExclusiveMode;

        if (_overlayContainer != null)
        {
            if (_overlayContainer.activeSelf != isExclusive)
            {
                _overlayContainer.SetActive(isExclusive);
            }
        }

        if (!isExclusive || _controller == null || !_controller.IsValid.Value)
            return;

        float j6Angle = _controller.MechanicalGroup.JointState[5];

        if (_dialPointer != null)
            _dialPointer.localEulerAngles = new Vector3(0f, 0f, -j6Angle);

        if (_angleText != null)
            _angleText.text = $"J6: {j6Angle:F1}°";
    }

    private void FindAndBuildUI()
    {
        GameObject cameraViewObj = GameObject.Find("CameraGripperView");
        if (cameraViewObj == null)
        {
            var allGo = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allGo)
            {
                if (go.name == "CameraGripperView")
                {
                    cameraViewObj = go;
                    break;
                }
            }
        }

        if (cameraViewObj == null)
        {
            return; // Try again in the next frame
        }

        BuildOverlayUI(cameraViewObj);
    }

    private void BuildOverlayUI(GameObject cameraViewObj)
    {
        // Create overlay container parented to the Canvas/Panel
        _overlayContainer = new GameObject("J6_Overlay_Content");
        _overlayContainer.transform.SetParent(cameraViewObj.transform.parent, false);
        RectTransform containerRect = _overlayContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(1f, 0f);
        containerRect.anchorMax = new Vector2(1f, 0f);
        containerRect.pivot = new Vector2(1f, 0f);
        containerRect.sizeDelta = new Vector2(512f, 512f);
        containerRect.anchoredPosition = new Vector2(-20f, 20f);

        // Translucent background - transparent for the direct Canvas container
        Image bgImg = _overlayContainer.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0f);
        bgImg.raycastTarget = false;

        // Dial Background
        GameObject dialBg = new GameObject("Dial_BG");
        dialBg.transform.SetParent(_overlayContainer.transform, false);
        RectTransform dialBgRect = dialBg.AddComponent<RectTransform>();
        dialBgRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialBgRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialBgRect.sizeDelta = new Vector2(140f, 140f);
        dialBgRect.anchoredPosition = Vector2.zero;

        Image dialBgImg = dialBg.AddComponent<Image>();
        dialBgImg.color = new Color(0f, 0f, 0f, 0.5f); // Negro y más traslúcido
        dialBgImg.raycastTarget = false;

        // Cardinal marks
        CreateCardinalLabel(dialBg.transform, 0f, "0°", 42f);
        CreateCardinalLabel(dialBg.transform, 90f, "90°", 42f);
        CreateCardinalLabel(dialBg.transform, 180f, "180°", 42f);
        CreateCardinalLabel(dialBg.transform, 270f, "-90°", 42f);

        // Pointer
        GameObject pointer = new GameObject("Pointer");
        pointer.transform.SetParent(dialBg.transform, false);
        _dialPointer = pointer.AddComponent<RectTransform>();
        _dialPointer.anchorMin = new Vector2(0.5f, 0.5f);
        _dialPointer.anchorMax = new Vector2(0.5f, 0.5f);
        _dialPointer.sizeDelta = new Vector2(3f, 48f);
        _dialPointer.pivot = new Vector2(0.5f, 0f);
        _dialPointer.anchoredPosition = Vector2.zero;

        Image ptrImg = pointer.AddComponent<Image>();
        ptrImg.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        ptrImg.raycastTarget = false;

        // Title text
        CreateTMPText("Title", "MODO J6", _overlayContainer.transform,
            new Vector2(0f, 80f), new Vector2(150f, 25f),
            14, Color.yellow, FontStyles.Bold);

        // Angle text
        _angleText = CreateTMPText("Angle", "J6: 0.0°", _overlayContainer.transform,
            new Vector2(0f, -80f), new Vector2(150f, 25f),
            12, Color.white, FontStyles.Normal);

        // Set active state based on current J6 exclusive mode
        _overlayContainer.SetActive(JoystickAdapter.IsJ6ExclusiveMode);
        Debug.Log("[J6OverlayController] J6 Overlay UI built successfully.");
    }

    private void CreateCardinalLabel(Transform parent, float angleDeg, string label, float radius)
    {
        float rad = (90f - angleDeg) * Mathf.Deg2Rad;
        Vector2 pos = new Vector2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);

        CreateTMPText($"Label_{label}", label, parent,
            pos, new Vector2(40f, 20f),
            10, new Color(0.6f, 0.8f, 1f, 0.9f), FontStyles.Normal);
    }

    private TextMeshProUGUI CreateTMPText(string name, string text, Transform parent,
        Vector2 anchoredPos, Vector2 sizeDelta,
        int fontSize, Color color, FontStyles style)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.raycastTarget = false;
        return tmp;
    }
}
