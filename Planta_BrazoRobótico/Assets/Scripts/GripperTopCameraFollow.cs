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

        transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
    }
}
