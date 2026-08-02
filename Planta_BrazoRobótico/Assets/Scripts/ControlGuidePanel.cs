using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ControlGuidePanel : MonoBehaviour
{
    [Header("Título")]
    [SerializeField] private TextMeshProUGUI _title;

    [Header("Ítems por defecto (fijos en el Editor)")]
    [SerializeField] private TextMeshProUGUI[] _defaultItems = new TextMeshProUGUI[7];

    [Header("Plantilla para ítems adicionales")]
    [Tooltip("Ítem TMP inactivo usado como plantilla al agregar por código un comando nuevo que no exista como ítem fijo.")]
    [SerializeField] private TextMeshProUGUI _itemTemplate;

    private readonly Dictionary<string, TextMeshProUGUI> _extraItems = new Dictionary<string, TextMeshProUGUI>();

    private static readonly string[] Ps4Items =
    {
        "• <b>Mover TCP (X/Z):</b> Stick Izquierdo",
        "• <b>Mover TCP (Y):</b> Stick Derecho",
        "• <b>Rotar J6:</b> L1 (Anti-hor) / R1 (Horario)",
        "• <b>Homing J6:</b> Botón Cuadrado",
        "• <b>Abrir/Cerrar Garra:</b> R2 / L2",
        "• <b>Hold Garra (1s):</b> Homing J6",
        "• <b>Cámara/Modo:</b> L3 (Doble click: Modo J6)"
    };

    private static readonly string[] Vr2Items =
    {
        "• <b>Mover TCP (X/Z):</b> Palanca Principal",
        "• <b>Mover TCP (Y):</b> Palanca Altura",
        "• <b>Abrir/Cerrar Garra:</b> Trigger Gripper",
        "• <b>Hold Gripper (1s):</b> Homing J6",
        "• <b>Cámara/Modo:</b> Botón Menú / Toggle"
    };

    public void SetProfile(InputProfileSwitcher.InputProfileKind profile)
    {
        string profileName = profile == InputProfileSwitcher.InputProfileKind.PS4 ? "PS4" : "VR-2";
        if (_title != null)
            _title.text = $"GUÍA DE CONTROLES ({profileName})";

        string[] items = profile == InputProfileSwitcher.InputProfileKind.PS4 ? Ps4Items : Vr2Items;
        ApplyDefaultItems(items);
    }

    public void AddOrUpdateItem(string key, string richText)
    {
        TextMeshProUGUI item = GetOrCreateExtraItem(key);
        if (item != null)
            item.text = richText;
    }

    private void ApplyDefaultItems(string[] items)
    {
        if (_defaultItems == null) return;

        for (int i = 0; i < _defaultItems.Length; i++)
        {
            if (_defaultItems[i] == null) continue;

            bool hasItem = i < items.Length;
            _defaultItems[i].gameObject.SetActive(hasItem);
            if (hasItem)
                _defaultItems[i].text = items[i];
        }
    }

    private TextMeshProUGUI GetOrCreateExtraItem(string key)
    {
        if (_extraItems.TryGetValue(key, out var existing) && existing != null)
            return existing;

        if (_itemTemplate == null) return null;

        TextMeshProUGUI clone = Instantiate(_itemTemplate, _itemTemplate.transform.parent);
        clone.name = $"Guide_Item_{key}";
        clone.gameObject.SetActive(true);
        _extraItems[key] = clone;
        return clone;
    }
}
