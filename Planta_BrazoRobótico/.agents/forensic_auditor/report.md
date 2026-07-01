# Forensic Audit Report

**Work Product**: Modified files:
- `Assets/Scripts/JoystickAdapter.cs`
- `Assets/Scripts/PauseMenuController.cs`
- `Assets/Scripts/J6HUDController.cs`
- `Assets/Scripts/J6OverlayController.cs`
- `Assets/Scripts/Ctrl_OnRobot_RG2_Custom.cs`
- `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs`
- `Assets/Editor/ControlDiagnosticBatch.cs`

**Profile**: General Project
**Verdict**: CLEAN

---

### Phase Results

#### 1. Hardcoded Output Detection: PASS
- **Observation**: Inspected the test runner `Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs` and the implementation files.
- **Details**: There are no hardcoded test results or static assertions. All checks in `RunJ6Diagnostic()` are dynamic. For example, the test asserts that:
  - The J6 angle is close to 0° after initialization: `Mathf.Abs(j6Angle) > 0.1f` (lines 197-203).
  - The maximum J6 velocity does not exceed the safety limit: `maxObservedVelocity > 95f` (lines 235-241).
  - The reset state is triggered: `!adapter.ResettingJ6` (lines 288-294).
  - The J6 angle returns to 0° after the reset: `Mathf.Abs(finalJ6Angle) > 0.1f` (lines 315-321).
- **Conclusion**: The test runner exercises the actual control loop and queries the physical robot arm state dynamically.

#### 2. Facade Detection: PASS
- **Observation**: Inspected the implementation of the control loop in `JoystickAdapter.cs` and the gripper trigger in `Ctrl_OnRobot_RG2_Custom.cs`.
- **Details**: There are no facade or dummy implementations. The J6 resetting is done via gradual interpolation (`Mathf.MoveTowardsAngle` at 90°/s) and is integrated into the joint PID loop (`ApplyPID`) which integrates velocity and applies it to the mechanical group via `SetJoints()`. The J6 exclusive mode reads inputs, limits the rate of change of target angle to 90°/s, and runs it through the PID loop.
- **Conclusion**: The control logic is fully genuine and integrated with the robot's physical control architecture.

#### 3. Genuine Implementation of Requirements (R1-R4): PASS
- **R1 (Optional TCP Orientation)**: Fully genuine. Implemented via the `AlignOrientationWithJ1` toggle in `JoystickAdapter.cs` and `PauseMenuController.cs`. When enabled, the target TCP orientation rotates with the J1 base angle (`j1Rotation * _fixedTcpFrameOrientation`), and when disabled, it remains fixed in the robot frame. This is passed directly to the Flange IK solver.
- **R2 (Translucent Dial Overlay)**: Fully genuine. Implemented via `J6OverlayController.cs`, which is dynamically instantiated and builds a translucent UI overlay with cardinal marks and a pointer on top of the `CameraGripperView` at runtime. The pointer rotates based on the actual J6 joint state.
- **R3 (J6 Sensitivity)**: Fully genuine. Both the target angle rate of change (clamped to `90f * Time.fixedDeltaTime` in `UpdateJ6ExclusiveControl()`) and the joint velocity of J6 (clamped to `90f` in `ApplyPID()`) are capped at 90°/s.
- **R4 (Double-Click Reset)**: Fully genuine. Implemented in `Ctrl_OnRobotRG2_Custom.cs` by detecting double clicks within 400ms on the trigger, which toggles the gripper and calls `joystickAdapter.ResetJ6ToZero()`. The reset interpolates the target angle to 0° at 90°/s and recaptures the reference orientation upon completion.
- **Conclusion**: The implementations follow the control architecture described in `_ARQUITECTURA_CONTROL.md` and are fully functional.

#### 4. Automated Diagnostic Test Verification: PASS
- **Observation**: The automated test `RunJ6Diagnostic` in `ControlDiagnosticRunner.cs` was executed in batchmode.
- **Details**: The log file `Logs/control_j6_diagnostic_unity.log` shows the test completed successfully:
  ```text
  [JoystickAdapter] Reset J6 to 0° completed. Recaptured orientation.
  [ControlDiagnosticRunner] Ángulo final de J6 tras reseteo: 0,0000°
  [ControlDiagnosticRunner] Diagnóstico de J6 completado con ÉXITO.
  [ControlDiagnosticBatch] Diagnostico finalizado: Diagnóstico de J6 completado con ÉXITO.
  ```
- **Conclusion**: The diagnostic test successfully runs the control loop, verifies J6 exclusive mode, limits velocity, triggers the reset via double click, and verifies the final state.

---

### Evidence

#### 1. J6 Reset and Velocity Clamping in `JoystickAdapter.cs`
Lines 458-469:
```csharp
        if (_resettingJ6)
        {
            _j6TargetAngle = Mathf.MoveTowardsAngle(_j6TargetAngle, 0f, 90f * dt);
            if (Mathf.Approximately(_j6TargetAngle, 0f))
            {
                _j6TargetAngle = 0f;
                _resettingJ6 = false;
                _orientationCaptured = false;
                CaptureFixedOrientation();
                Debug.Log("[JoystickAdapter] Reset J6 to 0° completed. Recaptured orientation.");
            }
        }
```
Line 912:
```csharp
            float maxVel = (i == 5) ? 90f : _maxJointVelocity;
```

#### 2. Double-Click Detection in `Ctrl_OnRobot_RG2_Custom.cs`
Lines 102-128:
```csharp
		float currentTime = Time.unscaledTime;
		if (currentTime - _lastGripClickTime <= 0.4f)
		{
			// Double-click detected!
			_isOpen = !_isOpen;
			Debug.Log($"[Gripper] Double Click detected! Reverting toggle → _isOpen={_isOpen}, stroke={(_isOpen ? s_max : s_min)}");
			stroke = _isOpen ? s_max : s_min;
			speed = v_max;
			start_movement = true;

			if (_gripperController != null)
			{
				_gripperController.ToggleGrip();
			}

			var joystickAdapter = FindFirstObjectByType<JoystickAdapter>();
			if (joystickAdapter != null)
			{
				joystickAdapter.ResetJ6ToZero();
			}

			_lastGripClickTime = -999f;
		}
```

#### 3. Optional TCP Orientation in `JoystickAdapter.cs`
Lines 503-513:
```csharp
        Quaternion dynamicTcpOrientation;
        if (AlignOrientationWithJ1)
        {
            float currentJ1 = _hasPrevIkTarget ? _prevIkTarget[0] : _controller.MechanicalGroup.JointState[0];
            float deltaJ1 = currentJ1 - _initialJ1Angle;
            Quaternion j1Rotation = Quaternion.AngleAxis(deltaJ1, Vector3.down);
            dynamicTcpOrientation = j1Rotation * _fixedTcpFrameOrientation;
        }
        else
        {
            dynamicTcpOrientation = _fixedTcpFrameOrientation;
        }
```

#### 4. Translucent Dial Overlay in `J6OverlayController.cs`
Lines 90-106:
```csharp
        // Translucent background
        Image bgImg = _overlayContainer.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.4f);
        bgImg.raycastTarget = false;

        // Dial Background (Knob.psd)
        GameObject dialBg = new GameObject("Dial_BG");
        dialBg.transform.SetParent(_overlayContainer.transform, false);
        RectTransform dialBgRect = dialBg.AddComponent<RectTransform>();
        dialBgRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialBgRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialBgRect.sizeDelta = new Vector2(130f, 130f);
        dialBgRect.anchoredPosition = Vector2.zero;

        Image dialBgImg = dialBg.AddComponent<Image>();
        dialBgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
```
