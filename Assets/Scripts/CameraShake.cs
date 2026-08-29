using UnityEngine;

/// <summary>
/// Shakes a camera by offsetting its local position with smooth Perlin noise that decays
/// over the shake's lifetime.
///
/// Position only, deliberately: FirstPersonController rewrites the camera's localRotation
/// every frame from the mouse look, so a rotational shake would be overwritten. Runs in
/// LateUpdate so the offset is applied after that look code has run.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [Header("Constants")]
    [Tooltip("Seconds a shake lasts when triggered with no explicit duration.")]
    [SerializeField] float defaultDuration = 8f;

    [Tooltip("Peak offset in metres when triggered with no explicit magnitude.")]
    [SerializeField] float defaultMagnitude = 0.35f;

    [Tooltip("How fast the shake oscillates. Higher reads as a sharper rattle.")]
    [SerializeField] float frequency = 14f;

    private Vector3 restLocalPosition;
    private float startTime;
    private float duration;
    private float magnitude;
    private bool isShaking;
    private float noiseSeed;

    public bool IsShaking => isShaking;

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

        transform.localPosition = restLocalPosition + offset * (magnitude * damper);
    }
}
