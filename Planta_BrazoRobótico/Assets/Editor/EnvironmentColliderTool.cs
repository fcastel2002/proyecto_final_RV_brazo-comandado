using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Genera los colliders que le faltan al entorno.
///
/// Los assets de RPG_FPS_game_assets_industrial vienen SIN colliders: sus FBX estan importados con
/// "Generate Colliders" desactivado y, como la escena instancia los .prefab derivados y no los FBX,
/// activar esa casilla en el importador tampoco serviria. Por eso los colliders se anaden sobre los
/// objetos de la escena.
///
/// Ademas asigna la layer "Entorno" (6), que estaba definida y sin usar. Asi el sensor de distancia
/// puede apuntar a Manipulable + Entorno en vez de a Default, que arrastra piezas del propio robot.
///
/// Funciona sobre TODAS las escenas cargadas, para que sirva con el multi-scene editing de
/// Planta + Map_v2. Es reversible con Undo (Ctrl+Z).
/// </summary>
public static class EnvironmentColliderTool
{
    private const int EnvironmentLayer = 6;   // "Entorno"
    private const int UiLayer = 5;

    [MenuItem("Tools/Entorno/Generar colliders faltantes")]
    public static void GenerateMissingColliders()
    {
        List<Transform> skipRoots = CollectRootsToSkip();
        int added = 0;
        int relayered = 0;
        int skipped = 0;

        foreach (GameObject root in EnumerateLoadedRootObjects())
        {
            foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                GameObject target = meshFilter.gameObject;

                if (ShouldSkip(target, skipRoots))
                {
                    skipped++;
                    continue;
                }

                if (target.GetComponent<Collider>() == null && meshFilter.sharedMesh != null)
                {
                    MeshCollider collider = Undo.AddComponent<MeshCollider>(target);
                    collider.sharedMesh = meshFilter.sharedMesh;
                    // Concavo: valido para geometria estatica, que es lo que es el entorno.
                    collider.convex = false;
                    added++;
                }

                if (target.layer != EnvironmentLayer)
                {
                    Undo.RecordObject(target, "Asignar layer Entorno");
                    target.layer = EnvironmentLayer;
                    relayered++;
                }
            }
        }

        MarkLoadedScenesDirty();

        Debug.Log($"[EnvironmentColliderTool] {added} MeshCollider anadidos, {relayered} objetos movidos a la layer 'Entorno', {skipped} omitidos (robot, gripper, piezas agarrables o UI). Reversible con Ctrl+Z.");
    }

    /// <summary>
    /// Responde por que el brazo atraviesa o no atraviesa el entorno, con numeros en vez de suposiciones.
    ///
    /// Los tres fallos que se han dado en la practica y que este informe detecta de un vistazo:
    /// ejecutar la herramienta con Map_v2 sin cargar (el entorno vive ahi, no en Planta), que la
    /// mascara del veto no cubra la layer Entorno, y que la del sensor tampoco, con lo que el bloqueo
    /// de descenso no ve las superficies elevadas.
    /// </summary>
    [MenuItem("Tools/Entorno/Diagnosticar anticolision")]
    public static void DiagnoseCollisionSetup()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("[EnvironmentColliderTool] Diagnostico de anticolision");

        report.AppendLine($"\nEscenas cargadas ({SceneManager.sceneCount}):");
        List<Transform> skipRoots = CollectRootsToSkip();
        int totalEnvironmentColliders = 0;

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
            {
                report.AppendLine($"  - {scene.name}: NO CARGADA");
                continue;
            }

            int meshes = 0;
            int withCollider = 0;
            int inEnvironmentLayer = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    GameObject target = meshFilter.gameObject;
                    if (ShouldSkip(target, skipRoots)) continue;

                    meshes++;
                    if (target.GetComponent<Collider>() != null) withCollider++;
                    if (target.layer == EnvironmentLayer) inEnvironmentLayer++;
                }
            }

            totalEnvironmentColliders += inEnvironmentLayer;
            report.AppendLine(
                $"  - {scene.name}: {meshes} mallas candidatas, {withCollider} con collider, " +
                $"{inEnvironmentLayer} en layer 'Entorno'.");
        }

        if (totalEnvironmentColliders == 0)
        {
            report.AppendLine(
                "  >> NADA en la layer 'Entorno'. Si la escena del entorno (Map_v2) no aparece arriba " +
                "como cargada, ese es el motivo: hay que cargarla ANTES de generar los colliders.");
        }

        JoystickAdapter adapter = Object.FindFirstObjectByType<JoystickAdapter>(FindObjectsInactive.Include);
        report.AppendLine("\nVeto de colision (JoystickAdapter):");
        if (adapter == null)
        {
            report.AppendLine("  - No se encontro ningun JoystickAdapter en escena.");
        }
        else
        {
            var serialized = new SerializedObject(adapter);
            int mask = serialized.FindProperty("_obstacleMask").intValue;
            bool coversEnvironment = (mask & (1 << EnvironmentLayer)) != 0;

            report.AppendLine($"  - _enableCollisionVeto: {serialized.FindProperty("_enableCollisionVeto").boolValue}");
            report.AppendLine($"  - _obstacleMask: {mask} -> cubre layer 'Entorno': {(coversEnvironment ? "SI" : "NO")}");
            report.AppendLine($"  - _endEffector: {(serialized.FindProperty("_endEffector").objectReferenceValue != null ? "asignado" : "SIN ASIGNAR (el veto no actua)")}");
            report.AppendLine($"  - _enableHardFloor: {serialized.FindProperty("_enableHardFloor").boolValue}, cota Y = {serialized.FindProperty("_hardFloorWorldY").floatValue}");
        }

        report.AppendLine("\nSensor que gobierna el bloqueo de descenso:");
        GripperDistanceSensor speedSensor = null;
        foreach (var sensor in Object.FindObjectsByType<GripperDistanceSensor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (new SerializedObject(sensor).FindProperty("contributesToSpeedReduction").boolValue)
            {
                speedSensor = sensor;
                break;
            }
        }

        if (speedSensor == null)
        {
            report.AppendLine("  - Ningun sensor tiene 'contributesToSpeedReduction'. El bloqueo de descenso no actua.");
        }
        else
        {
            var serialized = new SerializedObject(speedSensor);
            int mask = serialized.FindProperty("detectionMask").intValue;
            report.AppendLine($"  - {speedSensor.name}: detectionMask {mask} -> cubre 'Entorno': {((mask & (1 << EnvironmentLayer)) != 0 ? "SI" : "NO")}, maxDistance {serialized.FindProperty("maxDistance").floatValue} m");
        }

        report.AppendLine(
            "\nLimites por diseno (no son fallos): el veto solo vigila el GRIPPER y la pieza que lleve, " +
            "no los eslabones del brazo (codo, antebrazo), que no tienen colliders. Y descarta las " +
            "superficies horizontales: mesas y palés los gobierna el bloqueo de descenso via sensor, " +
            "no el veto.");

        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/Entorno/Quitar colliders generados")]
    public static void RemoveGeneratedColliders()
    {
        int removed = 0;

        foreach (GameObject root in EnumerateLoadedRootObjects())
        {
            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.gameObject.layer != EnvironmentLayer) continue;

                Undo.DestroyObjectImmediate(collider);
                removed++;
            }
        }

        MarkLoadedScenesDirty();

        Debug.Log($"[EnvironmentColliderTool] {removed} MeshCollider eliminados de la layer 'Entorno'.");
    }

    /// <summary>
    /// Jerarquias que no deben tocarse: el robot y el gripper (sus colliders propios se gestionan
    /// aparte y moverlos de layer romperia el filtrado del sensor).
    /// </summary>
    private static List<Transform> CollectRootsToSkip()
    {
        var roots = new List<Transform>();

        foreach (var adapter in Object.FindObjectsByType<JoystickAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            AddRoot(roots, adapter.transform);

        foreach (var gripper in Object.FindObjectsByType<GripperController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            AddRoot(roots, gripper.transform);

        foreach (var sensor in Object.FindObjectsByType<GripperDistanceSensor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            AddRoot(roots, sensor.transform);

        return roots;
    }

    private static void AddRoot(List<Transform> roots, Transform candidate)
    {
        Transform root = candidate.root;
        if (!roots.Contains(root))
            roots.Add(root);
    }

    private static bool ShouldSkip(GameObject target, List<Transform> skipRoots)
    {
        if (target.layer == UiLayer) return true;

        // Las piezas agarrables ya tienen su BoxCollider y viven en la layer Manipulable.
        if (target.CompareTag("Agarrable")) return true;
        if (target.GetComponentInParent<Rigidbody>() != null) return true;

        for (int i = 0; i < skipRoots.Count; i++)
        {
            if (target.transform.IsChildOf(skipRoots[i])) return true;
        }

        return false;
    }

    private static IEnumerable<GameObject> EnumerateLoadedRootObjects()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                yield return root;
        }
    }

    private static void MarkLoadedScenesDirty()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
