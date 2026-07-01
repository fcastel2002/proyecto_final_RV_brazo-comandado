using UnityEngine;

public class GripperTopCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float height = -0.17f;
    [SerializeField] private float positionSmooth = 1000f;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + Vector3.up * height;
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            Time.deltaTime * positionSmooth
        );

        // Proyectar el vector forward del efector en el plano horizontal para usarlo como 'up' de la cámara
        Vector3 targetForward = target.forward;
        targetForward.y = 0f;
        
        if (targetForward.sqrMagnitude < 1e-6f)
        {
            targetForward = target.up;
            targetForward.y = 0f;
        }

        Vector3 cameraUp = targetForward.sqrMagnitude > 1e-6f ? targetForward.normalized : Vector3.forward;
        
        // La cámara siempre mira hacia abajo (LookRotation(down, up)) pero rota con J6
        transform.rotation = Quaternion.LookRotation(Vector3.down, cameraUp);
    }
}
