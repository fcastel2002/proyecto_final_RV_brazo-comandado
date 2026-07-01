using UnityEditor;
using UnityEngine;

/// <summary>
/// Script de Editor para asegurar que exista un J6HUDController en la escena.
/// La UI visual se auto-construye en runtime dentro de J6HUDController.BuildUI().
/// </summary>
public class SetupJ6HUD : EditorWindow
{
    [MenuItem("Tools/Setup J6 HUD")]
    public static void Setup()
    {
        // Buscar si ya existe un J6HUDController en la escena
        J6HUDController existing = Object.FindFirstObjectByType<J6HUDController>();
        if (existing != null)
        {
            Debug.Log("[SetupJ6HUD] Ya existe un J6HUDController en la escena.");
            return;
        }

        // Buscar el Canvas principal
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[SetupJ6HUD] No se encontró ningún Canvas en la escena.");
            return;
        }

        // Crear un GameObject ligero con el componente. La UI se genera sola en Start().
        GameObject j6Hud = new GameObject("J6_HUD_Controller");
        j6Hud.transform.SetParent(canvas.transform, false);
        j6Hud.AddComponent<J6HUDController>();
        Undo.RegisterCreatedObjectUndo(j6Hud, "Crear J6 HUD Controller");

        Debug.Log("[SetupJ6HUD] J6HUDController añadido a la escena.");
    }

    public static void BatchmodeSetup()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Planta.unity");
        Setup();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }
}
