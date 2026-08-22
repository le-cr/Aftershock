using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Earthquake-style camera shake, toggled with the C key. Follows the classic
/// cache-the-origin / offset / restore structure, but drives the offset with Perlin noise
/// rather than a fresh random value each frame: at earthquake magnitudes, per-frame random
/// jitter reads as video static instead of ground rumble.
///
/// Lives on a scene object rather than on a camera, because the active camera changes at
/// runtime (HelicopterInteraction swaps between the player camera and the helicopter chase
/// camera by toggling Camera.enabled). The shake follows whichever one is live.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [Header("Shake")]
    [Tooltip("Peak positional offset, in metres, once the shake has fully ramped in.")]
    [SerializeField] private float magnitude = 0.15f;

    [Tooltip("How fast the noise scrolls, i.e. the rumble rate. Higher is more frantic.")]
    [SerializeField] private float frequency = 18f;

    [Tooltip("Scales the vertical axis. Real quakes shove sideways far more than up and down, so keep this below 1.")]
    [Range(0f, 1f)]
    [SerializeField] private float verticalScale = 0.5f;

    [Tooltip("Seconds to fade the shake in when toggled on and out when toggled off, so it never pops.")]
    [SerializeField] private float rampTime = 0.35f;

    /// <summary>True while the shake is toggled on. It may still be ramping in or out.</summary>
    public bool IsShaking => shaking;

    private bool shaking;
    private float intensity;

    private Transform target;
    private Vector3 targetOrigin;

    private float seedX;
    private float seedY;
    private float seedZ;

    private void Awake()
    {
        // Randomised per session so repeat quakes don't replay the identical motion.
        seedX = Random.value * 1000f;
        seedY = Random.value * 1000f;
        seedZ = Random.value * 1000f;
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.cKey.wasPressedThisFrame)
        {
            Toggle();
        }
    }

    /// <summary>
    /// Runs in LateUpdate to stay clear of the two scripts that also drive these transforms:
    /// FirstPersonController writes the player camera's localRotation in Update, and
    /// FollowTargetCamera writes the helicopter rig's world position in FixedUpdate. Neither
    /// touches localPosition, which is what we drive here.
    /// </summary>
    private void LateUpdate()
    {
        float rampRate = rampTime > 0f ? Time.deltaTime / rampTime : 1f;
        intensity = Mathf.MoveTowards(intensity, shaking ? 1f : 0f, rampRate);

        if (!shaking && intensity <= 0f)
        {
            // Fully faded out: put the camera back exactly where it started and stop touching it.
            ReleaseTarget();
            return;
        }

        AcquireTarget(ResolveActiveCamera());

        if (target == null)
        {
            return;
        }

        target.localPosition = targetOrigin + SampleNoise() * intensity;
    }

    /// <summary>Starts the shake if it is stopped, stops it if it is running.</summary>
    [ContextMenu("Toggle Shake")]
    public void Toggle()
    {
        SetShaking(!shaking);
    }

    /// <summary>Starts or stops the shake. Ramping is handled either way, so this is safe to spam.</summary>
    public void SetShaking(bool value)
    {
        shaking = value;
    }

    private Vector3 SampleNoise()
    {
        float t = Time.time * frequency;

        // Three separately seeded channels. Sampling one noise field along its diagonal, as is
        // often done, leaves the axes visibly correlated and the shake looks like it is sliding
        // along one line rather than rattling.
        return new Vector3(
            (Mathf.PerlinNoise(seedX + t, 0f) - 0.5f) * 2f,
            (Mathf.PerlinNoise(seedY + t, 0f) - 0.5f) * 2f * verticalScale,
            (Mathf.PerlinNoise(seedZ + t, 0f) - 0.5f) * 2f) * magnitude;
    }

    /// <summary>
    /// Points the shake at a camera, restoring whatever it was shaking before. Switching cameras
    /// mid-quake (entering or leaving the helicopter) would otherwise strand the old one at an offset.
    /// </summary>
    private void AcquireTarget(Camera camera)
    {
        Transform wanted = camera != null ? camera.transform : null;

        if (wanted == target)
        {
            return;
        }

        ReleaseTarget();

        target = wanted;
        if (target != null)
        {
            targetOrigin = target.localPosition;
        }
    }

    /// <summary>Restores the current target to its captured rest position and forgets it.</summary>
    private void ReleaseTarget()
    {
        if (target != null)
        {
            target.localPosition = targetOrigin;
            target = null;
        }
    }

    /// <summary>
    /// Finds the camera currently rendering to the screen. HelicopterInteraction switches cameras
    /// by toggling Camera.enabled rather than SetActive, so the MainCamera tag alone is ambiguous:
    /// both cameras carry it and Camera.main can hand back the disabled one. The fallback picks the
    /// highest-depth screen-targeting camera, which is the one the player is actually looking
    /// through; Camera.allCameras order is not documented as stable, so it is not relied on.
    /// </summary>
    private static Camera ResolveActiveCamera()
    {
        Camera main = Camera.main;
        if (IsLiveCamera(main))
        {
            return main;
        }

        Camera best = null;
        foreach (Camera candidate in Camera.allCameras)
        {
            if (!IsLiveCamera(candidate))
            {
                continue;
            }

            if (best == null || candidate.depth > best.depth)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>A camera is live if it is on, in the scene, and drawing to the screen.</summary>
    private static bool IsLiveCamera(Camera camera)
    {
        return camera != null
            && camera.enabled
            && camera.gameObject.activeInHierarchy
            && camera.targetTexture == null;
    }
}
