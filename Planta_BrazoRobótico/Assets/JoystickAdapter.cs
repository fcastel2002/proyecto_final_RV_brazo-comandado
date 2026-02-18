using UnityEngine;
using UnityEngine.InputSystem;
using Preliy.Flange;

/// <summary>
/// Reads velocity commands from a joystick (X, Y, Z) via InputActionReference
/// and integrates them in FixedUpdate to produce cartesian position targets
/// for the Flange inverse-kinematics solver.
/// </summary>
public class JoystickAdapter : MonoBehaviour
{
	[Header("Controller")]
	[SerializeField]
	private Controller _controller;

	[Header("Input Actions (each axis returns a float -1..1)")]
	[SerializeField]
	private InputActionReference _moveX;
	[SerializeField]
	private InputActionReference _moveY;
	[SerializeField]
	private InputActionReference _moveZ;

	[Header("Settings")]
	[Tooltip("Max linear speed (m/s)")]
	[SerializeField]
	private float _speed = 0.1f;

	private Vector3 _velocity;

	private void OnEnable()
	{
		_moveX?.action.Enable();
		_moveY?.action.Enable();
		_moveZ?.action.Enable();
	}

	private void OnDisable()
	{
		_moveX?.action.Disable();
		_moveY?.action.Disable();
		_moveZ?.action.Disable();
	}

	/// <summary>
	/// Sample the joystick axes every visual frame.
	/// </summary>
	private void Update()
	{
		_velocity = new Vector3(
			_moveX?.action.ReadValue<float>() ?? 0f,
			_moveY?.action.ReadValue<float>() ?? 0f,
			_moveZ?.action.ReadValue<float>() ?? 0f
		);
	}

	/// <summary>
	/// Integrate velocity → position delta at a fixed rate and feed it
	/// to the IK solver so the robot follows smoothly.
	/// </summary>
	private void FixedUpdate()
	{
		if (_controller == null || !_controller.IsValid.Value)
			return;

		if (_velocity.sqrMagnitude < 1e-6f)
			return;

		// Velocity (m/s) × dt → position increment (m)
		var delta = _velocity * (_speed * Time.fixedDeltaTime);

		// Current TCP expressed in the active reference frame
		var currentPose = _controller.PoseObserver.ToolCenterPointFrame.Value;

		// Build a pure-translation 4×4 offset and apply it
		var offset = Matrix4x4.TRS(delta, Quaternion.identity, Vector3.one);
		var targetPose = currentPose * offset;

		// Reuse the current configuration & external joints
		var frame = _controller.Frame.Value;
		var tool = _controller.Tool.Value;
		var configuration = _controller.Configuration.Value;
		var extJoint = _controller.MechanicalGroup.JointState.ExtJoint;

		var target = new CartesianTarget(targetPose, configuration, extJoint);

		// Solve IK — silently skip if unreachable
		var solution = _controller.Solver.ComputeInverse(target, tool, frame);

		if (!solution.IsValid)
			return;

		// Apply the new joint values and notify observers (PoseObserver, etc.)
		_controller.MechanicalGroup.SetJoints(solution.JointTarget, notify: true);
	}
}