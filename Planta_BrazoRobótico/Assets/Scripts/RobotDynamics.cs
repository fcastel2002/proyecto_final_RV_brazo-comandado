using System.Collections.Generic;
using Preliy.Flange;
using UnityEngine;

/// <summary>
/// Datos de masa y CoM del KUKA KR210 R3100-2 (fuente: URDF Udacity RoboND).
/// Calcula la inercia efectiva por articulación según la configuración actual.
/// </summary>
public static class RobotDynamics
{
    public readonly struct LinkData
    {
        public readonly float Mass;       // kg
        public readonly Vector3 ComLocal; // CoM en frame local del TransformJoint (m)

        public LinkData(float mass, Vector3 comLocal)
        {
            Mass = mass;
            ComLocal = comLocal;
        }
    }

    // Índice 0 = link_1 … índice 5 = link_6
    // CoM expresado en el espacio local del TransformJoint correspondiente
    public static readonly LinkData[] Links =
    {
        new LinkData(138f, new Vector3( 0.000f,  0.000f,  0.400f)),  // link_1
        new LinkData( 95f, new Vector3( 0.000f,  0.000f,  0.448f)),  // link_2
        new LinkData( 71f, new Vector3( 0.188f,  0.183f, -0.043f)),  // link_3
        new LinkData( 17f, new Vector3( 0.271f, -0.007f,  0.000f)),  // link_4
        new LinkData(  7f, new Vector3( 0.000f,  0.000f,  0.000f)),  // link_5
        new LinkData(0.5f, new Vector3( 0.000f,  0.000f,  0.000f)),  // link_6
    };

    /// <summary>
    /// Devuelve J_eff[i] = Σ_{j≥i} m_j · d_ij²
    /// donde d_ij es la distancia perpendicular del eje del joint i al CoM del link j en mundo.
    ///
    /// Eje del joint i: en la convención DH de Flange todos los joints revolutos
    /// giran sobre el –Y del frame padre, por lo que el eje en mundo es
    ///   i == 0 → robotJoints[0].transform.parent.up
    ///   i  > 0 → robotJoints[i-1].transform.up
    /// </summary>
    public static float[] ComputeEffectiveInertia(IReadOnlyList<TransformJoint> robotJoints)
    {
        var result = new float[6];

        for (int i = 0; i < 6; i++)
        {
            Vector3 axisOrigin = robotJoints[i].transform.position;
            Vector3 axisDir = i == 0
                ? robotJoints[0].transform.parent.up
                : robotJoints[i - 1].transform.up;

            float jEff = 0f;
            for (int j = i; j < 6; j++)
            {
                Vector3 comWorld = robotJoints[j].transform.TransformPoint(Links[j].ComLocal);
                float d = PerpendicularDistance(comWorld, axisOrigin, axisDir);
                jEff += Links[j].Mass * d * d;
            }

            result[i] = jEff;
        }

        return result;
    }

    // Distancia perpendicular de un punto a una línea definida por origen + dirección normalizada
    private static float PerpendicularDistance(
        Vector3 point, Vector3 axisOrigin, Vector3 axisDirection)
    {
        Vector3 v = point - axisOrigin;
        float proj = Vector3.Dot(v, axisDirection);
        float distSq = v.sqrMagnitude - proj * proj;
        return Mathf.Sqrt(Mathf.Max(distSq, 0f));
    }
}
