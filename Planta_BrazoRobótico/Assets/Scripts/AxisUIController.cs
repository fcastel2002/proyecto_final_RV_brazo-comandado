using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class AxisUIController : MonoBehaviour
{
    [Header("Axis - Joystick")]
    [SerializeField]
    private InputActionReference axisInputValue;
    [Header("UI References")]
    [SerializeField]
    private RectTransform visualAxisValue;
    [SerializeField]
    private TextMeshProUGUI axisValueText;
    [Header("Setting")]
    private float neutralOffset = 50f;

    [SerializeField] private AnalogCalibrationManager calibrationManager;

    public InputActionReference CurrentInputAction => axisInputValue;


    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (calibrationManager == null)
            calibrationManager = FindFirstObjectByType<AnalogCalibrationManager>();
    }

    public void SetInputAction(InputActionReference axisAction)
    {
        axisInputValue = axisAction;
        UpdateVisuals(0f);
    }

    // Update is called once per frame
    void Update()
    {
        if (axisInputValue != null && axisInputValue.action != null)
        {
            float currentInputValue = calibrationManager != null
                ? calibrationManager.ReadCalibrated(axisInputValue)
                : axisInputValue.action.ReadValue<float>();

            UpdateVisuals(currentInputValue);
        }
    }
    private void UpdateVisuals(float inputValue)
    {
        axisValueText.text = inputValue.ToString("F3");
        float topVal = neutralOffset;
        float bottomVal = topVal;
        if (inputValue > 0)
        {
            topVal = neutralOffset - (inputValue * neutralOffset);

        }
        else if(inputValue < 0)
        {
            bottomVal = neutralOffset - (Mathf.Abs(inputValue) * neutralOffset);
        }
        visualAxisValue.offsetMax = new Vector2(visualAxisValue.offsetMax.x, -topVal);
        visualAxisValue.offsetMin = new Vector2(visualAxisValue.offsetMin.x, bottomVal);
    }
}
