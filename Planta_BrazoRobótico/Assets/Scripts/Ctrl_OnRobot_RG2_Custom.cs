using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;

public class Ctrl_OnRobotRG2_Custom : MonoBehaviour
{

	// Private constants.
	//  The motion parameters of the OnRobot RG2 end-effector.
	//      Stroke in mm.
	private const float s_min = 0.0f;
	private const float s_max = 100.0f;
	//      Velocity in mm/s.
	private const float v_min = 55.0f;
	private const float v_max = 180.0f;
	//  Polynomial coefficients.
	private readonly float[] coefficients = new float[] { 2.858763157442875e-09f, -1.443536999119206e-07f,
														  1.0835088633169088e-05f, 0.008994451797350918f,
														  2.6805554267921243e-07f };

	// Private variables.
	//  Motion parameters.
	private float __speed;
	private float __stroke;
	private float __theta;
	private float __theta_i;

	//  Parts (left, right hand) to be transformed.
	private GameObject R_Arm_ID_0; private GameObject R_Arm_ID_1;
	private GameObject R_Arm_ID_2;
	private GameObject L_Arm_ID_0; private GameObject L_Arm_ID_1;
	private GameObject L_Arm_ID_2;

	// Rigidbodies for physics-based movement
	private Rigidbody rb_R_Arm_ID_0; private Rigidbody rb_R_Arm_ID_1; private Rigidbody rb_R_Arm_ID_2;
	private Rigidbody rb_L_Arm_ID_0; private Rigidbody rb_L_Arm_ID_1; private Rigidbody rb_L_Arm_ID_2;

	//  Others.
	public int ctrl_state;
	//  Tracks whether the gripper is currently open.
	private bool _isOpen = false;

	// Public variables.
	public bool start_movement;
	//  Input motion parameters.
	public float speed;
	public float stroke;

	[Header("Input")]
	[Tooltip("Button action (boolean) that toggles the gripper open/close. Bind to Space, a gamepad button, etc.")]
	[SerializeField]
	private InputActionReference _toggleGripAction;

	[Tooltip("Optional reference to GripperController for grab/release logic.")]
	[SerializeField]
	private GripperController _gripperController;

#if UNITY_EDITOR
	// The [Read-only] attributes that are read-only in the Unity Inspector.
	[ReadOnly]
	public bool in_position;
#else
        private bool in_position;
#endif

	private void OnEnable()
	{
		Debug.Log("[Gripper] OnEnable called");

		if (_toggleGripAction != null)
		{
			_toggleGripAction.action.Enable();
			_toggleGripAction.action.performed += OnToggleGrip;
		}
	}

	private void OnDisable()
	{
		if (_toggleGripAction != null)
		{
			_toggleGripAction.action.performed -= OnToggleGrip;
			_toggleGripAction.action.Disable();
		}
	}

	private void OnToggleGrip(InputAction.CallbackContext ctx)
	{
		// Toggle open ↔ closed.
		_isOpen = !_isOpen;
		Debug.Log($"[Gripper] Toggle → _isOpen={_isOpen}, stroke={(_isOpen ? s_max : s_min)}");
		// Drive the existing stroke/speed logic.
		stroke = _isOpen ? s_max : s_min;
		speed = v_max;
		start_movement = true;

		// Notify the grab/release controller if assigned.
		if (_gripperController != null)
		{
			_gripperController.ToggleGrip();
		}
	}

	// Start is called before the first frame update
	void Start()
	{
		Debug.Log("[Gripper] Start called");

		// Initialization of the end-effector movable parts.
		//  Right arm.
		R_Arm_ID_0 = transform.Find("R_Arm_ID_0").gameObject; R_Arm_ID_1 = transform.Find("R_Arm_ID_1").gameObject;
		R_Arm_ID_2 = R_Arm_ID_0.transform.Find("R_Arm_ID_2").gameObject;
		//  Left arm.
		L_Arm_ID_0 = transform.Find("L_Arm_ID_0").gameObject; L_Arm_ID_1 = transform.Find("L_Arm_ID_1").gameObject;
		L_Arm_ID_2 = L_Arm_ID_0.transform.Find("L_Arm_ID_2").gameObject;

		// Get Rigidbodies
		rb_R_Arm_ID_0 = R_Arm_ID_0.GetComponent<Rigidbody>();
		rb_R_Arm_ID_1 = R_Arm_ID_1.GetComponent<Rigidbody>();
		rb_R_Arm_ID_2 = R_Arm_ID_2.GetComponent<Rigidbody>();
		rb_L_Arm_ID_0 = L_Arm_ID_0.GetComponent<Rigidbody>();
		rb_L_Arm_ID_1 = L_Arm_ID_1.GetComponent<Rigidbody>();
		rb_L_Arm_ID_2 = L_Arm_ID_2.GetComponent<Rigidbody>();

		// Reset variables.
		ctrl_state = 0;
		//  Reset the read-only variables to null.
		in_position = false;
	}

	// Update is called once per frame
	void FixedUpdate()
	{
		switch (ctrl_state)
		{
			case 0:
				{
					// If the values are out of range, clamp them.
					__stroke = Mathf.Clamp(stroke, s_min, s_max);
					__speed = Mathf.Clamp(speed, v_min, v_max);

					if (start_movement == true)
					{
						ctrl_state = 1;
					}
				}
				break;

			case 1:
				{
					// Reset variables.
					in_position = false;

					// Convert the stroke to the angle in degrees.
					__theta = Polyval(coefficients, __stroke) * Mathf.Rad2Deg;

					ctrl_state = 2;
				}
				break;

			case 2:
				{
					// Interpolate the orientation between the current position and the target position.
					__theta_i = Mathf.MoveTowards(__theta_i, __theta, __speed * Time.deltaTime);

					// Change the orientation of the end-effector arm.
					Quaternion rotR0 = Quaternion.Euler(0.0f, -__theta_i, 0.0f);
					Quaternion rotR1 = Quaternion.Euler(0.0f, -__theta_i, 0.0f);
					Quaternion rotR2 = Quaternion.Euler(0.0f, __theta_i, 0.0f);

					Quaternion rotL0 = Quaternion.Euler(0.0f, __theta_i, 0.0f);
					Quaternion rotL1 = Quaternion.Euler(0.0f, __theta_i, 0.0f);
					Quaternion rotL2 = Quaternion.Euler(0.0f, -__theta_i, 0.0f);

					//  Right arm.
					if (rb_R_Arm_ID_0 != null) rb_R_Arm_ID_0.MoveRotation(R_Arm_ID_0.transform.parent.rotation * rotR0);
					else R_Arm_ID_0.transform.localRotation = rotR0;

					if (rb_R_Arm_ID_1 != null) rb_R_Arm_ID_1.MoveRotation(R_Arm_ID_1.transform.parent.rotation * rotR1);
					else R_Arm_ID_1.transform.localRotation = rotR1;

					if (rb_R_Arm_ID_2 != null) rb_R_Arm_ID_2.MoveRotation(R_Arm_ID_2.transform.parent.rotation * rotR2);
					else R_Arm_ID_2.transform.localRotation = rotR2;

					//  Left arm.
					if (rb_L_Arm_ID_0 != null) rb_L_Arm_ID_0.MoveRotation(L_Arm_ID_0.transform.parent.rotation * rotL0);
					else L_Arm_ID_0.transform.localRotation = rotL0;

					if (rb_L_Arm_ID_1 != null) rb_L_Arm_ID_1.MoveRotation(L_Arm_ID_1.transform.parent.rotation * rotL1);
					else L_Arm_ID_1.transform.localRotation = rotL1;

					if (rb_L_Arm_ID_2 != null) rb_L_Arm_ID_2.MoveRotation(L_Arm_ID_2.transform.parent.rotation * rotL2);
					else L_Arm_ID_2.transform.localRotation = rotL2;

					if (__theta_i == __theta)
					{
						in_position = true; start_movement = false;
						ctrl_state = 0;
					}
				}
				break;

		}
	}

	public float Polyval(float[] coefficients, float x)
	{
		/*
             Description:
                A function to evaluate a polynomial at a specific value.

                Equation:
                    y = coeff[0]*x**(n-1) + coeff[1]*x**(n-2) + ... + coeff[n-2]*x + coeff[n-1]

            Args:
                (1) coefficients [Vector<float>]: Polynomial coefficients.
                (2) x [float]: An input value to be evaluated.

            Returns:
                (1) parameter [float]: The output value, which is evaluated using the input 
                                       polynomial coefficients.
         */

		float y = 0.0f; int n = coefficients.Length - 1;
		foreach (var (coeff_i, i) in coefficients.Select((coeff_i, i) => (coeff_i, i)))
		{

			y += coeff_i * Mathf.Pow(x, (n - i));
		}

		return y;
	}
}