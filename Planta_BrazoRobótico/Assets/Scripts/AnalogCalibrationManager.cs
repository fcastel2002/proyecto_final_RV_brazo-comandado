using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class AnalogCalibrationManager : MonoBehaviour
{
    [SerializeField, Range(0f, 0.3f)] private float deadZone = 0.05f;
    [SerializeField] private float minimumCalibrationRange = 0.25f;

    private readonly Dictionary<string, CaptureRange> _captureRanges = new Dictionary<string, CaptureRange>();

    public static AnalogCalibrationManager Instance { get; private set; }

    public InputProfileSwitcher.InputProfileKind ActiveProfile { get; private set; } = InputProfileSwitcher.InputProfileKind.PS4;
    public bool IsCapturing { get; private set; }

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
            return;
        }

        Debug.LogWarning("[AnalogCalibrationManager] Ya existe otra instancia activa; se usara la primera.");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetActiveProfile(InputProfileSwitcher.InputProfileKind profile)
    {
        ActiveProfile = profile;
    }

    public float ReadCalibrated(InputActionReference actionReference)
    {
        return ReadCalibrated(ActiveProfile, actionReference);
    }

    public float ReadCalibrated(InputProfileSwitcher.InputProfileKind profile, InputActionReference actionReference)
    {
        float raw = ReadRaw(actionReference);
        if (actionReference == null || actionReference.action == null)
            return 0f;

        string key = BuildActionKey(profile, actionReference);
        float min = PlayerPrefs.GetFloat(MinKey(key), -1f);
        float max = PlayerPrefs.GetFloat(MaxKey(key), 1f);

        if (max - min < 1e-4f)
            return ApplyDeadZone(Mathf.Clamp(raw, -1f, 1f));

        float center = (min + max) * 0.5f;
        float span = raw >= center ? max - center : center - min;
        if (span < 1e-4f)
            return 0f;

        float normalized = Mathf.Clamp((raw - center) / span, -1f, 1f);
        return ApplyDeadZone(normalized);
    }

    public void BeginCapture(IEnumerable<InputActionReference> axes)
    {
        IsCapturing = true;
        _captureRanges.Clear();
        Capture(axes);
    }

    public void Capture(IEnumerable<InputActionReference> axes)
    {
        if (!IsCapturing || axes == null)
            return;

        foreach (var axis in axes)
        {
            if (axis == null || axis.action == null)
                continue;

            string key = BuildActionKey(axis);
            float raw = ReadRaw(axis);

            if (!_captureRanges.TryGetValue(key, out var range))
            {
                range = new CaptureRange(axis.action.name, raw);
                _captureRanges[key] = range;
                continue;
            }

            range.Include(raw);
        }
    }

    public bool FinishCapture(IEnumerable<InputActionReference> axes, out string status)
    {
        IsCapturing = false;

        int saved = 0;
        int skipped = 0;
        var builder = new StringBuilder();
        builder.AppendLine("Calibracion finalizada");

        foreach (var axis in UniqueAxes(axes))
        {
            string key = BuildActionKey(axis);
            if (!_captureRanges.TryGetValue(key, out var range) || range.Range < minimumCalibrationRange)
            {
                skipped++;
                builder.AppendLine($"{axis.action.name}: sin recorrido suficiente");
                continue;
            }

            PlayerPrefs.SetFloat(MinKey(key), range.Min);
            PlayerPrefs.SetFloat(MaxKey(key), range.Max);
            saved++;
            builder.AppendLine($"{axis.action.name}: {range.Min:F3} / {range.Max:F3}");
        }

        PlayerPrefs.Save();

        if (saved == 0 && skipped == 0)
            builder.AppendLine("No hay ejes para calibrar");
        else if (skipped > 0)
            builder.AppendLine("Los ejes omitidos conservan su calibracion anterior");

        status = builder.ToString().TrimEnd();
        return saved > 0;
    }

    public void CancelCapture()
    {
        IsCapturing = false;
        _captureRanges.Clear();
    }

    public void ResetCalibration(IEnumerable<InputActionReference> axes)
    {
        foreach (var axis in UniqueAxes(axes))
        {
            string key = BuildActionKey(axis);
            PlayerPrefs.DeleteKey(MinKey(key));
            PlayerPrefs.DeleteKey(MaxKey(key));
        }

        PlayerPrefs.Save();
    }

    public string GetCaptureStatus(IEnumerable<InputActionReference> axes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Mueve los analogicos en circulos completos");

        foreach (var axis in UniqueAxes(axes))
        {
            string key = BuildActionKey(axis);
            if (_captureRanges.TryGetValue(key, out var range))
                builder.AppendLine($"{axis.action.name}: {range.Min:F3} / {range.Max:F3}");
            else
                builder.AppendLine($"{axis.action.name}: esperando");
        }

        return builder.ToString().TrimEnd();
    }

    private float ApplyDeadZone(float value)
    {
        float magnitude = Mathf.Abs(value);
        if (magnitude <= deadZone)
            return 0f;

        float scaled = (magnitude - deadZone) / Mathf.Max(1f - deadZone, 1e-4f);
        return Mathf.Sign(value) * Mathf.Clamp01(scaled);
    }

    private static float ReadRaw(InputActionReference actionReference)
    {
        return actionReference?.action.ReadValue<float>() ?? 0f;
    }

    private string BuildActionKey(InputActionReference actionReference)
    {
        return BuildActionKey(ActiveProfile, actionReference);
    }

    private static string BuildActionKey(InputProfileSwitcher.InputProfileKind profile, InputActionReference actionReference)
    {
        return $"{profile}.{actionReference.action.id:N}";
    }

    private static string MinKey(string key)
    {
        return $"AnalogCalibration.{key}.min";
    }

    private static string MaxKey(string key)
    {
        return $"AnalogCalibration.{key}.max";
    }

    private static IEnumerable<InputActionReference> UniqueAxes(IEnumerable<InputActionReference> axes)
    {
        if (axes == null)
            yield break;

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

    private sealed class CaptureRange
    {
        public readonly string Name;
        public float Min;
        public float Max;

        public float Range => Max - Min;

        public CaptureRange(string name, float value)
        {
            Name = name;
            Min = value;
            Max = value;
        }

        public void Include(float value)
        {
            Min = Mathf.Min(Min, value);
            Max = Mathf.Max(Max, value);
        }
    }
}
