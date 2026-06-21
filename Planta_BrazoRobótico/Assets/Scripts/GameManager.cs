using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Performance")]
    [SerializeField] private int targetFPS = 60;

    void Awake()
    {
        Application.targetFrameRate = targetFPS;
        QualitySettings.vSyncCount = 0;
    }
}