## 2026-06-30T18:16:31Z
You are a worker tasked with implementing the following requirements in the Unity project:

1. R1. Modo de Orientación del TCP (Menú de Pausa)
- Modify `Assets/Scripts/JoystickAdapter.cs` to add a serialized and public property `AlignOrientationWithJ1` (defaulting to `false`).
- In `FixedUpdate()`, if `AlignOrientationWithJ1` is `false`, keep the original exact TCP orientation `_fixedTcpFrameOrientation` without rotating with J1.
- In `Assets/Scripts/PauseMenuController.cs`, add a button to toggle this mode, updating the text to "Orientación: Fija Absoluta" or "Orientación: Seguir Base (J1)" accordingly.

2. R2. Interfaz del Modo J6 Exclusivo (Superposición)
- Remove or disable the old side panel UI from `Assets/Scripts/J6HUDController.cs`.
- Create a new script `Assets/Scripts/J6OverlayController.cs` (or implement it in an appropriate location) that:
  - Dynamically builds a translucent dial UI (using `Knob.psd` or standard UI elements) and attaches it as a child of the `CameraGripperView` GameObject (the RawImage displaying the gripper camera).
  - Shows reference marks (0°, 90°, 180°, -90°) and the current angle of J6 in text.
  - Active only when `JoystickAdapter.IsJ6ExclusiveMode` is `true`.
- Note: You may need to add `J6OverlayController` to a persistent GameObject in the scene (e.g. the same GameObject holding `JoystickAdapter` or `GameManager` or create a new one).

3. R3. Sensibilidad del Modo J6
- In `Assets/Scripts/JoystickAdapter.cs`'s `UpdateJ6ExclusiveControl()`, limit the rate of change of `_j6TargetAngle` to a maximum speed of 90°/s (using `Time.fixedDeltaTime`).
- In `ApplyPID()`, limit the joint velocity of J6 (index 5) to 90°/s.

4. R4. Reseteo de J6 con Doble Clic del Gripper
- In `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`, detect a double-click on the gripper button (trigger) within a 400ms window.
- A simple click should toggle the gripper as usual.
- A double-click should cancel/revert the gripper open/close action (by toggling it again) and call `ResetJ6ToZero()` on `JoystickAdapter`.
- In `Assets/Scripts/JoystickAdapter.cs`, implement `ResetJ6ToZero()` which sets a flag `_resettingJ6 = true`.
- In `FixedUpdate()`, if `_resettingJ6` is true, smoothly interpolate `_j6TargetAngle` towards 0° at 90°/s. Once it reaches 0°, set `_resettingJ6 = false` and recaptures the orientation to avoid a sudden jump.
- In `ApplyPID()`, if `_resettingJ6` is true and `i == 5`, override the target joint angle with `_j6TargetAngle`.

5. R5. Automated Diagnostics
- Add a new menu item and batch method `RunJ6Diagnostic` in `Assets/Editor/ControlDiagnosticBatch.cs`.
- Implement the test in `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs` in a coroutine `RunJ6Diagnostic()`. The test should:
  - Initialize J6 to 0°.
  - Enable J6 exclusive mode, inject J6 stick input, and assert that J6 velocity does not exceed 90°/s.
  - Set J6 to 45°, trigger a double-click on the gripper, assert that `ResettingJ6` becomes true, wait for it to complete, and assert that J6 is now at 0° (with a small tolerance like 0.1°).
  - Log the results and complete with exit code 0 or 1.

6. Verification
- Compile the Unity project and run the batch mode diagnostic test.
- Verify that the project compiles cleanly and the J6 diagnostic passes.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Please write a detailed handoff report when done at d:\06. Proyectos\proyecto_final_RV_brazo-comandado\Planta_BrazoRobótico\.agents\worker_implementation_1\handoff.md.
Your working directory is d:\06. Proyectos\proyecto_final_RV_brazo-comandado\Planta_BrazoRobótico\.agents\worker_implementation_1\
