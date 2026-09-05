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
    [Tooltip("Move speed multiplier while Left Shift is held.")]
    [SerializeField] private float sprintMultiplier = 1.6f;
    [Tooltip("Units/s² ramp up to full speed. High values keep starts snappy; ~100 reaches moveSpeed in about a frame or two.")]
    [SerializeField] private float acceleration = 100f;
    [Tooltip("Units/s² ramp down to a stop. Keep at or above acceleration so stops don't feel slidey.")]
    [SerializeField] private float deceleration = 120f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -9.81f;
    [Tooltip("Extra gravity while falling, so the descent feels snappier than the rise.")]
    [SerializeField] private float fallMultiplier = 2.5f;
    [Tooltip("Extra gravity while rising if jump is released early, allowing short hops.")]
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.25f;
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

    /// <summary>
    /// Move-speed multiplier applied by hazards (deep water, blizzard cold). 1 = unaffected.
    /// Set every frame by PlayerController; sprinting stacks on top of it.
    /// </summary>
    public float EnvironmentSpeedMultiplier { get; set; } = 1f;

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

        Vector3 moveDirection = (transform.right * input.x + transform.forward * input.y);
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        // Ramp toward the target velocity fast enough to feel instant, while still
        // smoothing over a frame or two so the animator blend doesn't pop.
        float sprint = keyboard.leftShiftKey.isPressed ? sprintMultiplier : 1f;
        float currentMoveSpeed = moveSpeed * sprint * Mathf.Clamp(EnvironmentSpeedMultiplier, 0.1f, 2f);
        Vector3 targetVelocity = moveDirection * currentMoveSpeed;
        float rate = targetVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * Time.deltaTime);

        if (animator != null)
        {
            animator.SetFloat(SpeedParam, horizontalVelocity.magnitude / Mathf.Max(currentMoveSpeed, 0.01f));
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

        // Better jumping (Board To Bits): fall faster than we rise, and cut the
        // jump short when the button is released early for variable jump height.
        float gravityScale = 1f;
        if (velocity.y < 0f)
        {
            gravityScale = fallMultiplier;
        }
        else if (velocity.y > 0f && !keyboard.spaceKey.isPressed)
        {
            gravityScale = lowJumpMultiplier;
        }

        velocity.y += gravity * gravityScale * Time.deltaTime;

        Vector3 displacement = horizontalVelocity + Vector3.up * velocity.y;
        characterController.Move(displacement * Time.deltaTime);
    }
}
