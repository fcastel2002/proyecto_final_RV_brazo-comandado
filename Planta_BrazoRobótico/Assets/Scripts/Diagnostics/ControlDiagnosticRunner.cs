using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Preliy.Flange;
using UnityEngine;
using UnityEngine.InputSystem;

public class ControlDiagnosticRunner : MonoBehaviour
{
    [Serializable]
    public class RunSummary
    {
        public string variantName;
        public string startedAt;
        public string sceneName;
        public float fixedDeltaTime;
        public float inputMagnitude;
        public int settleTicks;
        public int segmentTicks;
        public float speed;
        public float maxJointVelocity;
        public float ikJointStepLimitMultiplier;
        public float finalTcpWorldError;
        public float finalTcpFrameError;
        public float finalTcpWorldRotationError;
        public float finalTcpFrameRotationError;
        public float finalMaxJointRoundTripError;
        public float maxRestWorldDrift;
        public float netWorldYDisplacement;
        public List<SegmentSummary> segments = new List<SegmentSummary>();
    }

    [Serializable]
    public class MatrixSummary
    {
        public string startedAt;
        public string sceneName;
        public float fixedDeltaTime;
        public float inputMagnitude;
        public int settleTicks;
        public int segmentTicks;
        public List<RunSummary> runs = new List<RunSummary>();
    }

    [Serializable]
    public class SegmentSummary
    {
        public string name;
        public int samples;
        public float maxTargetRotationStep;
        public float averageTargetRotationStep;
        public float maxJointError;
        public float averageJointError;
        public float maxWristError;
        public float averageWristError;
        public float minStepScale;
        public float averageStepScale;
        public float maxPoseError;
        public float maxRotationError;
        public float actualDistanceWorld;
        public float signedDistanceWorld;
    }

    private class VariantSettings
    {
        public string Name;
        public float IkJointStepLimitMultiplier;
        public float MaxJointVelocity;
        public float Speed;
    }

    private struct SegmentAccumulator
    {
        public int Samples;
        public float SumTargetRotationStep;
        public float MaxTargetRotationStep;
        public float SumJointError;
        public float MaxJointError;
        public float SumWristError;
        public float MaxWristError;
        public float SumStepScale;
        public float MinStepScale;
        public float MaxPoseError;
        public float MaxRotationError;

        public void Add(JoystickAdapter.IkDiagnosticSample sample)
        {
            if (Samples == 0)
                MinStepScale = sample.StepScale;

            Samples++;
            SumTargetRotationStep += sample.TargetRotationStep;
            MaxTargetRotationStep = Mathf.Max(MaxTargetRotationStep, sample.TargetRotationStep);
            SumJointError += sample.MaxJointError;
            MaxJointError = Mathf.Max(MaxJointError, sample.MaxJointError);
            SumWristError += sample.WristJointError;
            MaxWristError = Mathf.Max(MaxWristError, sample.WristJointError);
            SumStepScale += sample.StepScale;
            MinStepScale = Mathf.Min(MinStepScale, sample.StepScale);
            MaxPoseError = Mathf.Max(MaxPoseError, sample.PoseError);
            MaxRotationError = Mathf.Max(MaxRotationError, sample.RotationError);
        }

        public SegmentSummary ToSummary(string name)
        {
            return new SegmentSummary
            {
                name = name,
                samples = Samples,
                maxTargetRotationStep = MaxTargetRotationStep,
                averageTargetRotationStep = SafeAverage(SumTargetRotationStep, Samples),
                maxJointError = MaxJointError,
                averageJointError = SafeAverage(SumJointError, Samples),
                maxWristError = MaxWristError,
                averageWristError = SafeAverage(SumWristError, Samples),
                minStepScale = Samples > 0 ? MinStepScale : 0f,
                averageStepScale = SafeAverage(SumStepScale, Samples),
                maxPoseError = MaxPoseError,
                maxRotationError = MaxRotationError
            };
        }
    }

    public event Action<int, string> Completed;

    public IEnumerator RunVerticalSweep(float inputMagnitude = 1f, int settleTicks = 20, int segmentTicks = 120)
    {
        var adapter = FindFirstObjectByType<JoystickAdapter>();
        var controller = FindFirstObjectByType<Controller>();

        if (adapter == null || controller == null || !controller.IsValid.Value)
        {
            string error = "[ControlDiagnosticRunner] No se encontro JoystickAdapter o Controller valido.";
            Debug.LogError(error, this);
            Completed?.Invoke(1, error);
            yield break;
        }

        var initialTarget = CopyJointTarget(controller.MechanicalGroup.JointState);
        var summary = CreateRunSummary(adapter, "scene", inputMagnitude, settleTicks, segmentTicks);

        for (int i = 0; i < settleTicks; i++)
        {
            adapter.SetDiagnosticInputOverride(Vector3.zero);
            yield return new WaitForFixedUpdate();
        }

        var startWorldPose = GetTcpWorldPose(controller);
        var startFramePose = GetTcpFramePose(controller);
        var startJointTarget = CopyJointTarget(controller.MechanicalGroup.JointState);

        yield return RunSweepSegments(adapter, controller, summary, inputMagnitude, settleTicks, segmentTicks);

        FillRoundTripMetrics(summary, controller, startWorldPose, startFramePose, startJointTarget);

        adapter.ClearDiagnosticInputOverride();
        controller.MechanicalGroup.SetJoints(initialTarget, notify: true);

        string outputPath = WriteSummary(summary);
        Debug.Log(BuildConsoleSummary(summary, outputPath), this);
        Completed?.Invoke(0, outputPath);
    }

    public IEnumerator RunJ6Diagnostic()
    {
        var adapter = FindFirstObjectByType<JoystickAdapter>();
        var controller = FindFirstObjectByType<Controller>();
        var gripper = FindFirstObjectByType<Ctrl_OnRobotRG2_Custom>();

        if (adapter == null || controller == null || !controller.IsValid.Value || gripper == null)
        {
            string error = "[ControlDiagnosticRunner] No se encontró JoystickAdapter, Controller o Gripper válido.";
            Debug.LogError(error, this);
            Completed?.Invoke(1, error);
            yield break;
        }

        Debug.Log("[ControlDiagnosticRunner] Iniciando diagnóstico J6...");

        // 1. Inicializar J6 a 0°
        float[] initialJoints = new float[6];
        for (int i = 0; i < 6; i++)
            initialJoints[i] = controller.MechanicalGroup.JointState[i];
        initialJoints[5] = 0f;
        controller.MechanicalGroup.SetJoints(new JointTarget(initialJoints), notify: true);

        // Esperar unos frames para que se estabilice
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        float j6Angle = controller.MechanicalGroup.JointState[5];
        if (Mathf.Abs(j6Angle) > 0.1f)
        {
            string error = $"[ControlDiagnosticRunner] Error al inicializar J6 a 0°. Ángulo actual: {j6Angle:F2}°";
            Debug.LogError(error);
            Completed?.Invoke(1, error);
            yield break;
        }
        Debug.Log("[ControlDiagnosticRunner] J6 inicializado a 0° correctamente.");

        // 2. Activar modo J6 exclusivo
        JoystickAdapter.SetJ6ExclusiveMode(true);
        if (!JoystickAdapter.IsJ6ExclusiveMode)
        {
            string error = "[ControlDiagnosticRunner] No se pudo activar el modo J6 exclusivo.";
            Debug.LogError(error);
            Completed?.Invoke(1, error);
            yield break;
        }

        // Inyectar entrada de stick para J6
        adapter.SetDiagnosticInputOverride(new Vector3(0f, 0f, -1f));

        float maxObservedVelocity = 0f;
        float prevAngle = controller.MechanicalGroup.JointState[5];

        // Monitorear durante 30 fixed updates (0.6 segundos a 50Hz)
        for (int i = 0; i < 30; i++)
        {
            yield return new WaitForFixedUpdate();
            float currAngle = controller.MechanicalGroup.JointState[5];
            float velocity = Mathf.Abs(Mathf.DeltaAngle(prevAngle, currAngle)) / Time.fixedDeltaTime;
            maxObservedVelocity = Mathf.Max(maxObservedVelocity, velocity);
            prevAngle = currAngle;
        }

        Debug.Log($"[ControlDiagnosticRunner] Max J6 velocity observed: {maxObservedVelocity:F2}°/s");

        // Assert that velocity does not exceed 90°/s (allowing a tiny margin for discrete stepping)
        if (maxObservedVelocity > 95f)
        {
            string error = $"[ControlDiagnosticRunner] Velocidad de J6 excedió el límite de 90°/s: {maxObservedVelocity:F2}°/s";
            Debug.LogError(error);
            Completed?.Invoke(1, error);
            yield break;
        }

        // 3. Set J6 to 45°
        adapter.ClearDiagnosticInputOverride();
        float[] joints45 = new float[6];
        for (int i = 0; i < 6; i++) joints45[i] = controller.MechanicalGroup.JointState[i];
        joints45[5] = 45f;
        controller.MechanicalGroup.SetJoints(new JointTarget(joints45), notify: true);

        // Sync adapter's internal target angles via reflection
        var targetAngleField = typeof(JoystickAdapter).GetField("_j6TargetAngle", BindingFlags.Instance | BindingFlags.NonPublic);
        var prevIkTargetField = typeof(JoystickAdapter).GetField("_prevIkTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        if (targetAngleField != null) targetAngleField.SetValue(adapter, 45f);
        float[] prevIk = (float[])prevIkTargetField?.GetValue(adapter);
        if (prevIk != null) prevIk[5] = 45f;

        // Esperar unos frames para estabilizarse en 45°
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        float currentJ6Angle = controller.MechanicalGroup.JointState[5];
        Debug.Log($"[ControlDiagnosticRunner] J6 establecido a {currentJ6Angle:F2}°");

        // 4. Trigger double-click on the gripper
        var method = typeof(Ctrl_OnRobotRG2_Custom).GetMethod("OnToggleGrip", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            string error = "[ControlDiagnosticRunner] No se encontró el método OnToggleGrip en Ctrl_OnRobotRG2_Custom.";
            Debug.LogError(error);
            Completed?.Invoke(1, error);
            yield break;
        }

        Debug.Log("[ControlDiagnosticRunner] Ejecutando doble clic en el gripper...");
        method.Invoke(gripper, new object[] { new InputAction.CallbackContext() });
        
        // Wait 5 fixed updates (approx 100ms at 50Hz)
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        
        method.Invoke(gripper, new object[] { new InputAction.CallbackContext() });

        // Assert that ResettingJ6 becomes true
        if (!adapter.ResettingJ6)
        {
            string error = "[ControlDiagnosticRunner] Error: ResettingJ6 no se activó tras el doble clic.";
            Debug.LogError(error);
            Completed?.Invoke(1, error);
            yield break;
        }
        Debug.Log("[ControlDiagnosticRunner] ResettingJ6 se activó correctamente.");

        // Esperar a que se complete el reset (timeout de 3 segundos)
        float timeout = Time.time + 3f;
        while (adapter.ResettingJ6 && Time.time < timeout)
        {
            yield return new WaitForFixedUpdate();
        }

        if (adapter.ResettingJ6)
        {
            string error = "[ControlDiagnosticRunner] Error: El reseteo de J6 no completó dentro de los 3 segundos.";
            Debug.LogError(error);
            Completed?.Invoke(1, error);
            yield break;
        }

        // Assert that J6 is now at 0° (with 0.1° tolerance)
        float finalJ6Angle = controller.MechanicalGroup.JointState[5];
        Debug.Log($"[ControlDiagnosticRunner] Ángulo final de J6 tras reseteo: {finalJ6Angle:F4}°");
        if (Mathf.Abs(finalJ6Angle) > 0.1f)
        {
            string error = $"[ControlDiagnosticRunner] Error: J6 no regresó a 0°. Ángulo final: {finalJ6Angle:F4}°";
            Debug.LogError(error);
            Completed?.Invoke(1, error);
            yield break;
        }

        Debug.Log("[ControlDiagnosticRunner] Diagnóstico de J6 completado con ÉXITO.");
        Completed?.Invoke(0, "Diagnóstico de J6 completado con ÉXITO.");
    }

    public IEnumerator RunVerticalSweepMatrix(float inputMagnitude = 1f, int settleTicks = 20, int segmentTicks = 120)
    {
        var adapter = FindFirstObjectByType<JoystickAdapter>();
        var controller = FindFirstObjectByType<Controller>();

        if (adapter == null || controller == null || !controller.IsValid.Value)
        {
            string error = "[ControlDiagnosticRunner] No se encontro JoystickAdapter o Controller valido.";
            Debug.LogError(error, this);
            Completed?.Invoke(1, error);
            yield break;
        }

        var initialTarget = CopyJointTarget(controller.MechanicalGroup.JointState);
        float originalMultiplier = GetPrivateFloat(adapter, "_ikJointStepLimitMultiplier");
        float originalMaxJointVelocity = GetPrivateFloat(adapter, "_maxJointVelocity");
        float originalSpeed = GetPrivateFloat(adapter, "_speed");

        var matrix = new MatrixSummary
        {
            startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
            fixedDeltaTime = Time.fixedDeltaTime,
            inputMagnitude = inputMagnitude,
            settleTicks = settleTicks,
            segmentTicks = segmentTicks
        };

        var variants = new[]
        {
            new VariantSettings { Name = "scene_current", IkJointStepLimitMultiplier = originalMultiplier, MaxJointVelocity = originalMaxJointVelocity, Speed = originalSpeed },
            new VariantSettings { Name = "joint_limit_3", IkJointStepLimitMultiplier = 3f, MaxJointVelocity = originalMaxJointVelocity, Speed = originalSpeed },
            new VariantSettings { Name = "joint_limit_2", IkJointStepLimitMultiplier = 2f, MaxJointVelocity = originalMaxJointVelocity, Speed = originalSpeed },
            new VariantSettings { Name = "joint_limit_1", IkJointStepLimitMultiplier = 1f, MaxJointVelocity = originalMaxJointVelocity, Speed = originalSpeed },
            new VariantSettings { Name = "joint_limit_2_maxvel_120", IkJointStepLimitMultiplier = 2f, MaxJointVelocity = 120f, Speed = originalSpeed }
        };

        foreach (var variant in variants)
        {
            ApplyVariant(adapter, variant);
            controller.MechanicalGroup.SetJoints(initialTarget, notify: true);
            adapter.ClearDiagnosticInputOverride();

            for (int i = 0; i < settleTicks; i++)
            {
                adapter.SetDiagnosticInputOverride(Vector3.zero);
                yield return new WaitForFixedUpdate();
            }

            var startWorldPose = GetTcpWorldPose(controller);
            var startFramePose = GetTcpFramePose(controller);
            var startJointTarget = CopyJointTarget(controller.MechanicalGroup.JointState);
            var summary = CreateRunSummary(adapter, variant.Name, inputMagnitude, settleTicks, segmentTicks);
            yield return RunSweepSegments(adapter, controller, summary, inputMagnitude, settleTicks, segmentTicks);
            FillRoundTripMetrics(summary, controller, startWorldPose, startFramePose, startJointTarget);
            matrix.runs.Add(summary);
        }

        SetPrivateFloat(adapter, "_ikJointStepLimitMultiplier", originalMultiplier);
        SetPrivateFloat(adapter, "_maxJointVelocity", originalMaxJointVelocity);
        SetPrivateFloat(adapter, "_speed", originalSpeed);
        adapter.ClearDiagnosticInputOverride();
        controller.MechanicalGroup.SetJoints(initialTarget, notify: true);

        string outputPath = WriteMatrixSummary(matrix);
        Debug.Log(BuildMatrixConsoleSummary(matrix, outputPath), this);
        Completed?.Invoke(0, outputPath);
    }

    /// <summary>
    /// Barrido completo: Y (vertical), X (lateral), Z (profundidad) y diagonal XZ.
    /// Cada dirección sube/avanza, pausa, baja/retrocede, pausa.
    /// </summary>
    public IEnumerator RunFullSweepMatrix(float inputMagnitude = 1f, int settleTicks = 20, int segmentTicks = 120)
    {
        var adapter = FindFirstObjectByType<JoystickAdapter>();
        var controller = FindFirstObjectByType<Controller>();

        if (adapter == null || controller == null || !controller.IsValid.Value)
        {
            string error = "[ControlDiagnosticRunner] No se encontro JoystickAdapter o Controller valido.";
            Debug.LogError(error, this);
            Completed?.Invoke(1, error);
            yield break;
        }

        var initialTarget = CopyJointTarget(controller.MechanicalGroup.JointState);

        var matrix = new MatrixSummary
        {
            startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
            fixedDeltaTime = Time.fixedDeltaTime,
            inputMagnitude = inputMagnitude,
            settleTicks = settleTicks,
            segmentTicks = segmentTicks
        };

        // Direcciones a probar: Y, X, Z, diagonal XZ
        var directions = new[]
        {
            ("sweep_Y",  Vector3.up,                              Vector3.down),
            ("sweep_X",  Vector3.right * inputMagnitude,          Vector3.left * inputMagnitude),
            ("sweep_Z",  Vector3.forward * inputMagnitude,        Vector3.back * inputMagnitude),
            ("sweep_XZ", (Vector3.right + Vector3.forward).normalized * inputMagnitude,
                         (Vector3.left + Vector3.back).normalized * inputMagnitude),
        };

        foreach (var (dirName, forward, backward) in directions)
        {
            // Restaurar posición inicial para cada dirección
            controller.MechanicalGroup.SetJoints(initialTarget, notify: true);
            adapter.ClearDiagnosticInputOverride();

            for (int i = 0; i < settleTicks; i++)
            {
                adapter.SetDiagnosticInputOverride(Vector3.zero);
                yield return new WaitForFixedUpdate();
            }

            var startWorldPose = GetTcpWorldPose(controller);
            var startFramePose = GetTcpFramePose(controller);
            var startJointTarget = CopyJointTarget(controller.MechanicalGroup.JointState);
            var summary = CreateRunSummary(adapter, dirName, inputMagnitude, settleTicks, segmentTicks);

            yield return RunDirectionalSegments(adapter, controller, summary, forward, backward, settleTicks, segmentTicks);
            FillRoundTripMetrics(summary, controller, startWorldPose, startFramePose, startJointTarget);
            matrix.runs.Add(summary);
        }

        adapter.ClearDiagnosticInputOverride();
        controller.MechanicalGroup.SetJoints(initialTarget, notify: true);

        string outputPath = WriteFullSweepSummary(matrix);
        Debug.Log(BuildMatrixConsoleSummary(matrix, outputPath), this);
        Completed?.Invoke(0, outputPath);
    }

    private IEnumerator RunDirectionalSegments(JoystickAdapter adapter, Controller controller, RunSummary summary,
        Vector3 forward, Vector3 backward, int settleTicks, int segmentTicks)
    {
        yield return RunSegment(adapter, controller, summary, "avanzar", forward, segmentTicks);
        yield return RunSegment(adapter, controller, summary, "reposo_1", Vector3.zero, settleTicks);
        yield return RunSegment(adapter, controller, summary, "retroceder", backward, segmentTicks);
        yield return RunSegment(adapter, controller, summary, "reposo_2", Vector3.zero, settleTicks);
    }

    private IEnumerator RunSweepSegments(JoystickAdapter adapter, Controller controller, RunSummary summary, float inputMagnitude, int settleTicks, int segmentTicks)
    {
        yield return RunSegment(adapter, controller, summary, "subir", Vector3.up * inputMagnitude, segmentTicks);
        yield return RunSegment(adapter, controller, summary, "reposo_1", Vector3.zero, settleTicks);
        yield return RunSegment(adapter, controller, summary, "bajar", Vector3.down * inputMagnitude, segmentTicks);
        yield return RunSegment(adapter, controller, summary, "reposo_2", Vector3.zero, settleTicks);
    }

    private IEnumerator RunSegment(JoystickAdapter adapter, Controller controller, RunSummary summary, string name, Vector3 velocity, int ticks)
    {
        var accumulator = new SegmentAccumulator();
        float lastSampleTime = adapter.LastIkDiagnostic.Time;
        Vector3 startPosition = GetTcpWorldPosition(controller);

        for (int i = 0; i < ticks; i++)
        {
            adapter.SetDiagnosticInputOverride(velocity);
            yield return new WaitForFixedUpdate();

            var sample = adapter.LastIkDiagnostic;
            if (!sample.IsValid || sample.Time <= lastSampleTime)
                continue;

            lastSampleTime = sample.Time;
            accumulator.Add(sample);
        }

        Vector3 endPosition = GetTcpWorldPosition(controller);
        Vector3 displacement = endPosition - startPosition;
        Vector3 direction = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector3.zero;
        var segmentSummary = accumulator.ToSummary(name);
        segmentSummary.actualDistanceWorld = displacement.magnitude;
        segmentSummary.signedDistanceWorld = Vector3.Dot(displacement, direction);
        summary.segments.Add(segmentSummary);
    }

    private static JointTarget CopyJointTarget(JointTarget source)
    {
        return new JointTarget(source.RobJoint, source.ExtJoint);
    }

    private static string WriteSummary(RunSummary summary)
    {
        string logsDir = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(logsDir);

        string json = JsonUtility.ToJson(summary, prettyPrint: true);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string timestampPath = Path.Combine(logsDir, $"control_vertical_sweep_{timestamp}.json");
        string latestPath = Path.Combine(logsDir, "control_vertical_sweep_latest.json");

        File.WriteAllText(timestampPath, json);
        File.WriteAllText(latestPath, json);
        return latestPath;
    }

    private static string WriteMatrixSummary(MatrixSummary summary)
    {
        string logsDir = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(logsDir);

        string json = JsonUtility.ToJson(summary, prettyPrint: true);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string timestampPath = Path.Combine(logsDir, $"control_vertical_sweep_matrix_{timestamp}.json");
        string latestPath = Path.Combine(logsDir, "control_vertical_sweep_matrix_latest.json");

        File.WriteAllText(timestampPath, json);
        File.WriteAllText(latestPath, json);
        return latestPath;
    }

    private static string WriteFullSweepSummary(MatrixSummary summary)
    {
        string logsDir = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(logsDir);

        string json = JsonUtility.ToJson(summary, prettyPrint: true);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string timestampPath = Path.Combine(logsDir, $"control_full_sweep_{timestamp}.json");
        string latestPath = Path.Combine(logsDir, "control_full_sweep_latest.json");

        File.WriteAllText(timestampPath, json);
        File.WriteAllText(latestPath, json);
        return latestPath;
    }

    private static string BuildConsoleSummary(RunSummary summary, string outputPath)
    {
        var lines = new List<string>
        {
            "[ControlDiagnosticRunner] Vertical sweep completado.",
            $"[ControlDiagnosticRunner] Reporte: {outputPath}"
        };

        foreach (var segment in summary.segments)
        {
            lines.Add(
                $"[ControlDiagnosticRunner] {segment.name}: samples={segment.samples} " +
                $"maxRot={segment.maxTargetRotationStep:F2}deg avgRot={segment.averageTargetRotationStep:F2}deg " +
                $"maxJoint={segment.maxJointError:F2}deg avgJoint={segment.averageJointError:F2}deg " +
                $"maxWrist={segment.maxWristError:F2}deg minStepScale={segment.minStepScale:F2}");
        }

        lines.Add(
            $"[ControlDiagnosticRunner] roundTrip: worldErr={summary.finalTcpWorldError:F4}m " +
            $"frameErr={summary.finalTcpFrameError:F4}m frameRotErr={summary.finalTcpFrameRotationError:F2}deg " +
            $"jointErr={summary.finalMaxJointRoundTripError:F2}deg restDrift={summary.maxRestWorldDrift:F4}m");

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildMatrixConsoleSummary(MatrixSummary summary, string outputPath)
    {
        var lines = new List<string>
        {
            "[ControlDiagnosticRunner] Matriz de barridos completada.",
            $"[ControlDiagnosticRunner] Reporte: {outputPath}"
        };

        foreach (var run in summary.runs)
        {
            SegmentSummary subir = FindSegment(run, "subir");
            SegmentSummary bajar = FindSegment(run, "bajar");
            lines.Add(
                $"[ControlDiagnosticRunner] {run.variantName}: " +
                $"limit={run.ikJointStepLimitMultiplier:F2} maxVel={run.maxJointVelocity:F1} speed={run.speed:F2} " +
                $"upAvgRot={subir.averageTargetRotationStep:F2} upAvgJoint={subir.averageJointError:F2} upDist={subir.signedDistanceWorld:F3} " +
                $"downAvgRot={bajar.averageTargetRotationStep:F2} downAvgJoint={bajar.averageJointError:F2} downDist={bajar.signedDistanceWorld:F3} " +
                $"rtWorld={run.finalTcpWorldError:F4} rtRot={run.finalTcpFrameRotationError:F2} jointRt={run.finalMaxJointRoundTripError:F2}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static RunSummary CreateRunSummary(JoystickAdapter adapter, string variantName, float inputMagnitude, int settleTicks, int segmentTicks)
    {
        return new RunSummary
        {
            variantName = variantName,
            startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
            fixedDeltaTime = Time.fixedDeltaTime,
            inputMagnitude = inputMagnitude,
            settleTicks = settleTicks,
            segmentTicks = segmentTicks,
            speed = GetPrivateFloat(adapter, "_speed"),
            maxJointVelocity = GetPrivateFloat(adapter, "_maxJointVelocity"),
            ikJointStepLimitMultiplier = GetPrivateFloat(adapter, "_ikJointStepLimitMultiplier")
        };
    }

    private static void ApplyVariant(JoystickAdapter adapter, VariantSettings variant)
    {
        SetPrivateFloat(adapter, "_ikJointStepLimitMultiplier", variant.IkJointStepLimitMultiplier);
        SetPrivateFloat(adapter, "_maxJointVelocity", variant.MaxJointVelocity);
        SetPrivateFloat(adapter, "_speed", variant.Speed);
    }

    private static SegmentSummary FindSegment(RunSummary summary, string name)
    {
        foreach (var segment in summary.segments)
        {
            if (segment.name == name)
                return segment;
        }

        return new SegmentSummary { name = name };
    }

    private static Vector3 GetTcpWorldPosition(Controller controller)
    {
        return GetPosePosition(GetTcpWorldPose(controller));
    }

    private static Matrix4x4 GetTcpWorldPose(Controller controller)
    {
        return controller.PoseObserver.ToolCenterPointWorld.Value;
    }

    private static Matrix4x4 GetTcpFramePose(Controller controller)
    {
        return controller.PoseObserver.ToolCenterPointFrame.Value;
    }

    private static Vector3 GetPosePosition(Matrix4x4 pose)
    {
        return (Vector3)pose.GetColumn(3);
    }

    private static void FillRoundTripMetrics(RunSummary summary, Controller controller, Matrix4x4 startWorldPose, Matrix4x4 startFramePose, JointTarget startJointTarget)
    {
        Matrix4x4 endWorldPose = GetTcpWorldPose(controller);
        Matrix4x4 endFramePose = GetTcpFramePose(controller);
        Vector3 startWorldPosition = GetPosePosition(startWorldPose);
        Vector3 endWorldPosition = GetPosePosition(endWorldPose);

        summary.finalTcpWorldError = Vector3.Distance(startWorldPosition, endWorldPosition);
        summary.finalTcpFrameError = Vector3.Distance(GetPosePosition(startFramePose), GetPosePosition(endFramePose));
        summary.finalTcpWorldRotationError = Quaternion.Angle(startWorldPose.rotation, endWorldPose.rotation);
        summary.finalTcpFrameRotationError = Quaternion.Angle(startFramePose.rotation, endFramePose.rotation);
        summary.finalMaxJointRoundTripError = GetMaxJointDifference(startJointTarget, controller.MechanicalGroup.JointState);
        summary.maxRestWorldDrift = GetMaxRestWorldDrift(summary);
        summary.netWorldYDisplacement = endWorldPosition.y - startWorldPosition.y;
    }

    private static float GetMaxJointDifference(JointTarget a, JointTarget b)
    {
        float maxError = 0f;
        for (int i = 0; i < 6; i++)
            maxError = Mathf.Max(maxError, Mathf.Abs(Mathf.DeltaAngle(a[i], b[i])));

        return maxError;
    }

    private static float GetMaxRestWorldDrift(RunSummary summary)
    {
        float maxDrift = 0f;
        foreach (var segment in summary.segments)
        {
            if (segment.name.StartsWith("reposo", StringComparison.Ordinal))
                maxDrift = Mathf.Max(maxDrift, segment.actualDistanceWorld);
        }

        return maxDrift;
    }

    private static float GetPrivateFloat(JoystickAdapter adapter, string fieldName)
    {
        FieldInfo field = typeof(JoystickAdapter).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? (float)field.GetValue(adapter) : 0f;
    }

    private static void SetPrivateFloat(JoystickAdapter adapter, string fieldName, float value)
    {
        FieldInfo field = typeof(JoystickAdapter).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(adapter, value);
    }

    private static float SafeAverage(float sum, int count)
    {
        return count > 0 ? sum / count : 0f;
    }
}
