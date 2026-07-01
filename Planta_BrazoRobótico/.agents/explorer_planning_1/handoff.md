# Handoff Report — Planning & Analysis for R1 to R4 + Diagnostics

This report summarizes the read-only investigation, findings, and concrete recommendations for the implementation of the four requirements and the test suite.

---

## 1. Observation
I have analyzed the workspace and identified the following files, classes, and code sections:

* **TCP Orientation (R1):**
  * File: `Assets/Scripts/JoystickAdapter.cs` (Lines 469–473)
    ```csharp
    float currentJ1 = _hasPrevIkTarget ? _prevIkTarget[0] : _controller.MechanicalGroup.JointState[0];
    float deltaJ1 = currentJ1 - _initialJ1Angle;
    Quaternion j1Rotation = Quaternion.AngleAxis(deltaJ1, Vector3.down);
    Quaternion dynamicTcpOrientation = j1Rotation * _fixedTcpFrameOrientation;
    ```
  * File: `Assets/Scripts/PauseMenuController.cs`.

* **J6 Exclusive Mode UI Overlay (R2):**
  * File: `Assets/Scripts/J6HUDController.cs` (Builds a side panel UI dynamically at runtime via `BuildUI()` on lines 81–191).
  * File: `Assets/Scenes/Planta.unity` (Contains `CameraGripperView` GameObject at line 5340 with `RawImage` component displaying `RT_GripperCamera.renderTexture`).
  * File: `Assets/RT_GripperCamera.renderTexture.meta` (GUID: `05f822a84f5d49a48ba480ed5279cbf7`).

* **J6 Sensitivity (R3):**
  * File: `Assets/Scripts/JoystickAdapter.cs` (Method `UpdateJ6ExclusiveControl` at lines 675–735).

* **J6 Reset via Double-Click (R4):**
  * File: `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs` (Method `OnToggleGrip` at lines 95–113).
  * File: `Assets/Scripts/GripperController.cs` (Method `ToggleGrip` at lines 30–43).

* **Diagnostics and Tests:**
  * File: `Assets/Editor/ControlDiagnosticBatch.cs` (Manages batch mode execution).
  * File: `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs` (Runs kinematic sweeps and logs telemetry to JSON in `Logs/`).

---

## 2. Logic Chain
1. **R1 (TCP Orientation Option):** The J1-relative rotation is calculated as `j1Rotation * _fixedTcpFrameOrientation`. If we introduce a boolean `_alignOrientationWithJ1` in `JoystickAdapter.cs` that defaults to `false`, we can conditionally assign `dynamicTcpOrientation = _fixedTcpFrameOrientation`. In `PauseMenuController.cs`, we can add a button that toggles this boolean at runtime and updates its text dynamically.
2. **R2 (J6 Overlay):** Since `CameraGripperView` is a standard UI `RawImage` in the scene, we can make a translucent dial UI a child of `CameraGripperView` instead of having a separate side HUD container. Removing `J6HUDController` and replacing it with a new `J6OverlayController` that instantiates the dial under `CameraGripperView` achieves the desired translucent overlay effect perfectly.
3. **R3 (J6 Sensitivity):** J6 target currently jumps to the stick angle immediately. Limiting the rate of change of `_j6TargetAngle` to `90f * Time.fixedDeltaTime` (90°/s, which is 1/4 of the 360°/s max joint velocity) in `UpdateJ6ExclusiveControl()` and clamping J6's joint velocity to 90°/s in `ApplyPID()` will make J6 rotation exactly 4x slower and extremely smooth.
4. **R4 (J6 Double-Click Reset):** By tracking the click timestamp in `OnToggleGrip` using `Time.unscaledTime`, we can detect double-clicks within a 400ms window. A second click within this window will revert the first click's gripper toggle (calling `_gripperController.ToggleGrip()` again) and call a new `ResetJ6ToZero()` method on `JoystickAdapter`, which smoothly interpolates J6 back to 0° at 90°/s.
5. **Diagnostics & Tests:** We can extend `ControlDiagnosticRunner.cs` with a new coroutine `RunJ6Diagnostic()` that simulates a J6 stick input (to assert that velocity is capped at 90°/s) and simulates a double-click on the gripper (to assert that J6 is smoothly reset to 0° and that the gripper state is reverted). We can wire this in `ControlDiagnosticBatch.cs` under a new `"j6"` mode.

---

## 3. Caveats
* **Platform/Graphics Support in Batch Mode:** The Unity project uses URP and RenderTexture. It is important to run batch mode without the `-nographics` flag to prevent Unity from crashing when trying to initialize RenderTextures.
* **Reflexive Property Access:** During testing, the static property `IsJ6ExclusiveMode` and private fields must be manipulated or accessed. Using reflection is the most robust way to do this in the test runner without exposing unnecessary public APIs in production code.

---

## 4. Conclusion
The implementation of all four requirements is highly feasible and can be achieved with localized, clean changes to `JoystickAdapter.cs`, `PauseMenuController.cs`, `Ctrl_OnRobot_RG2_Custom.cs`, and a new `J6OverlayController.cs` script. A dedicated test routine can be integrated into the existing diagnostic framework to verify both sensitivity limits and double-click behaviors automatically.

---

## 5. Verification Method
* **Manual Verification:**
  1. Open the Pause Menu and toggle the "TCP Orientation" button to verify that the TCP maintains its absolute orientation when J1 rotates.
  2. Enter J6 Exclusive mode and verify that a translucent circular dial is overlaid directly on the gripper camera view.
  3. Verify J6 rotation is 4x slower.
  4. Verify that double-clicking the gripper trigger reverts any open/close action and smoothly returns J6 to 0°.
* **Automated Batch Verification:**
  Run the new J6 diagnostic sweep from the command line:
  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe" `
    -batchmode `
    -projectPath . `
    -executeMethod ControlDiagnosticBatch.RunJ6Diagnostic `
    -logFile "Logs/control_j6_diagnostic_unity.log"
  ```
  Check that the exit code is `0` and that the log files confirm successful verification of R3 and R4.
