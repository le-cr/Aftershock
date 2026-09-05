using System.Collections;
using UnityEngine;

/// <summary>
/// The flood. Rain sets in during the warning and the water then rises in surges rather than at
/// a fixed rate. Once the player is in it they are carried by a current, float rather than walk
/// when it gets deep, and drown when it closes over their head. Lightning flashes the sun and is
/// followed by thunder delayed by distance.
///
/// The rising water is this GameObject: a large plane whose Y is the surface height.
/// </summary>
public class Flood : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerController playerController;
    [SerializeField] GameObject waterTintPanel;
    [SerializeField] DisasterAtmosphere atmosphere;

    [Tooltip("Particle material for the rain streaks. Leave empty for no rain.")]
    [SerializeField] Material rainMaterial;

    [Header("Rise")]
    [SerializeField] float floodSpeed = 0.5f;
    [SerializeField] float maxFloodHeight = 10f;

    [Tooltip("The rise accelerates by this factor over the flood's life, so it starts gentle and ends urgent.")]
    [SerializeField] float endSpeedMultiplier = 2.5f;

    [Tooltip("Seconds over which the rise ramps from base speed to base speed x endSpeedMultiplier.")]
    [SerializeField] float rampSeconds = 90f;

    [Tooltip("How much the rise speed swells and lulls in surges. 0 = steady, 1 = up to double / down to nothing.")]
    [Range(0f, 1f)]
    [SerializeField] float surgeAmount = 0.6f;

    [Tooltip("Seconds per surge cycle.")]
    [SerializeField] float surgePeriod = 14f;

    [Header("Water")]
    [Tooltip("Metres per second the current carries a fully submerged player.")]
    [SerializeField] float currentStrength = 1.4f;

    [Tooltip("Seconds it takes the current to swing to a new heading.")]
    [SerializeField] float currentWanderSeconds = 25f;

    [Tooltip("Surface chop height in metres, felt as a bob while swimming.")]
    [SerializeField] float chopHeight = 0.12f;

    [Header("Storm")]
    [Tooltip("Rain particles per second at full storm.")]
    [SerializeField] float rainRate = 1400f;

    [Tooltip("Rain during the warning phase, as a fraction of the full storm.")]
    [Range(0f, 1f)]
    [SerializeField] float warningRainFraction = 0.35f;

    [SerializeField] float lightningMinGap = 10f;
    [SerializeField] float lightningMaxGap = 28f;

    private bool isFlooding;
    private bool stormActive;
    private float floodStartTime;
    private float stormStartTime;
    private float currentHeading;
    private float targetHeading;
    private float headingChangeTime;
    private Vector3 wind;
    private float startHeight;

    private FirstPersonController fpc;
    private ParticleSystem rain;
    private AudioSource rainLoop;
    private AudioSource thunderSource;
    private AudioClip thunderClip;
    private Coroutine lightning;
    private Vector3 rainWind;

    public bool IsFlooding => isFlooding;
    public bool StormActive => stormActive;
    public float SurfaceHeight => transform.position.y;

    void Awake()
    {
        startHeight = transform.position.y;

        if (playerController != null)
            fpc = playerController.GetComponent<FirstPersonController>();

        if (atmosphere == null)
            atmosphere = FindFirstObjectByType<DisasterAtmosphere>();

        currentHeading = targetHeading = Random.Range(0f, 360f);
    }

    /// <summary>Warning phase: the sky closes in and the rain starts, but the water is still.</summary>
    public void BeginStorm()
    {
        if (stormActive)
            return;

        stormActive = true;
        stormStartTime = Time.time;

        if (rainMaterial != null && rain == null)
            rain = ParticleFactory.Rain(transform.parent != null ? transform.parent : transform, rainMaterial);

        if (rainLoop == null)
            rainLoop = ProceduralAudio.AddLoop(gameObject, ProceduralAudio.Rain());
        rainLoop.Play();

        if (thunderSource == null)
        {
            thunderClip = ProceduralAudio.Thunder();
            thunderSource = gameObject.AddComponent<AudioSource>();
            thunderSource.playOnAwake = false;
            thunderSource.spatialBlend = 0f;
        }

        if (lightning == null)
            lightning = StartCoroutine(LightningLoop());
    }

    /// <summary>Start raising the water. Called by DisasterManager when Flood is the chosen disaster.</summary>
    public void BeginFlood()
    {
        BeginStorm();
        isFlooding = true;
        floodStartTime = Time.time;
    }

    public void StopFlood()
    {
        isFlooding = false;
    }

    void Update()
    {
        UpdateRise();
        UpdateStorm();
        UpdatePlayerInWater();
    }

    private void UpdateRise()
    {
        if (transform.position.y >= maxFloodHeight)
            isFlooding = false;

        if (!isFlooding)
            return;

        float ramp = Mathf.Clamp01((Time.time - floodStartTime) / Mathf.Max(rampSeconds, 0.01f));
        float speed = floodSpeed * Mathf.Lerp(1f, endSpeedMultiplier, ramp);

        // Surges: the water comes in pulses, never receding. Two offset sines keep it irregular.
        float phase = (Time.time - floodStartTime) / Mathf.Max(surgePeriod, 0.1f) * Mathf.PI * 2f;
        float surge = 0.6f * Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase * 1.7f + 0.8f);
        speed *= 1f + surgeAmount * surge;

        transform.Translate(Vector3.up * Mathf.Max(speed, 0f) * Time.deltaTime, Space.World);
    }

    private void UpdateStorm()
    {
        if (!stormActive)
            return;

        // Intensity: warning rain builds over the first 20 s, the full storm lands with the flood.
        float build = Mathf.Clamp01((Time.time - stormStartTime) / 20f);
        float intensity = isFlooding ? 1f : warningRainFraction * build;

        // A slow, wandering gust bends the rain and pushes on the player once they're swimming.
        float gust = Mathf.PerlinNoise(Time.time * 0.08f, 3.7f);
        float windAngle = Mathf.PerlinNoise(Time.time * 0.02f, 9.1f) * 360f;
        wind = Quaternion.Euler(0f, windAngle, 0f) * Vector3.forward * Mathf.Lerp(2f, 9f, gust) * intensity;

        if (rain != null && playerController != null)
        {
            var p = playerController.transform.position;
            rain.transform.position = new Vector3(p.x, Mathf.Max(p.y, SurfaceHeight), p.z);
            ParticleFactory.SetRate(rain, rainRate * intensity);
            ParticleFactory.SetWind(rain, wind * 0.6f);
        }

        if (rainLoop != null)
        {
            // Rain is muffled under water.
            float under = fpc != null ? Mathf.Clamp01((fpc.Submersion - 0.85f) / 0.15f) : 0f;
            rainLoop.volume = Mathf.Lerp(rainLoop.volume, 0.55f * intensity * (1f - 0.85f * under), Time.deltaTime * 2f);
            rainLoop.pitch = Mathf.Lerp(1f, 0.6f, under);
        }

        if (atmosphere != null && isFlooding)
        {
            // Sky darkens further as the water climbs.
            float climb = Mathf.InverseLerp(startHeight, maxFloodHeight, SurfaceHeight);
            atmosphere.SetIntensity(climb, 10f);
        }
    }

    private void UpdatePlayerInWater()
    {
        if (fpc == null)
            return;

        if (!stormActive && !isFlooding && SurfaceHeight <= startHeight)
        {
            fpc.WaterSurfaceY = float.NegativeInfinity;
            return;
        }

        // Surface chop: a gentle bob so a floating player isn't a statue on glass.
        float chop = Mathf.Sin(Time.time * 1.3f) * 0.6f + Mathf.Sin(Time.time * 2.1f + 1f) * 0.4f;
        fpc.WaterSurfaceY = SurfaceHeight + chop * chopHeight;

        // Current: strongest when fully submerged, nothing when just wet ankles. It wanders
        // slowly so the player has to keep correcting rather than lean once.
        if (Time.time > headingChangeTime)
        {
            targetHeading = currentHeading + Random.Range(-70f, 70f);
            headingChangeTime = Time.time + currentWanderSeconds * Random.Range(0.6f, 1.4f);
        }
        currentHeading = Mathf.MoveTowardsAngle(currentHeading, targetHeading, 12f * Time.deltaTime);

        float depthFactor = Mathf.Clamp01((fpc.Submersion - 0.25f) / 0.6f);
        Vector3 current = Quaternion.Euler(0f, currentHeading, 0f) * Vector3.forward * currentStrength * depthFactor;
        Vector3 push = current + wind * 0.05f * depthFactor;
        fpc.ExternalVelocity = Vector3.Lerp(fpc.ExternalVelocity, push, Time.deltaTime * 2f);
    }

    private IEnumerator LightningLoop()
    {
        while (stormActive)
        {
            yield return new WaitForSeconds(Random.Range(lightningMinGap, lightningMaxGap) * (isFlooding ? 0.7f : 1f));

            // Distance sets both how bright the flash is and how long the thunder takes to arrive.
            float distanceKm = Random.Range(0.5f, 6f);
            float brightness = Mathf.Lerp(2.5f, 0.8f, distanceKm / 6f);

            // Double-flash, like real strokes.
            yield return Flash(brightness, 0.08f);
            yield return new WaitForSeconds(0.07f);
            yield return Flash(brightness * 0.7f, 0.12f);

            yield return new WaitForSeconds(distanceKm / 0.343f * 0.35f);   // sound lag, compressed for pacing

            if (thunderSource != null)
            {
                thunderSource.pitch = Random.Range(0.75f, 1.1f);
                thunderSource.PlayOneShot(thunderClip, Mathf.Lerp(0.9f, 0.35f, distanceKm / 6f));
            }
        }

        lightning = null;
    }

    private IEnumerator Flash(float brightness, float seconds)
    {
        if (atmosphere == null)
            yield break;

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            atmosphere.SunFlash = brightness * (1f - t / seconds);
            yield return null;
        }
        atmosphere.SunFlash = 0f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("WaterDetection"))
        {
            playerController.inWater = true;
            waterTintPanel.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("WaterDetection"))
        {
            playerController.inWater = false;
            waterTintPanel.SetActive(false);
        }
    }

    void OnDisable()
    {
        if (fpc != null)
        {
            fpc.WaterSurfaceY = float.NegativeInfinity;
            fpc.ExternalVelocity = Vector3.zero;
        }
    }
}
