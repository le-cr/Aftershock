using UnityEngine;

/// <summary>
/// Shakes a camera by offsetting its local position with smooth Perlin noise that decays
/// over the shake's lifetime.
///
/// FirstPersonController rewrites the camera's localRotation every frame from the mouse look,
/// so the rotational part of the shake is composed on top of that rotation in LateUpdate,
/// after the look code has run, and is naturally discarded when the controller writes again.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [Header("Constants")]
    [Tooltip("Seconds a shake lasts when triggered with no explicit duration.")]
    [SerializeField] float defaultDuration = 10f;

    [Tooltip("Peak offset in metres when triggered with no explicit magnitude.")]
    [SerializeField] float defaultMagnitude = 0.62f;

    [Tooltip("How fast the shake oscillates. Higher reads as a sharper rattle.")]
    [SerializeField] float frequency = 16f;

    [Tooltip("Degrees of camera roll/pitch wobble per metre of positional magnitude.")]
    [SerializeField] float rotationDegreesPerMetre = 12f;

    [Tooltip("The rotational wobble is slower than the positional rattle: the ground heaves, the eye jitters.")]
    [SerializeField] float rotationFrequencyScale = 0.45f;

    private Vector3 restLocalPosition;
    private float startTime;
    private float duration;
    private float magnitude;
    private bool isShaking;
    private float noiseSeed;
    private Quaternion lastWrittenRotation;
    private Quaternion lastBaseRotation;
    private bool hasBase;

    public bool IsShaking => isShaking;

    /// <summary>Current positional shake amplitude in metres, decay included. 0 when still.</summary>
    public float CurrentIntensity { get; private set; }

    void Awake()
    {
        restLocalPosition = transform.localPosition;
    }

    public void Shake()
    {
        Shake(defaultDuration, defaultMagnitude);
    }

    public void Shake(float shakeDuration, float shakeMagnitude)
    {
        duration = Mathf.Max(shakeDuration, 0.01f);
        magnitude = shakeMagnitude;
        startTime = Time.time;
        noiseSeed = Random.value * 1000f;
        isShaking = true;
    }

    public void StopShake()
    {
        isShaking = false;
        CurrentIntensity = 0f;
        hasBase = false;
        transform.localPosition = restLocalPosition;
    }

    void LateUpdate()
    {
        if (!isShaking)
            return;

        float elapsed = (Time.time - startTime) / duration;
        if (elapsed >= 1f)
        {
            StopShake();
            return;
        }

        // Ease the rattle out so the ground "settles" rather than stopping dead.
        float damper = 1f - Mathf.Clamp01(elapsed);
        float t = Time.time * frequency;

        // Perlin sampled on separate rows gives smooth, uncorrelated motion per axis.
        Vector3 offset = new Vector3(
            Mathf.PerlinNoise(noiseSeed, t) * 2f - 1f,
            Mathf.PerlinNoise(noiseSeed + 100f, t) * 2f - 1f,
            Mathf.PerlinNoise(noiseSeed + 200f, t) * 2f - 1f);

        CurrentIntensity = magnitude * damper;
        transform.localPosition = restLocalPosition + offset * CurrentIntensity;

        // Rotational wobble. The controller normally rewrites localRotation each Update; if it
        // didn't this frame (no mouse), fall back to the base we saw last time so the wobble
        // never accumulates.
        Quaternion baseRot = transform.localRotation;
        if (hasBase && baseRot == lastWrittenRotation)
            baseRot = lastBaseRotation;

        float rt = t * rotationFrequencyScale;
        float deg = rotationDegreesPerMetre * CurrentIntensity;
        var wobble = Quaternion.Euler(
            (Mathf.PerlinNoise(noiseSeed + 300f, rt) * 2f - 1f) * deg,
            0f,
            (Mathf.PerlinNoise(noiseSeed + 400f, rt) * 2f - 1f) * deg * 1.4f);

        lastBaseRotation = baseRot;
        transform.localRotation = baseRot * wobble;
        lastWrittenRotation = transform.localRotation;
        hasBase = true;
    }
}
