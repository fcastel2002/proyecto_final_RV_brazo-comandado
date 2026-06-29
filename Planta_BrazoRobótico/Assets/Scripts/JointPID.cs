using UnityEngine;

/// <summary>
/// Controlador PID para una articulación individual.
/// Compute() devuelve un torque virtual (°/s² a inercia de referencia).
/// El caller divide por la inercia normalizada para obtener la aceleración real.
/// </summary>
[System.Serializable]
public class JointPID
{
    public float Kp;
    public float Ki;
    public float Kd;
    [Tooltip("Límite anti-windup del término integral (°·s).")]
    public float MaxIntegral = 200f;

    private float _integral;
    private float _prevCurrent;
    private bool _hasPrevCurrent;

    public JointPID(float kp, float ki, float kd)
    {
        Kp = kp;
        Ki = ki;
        Kd = kd;
    }

    /// <summary>
    /// Calcula el torque virtual para este tick.
    /// setpoint y current en grados; setpointVelocity en grados/s; dt en segundos.
    /// Retorna torque en °/s² (a inercia de referencia).
    /// </summary>
    public float Compute(float setpoint, float current, float dt, float setpointVelocity = 0f)
    {
        float error = Mathf.DeltaAngle(current, setpoint);
        _integral = Mathf.Clamp(_integral + error * dt, -MaxIntegral, MaxIntegral);
        float measuredVelocity = _hasPrevCurrent && dt > 1e-6f
            ? Mathf.DeltaAngle(_prevCurrent, current) / dt
            : 0f;
        _prevCurrent = current;
        _hasPrevCurrent = true;
        float derivative = setpointVelocity - measuredVelocity;
        return Kp * error + Ki * _integral + Kd * derivative;
    }

    /// <summary>Resetea integral y medicion previa. Llamar al (re)activar el modo robot.</summary>
    public void Reset()
    {
        _integral = 0f;
        _prevCurrent = 0f;
        _hasPrevCurrent = false;
    }
}
