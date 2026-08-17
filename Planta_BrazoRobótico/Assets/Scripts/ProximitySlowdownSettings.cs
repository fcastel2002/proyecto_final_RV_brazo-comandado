using UnityEngine;

/// <summary>
/// Ajustes globales de la asistencia por proximidad del gripper:
///
/// - <see cref="ThresholdMeters"/>: distancia (m) a partir de la cual el brazo empieza a frenar
///   de forma progresiva al acercarse a un objeto.
/// - <see cref="DescentMarginMeters"/>: hueco (m) que se reserva por debajo del gripper cerrado
///   **vacio**; por debajo de el no se permite seguir bajando. Es un valor propio y mucho mas corto
///   que el umbral de frenado, para poder acercarse a la pieza en vez de frenar a 30 cm del suelo.
/// - <see cref="CarryDescentMarginMeters"/>: el mismo hueco pero **transportando una pieza**, medido
///   bajo la pieza (el sensor ya descuenta cuanto sobresale). Es otro valor propio, y mucho mas corto
///   todavia, por la misma razon una vuelta mas: con el margen del gripper vacio, depositar la pieza
///   era imposible porque el brazo se frenaba con ella a 5 cm de la superficie.
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
    private const string CarryDescentMarginPrefsKey = "ProximitySlowdown.CarryDescentMarginMeters";
    private const float DefaultThresholdMeters = 0.30f;
    private const float DefaultDescentMarginMeters = 0.05f;
    private const float DefaultCarryDescentMarginMeters = 0.005f;

    /// <summary>Umbral de frenado seleccionable desde el menu de pausa, en metros. 0 = sin frenado.</summary>
    public static readonly float[] Presets = { 0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0f };

    /// <summary>Margen de bloqueo de descenso seleccionable desde el menu de pausa, en metros. 0 = sin bloqueo.</summary>
    public static readonly float[] DescentMarginPresets = { 0.03f, 0.05f, 0.08f, 0.12f, 0f };

    /// <summary>
    /// Margen de bloqueo de descenso **mientras se transporta una pieza**, en metros. 0 = sin bloqueo.
    /// Son valores mucho mas cortos que <see cref="DescentMarginPresets"/> a proposito: ese margen
    /// protege al gripper vacio de estrellarse contra el suelo, pero con una pieza agarrada la maniobra
    /// es la contraria, hay que poder apoyarla, y el hueco util tiene que llegar casi a cero.
    /// </summary>
    public static readonly float[] CarryDescentMarginPresets = { 0.005f, 0.01f, 0.02f, 0.03f, 0f };

    private static bool _loaded;
    private static float _thresholdMeters = DefaultThresholdMeters;
    private static float _descentMarginMeters = DefaultDescentMarginMeters;
    private static float _carryDescentMarginMeters = DefaultCarryDescentMarginMeters;

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

    public static float CarryDescentMarginMeters
    {
        get
        {
            EnsureLoaded();
            return _carryDescentMarginMeters;
        }
    }

    public static bool IsEnabled => ThresholdMeters > 0f;

    public static bool IsDescentBlockEnabled => DescentMarginMeters > 0f;

    public static bool IsCarryDescentBlockEnabled => CarryDescentMarginMeters > 0f;

    /// <summary>
    /// Margen que corresponde aplicar segun se lleve o no una pieza. Unico punto donde se decide,
    /// para que el bloqueo y su HUD no puedan discrepar.
    /// </summary>
    public static float GetDescentMargin(bool isCarryingPayload)
    {
        return isCarryingPayload ? CarryDescentMarginMeters : DescentMarginMeters;
    }

    /// <summary>true si el bloqueo de descenso debe actuar en la situacion actual.</summary>
    public static bool IsDescentBlockEnabledFor(bool isCarryingPayload)
    {
        return isCarryingPayload ? IsCarryDescentBlockEnabled : IsDescentBlockEnabled;
    }

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

    public static void SetCarryDescentMargin(float meters)
    {
        EnsureLoaded();

        float clamped = Mathf.Max(0f, meters);
        if (Mathf.Approximately(clamped, _carryDescentMarginMeters)) return;

        _carryDescentMarginMeters = clamped;
        PlayerPrefs.SetFloat(CarryDescentMarginPrefsKey, clamped);
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

    /// <summary>Avanza al siguiente preset de margen con pieza (con wraparound) y devuelve el valor aplicado.</summary>
    public static float CycleCarryDescentMarginToNext()
    {
        EnsureLoaded();

        int nextIndex = (NearestPresetIndex(CarryDescentMarginPresets, _carryDescentMarginMeters) + 1) % CarryDescentMarginPresets.Length;
        SetCarryDescentMargin(CarryDescentMarginPresets[nextIndex]);
        return _carryDescentMarginMeters;
    }

    /// <summary>Texto para el boton de bloqueo de descenso con pieza del menu de pausa.</summary>
    public static string DescribeCarryDescentMargin()
    {
        if (!IsCarryDescentBlockEnabled) return "Desactivado";

        // En milimetros: los presets con pieza son de pocos milimetros y en cm se leerian todos "0 cm".
        return $"{CarryDescentMarginMeters * 1000f:F0} mm";
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
        _carryDescentMarginMeters = Mathf.Max(0f, PlayerPrefs.GetFloat(CarryDescentMarginPrefsKey, DefaultCarryDescentMarginMeters));
    }
}
