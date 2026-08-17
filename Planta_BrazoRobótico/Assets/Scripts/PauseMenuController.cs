using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    private const string CalibrationUnavailableForPs4Status = "Calibracion disponible solo para VR-2";

    [Header("Menu")]
    [SerializeField] private GameObject pauseMenuRoot;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button ps4Button;
    [SerializeField] private Button vr2Button;
    [SerializeField] private Button calibrateButton;
    [SerializeField] private Button finishCalibrationButton;
    [SerializeField] private Button resetCalibrationButton;
    [SerializeField] private TextMeshProUGUI activeProfileText;
    [SerializeField] private TextMeshProUGUI calibrationStatusText;
    [SerializeField] private GameObject futureOptionsContainer;
    [SerializeField] private Button orientationModeButton;
    [SerializeField] private Button proximityThresholdButton;
    [SerializeField] private Button descentMarginButton;
    [SerializeField] private Button guideLengthButton;
    [SerializeField] private JoystickAdapter joystickAdapter;

    [Header("Menu Colors")]
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color panelColor = new Color(0.08f, 0.09f, 0.11f, 0.96f);
    [SerializeField] private Color normalButtonColor = new Color(0.18f, 0.23f, 0.30f, 1f);
    [SerializeField] private Color highlightedButtonColor = new Color(0.28f, 0.39f, 0.52f, 1f);
    [SerializeField] private Color selectedButtonColor = new Color(0.95f, 0.72f, 0.22f, 1f);
    [SerializeField] private Color pressedButtonColor = new Color(0.11f, 0.16f, 0.22f, 1f);
    [SerializeField] private Color disabledButtonColor = new Color(0.18f, 0.20f, 0.23f, 0.72f);
    [SerializeField] private Color buttonTextColor = Color.white;
    [SerializeField] private Color selectedButtonTextColor = Color.black;

    [Header("Input")]
    [SerializeField] private InputProfileSwitcher inputProfileSwitcher;
    [SerializeField] private AnalogCalibrationManager calibrationManager;
    [SerializeField] private float cameraButtonHoldSeconds = 2f;
    [SerializeField] private float buttonPressThreshold = 0.5f;

    [Header("Pause")]
    [SerializeField] private bool pauseWithTimeScale = true;
    [SerializeField] private bool unlockCursorWhenPaused = true;

    [Header("Menu Navigation")]
    [SerializeField] private float navigationThreshold = 0.55f;
    [SerializeField] private float navigationRepeatDelay = 0.35f;
    [SerializeField] private float navigationRepeatRate = 0.14f;

    private bool _isPaused;
    private bool _isCalibrating;
    private float _previousTimeScale = 1f;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLockMode;
    private bool _cameraButtonWasPressed;
    private bool _cameraButtonHoldConsumed;
    private bool _cameraButtonWasActiveAtPress;
    private bool _cameraButtonStartedWhilePaused;
    private bool _ignoreCameraButtonUntilRelease;
    private bool _ignoreMenuSubmitUntilRelease;
    private float _cameraButtonPressedAt;
    private Vector2Int _heldNavigationDirection;
    private float _nextNavigationAt;

    private void EnsureOrientationButtonExists()
    {
        if (orientationModeButton != null) return;

        Transform panel = pauseMenuRoot.transform.Find("Panel");
        if (panel == null)
        {
            panel = pauseMenuRoot.GetComponentInChildren<VerticalLayoutGroup>()?.transform;
        }

        if (panel != null)
        {
            Transform existing = panel.Find("OrientationButton");
            if (existing != null)
            {
                orientationModeButton = existing.GetComponent<Button>();
            }
            else
            {
                orientationModeButton = CreateButton("Orientación: Fija Absoluta", panel);
                orientationModeButton.gameObject.name = "OrientationButton";
                
                if (continueButton != null)
                {
                    orientationModeButton.transform.SetSiblingIndex(continueButton.transform.GetSiblingIndex());
                }
            }
        }
    }

    private void EnsureProximityThresholdButtonExists()
    {
        if (proximityThresholdButton != null) return;

        Transform panel = pauseMenuRoot.transform.Find("Panel");
        if (panel == null)
        {
            panel = pauseMenuRoot.GetComponentInChildren<VerticalLayoutGroup>()?.transform;
        }

        if (panel != null)
        {
            Transform existing = panel.Find("ProximityThresholdButton");
            if (existing != null)
            {
                proximityThresholdButton = existing.GetComponent<Button>();
            }
            else
            {
                proximityThresholdButton = CreateButton(BuildProximityThresholdLabel(), panel);
                proximityThresholdButton.gameObject.name = "ProximityThresholdButton";

                if (continueButton != null)
                {
                    proximityThresholdButton.transform.SetSiblingIndex(continueButton.transform.GetSiblingIndex());
                }
            }
        }
    }

    /// <summary>
    /// Cicla el umbral de frenado por proximidad entre los presets. Se usa un boton ciclico y no
    /// un Slider porque la navegacion izquierda/derecha del menu esta reservada a los botones de
    /// perfil (ver FindNextSelectable).
    /// </summary>
    public void CycleProximityThreshold()
    {
        ProximitySlowdownSettings.CycleToNext();
        UpdateProximityThresholdButtonText();
    }

    private void UpdateProximityThresholdButtonText()
    {
        if (proximityThresholdButton == null) return;

        var text = proximityThresholdButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
            text.text = BuildProximityThresholdLabel();
    }

    private static string BuildProximityThresholdLabel()
    {
        return $"Frenado prox.: {ProximitySlowdownSettings.DescribeCurrent()}";
    }

    /// <summary>
    /// Margen que se reserva por debajo del gripper cerrado. Es un valor propio y mucho mas corto que
    /// el umbral de frenado: atarlo al umbral impediria depositar una pieza, porque el brazo se
    /// frenaria a 30 cm del suelo.
    /// </summary>
    public void CycleDescentMargin()
    {
        ProximitySlowdownSettings.CycleDescentMarginToNext();
        UpdateDescentMarginButtonText();
    }

    private void UpdateDescentMarginButtonText()
    {
        if (descentMarginButton == null) return;

        var text = descentMarginButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
            text.text = BuildDescentMarginLabel();
    }

    private static string BuildDescentMarginLabel()
    {
        return $"Bloqueo desc.: {ProximitySlowdownSettings.DescribeDescentMargin()}";
    }

    public void CycleGuideLength()
    {
        GripperViewSettings.CycleGuideLengthToNext();
        UpdateGuideLengthButtonText();
    }

    private void UpdateGuideLengthButtonText()
    {
        if (guideLengthButton == null) return;

        var text = guideLengthButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
            text.text = BuildGuideLengthLabel();
    }

    private static string BuildGuideLengthLabel()
    {
        return $"Guías: {GripperViewSettings.DescribeGuideLength()}";
    }

    public void ToggleOrientationMode()
    {
        if (joystickAdapter == null)
            joystickAdapter = FindFirstObjectByType<JoystickAdapter>();

        if (joystickAdapter != null)
        {
            joystickAdapter.AlignOrientationWithJ1 = !joystickAdapter.AlignOrientationWithJ1;
            UpdateOrientationButtonText();
        }
    }

    private void UpdateOrientationButtonText()
    {
        if (orientationModeButton == null) return;
        var text = orientationModeButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
        {
            if (joystickAdapter == null)
                joystickAdapter = FindFirstObjectByType<JoystickAdapter>();

            if (joystickAdapter != null)
            {
                text.text = joystickAdapter.AlignOrientationWithJ1 
                    ? "Orientación: Seguir Base (J1)" 
                    : "Orientación: Fija Absoluta";
            }
        }
    }

    private void Awake()
    {
        if (inputProfileSwitcher == null)
            inputProfileSwitcher = FindFirstObjectByType<InputProfileSwitcher>();

        if (calibrationManager == null)
            calibrationManager = FindFirstObjectByType<AnalogCalibrationManager>();

        if (pauseMenuRoot == null)
        {
            BuildDefaultMenu();
        }
        else
        {
            EnsureOrientationButtonExists();
            EnsureProximityThresholdButtonExists();
        }

        WireButtons();
        ApplyButtonColors();
        SetMenuVisible(false);
        UpdateProfileUi();
    }

    private void OnEnable()
    {
        if (inputProfileSwitcher != null)
            inputProfileSwitcher.ActiveProfileChanged += OnActiveProfileChanged;
    }

    private void OnDisable()
    {
        if (inputProfileSwitcher != null)
            inputProfileSwitcher.ActiveProfileChanged -= OnActiveProfileChanged;

        if (_isPaused)
            SetPaused(false);
    }

    private void Update()
    {
        HandleKeyboardPause();
        HandleCameraButton();

        if (!_isPaused)
            return;

        if (_ignoreMenuSubmitUntilRelease)
        {
            if (inputProfileSwitcher == null || !inputProfileSwitcher.ReadMenuSubmitPressed(buttonPressThreshold))
                _ignoreMenuSubmitUntilRelease = false;
        }
        else if (inputProfileSwitcher != null && inputProfileSwitcher.WasMenuSubmitPressedThisFrame())
        {
            SubmitSelected();
        }

        if (_isCalibrating)
        {
            UpdateCalibrationCapture();
            RefreshSelectedTextColors();
            return;
        }

        HandleAnalogNavigation();
        RefreshSelectedTextColors();
    }

    public void TogglePause()
    {
        SetPaused(!_isPaused);
    }

    public void Resume()
    {
        SetPaused(false);
    }

    public void SelectPS4()
    {
        inputProfileSwitcher?.SelectPS4();
        UpdateProfileUi();
    }

    public void SelectVR2()
    {
        inputProfileSwitcher?.SelectVR2();
        UpdateProfileUi();
    }

    public void StartCalibration()
    {
        if (!EnsureCalibrationManager())
            return;

        if (!IsCalibrationAllowedForActiveProfile())
        {
            SetCalibrationStatus(CalibrationUnavailableForPs4Status);
            return;
        }

        _isCalibrating = true;
        calibrationManager.SetActiveProfile(inputProfileSwitcher.ActiveProfile);
        calibrationManager.BeginCapture(inputProfileSwitcher.GetCalibrationAxes());
        UpdateCalibrationCapture();
        UpdateProfileUi();
        SelectButton(finishCalibrationButton);
    }

    public void FinishCalibration()
    {
        if (!_isCalibrating || !EnsureCalibrationManager())
            return;

        calibrationManager.Capture(inputProfileSwitcher.GetCalibrationAxes());
        calibrationManager.FinishCapture(inputProfileSwitcher.GetCalibrationAxes(), out string status);
        _isCalibrating = false;
        SetCalibrationStatus(status);
        UpdateProfileUi();
        SelectButton(continueButton);
    }

    public void ResetCalibration()
    {
        if (!EnsureCalibrationManager())
            return;

        if (!IsCalibrationAllowedForActiveProfile())
        {
            SetCalibrationStatus(CalibrationUnavailableForPs4Status);
            return;
        }

        calibrationManager.ResetCalibration(inputProfileSwitcher.GetCalibrationAxes());
        SetCalibrationStatus($"Calibracion restablecida para {inputProfileSwitcher.GetDisplayName(inputProfileSwitcher.ActiveProfile)}");
    }

    private void HandleKeyboardPause()
    {
        bool keyboardPause = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool joystickPause = inputProfileSwitcher != null && inputProfileSwitcher.WasPausePressedThisFrame();

        if (keyboardPause || joystickPause)
            TogglePause();
    }

    private void HandleCameraButton()
    {
        if (inputProfileSwitcher == null || inputProfileSwitcher.ActiveCameraToggle == null)
            return;

        bool pressed = inputProfileSwitcher.ReadAnyCameraButtonPressed(buttonPressThreshold);
        bool activePressed = inputProfileSwitcher.ReadActiveCameraButtonPressed(buttonPressThreshold);
        float now = Time.unscaledTime;

        if (_ignoreCameraButtonUntilRelease)
        {
            if (!pressed)
            {
                _ignoreCameraButtonUntilRelease = false;
                ResetCameraButtonState();
            }

            return;
        }

        if (pressed && !_cameraButtonWasPressed)
        {
            _cameraButtonWasPressed = true;
            _cameraButtonHoldConsumed = false;
            _cameraButtonWasActiveAtPress = activePressed;
            _cameraButtonStartedWhilePaused = _isPaused;
            _cameraButtonPressedAt = now;
            return;
        }

        if (pressed && _cameraButtonWasPressed && !_cameraButtonHoldConsumed
            && now - _cameraButtonPressedAt >= cameraButtonHoldSeconds)
        {
            _cameraButtonHoldConsumed = true;
            SetPaused(!_isPaused);
            return;
        }

        if (pressed || !_cameraButtonWasPressed)
            return;

        float duration = now - _cameraButtonPressedAt;
        bool wasConsumed = _cameraButtonHoldConsumed;
        bool wasActiveAtPress = _cameraButtonWasActiveAtPress;
        bool startedWhilePaused = _cameraButtonStartedWhilePaused;
        ResetCameraButtonState();

        if (wasConsumed || duration >= cameraButtonHoldSeconds)
            return;

        if (!_isPaused && wasActiveAtPress && !startedWhilePaused)
            inputProfileSwitcher.ToggleCameraMode();
    }

    private void SetPaused(bool paused)
    {
        if (_isPaused == paused)
            return;

        if (!paused && _isCalibrating)
            CancelCalibration();

        _isPaused = paused;
        SetMenuVisible(_isPaused);
        inputProfileSwitcher?.SetSimulationInputEnabled(!_isPaused);
        _ignoreMenuSubmitUntilRelease = false;
        ResetNavigationState();

        if (!_isPaused)
            SuppressCameraToggleUntilRelease();

        if (_isPaused)
        {
            _previousTimeScale = Time.timeScale;
            _previousCursorVisible = Cursor.visible;
            _previousCursorLockMode = Cursor.lockState;

            if (pauseWithTimeScale)
                Time.timeScale = 0f;

            if (unlockCursorWhenPaused)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            SelectButton(continueButton);
            UpdateProfileUi();
            return;
        }

        if (pauseWithTimeScale)
            Time.timeScale = _previousTimeScale;

        if (unlockCursorWhenPaused)
        {
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLockMode;
        }

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void SuppressCameraToggleUntilRelease()
    {
        if (inputProfileSwitcher != null && inputProfileSwitcher.ReadAnyCameraButtonPressed(buttonPressThreshold))
            _ignoreCameraButtonUntilRelease = true;

        ResetCameraButtonState();
    }

    private void ResetCameraButtonState()
    {
        _cameraButtonWasPressed = false;
        _cameraButtonHoldConsumed = false;
        _cameraButtonWasActiveAtPress = false;
        _cameraButtonStartedWhilePaused = false;
        _cameraButtonPressedAt = 0f;
    }

    private void SetMenuVisible(bool visible)
    {
        if (pauseMenuRoot != null)
            pauseMenuRoot.SetActive(visible);
    }

    private void WireButtons()
    {
        if (continueButton != null)
            continueButton.onClick.AddListener(Resume);

        if (ps4Button != null)
            ps4Button.onClick.AddListener(SelectPS4);

        if (vr2Button != null)
            vr2Button.onClick.AddListener(SelectVR2);

        if (calibrateButton != null)
            calibrateButton.onClick.AddListener(StartCalibration);

        if (finishCalibrationButton != null)
            finishCalibrationButton.onClick.AddListener(FinishCalibration);

        if (resetCalibrationButton != null)
            resetCalibrationButton.onClick.AddListener(ResetCalibration);

        if (orientationModeButton != null)
            orientationModeButton.onClick.AddListener(ToggleOrientationMode);

        if (proximityThresholdButton != null)
            proximityThresholdButton.onClick.AddListener(CycleProximityThreshold);

        if (descentMarginButton != null)
            descentMarginButton.onClick.AddListener(CycleDescentMargin);

        if (guideLengthButton != null)
            guideLengthButton.onClick.AddListener(CycleGuideLength);
    }

    private void OnActiveProfileChanged(InputProfileSwitcher.InputProfileKind profile)
    {
        if (_isCalibrating)
            CancelCalibration();

        UpdateProfileUi();
        SelectFallbackMenuButton();
    }

    private void UpdateProfileUi()
    {
        if (inputProfileSwitcher == null)
            return;

        var activeProfile = inputProfileSwitcher.ActiveProfile;
        bool calibrationAllowed = IsCalibrationAllowedForActiveProfile();

        if (activeProfileText != null)
            activeProfileText.text = $"Joystick: {inputProfileSwitcher.GetDisplayName(activeProfile)}";

        if (ps4Button != null)
            ps4Button.interactable = !_isCalibrating && activeProfile != InputProfileSwitcher.InputProfileKind.PS4;

        if (vr2Button != null)
            vr2Button.interactable = !_isCalibrating && activeProfile != InputProfileSwitcher.InputProfileKind.VR2;

        if (calibrateButton != null)
            calibrateButton.interactable = calibrationAllowed && !_isCalibrating;

        if (finishCalibrationButton != null)
            finishCalibrationButton.interactable = calibrationAllowed && _isCalibrating;

        if (resetCalibrationButton != null)
            resetCalibrationButton.interactable = calibrationAllowed && !_isCalibrating;

        if (!_isCalibrating && calibrationStatusText != null)
        {
            if (!calibrationAllowed)
                SetCalibrationStatus(CalibrationUnavailableForPs4Status);
            else if (string.IsNullOrWhiteSpace(calibrationStatusText.text)
                || calibrationStatusText.text == CalibrationUnavailableForPs4Status)
                SetCalibrationStatus($"Calibracion lista para {inputProfileSwitcher.GetDisplayName(activeProfile)}");
        }

        if (joystickAdapter == null)
            joystickAdapter = FindFirstObjectByType<JoystickAdapter>();
        UpdateOrientationButtonText();
        UpdateProximityThresholdButtonText();
        UpdateDescentMarginButtonText();
        UpdateGuideLengthButtonText();

        if (EventSystem.current != null)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            var selectable = selected != null ? selected.GetComponent<Selectable>() : null;
            if (!IsSelectable(selectable))
                SelectFallbackMenuButton();
        }
    }

    private void HandleAnalogNavigation()
    {
        if (inputProfileSwitcher == null)
            return;

        float horizontal = inputProfileSwitcher.ReadMenuHorizontal();
        float vertical = inputProfileSwitcher.ReadMenuVertical();
        Vector2Int direction = GetNavigationDirection(horizontal, vertical);

        if (direction == Vector2Int.zero)
        {
            ResetNavigationState();
            return;
        }

        float now = Time.unscaledTime;
        if (direction != _heldNavigationDirection)
        {
            _heldNavigationDirection = direction;
            _nextNavigationAt = now + navigationRepeatDelay;
            MoveSelection(direction);
            return;
        }

        if (now < _nextNavigationAt)
            return;

        _nextNavigationAt = now + navigationRepeatRate;
        MoveSelection(direction);
    }

    private Vector2Int GetNavigationDirection(float horizontal, float vertical)
    {
        float absHorizontal = Mathf.Abs(horizontal);
        float absVertical = Mathf.Abs(vertical);

        if (absHorizontal < navigationThreshold && absVertical < navigationThreshold)
            return Vector2Int.zero;

        if (absHorizontal > absVertical)
            return horizontal > 0f ? Vector2Int.right : Vector2Int.left;

        return vertical > 0f ? Vector2Int.up : Vector2Int.down;
    }

    private void MoveSelection(Vector2Int direction)
    {
        if (EventSystem.current == null)
            return;

        Selectable current = EventSystem.current.currentSelectedGameObject != null
            ? EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>()
            : null;

        if (!IsSelectable(current))
        {
            SelectButton(continueButton);
            return;
        }

        Selectable next = FindNextSelectable(current, direction);
        if (IsSelectable(next))
            SelectButton(next);
    }

    private Selectable FindNextSelectable(Selectable current, Vector2Int direction)
    {
        if (direction == Vector2Int.left)
        {
            if (current == vr2Button && IsSelectable(ps4Button)) return ps4Button;
            return null;
        }

        if (direction == Vector2Int.right)
        {
            if (current == ps4Button && IsSelectable(vr2Button)) return vr2Button;
            return null;
        }

        var order = new Selectable[]
        {
            FirstProfileButton(),
            calibrateButton,
            finishCalibrationButton,
            resetCalibrationButton,
            orientationModeButton,
            proximityThresholdButton,
            descentMarginButton,
            guideLengthButton,
            continueButton
        };

        int currentIndex = IndexOf(order, current);
        if (currentIndex < 0 && (current == ps4Button || current == vr2Button))
            currentIndex = 0;

        int step = direction == Vector2Int.up ? -1 : 1;
        for (int i = currentIndex + step; i >= 0 && i < order.Length; i += step)
        {
            if (IsSelectable(order[i]))
                return order[i];
        }

        return null;
    }

    private Selectable FirstProfileButton()
    {
        if (IsSelectable(ps4Button)) return ps4Button;
        if (IsSelectable(vr2Button)) return vr2Button;
        return null;
    }

    private static int IndexOf(Selectable[] selectables, Selectable target)
    {
        for (int i = 0; i < selectables.Length; i++)
        {
            if (selectables[i] == target)
                return i;
        }

        return -1;
    }

    private static bool IsSelectable(Selectable selectable)
    {
        return selectable != null && selectable.IsActive() && selectable.IsInteractable();
    }

    private void SubmitSelected()
    {
        if (EventSystem.current == null)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null)
        {
            SelectButton(continueButton);
            selected = continueButton != null ? continueButton.gameObject : null;
        }

        if (selected == null)
            return;

        var selectable = selected.GetComponent<Selectable>();
        if (!IsSelectable(selectable))
            return;

        _ignoreMenuSubmitUntilRelease = true;
        ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
    }

    private void UpdateCalibrationCapture()
    {
        if (!EnsureCalibrationManager())
            return;

        calibrationManager.Capture(inputProfileSwitcher.GetCalibrationAxes());
        SetCalibrationStatus(calibrationManager.GetCaptureStatus(inputProfileSwitcher.GetCalibrationAxes()));
    }

    private void CancelCalibration()
    {
        _isCalibrating = false;
        calibrationManager?.CancelCapture();
        SetCalibrationStatus("Calibracion cancelada");
        UpdateProfileUi();
    }

    private bool EnsureCalibrationManager()
    {
        if (inputProfileSwitcher == null)
            inputProfileSwitcher = FindFirstObjectByType<InputProfileSwitcher>();

        if (calibrationManager == null)
            calibrationManager = FindFirstObjectByType<AnalogCalibrationManager>();

        if (inputProfileSwitcher != null && calibrationManager != null)
            return true;

        SetCalibrationStatus("Calibracion no disponible");
        return false;
    }

    private bool IsCalibrationAllowedForActiveProfile()
    {
        return inputProfileSwitcher != null
            && inputProfileSwitcher.ActiveProfile == InputProfileSwitcher.InputProfileKind.VR2;
    }

    private void SetCalibrationStatus(string status)
    {
        if (calibrationStatusText != null)
            calibrationStatusText.text = status;
    }

    private void SelectButton(Selectable selectable)
    {
        if (!IsSelectable(selectable) || EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        RefreshSelectedTextColors();
    }

    private void SelectFallbackMenuButton()
    {
        Selectable fallback = FirstProfileButton();

        if (!IsSelectable(fallback))
            fallback = calibrateButton;

        if (!IsSelectable(fallback))
            fallback = resetCalibrationButton;

        if (!IsSelectable(fallback))
            fallback = continueButton;

        SelectButton(fallback);
    }

    private void ResetNavigationState()
    {
        _heldNavigationDirection = Vector2Int.zero;
        _nextNavigationAt = 0f;
    }

    private void BuildDefaultMenu()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogWarning("[PauseMenuController] No se encontro un Canvas para crear el menu de pausa.");
            return;
        }

        pauseMenuRoot = CreateUiObject("PauseMenu", canvas.transform);
        StretchToParent(pauseMenuRoot.GetComponent<RectTransform>());

        var overlay = pauseMenuRoot.AddComponent<Image>();
        overlay.color = overlayColor;

        GameObject panel = CreateUiObject("Panel", pauseMenuRoot.transform);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        // 740 (y no 560) para que entren los tres botones de asistencia (frenado, bloqueo de
        // descenso y guías) sin recortar 'Continuar'.
        panelRect.sizeDelta = new Vector2(460f, 740f);

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = panelColor;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        CreateText("Title", "Pausa", panel.transform, 34, FontStyles.Bold);

        activeProfileText = CreateText("ActiveProfile", "Joystick: PS4", panel.transform, 20, FontStyles.Normal);

        GameObject selectorRow = CreateUiObject("JoystickSelector", panel.transform);
        var selectorLayout = selectorRow.AddComponent<HorizontalLayoutGroup>();
        selectorLayout.spacing = 12f;
        selectorLayout.childControlHeight = true;
        selectorLayout.childControlWidth = true;
        selectorLayout.childForceExpandHeight = false;
        selectorLayout.childForceExpandWidth = true;

        var selectorLayoutElement = selectorRow.AddComponent<LayoutElement>();
        selectorLayoutElement.preferredHeight = 48f;

        ps4Button = CreateButton("PS4", selectorRow.transform);
        vr2Button = CreateButton("VR-2", selectorRow.transform);

        futureOptionsContainer = CreateUiObject("CalibrationOptions", panel.transform);
        var calibrationLayout = futureOptionsContainer.AddComponent<VerticalLayoutGroup>();
        calibrationLayout.spacing = 8f;
        calibrationLayout.childControlHeight = true;
        calibrationLayout.childControlWidth = true;
        calibrationLayout.childForceExpandHeight = false;
        calibrationLayout.childForceExpandWidth = true;

        calibrationStatusText = CreateText("CalibrationStatus", "Calibracion lista", futureOptionsContainer.transform, 16, FontStyles.Normal);
        var calibrationStatusLayout = calibrationStatusText.GetComponent<LayoutElement>();
        calibrationStatusLayout.preferredHeight = 98f;

        calibrateButton = CreateButton("Calibrar analogicos", futureOptionsContainer.transform);
        finishCalibrationButton = CreateButton("Finalizar calibracion", futureOptionsContainer.transform);
        resetCalibrationButton = CreateButton("Restablecer calibracion", futureOptionsContainer.transform);

        orientationModeButton = CreateButton("Orientación: Fija Absoluta", panel.transform);
        orientationModeButton.gameObject.name = "OrientationButton";

        proximityThresholdButton = CreateButton(BuildProximityThresholdLabel(), panel.transform);
        proximityThresholdButton.gameObject.name = "ProximityThresholdButton";

        descentMarginButton = CreateButton(BuildDescentMarginLabel(), panel.transform);
        descentMarginButton.gameObject.name = "DescentMarginButton";

        guideLengthButton = CreateButton(BuildGuideLengthLabel(), panel.transform);
        guideLengthButton.gameObject.name = "GuideLengthButton";

        continueButton = CreateButton("Continuar", panel.transform);
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = 5;
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static TextMeshProUGUI CreateText(string name, string text, Transform parent, int fontSize, FontStyles fontStyle)
    {
        GameObject textObject = CreateUiObject(name, parent);
        var label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        var layoutElement = textObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = fontSize + 12f;

        return label;
    }

    private Button CreateButton(string label, Transform parent)
    {
        GameObject buttonObject = CreateUiObject($"{label}Button", parent);
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(0f, 46f);

        var image = buttonObject.AddComponent<Image>();
        image.color = normalButtonColor;

        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        ApplyButtonColors(button);

        var layoutElement = buttonObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 46f;

        TextMeshProUGUI text = CreateText("Label", label, buttonObject.transform, 20, FontStyles.Bold);
        text.color = buttonTextColor;
        StretchToParent(text.rectTransform);

        return button;
    }

    private void ApplyButtonColors()
    {
        ApplyButtonColors(continueButton);
        ApplyButtonColors(ps4Button);
        ApplyButtonColors(vr2Button);
        ApplyButtonColors(calibrateButton);
        ApplyButtonColors(finishCalibrationButton);
        ApplyButtonColors(resetCalibrationButton);
        ApplyButtonColors(orientationModeButton);
        ApplyButtonColors(proximityThresholdButton);
        ApplyButtonColors(descentMarginButton);
        ApplyButtonColors(guideLengthButton);
        RefreshSelectedTextColors();
    }

    private void ApplyButtonColors(Button button)
    {
        if (button == null)
            return;

        if (button.targetGraphic is Image image)
            image.color = normalButtonColor;

        var colors = button.colors;
        colors.normalColor = normalButtonColor;
        colors.highlightedColor = highlightedButtonColor;
        colors.selectedColor = selectedButtonColor;
        colors.pressedColor = pressedButtonColor;
        colors.disabledColor = disabledButtonColor;
        button.colors = colors;
    }

    private void RefreshSelectedTextColors()
    {
        if (EventSystem.current == null)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        SetButtonTextColor(continueButton, selected);
        SetButtonTextColor(ps4Button, selected);
        SetButtonTextColor(vr2Button, selected);
        SetButtonTextColor(calibrateButton, selected);
        SetButtonTextColor(finishCalibrationButton, selected);
        SetButtonTextColor(resetCalibrationButton, selected);
        SetButtonTextColor(orientationModeButton, selected);
        SetButtonTextColor(proximityThresholdButton, selected);
        SetButtonTextColor(descentMarginButton, selected);
        SetButtonTextColor(guideLengthButton, selected);
    }

    private void SetButtonTextColor(Button button, GameObject selected)
    {
        if (button == null)
            return;

        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null)
            return;

        label.color = button.gameObject == selected && button.interactable
            ? selectedButtonTextColor
            : buttonTextColor;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
