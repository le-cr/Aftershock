using UnityEngine;

/// <summary>
/// Drives the blizzard. Wind gusts drive the snow sideways and shove the player; gusts also
/// close the visibility down to a whiteout. Cold builds the longer the player is exposed and
/// thaws under shelter, so damage escalates rather than ticking flat. Snow settles on the
/// ground over the course of the storm. Wind howls, louder in gusts.
///
/// Exposure is decided two ways, either of which counts: an upward cover check (nothing solid
/// between the player's head and the sky), and snow particles physically landing on them. The
/// particles collide with the world, so a roof blocks them, but an open-sided lean-to still leaks
/// when the wind drives snow in sideways. Once the player is hypothermic, cover alone no longer
/// stops the damage: they keep losing health until they have thawed below the threshold.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class BlizzardManager : MonoBehaviour
{
    [Header("Exposure")]
    [Tooltip("How long the player stays 'in the snow' after the last particle hit. " +
             "Acts as a grace period so stepping under a roof clears exposure.")]
    [SerializeField] float exposureGraceSeconds = 1f;

    [Tooltip("The player is under cover when a collider blocks the sky within this height above their head. " +
             "Rays also lean into the wind, so a roof has to actually shield the windward side.")]
    [SerializeField] float coverCheckHeight = 40f;

    [Tooltip("Layers that count as cover. Triggers are always ignored.")]
    [SerializeField] LayerMask coverMask = ~0;

    [Tooltip("Seconds of continuous exposure to reach full cold.")]
    [SerializeField] float secondsToFreeze = 60f;

    [Tooltip("Seconds under shelter to thaw from full cold back to none.")]
    [SerializeField] float secondsToThaw = 30f;

    [Tooltip("Hazard damage multiplier at zero cold and at full cold.")]
    [SerializeField] Vector2 damageMultiplierRange = new Vector2(0.15f, 1.2f);

    [Tooltip("Cold level above which the player is hypothermic: damage continues under cover, " +
             "scaled by hypothermiaCoverDamageScale, until they thaw back below this.")]
    [Range(0f, 1f)]
    [SerializeField] float hypothermiaThreshold = 0.75f;

    [Tooltip("Fraction of the normal damage rate a hypothermic player still takes under cover.")]
    [Range(0f, 1f)]
    [SerializeField] float hypothermiaCoverDamageScale = 0.4f;

    [Header("Wind")]
    [Tooltip("Steady wind speed in m/s.")]
    [SerializeField] float baseWind = 6f;

    [Tooltip("Extra wind speed at the height of a gust.")]
    [SerializeField] float gustWind = 14f;

    [Tooltip("How much of the wind speed becomes player push (m/s per m/s of wind).")]
    [SerializeField] float playerPushFactor = 0.12f;

    [Tooltip("Fog distances are multiplied by this at the height of a gust (whiteout).")]
    [Range(0.1f, 1f)]
    [SerializeField] float whiteoutFogScale = 0.45f;

    [Header("Snow")]
    [Tooltip("Snow particles per second in still air and at full gust.")]
    [SerializeField] Vector2 emissionRange = new Vector2(900f, 2000f);

    [Tooltip("Metres above the player the snow is emitted from. Kept low so the snow reaches the " +
             "ground well inside its lifetime and lands densely enough to register hits.")]
    [SerializeField] float emitterHeight = 14f;

    [Tooltip("Mean downward speed of the snow in m/s: emitter start speed plus the velocity-over-lifetime " +
             "fall. Used to place the emitter upwind so the snow lands on the player rather than past them.")]
    [SerializeField] float meanFallSpeed = 6.5f;

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
    private bool underCover = true;
    private bool hypothermic;
    private readonly RaycastHit[] coverHits = new RaycastHit[8];

    public float Cold => cold;
    public bool UnderCover => underCover;
    public bool Hypothermic => hypothermic;
    public bool Exposed { get; private set; }
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

        windHeading = Random.Range(0f, 360f);
    }

    void OnEnable()
    {
        startTime = Time.time;
        cold = 0f;
        hypothermic = false;
        Exposed = false;

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
        bool snowHit = Time.time - lastSnowHitTime < exposureGraceSeconds;
        underCover = HasOverheadCover();
        bool exposed = !underCover || snowHit;
        Exposed = exposed;

        // Cold builds in the open and thaws under cover.
        cold = exposed
            ? Mathf.MoveTowards(cold, 1f, Time.deltaTime / Mathf.Max(secondsToFreeze, 0.1f))
            : Mathf.MoveTowards(cold, 0f, Time.deltaTime / Mathf.Max(secondsToThaw, 0.1f));

        // Hypothermia latches on at the threshold and only lets go once well below it, so the
        // damage doesn't flicker on and off right at the line.
        if (cold >= hypothermiaThreshold)
            hypothermic = true;
        else if (cold < hypothermiaThreshold - 0.25f)
            hypothermic = false;

        float multiplier = Mathf.Lerp(damageMultiplierRange.x, damageMultiplierRange.y, cold);
        if (!exposed && hypothermic)
            multiplier *= hypothermiaCoverDamageScale;

        playerController.touchingSnow = exposed || hypothermic;
        playerController.HazardDamageMultiplier = multiplier;

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

    /// <summary>
    /// True when something solid sits between the player's head and the sky. Three rays: one
    /// straight up and two leaning into the wind, so cover has to shield the windward side too.
    /// </summary>
    private bool HasOverheadCover()
    {
        var cc = playerController.GetComponent<CharacterController>();
        float top = cc != null ? cc.center.y + cc.height * 0.5f : 1.8f;
        Vector3 origin = playerController.transform.position + Vector3.up * (top + 0.15f);

        Vector3 horizontalWind = new Vector3(wind.x, 0f, wind.z);
        Vector3 into = horizontalWind.sqrMagnitude > 0.01f ? -horizontalWind.normalized : Vector3.zero;

        return CoverRay(origin, Vector3.up)
            && CoverRay(origin, (Vector3.up + into * 0.35f).normalized)
            && CoverRay(origin, (Vector3.up + into * 0.7f).normalized);
    }

    private bool CoverRay(Vector3 origin, Vector3 direction)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, coverHits, coverCheckHeight, coverMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = coverHits[i];
            if (hit.collider == null)
                continue;
            // Never let the player's own colliders count as a roof.
            if (hit.collider.GetComponentInParent<PlayerController>() != null)
                continue;
            return true;
        }
        return false;
    }

    private void UpdateSnow()
    {
        // Follow the player so the storm is everywhere they go, not a fixed patch of map. The
        // emitter sits upwind by the distance the snow drifts while it falls, so wind-driven
        // snow lands on the player instead of sailing past them.
        var p = playerController.transform.position;
        float fallTime = emitterHeight / Mathf.Max(meanFallSpeed, 0.5f);
        var drift = new Vector3(wind.x, 0f, wind.z) * fallTime;
        var upwind = -Vector3.ClampMagnitude(drift, 80f);
        transform.position = new Vector3(p.x + upwind.x, p.y + emitterHeight, p.z + upwind.z);

        var emission = ps.emission;
        emission.rateOverTime = Mathf.Lerp(emissionRange.x, emissionRange.y, gust);

        // Wind drives the snow sideways. World space so the emitter following the player doesn't matter.
        // All three axes must share a mode; the prefab authors them as random-between-two-constants.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.7f, wind.x * 1.3f);
        vel.y = new ParticleSystem.MinMaxCurve(-3f, -2f);
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
