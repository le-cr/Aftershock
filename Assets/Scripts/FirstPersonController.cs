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
    [SerializeField] private float acceleration = 12f;
    [SerializeField] private float deceleration = 16f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.25f;
    [SerializeField] private float keyboardTurnSpeed = 90f;
    [Tooltip("Camera to rotate for looking up/down. Defaults to Camera.main if unset.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Animation")]
    [Tooltip("Drives the 'Speed' float parameter (0 = idle, 1 = walking). Defaults to a child Animator if unset.")]
    [SerializeField] private Animator animator;

    private CharacterController characterController;
    private Vector3 velocity;
    private Vector3 horizontalVelocity;
    private float pitch;

    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }

    private void Start()
    {
        
    }

    private void Update()
    {
        HandleLook();
        HandleMovement();
    }

    private void HandleLook()
    {
        if (cameraTransform == null)
        {
            return;
        }

        float yaw = 0f;

        if (Keyboard.current != null)
        {
            // A/D turn the camera left/right instead of strafing.
            float keyboardYaw = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
            yaw += keyboardYaw * keyboardTurnSpeed * Time.deltaTime;
        }

        // Yaw rotates the whole body left/right.
        transform.Rotate(Vector3.up * yaw);

        // Pitch rotates only the camera up/down, clamped to avoid flipping over.
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        // W/S move forward/backward relative to where the player is facing.
        float forwardInput = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

        Vector3 moveDirection = transform.forward * forwardInput;
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        // Smoothly accelerate toward the target velocity and decelerate toward zero
        // so movement doesn't snap instantly to full speed or stop dead.
        Vector3 targetVelocity = moveDirection * moveSpeed;
        float rate = targetVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * Time.deltaTime);

        if (animator != null)
        {
            animator.SetFloat(SpeedParam, horizontalVelocity.magnitude / moveSpeed);
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

        Vector3 displacement = horizontalVelocity + Vector3.up * velocity.y;
        characterController.Move(displacement * Time.deltaTime);
    }
}
