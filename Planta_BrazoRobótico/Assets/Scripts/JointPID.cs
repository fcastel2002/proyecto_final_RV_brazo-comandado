using UnityEngine;

/// <summary>
/// Controlador PID para una articulación individual.
/// Compute() devuelve el delta de posición (grados) a sumar al ángulo actual.
/// Ki puede modificarse en runtime para simular inercia efectiva variable.
/// </summary>
[System.Serializable]
public class JointPID
{
    public float Kp;
    public float Ki;
    public float Kd;

    private float _integral;
    private float _prevError;

    public JointPID(float kp, float ki, float kd)
    {
        Kp = kp;
        Ki = ki;
        Kd = kd;
    }

    /// <summary>
    /// Calcula el delta de posición para este tick.
    /// setpoint y current en grados; dt en segundos.
    /// </summary>
    public float Compute(float setpoint, float current, float dt)
    {
        float error = setpoint - current;
        _integral += error * dt;
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
