using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public float speed = 6f;
    public float fixedHeight = 1.7f;

    void Start()
    {
        // Set initial position with fixed height
        Vector3 startPos = transform.position;
        startPos.y = fixedHeight;
        transform.position = startPos;
    }

    void Update()
    {
        // WASD Movement (relative to camera direction)
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // Only apply movement on X and Z axes (horizontal)
        Vector3 move = transform.right * x + transform.forward * z;

        // Get current position
        Vector3 newPos = transform.position + move * speed * Time.deltaTime;

        // Keep height fixed at 1.7
        newPos.y = fixedHeight;

        // Apply new position
        transform.position = newPos;
    }
}