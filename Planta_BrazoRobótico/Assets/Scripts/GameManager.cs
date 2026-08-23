using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    private const string EnvironmentScenePath = "Assets/RPG_FPS_game_assets_industrial/Map_v2.unity";

    [Header("Performance")]
    [SerializeField] private int targetFPS = 60;

    void Awake()
    {
        Application.targetFrameRate = targetFPS;
        QualitySettings.vSyncCount = 0;
    }

    void Start()
    {
        if (SceneManager.GetSceneByPath(EnvironmentScenePath).isLoaded)
        {
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(EnvironmentScenePath))
        {
            Debug.LogError(
                $"[GameManager] No se puede cargar el entorno '{EnvironmentScenePath}'. " +
                "Verifica que Map_v2 este habilitada en Build Profiles.", this);
            return;
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(EnvironmentScenePath, LoadSceneMode.Additive);
        if (loadOperation == null)
        {
            Debug.LogError($"[GameManager] No se pudo iniciar la carga de '{EnvironmentScenePath}'.", this);
            return;
        }

        loadOperation.completed += _ =>
        {
            if (!SceneManager.GetSceneByPath(EnvironmentScenePath).isLoaded)
            {
                Debug.LogError($"[GameManager] La carga aditiva de '{EnvironmentScenePath}' no se completo.", this);
            }
        };
    }
}
