using UnityEngine;

/// <summary>
/// Modo debug global, conmutable en caliente desde el menu de pausa y persistido en PlayerPrefs.
///
/// Existe porque los flags de log de la cadena de agarre son <c>[SerializeField]</c> privados: para
/// cambiarlos hay que salir de Play Mode y tocar el Inspector componente por componente, y algunos
/// (el de <see cref="GrabbableSafetyGuard"/>) ni siquiera tienen Inspector, porque el componente se
/// auto-anade por codigo. Con esto el operario los enciende y apaga desde el propio simulador.
///
/// Los flags locales del Inspector se conservan y actuan como override: un componente con su flag en
/// true loguea aunque el modo debug global este apagado (util para aislar uno solo mientras se depura).
///
/// Mismo patron que <see cref="ProximitySlowdownSettings"/> y <see cref="GripperViewSettings"/>.
/// </summary>
public static class DebugSettings
{
    private const string EnabledPrefsKey = "Debug.LogsEnabled";

    private static bool _loaded;
    private static bool _enabled;

    /// <summary>Se dispara cuando el operario conmuta el modo debug.</summary>
    public static event System.Action EnabledChanged;

    public static bool IsEnabled
    {
        get
        {
            EnsureLoaded();
            return _enabled;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        EnsureLoaded();

        if (_enabled == enabled) return;

        _enabled = enabled;
        PlayerPrefs.SetInt(EnabledPrefsKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
        EnabledChanged?.Invoke();
    }

    /// <summary>Invierte el estado y devuelve el valor aplicado.</summary>
    public static bool Toggle()
    {
        SetEnabled(!IsEnabled);
        return _enabled;
    }

    /// <summary>Texto para el boton del menu de pausa.</summary>
    public static string Describe()
    {
        return IsEnabled ? "Activado" : "Desactivado";
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;

        _loaded = true;
        // Default 0: la consola arranca limpia. El modo debug es una accion explicita del operario.
        _enabled = PlayerPrefs.GetInt(EnabledPrefsKey, 0) != 0;
    }
}
