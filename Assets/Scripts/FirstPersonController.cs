using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple first-person player controller for Unity's new Input System.
/// Move with WASD, jump with Space, and look around with the mouse.
/// Attach to a GameObject with a CharacterController and a child camera.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [Tooltip("Camera to rotate for looking up/down. Defaults to Camera.main if unset.")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController characterController;
    private Vector3 velocity;
    private float pitch;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        HandleLook();
        HandleMovement();
    }

    private void HandleLook()
    {
        if (Mouse.current == null || cameraTransform == null)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        // Yaw rotates the whole body left/right.
        transform.Rotate(Vector3.up * mouseDelta.x);

        // Pitch rotates only the camera up/down, clamped to avoid flipping over.
        pitch = Mathf.Clamp(pitch - mouseDelta.y, -90f, 90f);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        // Read WASD as a direction relative to where the player is facing.
        Vector2 input = new Vector2(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));

        Vector3 move = (transform.right * input.x + transform.forward * input.y);
        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        // Reset downward velocity while grounded so gravity doesn't accumulate.
        if (characterController.isGrounded && velocity.y < 0f)
        {
            velocity.y = -2f;
        }

        if (keyboard.spaceKey.wasPressedThisFrame && characterController.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;

        Vector3 displacement = move * moveSpeed + Vector3.up * velocity.y;
        characterController.Move(displacement * Time.deltaTime);
    }
}
