using UnityEngine;

/// <summary>
/// Ajustes globales de la asistencia por proximidad del gripper:
///
/// - <see cref="ThresholdMeters"/>: distancia (m) a partir de la cual el brazo empieza a frenar
///   de forma progresiva al acercarse a un objeto.
/// - <see cref="DescentMarginMeters"/>: hueco (m) que se reserva por debajo del gripper cerrado;
///   por debajo de el no se permite seguir bajando. Es un valor propio y mucho mas corto que el
///   umbral de frenado, para poder depositar una pieza con precision en vez de tener que soltarla
///   desde lejos.
///
/// Vive fuera de <see cref="JoystickAdapter"/> y de <see cref="GripperDistanceSensor"/> a proposito:
/// el adapter necesita leer la distancia del sensor y el sensor necesita leer el umbral. Si los
/// valores vivieran en cualquiera de los dos, se referenciarian mutuamente.
///
/// El operario los modifica desde el menu de pausa y se persisten en PlayerPrefs, siguiendo el
/// mismo patron que InputProfileSwitcher y AnalogCalibrationManager.
/// </summary>
public static class ProximitySlowdownSettings
{
    // Clave versionada: la v1 guardaba un umbral de 10 cm que resulto imperceptible. Al cambiar el
    // nombre, los usuarios que ya tenian un valor guardado vuelven a partir del nuevo default de 30 cm
    // en vez de quedarse con el viejo.
    private const string ThresholdPrefsKey = "ProximitySlowdown.ThresholdMeters.v2";
    private const string DescentMarginPrefsKey = "ProximitySlowdown.DescentMarginMeters";
    private const float DefaultThresholdMeters = 0.30f;
    private const float DefaultDescentMarginMeters = 0.05f;

    /// <summary>Umbral de frenado seleccionable desde el menu de pausa, en metros. 0 = sin frenado.</summary>
    public static readonly float[] Presets = { 0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0f };

    /// <summary>Margen de bloqueo de descenso seleccionable desde el menu de pausa, en metros. 0 = sin bloqueo.</summary>
    public static readonly float[] DescentMarginPresets = { 0.03f, 0.05f, 0.08f, 0.12f, 0f };

    private static bool _loaded;
    private static float _thresholdMeters = DefaultThresholdMeters;
    private static float _descentMarginMeters = DefaultDescentMarginMeters;

    /// <summary>Se dispara cuando el operario cambia cualquiera de los dos ajustes.</summary>
    public static event System.Action ThresholdChanged;

    public static float ThresholdMeters
    {
        get
        {
            EnsureLoaded();
            return _thresholdMeters;
        }
    }

    public static float DescentMarginMeters
    {
        get
        {
            EnsureLoaded();
            return _descentMarginMeters;
        }
    }

    public static bool IsEnabled => ThresholdMeters > 0f;

    public static bool IsDescentBlockEnabled => DescentMarginMeters > 0f;

    public static void SetThreshold(float meters)
    {
        EnsureLoaded();

        float clamped = Mathf.Max(0f, meters);
        if (Mathf.Approximately(clamped, _thresholdMeters)) return;

        _thresholdMeters = clamped;
        PlayerPrefs.SetFloat(ThresholdPrefsKey, clamped);
        PlayerPrefs.Save();
        ThresholdChanged?.Invoke();
    }

    public static void SetDescentMargin(float meters)
    {
        EnsureLoaded();

        float clamped = Mathf.Max(0f, meters);
        if (Mathf.Approximately(clamped, _descentMarginMeters)) return;

        _descentMarginMeters = clamped;
        PlayerPrefs.SetFloat(DescentMarginPrefsKey, clamped);
        PlayerPrefs.Save();
        ThresholdChanged?.Invoke();
    }

    /// <summary>Avanza al siguiente preset de umbral (con wraparound) y devuelve el valor aplicado.</summary>
    public static float CycleToNext()
    {
        EnsureLoaded();

        int nextIndex = (NearestPresetIndex(Presets, _thresholdMeters) + 1) % Presets.Length;
        SetThreshold(Presets[nextIndex]);
        return _thresholdMeters;
    }

    /// <summary>Avanza al siguiente preset de margen de descenso (con wraparound) y devuelve el valor aplicado.</summary>
    public static float CycleDescentMarginToNext()
    {
        EnsureLoaded();

        int nextIndex = (NearestPresetIndex(DescentMarginPresets, _descentMarginMeters) + 1) % DescentMarginPresets.Length;
        SetDescentMargin(DescentMarginPresets[nextIndex]);
        return _descentMarginMeters;
    }

    /// <summary>Texto para el boton de umbral del menu de pausa.</summary>
    public static string DescribeCurrent()
    {
        return IsEnabled ? $"{ThresholdMeters * 100f:F0} cm" : "Desactivado";
    }

    /// <summary>Texto para el boton de bloqueo de descenso del menu de pausa.</summary>
    public static string DescribeDescentMargin()
    {
        return IsDescentBlockEnabled ? $"{DescentMarginMeters * 100f:F0} cm" : "Desactivado";
    }

    /// <summary>
    /// Preset mas cercano al valor actual. Se busca por cercania y no por coincidencia exacta para
    /// que un valor heredado de una version anterior (p.ej. 0.10 cuando los presets cambiaron) avance
    /// desde donde esta, en vez de saltar siempre al primero de la lista.
    /// </summary>
    private static int NearestPresetIndex(float[] presets, float value)
    {
        int nearest = 0;
        float bestDelta = float.MaxValue;

        for (int i = 0; i < presets.Length; i++)
        {
            float delta = Mathf.Abs(presets[i] - value);
            if (delta >= bestDelta) continue;

            bestDelta = delta;
            nearest = i;
        }

        return nearest;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;

        _loaded = true;
        _thresholdMeters = Mathf.Max(0f, PlayerPrefs.GetFloat(ThresholdPrefsKey, DefaultThresholdMeters));
        _descentMarginMeters = Mathf.Max(0f, PlayerPrefs.GetFloat(DescentMarginPrefsKey, DefaultDescentMarginMeters));
    }
}
