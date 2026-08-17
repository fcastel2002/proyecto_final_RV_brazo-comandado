using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera <c>SM_CajaBateria</c>, un cubo de 1x1x1 con UV por cara hacia el atlas
/// de la caja de baterias Eternity, y lo aplica a las cajas de la escena.
///
/// El Cube nativo de Unity manda las 6 caras al mismo cuadrado 0..1, asi que no
/// permite una tapa distinta de los laterales. Este mesh mantiene exactamente la
/// misma geometria (extents +-0.5, mismo pivote) para no invalidar los
/// BoxCollider ni las escalas ya ajustadas en la escena: lo unico que cambia son
/// las UV.
///
/// La distribucion de celdas del atlas esta replicada en
/// Tools/generar_textura_bateria.py; si movés una celda alla, movela aca tambien.
/// </summary>
public static class CajaBateriaMeshTool
{
    private const string CarpetaCaja = "Assets/Props/CajaBateria";
    private const string RutaMesh = CarpetaCaja + "/SM_CajaBateria.asset";
    private const string RutaMatAtlas = CarpetaCaja + "/Mat_CajaBateria_Eternity_Atlas.mat";
    private const string RutaMatPanel = CarpetaCaja + "/Mat_CajaBateria_Eternity.mat";

    // Margen dentro de cada celda para que el mipmapping no mezcle celdas vecinas.
    private const float Padding = 6f / 2048f;

    // Celdas del atlas en UV (origen abajo-izquierda).
    private static readonly Rect CeldaTapa = new Rect(0f, 0f, 0.5f, 0.5f);
    private static readonly Rect CeldaFondo = new Rect(0.5f, 0f, 0.5f, 0.5f);
    private static readonly Rect CeldaLadoLogo = new Rect(0f, 0.5f, 0.5f, 0.5f);
    private static readonly Rect CeldaLadoRejilla = new Rect(0.5f, 0.5f, 0.5f, 0.5f);

    [MenuItem("Herramientas/Caja Batería/Generar mesh y aplicar a las cajas", false, 0)]
    public static void GenerarYAplicar()
    {
        Mesh mesh = GenerarMesh();
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RutaMatAtlas);
        if (material == null)
        {
            Debug.LogError($"[CajaBateria] No encontré el material {RutaMatAtlas}.");
            return;
        }

        int aplicadas = Aplicar(BuscarCajasEnEscena(), mesh, material);
        if (aplicadas == 0)
        {
            EditorUtility.DisplayDialog(
                "Caja Batería",
                "El mesh se generó, pero no encontré cajas usando los materiales de " +
                "batería en las escenas abiertas.\n\nSeleccioná las cajas en la " +
                "jerarquía y usá 'Aplicar a la selección'.",
                "Entendido");
        }
        else
        {
            Debug.Log($"[CajaBateria] Mesh y material aplicados a {aplicadas} caja(s). " +
                      "Acordate de guardar la escena.");
        }
    }

    [MenuItem("Herramientas/Caja Batería/Aplicar a la selección", false, 1)]
    public static void AplicarASeleccion()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RutaMesh) ?? GenerarMesh();
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RutaMatAtlas);
        if (material == null)
        {
            Debug.LogError($"[CajaBateria] No encontré el material {RutaMatAtlas}.");
            return;
        }

        var objetivos = new List<GameObject>();
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go.GetComponent<MeshFilter>() != null && go.GetComponent<MeshRenderer>() != null)
            {
                objetivos.Add(go);
            }
        }

        int aplicadas = Aplicar(objetivos, mesh, material);
        Debug.Log($"[CajaBateria] Mesh y material aplicados a {aplicadas} objeto(s) " +
                  "de la selección.");
    }

    [MenuItem("Herramientas/Caja Batería/Aplicar a la selección", true)]
    private static bool ValidarSeleccion() => Selection.gameObjects.Length > 0;

    // ----------------------------------------------------------------------
    // Generacion del mesh
    // ----------------------------------------------------------------------
    private static Mesh GenerarMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RutaMesh);
        bool esNuevo = mesh == null;
        if (esNuevo)
        {
            mesh = new Mesh();
        }
        else
        {
            // Se reutiliza el asset para no romper las referencias ya existentes.
            mesh.Clear();
        }

        mesh.name = "SM_CajaBateria";

        var vertices = new List<Vector3>(24);
        var normales = new List<Vector3>(24);
        var uvs = new List<Vector2>(24);
        var triangulos = new List<int>(36);

        // Para cada cara: derecha y arriba cumplen Cross(arriba, derecha) == normal,
        // que es la convencion de winding de Unity.
        AgregarCara(vertices, normales, uvs, triangulos, Vector3.up, Vector3.right, Vector3.forward, CeldaTapa);
        AgregarCara(vertices, normales, uvs, triangulos, Vector3.down, Vector3.left, Vector3.forward, CeldaFondo);
        AgregarCara(vertices, normales, uvs, triangulos, Vector3.forward, Vector3.left, Vector3.up, CeldaLadoLogo);
        AgregarCara(vertices, normales, uvs, triangulos, Vector3.back, Vector3.right, Vector3.up, CeldaLadoLogo);
        AgregarCara(vertices, normales, uvs, triangulos, Vector3.right, Vector3.forward, Vector3.up, CeldaLadoRejilla);
        AgregarCara(vertices, normales, uvs, triangulos, Vector3.left, Vector3.back, Vector3.up, CeldaLadoRejilla);

        mesh.SetVertices(vertices);
        mesh.SetNormals(normales);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangulos, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        if (esNuevo)
        {
            AssetDatabase.CreateAsset(mesh, RutaMesh);
        }
        else
        {
            EditorUtility.SetDirty(mesh);
        }

        AssetDatabase.SaveAssets();
        return mesh;
    }

    private static void AgregarCara(List<Vector3> vertices, List<Vector3> normales,
                                    List<Vector2> uvs, List<int> triangulos,
                                    Vector3 normal, Vector3 derecha, Vector3 arriba,
                                    Rect celda)
    {
        int baseIndice = vertices.Count;
        Vector3 centro = normal * 0.5f;
        Vector3 dx = derecha * 0.5f;
        Vector3 dy = arriba * 0.5f;

        vertices.Add(centro - dx - dy);
        vertices.Add(centro - dx + dy);
        vertices.Add(centro + dx + dy);
        vertices.Add(centro + dx - dy);

        for (int i = 0; i < 4; i++)
        {
            normales.Add(normal);
        }

        float u0 = celda.xMin + Padding;
        float u1 = celda.xMax - Padding;
        float v0 = celda.yMin + Padding;
        float v1 = celda.yMax - Padding;
        uvs.Add(new Vector2(u0, v0));
        uvs.Add(new Vector2(u0, v1));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(u1, v0));

        triangulos.Add(baseIndice + 0);
        triangulos.Add(baseIndice + 1);
        triangulos.Add(baseIndice + 2);
        triangulos.Add(baseIndice + 0);
        triangulos.Add(baseIndice + 2);
        triangulos.Add(baseIndice + 3);
    }

    // ----------------------------------------------------------------------
    // Aplicacion a la escena
    // ----------------------------------------------------------------------
    private static List<GameObject> BuscarCajasEnEscena()
    {
        var materialesCaja = new HashSet<Material>();
        foreach (string ruta in new[] { RutaMatPanel, RutaMatAtlas })
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (m != null)
            {
                materialesCaja.Add(m);
            }
        }

        var encontradas = new List<GameObject>();
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.GetComponent<MeshFilter>() == null)
            {
                continue;
            }

            foreach (Material m in renderer.sharedMaterials)
            {
                if (m != null && materialesCaja.Contains(m))
                {
                    encontradas.Add(renderer.gameObject);
                    break;
                }
            }
        }

        return encontradas;
    }

    private static int Aplicar(List<GameObject> objetivos, Mesh mesh, Material material)
    {
        int contador = 0;
        foreach (GameObject go in objetivos)
        {
            var filtro = go.GetComponent<MeshFilter>();
            var renderer = go.GetComponent<MeshRenderer>();
            if (filtro == null || renderer == null)
            {
                continue;
            }

            Undo.RecordObject(filtro, "Aplicar mesh de caja batería");
            Undo.RecordObject(renderer, "Aplicar material de caja batería");

            filtro.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { material };

            PrefabUtility.RecordPrefabInstancePropertyModifications(filtro);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(go);
            contador++;
        }

        if (contador > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
        }

        return contador;
    }
}
