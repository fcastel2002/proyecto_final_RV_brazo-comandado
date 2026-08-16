using UnityEngine;

/// <summary>
/// Ajustes de la vista de la gripper camera. Hoy solo el largo de las guias perpendiculares
/// (la retícula en cruz sobre el recuadro).
///
/// Vive en un static con PlayerPrefs y no como [SerializeField] de LeftLayoutManager porque ese
/// componente se autoinstancia por codigo (new GameObject) y por tanto no tiene Inspector donde
/// editarlo. El operario lo cambia desde el menu de pausa.
/// </summary>
public static class GripperViewSettings
{
    private const string GuideLengthPrefsKey = "GripperView.GuideLengthFactor";
    private const float DefaultGuideLengthFactor = 0.5f;

    /// <summary>Fraccion del lado del recuadro que ocupa cada guia. 0 = ocultas, 1 = de borde a borde.</summary>
    public static readonly float[] GuideLengthPresets = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    private static bool _loaded;
    private static float _guideLengthFactor = DefaultGuideLengthFactor;

    public static event System.Action GuideLengthChanged;

    public static float GuideLengthFactor
    {
        get
        {
            EnsureLoaded();
            return _guideLengthFactor;
        }
    }

    public static void SetGuideLength(float factor)
    {
        EnsureLoaded();

        float clamped = Mathf.Clamp01(factor);
        if (Mathf.Approximately(clamped, _guideLengthFactor)) return;

        _guideLengthFactor = clamped;
        PlayerPrefs.SetFloat(GuideLengthPrefsKey, clamped);
        PlayerPrefs.Save();
        GuideLengthChanged?.Invoke();
    }

    /// <summary>Avanza al siguiente preset (con wraparound) y devuelve el valor aplicado.</summary>
    public static float CycleGuideLengthToNext()
    {
        EnsureLoaded();

        int nearest = 0;
        float bestDelta = float.MaxValue;
        for (int i = 0; i < GuideLengthPresets.Length; i++)
        {
            float delta = Mathf.Abs(GuideLengthPresets[i] - _guideLengthFactor);
            if (delta >= bestDelta) continue;

            bestDelta = delta;
            nearest = i;
        }

        SetGuideLength(GuideLengthPresets[(nearest + 1) % GuideLengthPresets.Length]);
        return _guideLengthFactor;
    }

    /// <summary>Texto para el boton del menu de pausa.</summary>
    public static string DescribeGuideLength()
    {
        if (GuideLengthFactor <= 0f) return "Ocultas";
        if (Mathf.Approximately(GuideLengthFactor, 1f)) return "Completa";

        return $"{GuideLengthFactor * 100f:F0}%";
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;

        _loaded = true;
        _guideLengthFactor = Mathf.Clamp01(PlayerPrefs.GetFloat(GuideLengthPrefsKey, DefaultGuideLengthFactor));
    }
}
