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
    private float _prevError;

    public JointPID(float kp, float ki, float kd)
    {
        Kp = kp;
        Ki = ki;
        Kd = kd;
    }

    /// <summary>
    /// Calcula el torque virtual para este tick.
    /// setpoint y current en grados; dt en segundos.
    /// Retorna torque en °/s² (a inercia de referencia).
    /// </summary>
    public float Compute(float setpoint, float current, float dt)
    {
        float error = setpoint - current;
        _integral = Mathf.Clamp(_integral + error * dt, -MaxIntegral, MaxIntegral);
        float derivative = dt > 1e-6f ? (error - _prevError) / dt : 0f;
        _prevError = error;
        return Kp * error + Ki * _integral + Kd * derivative;
    }

    /// <summary>Resetea integral y error previo. Llamar al (re)activar el modo robot.</summary>
    public void Reset()
    {
        _integral = 0f;
        _prevError = 0f;
    }
}
