using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controlador de la HUD exclusiva para el modo de rotación independiente de J6.
/// Se auto-construye al arrancar si no tiene referencias asignadas desde el Inspector.
/// IMPORTANTE: Este componente debe vivir en un GameObject siempre activo (ej. el Canvas).
/// El hudContainer es un hijo separado que se muestra/oculta.
/// </summary>
public class J6HUDController : MonoBehaviour
{
    [Header("UI References (se auto-generan si están vacías)")]
    [SerializeField] private GameObject hudContainer;
    [SerializeField] private RectTransform dialPointer;
    [SerializeField] private TextMeshProUGUI angleText;
    [SerializeField] private TextMeshProUGUI titleText;

    [Header("Robot Reference")]
    [SerializeField] private Preliy.Flange.Controller controller;

    private void Start()
    {
        // Desactivado según R2
        enabled = false;
        if (hudContainer != null)
            hudContainer.SetActive(false);
    }

    private void Update()
    {
        // Desactivado según R2
        return;
    }

    /// <summary>
    /// Elimina hijos residuales del viejo SetupJ6HUD de Editor (Dial_Background, Angle_Text, etc.)
    /// que ya no se usan porque la UI se construye en runtime.
    /// </summary>
    private void CleanupLegacyChildren()
    {
    }

    /// <summary>
    /// Construye toda la UI del dial de J6 en tiempo de ejecución como un hijo separado.
    /// </summary>
    private void BuildUI()
    {
        // Desactivado según R2
    }

    private void CreateCardinalLabel(Transform parent, float angleDeg, string label, float radius)
    {
    }

    private TextMeshProUGUI CreateTMPText(string name, string text, Transform parent,
        Vector2 anchoredPos, Vector2 sizeDelta,
        int fontSize, Color color, FontStyles style)
    {
        return null;
    }
}
