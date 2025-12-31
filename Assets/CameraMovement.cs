using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    CharacterController controller;
    public float speed = 6f;
    public float mouseSensitivity = 100f;
    float xRotation = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // WASD Movement (relative to camera direction)
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * speed * Time.deltaTime);
    }
}