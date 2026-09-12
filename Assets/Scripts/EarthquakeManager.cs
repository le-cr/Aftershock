using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the earthquake for the whole survival window. A short foreshock rattles first, then the
/// main shock, then aftershocks that come less often and weaker as time goes on (Omori's law),
/// with the occasional big one. Buildings are held back rather than all dropped at once, and
/// only tremors strong enough bring one down.
///
/// While the ground shakes: a low rumble scaled to the shaking, dust lifts off the ground around
/// the player, the player stumbles, and each collapse throws up a burst of dust.
/// Driven by DisasterManager rather than a key press.
/// </summary>
public class EarthquakeManager : MonoBehaviour
{
    [Header("Main shock")]
    [Tooltip("Seconds of faint pre-tremor before the main shock. Real quakes announce themselves with P-waves.")]
    [SerializeField] float foreshockSeconds = 3.5f;

    [Tooltip("Seconds the camera shakes on the main shock. The shake decays to nothing over this time.")]
    [SerializeField] float shakeDuration = 10f;

    [Tooltip("Peak camera offset in metres on the main shock.")]
    [SerializeField] float shakeMagnitude = 0.62f;

    [Tooltip("Seconds of shaking before the first building gives way.")]
    [SerializeField] float leadIn = 0.45f;

    [Tooltip("Buildings brought down by the main shock.")]
    [SerializeField] int initialCollapseCount = 4;

    [Header("Aftershocks")]
    [Tooltip("Gap before the first aftershock, in seconds. Later gaps grow from here.")]
    [SerializeField] float firstInterval = 8f;

    [Tooltip("Each successive gap grows by this fraction (Omori decay of aftershock rate).")]
    [SerializeField] float intervalGrowth = 0.22f;

    [Tooltip("Fallback quake window when triggered without an explicit duration. DisasterManager passes the survival time instead.")]
    [SerializeField] float defaultDuration = 120f;

    [Tooltip("Seconds an aftershock shakes for.")]
    [SerializeField] float aftershockShakeDuration = 5f;

    [Tooltip("Typical aftershock strength as a fraction of the main shock, before decay and randomness.")]
    [Range(0f, 1f)]
    [SerializeField] float aftershockMagnitudeScale = 0.78f;

    [Tooltip("Chance any aftershock is a big one, nearly as strong as the main shock.")]
    [Range(0f, 1f)]
    [SerializeField] float bigAftershockChance = 0.28f;

    [Tooltip("An aftershock must reach this fraction of the main shock to bring a building down.")]
    [Range(0f, 1f)]
    [SerializeField] float collapseThreshold = 0.35f;

    [Header("Feel")]
    [Tooltip("Horizontal stumble pushed onto the player per metre of shake, in m/s.")]
    [SerializeField] float stumblePerMetre = 5.5f;

    [Tooltip("Dust particles per second around the player at full shake.")]
    [SerializeField] float dustRateAtFullShake = 90f;

    [Tooltip("Dust particles thrown up when a building collapses.")]
    [SerializeField] int collapseDustBurst = 130;

    [Tooltip("Particle material for the dust. Leave empty for no dust.")]
    [SerializeField] Material dustMaterial;

    [SerializeField] Color dustColor = new Color(0.62f, 0.55f, 0.45f, 0.28f);

    [Header("Constants")]
    [Tooltip("Seconds between one building coming down and the next within a single tremor.")]
    [SerializeField] float collapseStagger = 0.45f;

    [Header("References")]
    [Tooltip("Leave empty to collapse every BuildingCollapse in the scene, so new buildings need no rewiring.")]
    [SerializeField] BuildingCollapse[] buildings;

    [Tooltip("Leave empty to shake every CameraShake in the scene.")]
    [SerializeField] CameraShake[] cameraShakes;

    [SerializeField] PlayerController playerController;
    [SerializeField] DisasterAtmosphere atmosphere;

    private bool triggered;
    private int quakeCount;
    private readonly List<BuildingCollapse> standing = new List<BuildingCollapse>();
    private FirstPersonController fpc;
    private AudioSource rumble;
    private ParticleSystem dust;
    private float stumbleSeed;
    private float currentShake;      // 0-1 of main-shock magnitude, this frame

    public bool HasTriggered => triggered;

    /// <summary>Number of tremors so far, main shock included. Useful for testing.</summary>
    public int QuakeCount => quakeCount;

    public int StandingBuildings => standing.Count;

    void Awake()
    {
        if (buildings == null || buildings.Length == 0)
            buildings = FindObjectsByType<BuildingCollapse>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (cameraShakes == null || cameraShakes.Length == 0)
            cameraShakes = FindObjectsByType<CameraShake>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>();

        if (playerController != null)
            fpc = playerController.GetComponent<FirstPersonController>();

        if (atmosphere == null)
            atmosphere = FindFirstObjectByType<DisasterAtmosphere>();

        stumbleSeed = Random.value * 100f;
    }

    /// <summary>Start the quake using the inspector-authored window.</summary>
    public void TriggerEarthquake()
    {
        TriggerEarthquake(defaultDuration);
    }

    /// <summary>
    /// Start the quake and keep it going for <paramref name="duration"/> seconds.
    /// DisasterManager passes the survival time so tremors last exactly as long as the run.
    /// </summary>
    public void TriggerEarthquake(float duration)
    {
        if (triggered)
            return;

        triggered = true;

        standing.Clear();
        foreach (var b in buildings)
        {
            if (b != null && !b.HasCollapsed)
                standing.Add(b);
        }

        // Randomise collapse order so the same building isn't always first.
        for (int i = standing.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (standing[i], standing[j]) = (standing[j], standing[i]);
        }

        if (rumble == null)
        {
            rumble = ProceduralAudio.AddLoop(gameObject, ProceduralAudio.Rumble());
            rumble.pitch = 0.8f;
        }
        rumble.Play();

        if (dustMaterial != null && dust == null)
            dust = ParticleFactory.Dust(transform, dustMaterial, dustColor);

        StartCoroutine(Run(duration));
    }

    private IEnumerator Run(float duration)
    {
        float endTime = Time.time + duration;

        // Foreshock: a hard, fast rattle that tells the player to get out of the buildings.
        quakeCount++;
        ShakeAll(foreshockSeconds, shakeMagnitude * 0.28f);
        yield return new WaitForSeconds(foreshockSeconds);

        // Main shock.
        ShakeAll(shakeDuration, shakeMagnitude);
        yield return new WaitForSeconds(leadIn);
        yield return CollapseNext(initialCollapseCount);

        // Aftershocks, until the survival window closes. Gaps grow and strength fades, with
        // the occasional big one so the player can never fully relax.
        float interval = firstInterval;
        int k = 0;
        while (Time.time < endTime)
        {
            yield return new WaitForSeconds(interval * Random.Range(0.7f, 1.3f));
            interval *= 1f + intervalGrowth;
            k++;

            if (Time.time >= endTime)
                break;

            float decay = 1f / (1f + 0.1f * k);
            float scale = aftershockMagnitudeScale * decay * Random.Range(0.6f, 1.15f);
            bool bigOne = Random.value < bigAftershockChance;
            if (bigOne)
                scale = Random.Range(0.85f, 1.05f);

            quakeCount++;
            ShakeAll(aftershockShakeDuration * Mathf.Lerp(0.75f, 1.4f, scale), shakeMagnitude * scale);

            if (scale >= collapseThreshold)
                yield return CollapseNext(bigOne ? 2 : 1);
        }
    }

    void Update()
    {
        if (!triggered)
            return;

        // Shake intensity this frame, from whichever camera is shaking hardest.
        float metres = 0f;
        foreach (var s in cameraShakes)
            if (s != null) metres = Mathf.Max(metres, s.CurrentIntensity);
        currentShake = Mathf.Clamp01(metres / Mathf.Max(shakeMagnitude, 0.001f));

        // Rumble follows the shaking; deeper on the strong ones.
        if (rumble != null)
        {
            rumble.volume = Mathf.Lerp(rumble.volume, currentShake * 0.9f, Time.deltaTime * 6f);
            rumble.pitch = Mathf.Lerp(0.95f, 0.6f, currentShake);
        }

        // Stumble: a slow, wandering shove, so footing is unreliable rather than jittery.
        if (fpc != null)
        {
            float t = Time.time * 1.7f;
            var dir = new Vector3(Mathf.PerlinNoise(stumbleSeed, t) - 0.5f, 0f, Mathf.PerlinNoise(stumbleSeed + 50f, t) - 0.5f) * 2f;
            fpc.ExternalVelocity = dir * (stumblePerMetre * metres);
        }

        // Dust lifts off the ground around the player while it shakes.
        if (dust != null && playerController != null)
        {
            var p = playerController.transform.position;
            dust.transform.position = new Vector3(p.x, p.y - 0.8f, p.z);
            ParticleFactory.SetRate(dust, dustRateAtFullShake * currentShake);
        }

        // Air fills with dust as the quake wears on.
        if (atmosphere != null)
            atmosphere.SetIntensity(Mathf.Clamp01((buildings.Length - standing.Count) / Mathf.Max(buildings.Length, 1f)), 12f);
    }

    private void ShakeAll(float duration, float magnitude)
    {
        foreach (var shake in cameraShakes)
        {
            if (shake != null)
                shake.Shake(duration, magnitude);
        }
    }

    /// <summary>Bring down up to <paramref name="count"/> of the buildings still standing.</summary>
    private IEnumerator CollapseNext(int count)
    {
        for (int i = 0; i < count && standing.Count > 0; i++)
        {
            var building = standing[0];
            standing.RemoveAt(0);

            if (building == null)
                continue;

            var renderer = building.GetComponent<Renderer>();
            var at = renderer != null ? renderer.bounds : new Bounds(building.transform.position, Vector3.one * 6f);

            building.Collapse();
            BurstDust(at);

            yield return new WaitForSeconds(collapseStagger);
        }
    }

    /// <summary>Throw a cloud of dust up from the footprint of a collapsing building.</summary>
    private void BurstDust(Bounds footprint)
    {
        if (dust == null)
            return;

        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < collapseDustBurst; i++)
        {
            var p = new Vector3(
                Random.Range(footprint.min.x, footprint.max.x),
                footprint.min.y + 0.3f,
                Random.Range(footprint.min.z, footprint.max.z));
            emit.position = p;
            emit.velocity = new Vector3(Random.Range(-2f, 2f), Random.Range(1f, 4f), Random.Range(-2f, 2f));
            emit.startSize = Random.Range(3f, 7f);
            emit.startLifetime = Random.Range(3f, 6f);
            dust.Emit(emit, 1);
        }
    }

    void OnDisable()
    {
        if (fpc != null)
            fpc.ExternalVelocity = Vector3.zero;
    }
}
