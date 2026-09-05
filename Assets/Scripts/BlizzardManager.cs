using UnityEngine;

/// <summary>
/// Drives the blizzard. Wind gusts drive the snow sideways and shove the player; gusts also
/// close the visibility down to a whiteout. Cold builds the longer the player is exposed and
/// thaws under shelter, so damage escalates rather than ticking flat. Snow settles on the
/// ground over the course of the storm. Wind howls, louder in gusts.
///
/// Snow particles physically collide with the world (so a shelter roof actually blocks them),
/// and the player counts as exposed only while snow is still landing on them.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class BlizzardManager : MonoBehaviour
{
    [Header("Exposure")]
    [Tooltip("How long the player stays 'in the snow' after the last particle hit. " +
             "Acts as a grace period so stepping under a roof clears exposure.")]
    [SerializeField] float exposureGraceSeconds = 0.5f;

    [Tooltip("Seconds of continuous exposure to reach full cold.")]
    [SerializeField] float secondsToFreeze = 50f;

    [Tooltip("Seconds under shelter to thaw from full cold back to none.")]
    [SerializeField] float secondsToThaw = 20f;

    [Tooltip("Hazard damage multiplier at zero cold and at full cold.")]
    [SerializeField] Vector2 damageMultiplierRange = new Vector2(0.4f, 2.2f);

    [Header("Wind")]
    [Tooltip("Steady wind speed in m/s.")]
    [SerializeField] float baseWind = 6f;

    [Tooltip("Extra wind speed at the height of a gust.")]
    [SerializeField] float gustWind = 14f;

    [Tooltip("How much of the wind speed becomes player push (m/s per m/s of wind).")]
    [SerializeField] float playerPushFactor = 0.11f;

    [Tooltip("Fog distances are multiplied by this at the height of a gust (whiteout).")]
    [Range(0.1f, 1f)]
    [SerializeField] float whiteoutFogScale = 0.45f;

    [Header("Snow")]
    [Tooltip("Snow particles per second in still air and at full gust.")]
    [SerializeField] Vector2 emissionRange = new Vector2(600f, 1600f);

    [Tooltip("Seconds of storm for the ground to turn fully white.")]
    [SerializeField] float secondsToCoverGround = 100f;

    [SerializeField] Color snowGroundColor = new Color(0.90f, 0.93f, 0.97f);

    [Header("References")]
    [SerializeField] PlayerController playerController;
    [SerializeField] DisasterAtmosphere atmosphere;
    [Tooltip("Blue screen tint that deepens with cold. Optional.")]
    [SerializeField] DamageTint coldTint;
    [SerializeField] Terrain terrain;

    private ParticleSystem ps;
    private FirstPersonController fpc;
    private float lastSnowHitTime = float.NegativeInfinity;
    private float cold;
    private float gust;
    private Vector3 wind;
    private float windHeading;
    private float startTime;
    private AudioSource windLoop;
    private Material terrainRuntimeMaterial;
    private Material terrainOriginalMaterial;
    private Color terrainOriginalColor;
    private Vector3 emitterOffset;

    public float Cold => cold;
    public float Gust => gust;
    public Vector3 Wind => wind;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();

        if (playerController != null)
            fpc = playerController.GetComponent<FirstPersonController>();

        if (atmosphere == null)
            atmosphere = FindFirstObjectByType<DisasterAtmosphere>();

        if (terrain == null)
            terrain = FindFirstObjectByType<Terrain>();

        // The emitter was authored at a fixed height above the map; keep that height but follow the player.
        emitterOffset = new Vector3(0f, transform.position.y, 0f);
        windHeading = Random.Range(0f, 360f);
    }

    void OnEnable()
    {
        startTime = Time.time;
        cold = 0f;

        if (windLoop == null)
            windLoop = ProceduralAudio.AddLoop(gameObject, ProceduralAudio.Wind());
        windLoop.Play();

        // Snow settles on a runtime copy of the terrain material; the asset is never touched.
        if (terrain != null && terrain.materialTemplate != null && terrainRuntimeMaterial == null)
        {
            terrainOriginalMaterial = terrain.materialTemplate;
            terrainRuntimeMaterial = new Material(terrainOriginalMaterial);
            terrainOriginalColor = terrainRuntimeMaterial.HasProperty("_BaseColor")
                ? terrainRuntimeMaterial.GetColor("_BaseColor") : Color.white;
            terrain.materialTemplate = terrainRuntimeMaterial;
        }
    }

    void OnDisable()
    {
        // The blizzard is over (or never started): never leave the player stuck taking damage.
        lastSnowHitTime = float.NegativeInfinity;
        if (playerController != null)
        {
            playerController.touchingSnow = false;
            playerController.HazardDamageMultiplier = 1f;
        }

        if (fpc != null)
            fpc.ExternalVelocity = Vector3.zero;

        if (windLoop != null)
            windLoop.Stop();

        if (atmosphere != null)
            atmosphere.FogDistanceScale = 1f;

        if (terrain != null && terrainOriginalMaterial != null)
        {
            terrain.materialTemplate = terrainOriginalMaterial;
            Destroy(terrainRuntimeMaterial);
            terrainRuntimeMaterial = null;
        }
    }

    void Update()
    {
        if (playerController == null)
            return;

        UpdateWind();
        UpdateExposure();
        UpdateSnow();
    }

    private void UpdateWind()
    {
        // Gusts: squared Perlin noise gives long lulls and short, sharp peaks.
        float n = Mathf.PerlinNoise(Time.time * 0.13f, 17.3f);
        gust = n * n;

        windHeading += (Mathf.PerlinNoise(Time.time * 0.03f, 41f) - 0.5f) * 20f * Time.deltaTime;
        wind = Quaternion.Euler(0f, windHeading, 0f) * Vector3.forward * (baseWind + gustWind * gust);

        if (windLoop != null)
        {
            windLoop.volume = Mathf.Lerp(0.3f, 0.9f, gust);
            windLoop.pitch = Mathf.Lerp(0.85f, 1.2f, gust);
        }

        if (atmosphere != null)
        {
            atmosphere.FogDistanceScale = Mathf.Lerp(1f, whiteoutFogScale, gust);
            atmosphere.SetIntensity(Mathf.Clamp01((Time.time - startTime) / secondsToCoverGround), 8f);
        }
    }

    private void UpdateExposure()
    {
        bool exposed = Time.time - lastSnowHitTime < exposureGraceSeconds;
        playerController.touchingSnow = exposed;

        // Cold builds in the open and thaws under cover.
        cold = exposed
            ? Mathf.MoveTowards(cold, 1f, Time.deltaTime / Mathf.Max(secondsToFreeze, 0.1f))
            : Mathf.MoveTowards(cold, 0f, Time.deltaTime / Mathf.Max(secondsToThaw, 0.1f));

        playerController.HazardDamageMultiplier = Mathf.Lerp(damageMultiplierRange.x, damageMultiplierRange.y, cold);

        if (coldTint != null)
            coldTint.SetSustained(cold);

        // Wind shove: full in the open, a fraction under cover (it still leaks in).
        if (fpc != null)
        {
            float exposure = exposed ? 1f : 0.15f;
            // Swaying strength so it reads as buffeting rather than a conveyor belt.
            float buffet = 0.7f + 0.3f * Mathf.Sin(Time.time * 2.3f) * Mathf.Sin(Time.time * 0.7f);
            Vector3 push = wind * playerPushFactor * exposure * buffet;
            fpc.ExternalVelocity = Vector3.Lerp(fpc.ExternalVelocity, push, Time.deltaTime * 3f);
        }
    }

    private void UpdateSnow()
    {
        // Follow the player so the storm is everywhere they go, not a fixed patch of map. The
        // emitter sits upwind, so wind-driven snow arrives at the player instead of leaving them.
        var p = playerController.transform.position;
        var upwind = -new Vector3(wind.x, 0f, wind.z).normalized * Mathf.Min(wind.magnitude * 2f, 30f);
        transform.position = new Vector3(p.x + upwind.x, emitterOffset.y, p.z + upwind.z);

        var emission = ps.emission;
        emission.rateOverTime = Mathf.Lerp(emissionRange.x, emissionRange.y, gust);

        // Wind drives the snow sideways. World space so the emitter following the player doesn't matter.
        // All three axes must share a mode; the prefab authors them as random-between-two-constants.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.7f, wind.x * 1.3f);
        vel.y = new ParticleSystem.MinMaxCurve(-2f, -1f);
        vel.z = new ParticleSystem.MinMaxCurve(wind.z * 0.7f, wind.z * 1.3f);

        if (terrainRuntimeMaterial != null && terrainRuntimeMaterial.HasProperty("_BaseColor"))
        {
            float cover = Mathf.Clamp01((Time.time - startTime) / Mathf.Max(secondsToCoverGround, 0.1f));
            terrainRuntimeMaterial.SetColor("_BaseColor", Color.Lerp(terrainOriginalColor, snowGroundColor, cover));
        }
    }

    /// <summary>
    /// Sent by the collision module (Send Collision Messages) with the GameObject that was hit.
    /// Unlike the trigger module this identifies the actual collider, so it can't confuse the
    /// player with terrain the way indexing into the trigger collider list did.
    /// </summary>
    private void OnParticleCollision(GameObject other)
    {
        // The hit may land on the Player root (CharacterController) or the Capsule child.
        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        lastSnowHitTime = Time.time;
    }
}
