using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputProfileSwitcher : MonoBehaviour
{
    private const string SavedProfilePlayerPrefsKey = "InputProfileSwitcher.ActiveProfile";

    public enum InputProfileKind
    {
        PS4,
        VR2
    }

    private enum AxisRole
    {
        MoveX,
        MoveY,
        MoveZ
    }

    [Serializable]
    private sealed class AxisUiBinding
    {
        public AxisRole axis;
        public AxisUIController controller;
    }

    [Header("Targets")]
    [SerializeField] private JoystickAdapter joystickAdapter;
    [SerializeField] private CameraJoystickController cameraController;
    [SerializeField] private Ctrl_OnRobotRG2_Custom gripperController;
    [SerializeField] private AnalogCalibrationManager calibrationManager;
    [SerializeField] private AxisUiBinding[] axisUiBindings = Array.Empty<AxisUiBinding>();
    [SerializeField] private bool autoDiscoverAxisUi = true;

    [Header("Input Assets")]
    [SerializeField] private InputActionAsset ps4JoystickAsset;
    [SerializeField] private InputActionAsset robotBasicAsset;

    [Header("Startup")]
    [SerializeField] private InputProfileKind defaultProfile = InputProfileKind.PS4;
    [SerializeField] private bool applyDefaultOnStart = true;
    [SerializeField] private bool rememberSelectedProfile = true;

    [Header("Global Menu Input")]
    [SerializeField] private bool invertVr2MenuHorizontal;
    [SerializeField] private bool invertVr2MenuVertical;

    private readonly Dictionary<string, InputActionReference> _runtimeReferences = new Dictionary<string, InputActionReference>();
    private bool _axisUiDiscovered;
    private bool _globalActionsResolved;
    private bool _simulationInputEnabled = true;
    private GlobalMenuActions _globalMenuActions;
    private ResolvedProfile _activeActions;

    public event Action<InputProfileKind> ActiveProfileChanged;

    public InputProfileKind ActiveProfile { get; private set; } = InputProfileKind.PS4;
    public JoystickAdapter JoystickAdapter => joystickAdapter;
    public InputActionReference ActiveCameraToggle => _activeActions.CameraToggle;

    private void Start()
    {
        EnsureTargets();

        if (applyDefaultOnStart)
            ApplyProfile(GetStartupProfile());
    }

    private void OnDestroy()
    {
        DisableGlobalActions();

        foreach (var reference in _runtimeReferences.Values)
        {
            if (reference != null)
                Destroy(reference);
        }

        _runtimeReferences.Clear();
    }

    public void SelectPS4()
    {
        ApplyProfile(InputProfileKind.PS4);
    }

    public void SelectVR2()
    {
        ApplyProfile(InputProfileKind.VR2);
    }

    public void ApplyProfile(InputProfileKind profile)
    {
        EnsureTargets();
        EnsureGlobalActions();

        if (!TryBuildProfile(profile, out var actions))
            return;

        DisableGlobalActions();

        ActiveProfile = profile;
        _activeActions = actions;
        calibrationManager?.SetActiveProfile(ActiveProfile);

        joystickAdapter?.ApplyInputActions(
            actions.MoveX,
            actions.MoveY,
            actions.MoveZ,
            null,
            actions.CameraModeAllowed);

        cameraController?.ApplyInputActions(
            actions.CameraModeAllowed ? actions.MoveForward : null,
            actions.CameraModeAllowed ? actions.MoveSide : null,
            actions.CameraModeAllowed ? actions.ViewUp : null,
            actions.CameraModeAllowed ? actions.ViewSide : null);

        gripperController?.ApplyInputAction(actions.Gripper);
        ApplyAxisUiBindings(actions);
        EnableGlobalActions();
        ApplySimulationInputState();
        SaveSelectedProfile();
        ActiveProfileChanged?.Invoke(ActiveProfile);

        Debug.Log($"[InputProfileSwitcher] Perfil activo: {GetDisplayName(ActiveProfile)}");
    }

    public float ReadMenuHorizontal()
    {
        EnsureGlobalActions();

        float ps4 = ReadDigitalPair(_globalMenuActions.Ps4Left, _globalMenuActions.Ps4Right);
        float vr2 = ReadAxisForProfile(InputProfileKind.VR2, _globalMenuActions.Vr2Horizontal, _globalMenuActions.Vr2HorizontalInverted);
        return Mathf.Abs(ps4) >= Mathf.Abs(vr2) ? ps4 : vr2;
    }

    public float ReadMenuVertical()
    {
        EnsureGlobalActions();

        float ps4 = ReadDigitalPair(_globalMenuActions.Ps4Down, _globalMenuActions.Ps4Up);
        float vr2 = ReadAxisForProfile(InputProfileKind.VR2, _globalMenuActions.Vr2Vertical, _globalMenuActions.Vr2VerticalInverted);
        return Mathf.Abs(ps4) >= Mathf.Abs(vr2) ? ps4 : vr2;
    }

    public bool ReadCameraButtonPressed(float threshold = 0.5f)
    {
        return ReadRaw(_activeActions.CameraToggle) >= threshold;
    }

    public bool ReadAnyCameraButtonPressed(float threshold = 0.5f)
    {
        EnsureGlobalActions();

        return ReadRaw(_globalMenuActions.Ps4CameraToggle) >= threshold
            || ReadRaw(_globalMenuActions.Vr2CameraToggle) >= threshold;
    }

    public bool ReadActiveCameraButtonPressed(float threshold = 0.5f)
    {
        return ReadRaw(_activeActions.CameraToggle) >= threshold;
    }

    public bool WasMenuSubmitPressedThisFrame()
    {
        EnsureGlobalActions();

        return WasPressedThisFrame(_globalMenuActions.Ps4Click)
            || WasPressedThisFrame(_globalMenuActions.Vr2Click);
    }

    public bool ReadMenuSubmitPressed(float threshold = 0.5f)
    {
        EnsureGlobalActions();

        return ReadRaw(_globalMenuActions.Ps4Click) >= threshold
            || ReadRaw(_globalMenuActions.Vr2Click) >= threshold;
    }

    public bool WasPausePressedThisFrame()
    {
        EnsureGlobalActions();

        return WasPressedThisFrame(_globalMenuActions.Ps4Pause);
    }

    public void ToggleCameraMode()
    {
        joystickAdapter?.ToggleCameraMode();
    }

    public void SetSimulationInputEnabled(bool enabled)
    {
        EnsureTargets();

        if (_simulationInputEnabled == enabled)
            return;

        _simulationInputEnabled = enabled;
        ApplySimulationInputState();
    }

    public IEnumerable<InputActionReference> GetCalibrationAxes()
    {
        foreach (var axis in UniqueAxes(
            _activeActions.MoveX,
            _activeActions.MoveY,
            _activeActions.MoveZ,
            _activeActions.MoveForward,
            _activeActions.MoveSide,
            _activeActions.ViewUp,
            _activeActions.ViewSide))
        {
            yield return axis;
        }
    }

    public string GetDisplayName(InputProfileKind profile)
    {
        return profile == InputProfileKind.PS4 ? "PS4" : "VR-2";
    }

    private bool TryBuildProfile(InputProfileKind profile, out ResolvedProfile actions)
    {
        actions = default;

        if (profile == InputProfileKind.PS4)
        {
            if (ps4JoystickAsset == null)
            {
                Debug.LogWarning("[InputProfileSwitcher] Falta asignar PS4Joystick.inputactions.");
                return false;
            }

            actions = new ResolvedProfile
            {
                MoveX = Resolve(ps4JoystickAsset, "PS4Joystick", "MoveX"),
                MoveY = Resolve(ps4JoystickAsset, "PS4Joystick", "MoveY"),
                MoveZ = Resolve(ps4JoystickAsset, "PS4Joystick", "MoveZ"),
                CameraToggle = Resolve(ps4JoystickAsset, "PS4Joystick", "ModoCamara"),
                MoveForward = Resolve(ps4JoystickAsset, "PS4Joystick", "MoveForward"),
                MoveSide = Resolve(ps4JoystickAsset, "PS4Joystick", "MoveSide"),
                ViewUp = Resolve(ps4JoystickAsset, "PS4Joystick", "ViewUp"),
                ViewSide = Resolve(ps4JoystickAsset, "PS4Joystick", "ViewSide"),
                Gripper = Resolve(ps4JoystickAsset, "PS4Joystick", "Gripper"),
                CameraModeAllowed = true
            };

            return HasRequiredRobotActions(actions, "PS4");
        }

        if (robotBasicAsset == null)
        {
            Debug.LogWarning("[InputProfileSwitcher] Falta asignar RobotBasic.inputactions.");
            return false;
        }

        actions = new ResolvedProfile
        {
            MoveX = Resolve(robotBasicAsset, "Basico Cartesiano", "Mover X"),
            MoveY = Resolve(robotBasicAsset, "Basico Cartesiano", "Mover Y"),
            MoveZ = Resolve(robotBasicAsset, "Basico Cartesiano", "Mover Z"),
            CameraToggle = Resolve(robotBasicAsset, "Basico Cartesiano", "Camera Toggle"),
            MoveForward = Resolve(robotBasicAsset, "Basico Cartesiano", "Mover X"),
            MoveSide = Resolve(robotBasicAsset, "Basico Cartesiano", "Mover Z"),
            Gripper = Resolve(robotBasicAsset, "Basico Cartesiano", "Gripper"),
            CameraModeAllowed = true
        };

        return HasRequiredRobotActions(actions, "VR-2");
    }

    private InputProfileKind GetStartupProfile()
    {
        if (!rememberSelectedProfile || !PlayerPrefs.HasKey(SavedProfilePlayerPrefsKey))
            return defaultProfile;

        int savedProfile = PlayerPrefs.GetInt(SavedProfilePlayerPrefsKey, (int)defaultProfile);
        if (Enum.IsDefined(typeof(InputProfileKind), savedProfile))
            return (InputProfileKind)savedProfile;

        return defaultProfile;
    }

    private void SaveSelectedProfile()
    {
        if (!rememberSelectedProfile)
            return;

        PlayerPrefs.SetInt(SavedProfilePlayerPrefsKey, (int)ActiveProfile);
        PlayerPrefs.Save();
    }

    private bool HasRequiredRobotActions(ResolvedProfile actions, string profileName)
    {
        bool ok = actions.MoveX != null
            && actions.MoveY != null
            && actions.MoveZ != null
            && actions.CameraToggle != null
            && actions.Gripper != null;

        if (!ok)
            Debug.LogWarning($"[InputProfileSwitcher] El perfil {profileName} no tiene todas las actions necesarias.");

        return ok;
    }

    private InputActionReference Resolve(InputActionAsset asset, string mapName, string actionName)
    {
        if (asset == null || string.IsNullOrWhiteSpace(actionName))
            return null;

        string path = string.IsNullOrWhiteSpace(mapName) ? actionName : $"{mapName}/{actionName}";
        InputAction action = asset.FindAction(path, throwIfNotFound: false)
            ?? asset.FindAction(actionName, throwIfNotFound: false);

        if (action == null)
        {
            Debug.LogWarning($"[InputProfileSwitcher] No se encontro la action '{path}' en {asset.name}.");
            return null;
        }

        string key = $"{asset.GetInstanceID()}:{action.id}";
        if (_runtimeReferences.TryGetValue(key, out var reference) && reference != null)
            return reference;

        reference = InputActionReference.Create(action);
        reference.name = $"{asset.name}/{action.name}";
        _runtimeReferences[key] = reference;
        return reference;
    }

    private void ApplyAxisUiBindings(ResolvedProfile actions)
    {
        if (axisUiBindings == null)
            return;

        foreach (var binding in axisUiBindings)
        {
            if (binding?.controller == null)
                continue;

            binding.controller.SetInputAction(GetAxisAction(actions, binding.axis));
        }
    }

    private static InputActionReference GetAxisAction(ResolvedProfile actions, AxisRole axis)
    {
        return axis switch
        {
            AxisRole.MoveX => actions.MoveX,
            AxisRole.MoveY => actions.MoveY,
            AxisRole.MoveZ => actions.MoveZ,
            _ => null
        };
    }

    private void EnsureTargets()
    {
        if (joystickAdapter == null)
            joystickAdapter = FindFirstObjectByType<JoystickAdapter>();

        if (cameraController == null)
            cameraController = FindFirstObjectByType<CameraJoystickController>();

        if (gripperController == null)
            gripperController = FindFirstObjectByType<Ctrl_OnRobotRG2_Custom>();

        if (calibrationManager == null)
            calibrationManager = FindFirstObjectByType<AnalogCalibrationManager>();

        if (autoDiscoverAxisUi && !_axisUiDiscovered)
            DiscoverAxisUiBindings();
    }

    private void EnableGlobalActions()
    {
        _activeActions.CameraToggle?.action.Enable();
        _globalMenuActions.Enable();
    }

    private void DisableGlobalActions()
    {
        _activeActions.CameraToggle?.action.Disable();
        _globalMenuActions.Disable();
    }

    private void ApplySimulationInputState()
    {
        bool suppressed = !_simulationInputEnabled;
        joystickAdapter?.SetInputSuppressed(suppressed);
        cameraController?.SetInputSuppressed(suppressed);
        gripperController?.SetInputSuppressed(suppressed);
    }

    private float ReadAxis(InputActionReference actionReference, bool inverted)
    {
        float value = calibrationManager != null
            ? calibrationManager.ReadCalibrated(actionReference)
            : ReadRaw(actionReference);

        return inverted ? -value : value;
    }

    private float ReadAxisForProfile(InputProfileKind profile, InputActionReference actionReference, bool inverted)
    {
        float value = calibrationManager != null
            ? calibrationManager.ReadCalibrated(profile, actionReference)
            : ReadRaw(actionReference);

        return inverted ? -value : value;
    }

    private static float ReadRaw(InputActionReference actionReference)
    {
        return actionReference?.action.ReadValue<float>() ?? 0f;
    }

    private static float ReadDigitalPair(InputActionReference negative, InputActionReference positive)
    {
        float value = 0f;

        if (ReadRaw(negative) >= 0.5f)
            value -= 1f;

        if (ReadRaw(positive) >= 0.5f)
            value += 1f;

        return Mathf.Clamp(value, -1f, 1f);
    }

    private static bool WasPressedThisFrame(InputActionReference actionReference)
    {
        return actionReference != null
            && actionReference.action != null
            && actionReference.action.WasPressedThisFrame();
    }

    private void EnsureGlobalActions()
    {
        if (_globalActionsResolved)
            return;

        _globalActionsResolved = true;

        _globalMenuActions = new GlobalMenuActions
        {
            Ps4Up = Resolve(ps4JoystickAsset, "PS4Joystick", "SelectorUP"),
            Ps4Down = Resolve(ps4JoystickAsset, "PS4Joystick", "SelectorDOWN"),
            Ps4Left = Resolve(ps4JoystickAsset, "PS4Joystick", "SelectorLEFT"),
            Ps4Right = Resolve(ps4JoystickAsset, "PS4Joystick", "SelectorRIGHT"),
            Ps4Click = Resolve(ps4JoystickAsset, "PS4Joystick", "Click"),
            Ps4Pause = Resolve(ps4JoystickAsset, "PS4Joystick", "Pause"),
            Ps4CameraToggle = Resolve(ps4JoystickAsset, "PS4Joystick", "ModoCamara"),
            Vr2Vertical = Resolve(robotBasicAsset, "Basico Cartesiano", "SelectorUP/DOWN"),
            Vr2Horizontal = Resolve(robotBasicAsset, "Basico Cartesiano", "SelectorLEFT/RIGHT"),
            Vr2Click = Resolve(robotBasicAsset, "Basico Cartesiano", "Click"),
            Vr2CameraToggle = Resolve(robotBasicAsset, "Basico Cartesiano", "Camera Toggle"),
            Vr2HorizontalInverted = invertVr2MenuHorizontal,
            Vr2VerticalInverted = invertVr2MenuVertical
        };

        _globalMenuActions.Enable();
    }

    private static IEnumerable<InputActionReference> UniqueAxes(params InputActionReference[] axes)
    {
        var seen = new HashSet<string>();
        foreach (var axis in axes)
        {
            if (axis == null || axis.action == null)
                continue;

            string id = axis.action.id.ToString("N");
            if (seen.Add(id))
                yield return axis;
        }
    }

    private void DiscoverAxisUiBindings()
    {
        _axisUiDiscovered = true;

        if (axisUiBindings != null && axisUiBindings.Length > 0)
            return;

        var discovered = new List<AxisUiBinding>();
        foreach (var controller in FindObjectsByType<AxisUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (TryGetAxisRole(controller, out AxisRole axis))
            {
                discovered.Add(new AxisUiBinding
                {
                    axis = axis,
                    controller = controller
                });
            }
        }

        axisUiBindings = discovered.ToArray();
    }

    private static bool TryGetAxisRole(AxisUIController controller, out AxisRole axis)
    {
        axis = AxisRole.MoveX;

        string actionName = controller?.CurrentInputAction?.action?.name;
        if (string.IsNullOrEmpty(actionName))
            return false;

        switch (actionName)
        {
            case "MoveX":
            case "Mover X":
                axis = AxisRole.MoveX;
                return true;
            case "MoveY":
            case "Mover Y":
                axis = AxisRole.MoveY;
                return true;
            case "MoveZ":
            case "Mover Z":
                axis = AxisRole.MoveZ;
                return true;
            default:
                return false;
        }
    }

    private struct ResolvedProfile
    {
        public InputActionReference MoveX;
        public InputActionReference MoveY;
        public InputActionReference MoveZ;
        public InputActionReference CameraToggle;
        public InputActionReference MoveForward;
        public InputActionReference MoveSide;
        public InputActionReference ViewUp;
        public InputActionReference ViewSide;
        public InputActionReference Gripper;
        public bool CameraModeAllowed;
    }

    private struct GlobalMenuActions
    {
        public InputActionReference Ps4Up;
        public InputActionReference Ps4Down;
        public InputActionReference Ps4Left;
        public InputActionReference Ps4Right;
        public InputActionReference Ps4Click;
        public InputActionReference Ps4Pause;
        public InputActionReference Ps4CameraToggle;
        public InputActionReference Vr2Vertical;
        public InputActionReference Vr2Horizontal;
        public InputActionReference Vr2Click;
        public InputActionReference Vr2CameraToggle;
        public bool Vr2HorizontalInverted;
        public bool Vr2VerticalInverted;

        public void Enable()
        {
            Ps4Up?.action.Enable();
            Ps4Down?.action.Enable();
            Ps4Left?.action.Enable();
            Ps4Right?.action.Enable();
            Ps4Click?.action.Enable();
            Ps4Pause?.action.Enable();
            Ps4CameraToggle?.action.Enable();
            Vr2Vertical?.action.Enable();
            Vr2Horizontal?.action.Enable();
            Vr2Click?.action.Enable();
            Vr2CameraToggle?.action.Enable();
        }

        public void Disable()
        {
            Ps4Up?.action.Disable();
            Ps4Down?.action.Disable();
            Ps4Left?.action.Disable();
            Ps4Right?.action.Disable();
            Ps4Click?.action.Disable();
            Ps4Pause?.action.Disable();
            Ps4CameraToggle?.action.Disable();
            Vr2Vertical?.action.Disable();
            Vr2Horizontal?.action.Disable();
            Vr2Click?.action.Disable();
            Vr2CameraToggle?.action.Disable();
        }
    }
}
