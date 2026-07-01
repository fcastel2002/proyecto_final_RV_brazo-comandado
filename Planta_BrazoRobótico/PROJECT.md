# Project: Robotic Arm Control & UI Modifications

## Architecture
The system consists of a Unity application that controls a 6-DOF robotic arm using inverse kinematics (IK) and PID controllers.
Inputs are read via the Unity Input System (using a PS4 controller or keyboard/mouse).
- **JoystickAdapter.cs** is the central controller. It reads inputs, updates the Cartesian TCP target, runs the IK solver, and applies joint PIDs.
- **Ctrl_OnRobot_RG2_Custom.cs** manages the gripper state and actions.
- **PauseMenuController.cs** manages the pause menu UI and settings.
- **ControlDiagnosticRunner.cs** and **ControlDiagnosticBatch.cs** run automated verification sweeps.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | R1. Optional TCP Orientation | Make J1-relative TCP rotation optional. Add toggle in Pause Menu. | None | DONE |
| 2 | R2. J6 Exclusive Mode UI Overlay | Overlay J6 angle dial directly on Gripper Camera view; remove side J6 HUD. | None | DONE |
| 3 | R3. J6 Sensitivity | Reduce J6 input sensitivity by 4x (90°/s max). | None | DONE |
| 4 | R4. J6 Double-Click Reset | Implement double-click on gripper to cancel toggle and smoothly reset J6 to 0°. | R3 | DONE |
| 5 | R5. Automated Diagnostics | Integrate automated test cases in ControlDiagnosticRunner/Batch. | R1, R2, R3, R4 | DONE |

## Interface Contracts
### JoystickAdapter ↔ Ctrl_OnRobot_RG2_Custom
- `JoystickAdapter.ResetJ6ToZero()`: Initiates a smooth return of J6 to 0° at 90°/s.
- `JoystickAdapter.ResettingJ6`: Property indicating if a reset is in progress.

### JoystickAdapter ↔ PauseMenuController
- `JoystickAdapter.AlignOrientationWithJ1`: Boolean property to enable/disable J1-relative TCP rotation.

## Code Layout
- Main Scripts: `Planta_BrazoRobótico/Assets/Scripts/`
  - `JoystickAdapter.cs` - Handles IK, PID, J6 exclusive mode, and TCP orientation.
  - `Ctrl_OnRobot_RG2_Custom.cs` - Gripper controller and trigger input.
  - `PauseMenuController.cs` - Pause menu UI.
  - `J6OverlayController.cs` - New script for the translucent J6 dial overlay.
- Diagnostics & Editor:
  - `Planta_BrazoRobótico/Assets/Scripts/Diagnostics/ControlDiagnosticRunner.cs`
  - `Planta_BrazoRobótico/Assets/Editor/ControlDiagnosticBatch.cs`
- Scene: `Planta_BrazoRobótico/Assets/Scenes/Planta.unity`
