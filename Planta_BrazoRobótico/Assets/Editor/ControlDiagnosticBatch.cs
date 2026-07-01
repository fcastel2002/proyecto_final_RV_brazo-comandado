using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ControlDiagnosticBatch
{
    private const string ScenePath = "Assets/Scenes/Planta.unity";
    private const string RunRequestedKey = "ControlDiagnosticBatch.RunRequested";
    private const string WaitingForExitKey = "ControlDiagnosticBatch.WaitingForExit";
    private const string ExitCodeKey = "ControlDiagnosticBatch.ExitCode";
    private const string RunModeKey = "ControlDiagnosticBatch.RunMode";

    static ControlDiagnosticBatch()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Control/Run Vertical Sweep Diagnostic")]
    public static void RunVerticalSweepMenu()
    {
        RunVerticalSweep();
    }

    public static void RunVerticalSweep()
    {
        StartRun("single");
    }

    [MenuItem("Tools/Control/Run Vertical Sweep Matrix Diagnostic")]
    public static void RunVerticalSweepMatrixMenu()
    {
        RunVerticalSweepMatrix();
    }

    public static void RunVerticalSweepMatrix()
    {
        StartRun("matrix");
    }

    [MenuItem("Tools/Control/Run Full Sweep (All Axes) Diagnostic")]
    public static void RunFullSweepMenu()
    {
        RunFullSweep();
    }

    public static void RunFullSweep()
    {
        StartRun("full");
    }

    [MenuItem("Tools/Control/Run J6 Control Diagnostic")]
    public static void RunJ6DiagnosticMenu()
    {
        RunJ6Diagnostic();
    }

    public static void RunJ6Diagnostic()
    {
        StartRun("j6");
    }

    private static void StartRun(string mode)
    {
        SessionState.SetBool(RunRequestedKey, false);
        SessionState.SetBool(WaitingForExitKey, false);
        SessionState.SetInt(ExitCodeKey, 1);
        SessionState.SetString(RunModeKey, mode);

        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[ControlDiagnosticBatch] Cerrar Play Mode antes de lanzar el diagnostico.");
            return;
        }

        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetBool(RunRequestedKey, true);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            if (!SessionState.GetBool(RunRequestedKey, false))
                return;

            SessionState.SetBool(RunRequestedKey, false);

            var runnerObject = new GameObject("Control Diagnostic Runner");
            Object.DontDestroyOnLoad(runnerObject);
            var runner = runnerObject.AddComponent<ControlDiagnosticRunner>();
            runner.Completed += OnRunnerCompleted;
            string mode = SessionState.GetString(RunModeKey, "single");
            if (mode == "matrix")
                runner.StartCoroutine(runner.RunVerticalSweepMatrix());
            else if (mode == "full")
                runner.StartCoroutine(runner.RunFullSweepMatrix());
            else if (mode == "j6")
                runner.StartCoroutine(runner.RunJ6Diagnostic());
            else
                runner.StartCoroutine(runner.RunVerticalSweep());
            return;
        }

        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(WaitingForExitKey, false))
        {
            int exitCode = SessionState.GetInt(ExitCodeKey, 1);
            SessionState.SetBool(WaitingForExitKey, false);

            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }

    private static void OnRunnerCompleted(int exitCode, string message)
    {
        SessionState.SetInt(ExitCodeKey, exitCode);
        SessionState.SetBool(WaitingForExitKey, true);

        if (exitCode == 0)
            Debug.Log($"[ControlDiagnosticBatch] Diagnostico finalizado: {message}");
        else
            Debug.LogError($"[ControlDiagnosticBatch] Diagnostico fallo: {message}");

        EditorApplication.ExitPlaymode();
    }
}
