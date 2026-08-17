using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera <c>SM_RackEternity</c>, una bandeja hueca de 1x1x1 (extents +-0.5,
/// mismo pivote que un Cube estandar) con UV por cara hacia el atlas del
/// rack metalico negro "Eternity" que se monta en el autoelevador.
///
/// A diferencia de <see cref="CajaBateriaMeshTool"/> y
/// <see cref="CajaMaderaMeshTool"/>, que generan un cubo cerrado, el rack
/// necesita hueco interior para que el brazo robotico pueda colocar las
/// celdas de bateria (ver Assets/Props/CajaBateria) dentro. El mesh se
/// construye como una "bandeja": piso + paredes exteriores + un borde
/// superior (marco) + paredes interiores + piso interior, todo con normales
/// hacia afuera de la chapa que corresponda en cada caso.
///
/// Parametros de la bandeja (en unidades del cubo unitario, ver constantes
/// mas abajo): <c>Espesor</c> es el grosor de las paredes/borde, y
/// <c>AlturaPiso</c> es cuanto sube el piso interior desde la base. Si
/// cambiás estos valores, el hueco interior cambia de tamaño.
///
/// La distribucion del atlas esta replicada en
/// Tools/generar_textura_rack.py; si movés una celda alla, movela aca
/// tambien.
/// </summary>
public static class RackEternityMeshTool
{
    private const string CarpetaRack = "Assets/Props/RackEternity";
    private const string RutaMesh = CarpetaRack + "/SM_RackEternity.asset";
    private const string RutaMatAtlas = CarpetaRack + "/Mat_RackEternity_Atlas.mat";

    // Grosor de paredes/borde y altura del piso interior, en unidades del cubo unitario.
    private const float Espesor = 0.045f;
    private const float AlturaPiso = 0.12f;

    // Margen dentro de cada celda para que el mipmapping no mezcle celdas vecinas.
    private const float Padding = 6f / 2048f;

    // Celdas del atlas en UV (origen abajo-izquierda).
    private static readonly Rect CeldaTapa = new Rect(0f, 0f, 0.5f, 0.5f);
    private static readonly Rect CeldaFondo = new Rect(0.5f, 0f, 0.5f, 0.5f);
    private static readonly Rect CeldaFrente = new Rect(0f, 0.5f, 0.5f, 0.5f);
    private static readonly Rect CeldaLateral = new Rect(0.5f, 0.5f, 0.5f, 0.5f);

    [MenuItem("Herramientas/Rack Eternity/Generar mesh y aplicar a los racks", false, 0)]
    public static void GenerarYAplicar()
    {
        Mesh mesh = GenerarMesh();
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RutaMatAtlas);
        if (material == null)
        {
            Debug.LogError($"[RackEternity] No encontré el material {RutaMatAtlas}.");
            return;
        }

        int aplicadas = Aplicar(BuscarRacksEnEscena(material), mesh, material);
        if (aplicadas == 0)
        {
            EditorUtility.DisplayDialog(
                "Rack Eternity",
                "El mesh se generó, pero no encontré racks usando el material del " +
                "rack en las escenas abiertas.\n\nSeleccioná los racks en la " +
                "jerarquía y usá 'Aplicar a la selección'.",
                "Entendido");
        }
        else
        {
            Debug.Log($"[RackEternity] Mesh y material aplicados a {aplicadas} rack(s). " +
                      "Acordate de guardar la escena.");
        }
    }

    [MenuItem("Herramientas/Rack Eternity/Aplicar a la selección", false, 1)]
    public static void AplicarASeleccion()
    {
        // Siempre se regenera (no se reutiliza el asset tal cual esta en disco):
        // de lo contrario, un cambio en el codigo de GenerarMesh no se refleja
        // hasta la proxima vez que se use "Generar mesh y aplicar a los racks".
        Mesh mesh = GenerarMesh();
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RutaMatAtlas);
        if (material == null)
        {
            Debug.LogError($"[RackEternity] No encontré el material {RutaMatAtlas}.");
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
        Debug.Log($"[RackEternity] Mesh y material aplicados a {aplicadas} objeto(s) " +
                  "de la selección.");
    }

    [MenuItem("Herramientas/Rack Eternity/Aplicar a la selección", true)]
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

        mesh.name = "SM_RackEternity";

        var vertices = new List<Vector3>(56);
        var normales = new List<Vector3>(56);
        var uvs = new List<Vector2>(56);
        var triangulos = new List<int>(84);

        float interior = 0.5f - Espesor;                 // semi-extent del hueco en X/Z
        float pisoY = -0.5f + AlturaPiso;                 // altura del piso interior
        float centroParedY = (pisoY + 0.5f) * 0.5f;       // centro Y de las paredes interiores
        float semiAlturaPared = (0.5f - pisoY) * 0.5f;    // semi-altura de las paredes interiores

        void Cara(Vector3 normal, Vector3 derecha, Vector3 arriba, Vector3 centro,
                  float semiAncho, float semiAlto, Rect celda)
        {
            AgregarCaraRect(vertices, normales, uvs, triangulos,
                            normal, derecha, arriba, centro, semiAncho, semiAlto, celda);
        }

        // --- Chapa exterior: piso + 4 paredes (igual a un cubo cerrado, sin tapa) ---
        Cara(Vector3.down, Vector3.left, Vector3.forward, Vector3.down * 0.5f, 0.5f, 0.5f, CeldaFondo);
        Cara(Vector3.forward, Vector3.left, Vector3.up, Vector3.forward * 0.5f, 0.5f, 0.5f, CeldaFrente);
        Cara(Vector3.back, Vector3.right, Vector3.up, Vector3.back * 0.5f, 0.5f, 0.5f, CeldaFrente);
        Cara(Vector3.right, Vector3.forward, Vector3.up, Vector3.right * 0.5f, 0.5f, 0.5f, CeldaLateral);
        Cara(Vector3.left, Vector3.back, Vector3.up, Vector3.left * 0.5f, 0.5f, 0.5f, CeldaLateral);

        // --- Borde superior (marco entre el hueco y el canto exterior) ---
        float centroBorde = (interior + 0.5f) * 0.5f;
        float semiBorde = (0.5f - interior) * 0.5f; // == Espesor / 2
        Cara(Vector3.up, Vector3.right, Vector3.forward, new Vector3(0f, 0.5f, centroBorde), 0.5f, semiBorde, CeldaFondo);
        Cara(Vector3.up, Vector3.right, Vector3.forward, new Vector3(0f, 0.5f, -centroBorde), 0.5f, semiBorde, CeldaFondo);
        Cara(Vector3.up, Vector3.right, Vector3.forward, new Vector3(centroBorde, 0.5f, 0f), semiBorde, interior, CeldaFondo);
        Cara(Vector3.up, Vector3.right, Vector3.forward, new Vector3(-centroBorde, 0.5f, 0f), semiBorde, interior, CeldaFondo);

        // --- Paredes interiores del hueco, normal hacia el centro de la bandeja ---
        Cara(Vector3.back, Vector3.right, Vector3.up, new Vector3(0f, centroParedY, interior), interior, semiAlturaPared, CeldaLateral);
        Cara(Vector3.forward, Vector3.left, Vector3.up, new Vector3(0f, centroParedY, -interior), interior, semiAlturaPared, CeldaLateral);
        Cara(Vector3.left, Vector3.back, Vector3.up, new Vector3(interior, centroParedY, 0f), interior, semiAlturaPared, CeldaLateral);
        Cara(Vector3.right, Vector3.forward, Vector3.up, new Vector3(-interior, centroParedY, 0f), interior, semiAlturaPared, CeldaLateral);

        // --- Piso interior: donde se apoyan las celdas, con las marcas guia ---
        Cara(Vector3.up, Vector3.right, Vector3.forward, new Vector3(0f, pisoY, 0f), interior, interior, CeldaTapa);

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

    /// <summary>
    /// Agrega un rectangulo generico: <paramref name="centro"/> +- (derecha*semiAncho)
    /// +- (arriba*semiAlto). Requiere Cross(arriba, derecha) == normal para que el
    /// winding de los triangulos quede orientado hacia afuera (misma convencion que
    /// CajaBateriaMeshTool.AgregarCara, generalizada con centro/semiAncho/semiAlto
    /// explicitos para poder describir caras que no pasan por el centro del cubo).
    /// </summary>
    private static void AgregarCaraRect(List<Vector3> vertices, List<Vector3> normales,
                                        List<Vector2> uvs, List<int> triangulos,
                                        Vector3 normal, Vector3 derecha, Vector3 arriba,
                                        Vector3 centro, float semiAncho, float semiAlto,
                                        Rect celda)
    {
        int baseIndice = vertices.Count;
        Vector3 dx = derecha * semiAncho;
        Vector3 dy = arriba * semiAlto;

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
    private static List<GameObject> BuscarRacksEnEscena(Material materialRack)
    {
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
                if (m == materialRack)
                {
                    encontradas.Add(renderer.gameObject);
                    break;
                }
            }
        }

        return encontradas;
    }

    /// <summary>
    /// Reemplaza cualquier BoxCollider/MeshCollider previo (tipicamente el BoxCollider
    /// solido de un Cube nativo) por 5 BoxCollider que replican el piso y las 4 paredes
    /// de la bandeja, dejando el hueco central libre para que las celdas de bateria
    /// puedan caer y apoyarse adentro en vez de golpear contra un cubo macizo.
    /// </summary>
    private static void ConfigurarColliders(GameObject go)
    {
        foreach (BoxCollider viejo in go.GetComponents<BoxCollider>())
        {
            Undo.DestroyObjectImmediate(viejo);
        }
        foreach (MeshCollider viejo in go.GetComponents<MeshCollider>())
        {
            Undo.DestroyObjectImmediate(viejo);
        }

        float interior = 0.5f - Espesor;
        float pisoY = -0.5f + AlturaPiso;
        float centroBorde = (interior + 0.5f) * 0.5f;

        void Pared(Vector3 centro, Vector3 tamano)
        {
            var box = Undo.AddComponent<BoxCollider>(go);
            box.center = centro;
            box.size = tamano;
        }

        Pared(new Vector3(0f, (-0.5f + pisoY) * 0.5f, 0f), new Vector3(1f, AlturaPiso, 1f)); // piso
        Pared(new Vector3(0f, 0f, centroBorde), new Vector3(1f, 1f, Espesor));               // pared frente
        Pared(new Vector3(0f, 0f, -centroBorde), new Vector3(1f, 1f, Espesor));              // pared fondo
        Pared(new Vector3(centroBorde, 0f, 0f), new Vector3(Espesor, 1f, 1f));               // pared derecha
        Pared(new Vector3(-centroBorde, 0f, 0f), new Vector3(Espesor, 1f, 1f));              // pared izquierda
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

            Undo.RecordObject(filtro, "Aplicar mesh de rack");
            Undo.RecordObject(renderer, "Aplicar material de rack");

            filtro.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { material };

            ConfigurarColliders(go);

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
