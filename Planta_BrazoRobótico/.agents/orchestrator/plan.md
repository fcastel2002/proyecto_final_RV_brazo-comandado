# Plan: Robotic Arm Control & UI Modifications

This plan outlines the steps for implementing and verifying the requirements R1-R4 and diagnostics.

## Steps

### Step 1: Codebase Exploration and Planning (Completed)
- [x] Analyze the requirements and locate the files.
- [x] Create PROJECT.md at the project root.

### Step 2: Implementation of R1 (Optional TCP Orientation)
- [ ] Dispatch worker to implement `AlignOrientationWithJ1` in `JoystickAdapter.cs`.
- [ ] Add the toggle button in the Pause Menu via `PauseMenuController.cs`.
- [ ] Verify compilation and basic logic.

### Step 3: Implementation of R2 (J6 Exclusive Mode UI Overlay)
- [ ] Dispatch worker to create `J6OverlayController.cs` and attach it to a persistent object in the scene.
- [ ] Superimpose the translucent dial under the `CameraGripperView` RawImage.
- [ ] Disable or remove the old `J6HUDController.cs` UI.
- [ ] Verify UI rendering in Play Mode.

### Step 4: Implementation of R3 & R4 (J6 Sensitivity & Double-Click Reset)
- [ ] Dispatch worker to reduce J6 input sensitivity by 4x (90°/s max) in `JoystickAdapter.cs` and `ApplyPID()`.
- [ ] Implement double-click detection (400ms window) in `Ctrl_OnRobot_RG2_Custom.cs`.
- [ ] Revert the gripper open/close on double-click and invoke `JoystickAdapter.ResetJ6ToZero()`.
- [ ] Implement `ResetJ6ToZero()` and smooth interpolation in `JoystickAdapter.cs`.

### Step 5: Diagnostics and Automation
- [ ] Implement `RunJ6Diagnostic()` in `ControlDiagnosticRunner.cs`.
- [ ] Wire the test to `ControlDiagnosticBatch.cs`.
- [ ] Run the batch mode diagnostic script.
- [ ] Verify that all tests pass and exit code is 0.

### Step 6: Final Verification & Review
- [ ] Spawn reviewers and checkers to verify the entire implementation.
- [ ] Perform a Forensic Audit to ensure integrity.
- [ ] Report victory to the user.
