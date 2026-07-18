using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shakes a kinematic ground Rigidbody with Perlin-noise displacement to simulate an earthquake.
/// Tuned after real quake footage: a rapid, mostly-horizontal vibration (with a smaller vertical
/// component and only slight tilt) rather than a slow rocking. Because the ground is kinematic and
/// moved via MovePosition/MoveRotation, PhysX imparts the motion to resting dynamic Rigidbodies.
///
/// Exposes <see cref="IsShaking"/> and <see cref="ShakeStrength"/> so building debris can break
/// off in time with the quake. Press E to trigger, or enable Auto Start.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EarthquakeSimulator : MonoBehaviour
{
    /// <summary>The most recently enabled simulator, so debris can find the active quake.</summary>
    public static EarthquakeSimulator Instance { get; private set; }

    /// <summary>True while an earthquake is playing.</summary>
    public bool IsShaking => shaking;

    /// <summary>Current quake intensity, 0..1, following the ramp-up/ramp-down envelope.</summary>
    public float ShakeStrength { get; private set; }

    [Header("Intensity")]
    [Tooltip("Horizontal displacement amplitude in world units. Real quakes vibrate; keep this small.")]
    [SerializeField] private float magnitude = 0.12f;
    [Tooltip("Vertical (up/down) displacement amplitude in world units.")]
    [SerializeField] private float verticalMagnitude = 0.05f;
    [Tooltip("Peak tilt of the ground, in degrees. Keep small so heavy buildings don't just tip over.")]
    [SerializeField] private float tiltMagnitude = 0.4f;
    [Tooltip("How fast the shake oscillates (noise speed). Higher = faster vibration.")]
    [SerializeField] private float frequency = 14f;

    [Header("Timing")]
    [Tooltip("Length of a single earthquake, in seconds.")]
    [SerializeField] private float duration = 12f;
    [SerializeField] private bool autoStart = true;
    [Tooltip("Delay before the auto-start quake, letting buildings settle first.")]
    [SerializeField] private float startDelay = 3f;

    [Header("Input")]
    [Tooltip("Key that triggers/re-triggers an earthquake.")]
    [SerializeField] private Key triggerKey = Key.E;

    private Rigidbody rb;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private float elapsed;
    private bool shaking;

    // Independent noise offsets so each axis shakes differently.
    private float seedX;
    private float seedY;
    private float seedZ;
    private float seedTiltX;
    private float seedTiltZ;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        seedX = Random.value * 100f;
        seedY = Random.value * 100f;
        seedZ = Random.value * 100f;
        seedTiltX = Random.value * 100f;
        seedTiltZ = Random.value * 100f;
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void Start()
    {
        CaptureRestPose();
        if (autoStart)
        {
            Invoke(nameof(TriggerEarthquake), startDelay);
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[triggerKey].wasPressedThisFrame)
        {
            TriggerEarthquake();
        }
    }

    private void FixedUpdate()
    {
        if (!shaking)
        {
            return;
        }

        elapsed += Time.fixedDeltaTime;
        if (elapsed >= duration)
        {
            StopEarthquake();
            return;
        }

        // Sine envelope: 0 -> 1 -> 0 across the duration for a smooth build-up and fade-out.
        float envelope = Mathf.Sin(Mathf.PI * (elapsed / duration));
        ShakeStrength = envelope;

        float t = Time.time * frequency;

        Vector3 offset = new Vector3(
            SignedNoise(seedX, t) * magnitude,
            SignedNoise(seedY, t) * verticalMagnitude,
            SignedNoise(seedZ, t) * magnitude) * envelope;

        Quaternion tilt = Quaternion.Euler(
            SignedNoise(seedTiltX, t) * tiltMagnitude * envelope,
            0f,
            SignedNoise(seedTiltZ, t) * tiltMagnitude * envelope);

        rb.MovePosition(restPosition + offset);
        rb.MoveRotation(tilt * restRotation);
    }

    /// <summary>Starts (or restarts) an earthquake from the ground's current rest pose.</summary>
    [ContextMenu("Trigger Earthquake")]
    public void TriggerEarthquake()
    {
        CaptureRestPose();
        elapsed = 0f;
        shaking = true;
    }

    /// <summary>Stops shaking and returns the ground to its rest pose.</summary>
    [ContextMenu("Stop Earthquake")]
    public void StopEarthquake()
    {
        shaking = false;
        ShakeStrength = 0f;
        rb.MovePosition(restPosition);
        rb.MoveRotation(restRotation);
    }

    private void CaptureRestPose()
    {
        restPosition = rb.position;
        restRotation = rb.rotation;
    }

    // Perlin noise remapped from [0,1] to [-1,1] so the shake moves both directions.
    private static float SignedNoise(float seed, float t)
    {
        return (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f;
    }
}
