using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the earthquake for the whole survival window. A short foreshock rattles first, then the
/// main shock, then aftershocks that come less often and weaker as time goes on (Omori's law),
/// with the occasional big one. Buildings are held back rather than all dropped at once, and
/// only tremors strong enough bring one down. Collapses favour the buildings nearest the
/// player, a building coming down beside them hurts in itself, and while the ground shakes
/// the facades of nearby buildings shed chunks that fall towards the player.
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

    [Header("Targeting")]
    [Tooltip("Chance each collapse takes the building nearest the player; otherwise one of the nearest few.")]
    [Range(0f, 1f)]
    [SerializeField] float nearestCollapseChance = 0.6f;

    [Tooltip("How many of the nearest standing buildings a non-nearest collapse is drawn from.")]
    [SerializeField] int nearestPoolSize = 4;

    [Header("Collapse damage")]
    [Tooltip("Health lost when a building collapses with the player right against it. Falls off to nothing at collapseDamageRadius.")]
    [SerializeField] float collapseDamage = 0.35f;

    [Tooltip("Metres from the building's footprint within which a collapse hurts the player.")]
    [SerializeField] float collapseDamageRadius = 12f;

    [Header("Facade debris")]
    [Tooltip("Chunks per second shed at full shake from the standing buildings nearest the player.")]
    [SerializeField] float facadeDebrisPerSecond = 4f;

    [Tooltip("Shake intensity (0-1 of the main shock) below which no facade debris falls.")]
    [Range(0f, 1f)]
    [SerializeField] float facadeDebrisShakeThreshold = 0.2f;

    [Tooltip("Only buildings whose footprint is within this many metres of the player shed chunks.")]
    [SerializeField] float facadeDebrisRadius = 20f;

    [Tooltip("Chunk size range in metres.")]
    [SerializeField] Vector2 facadeChunkSize = new Vector2(0.45f, 0.95f);

    [Tooltip("Health lost when a facade chunk hits the player.")]
    [SerializeField] float facadeChunkDamage = 0.12f;

    [Tooltip("How closely chunks are aimed at the player: 0 = straight down, 1 = dead on.")]
    [Range(0f, 1f)]
    [SerializeField] float facadeChunkAim = 0.7f;

    [Tooltip("Seconds a chunk lives before it is destroyed.")]
    [SerializeField] float facadeChunkLifetime = 8f;

    [Tooltip("Hard cap on live facade chunks.")]
    [SerializeField] int maxFacadeChunks = 24;

    [Header("Aftershocks")]
    [Tooltip("Gap before the first aftershock, in seconds. Later gaps grow from here.")]
    [SerializeField] float firstInterval = 6f;

    [Tooltip("Each successive gap grows by this fraction (Omori decay of aftershock rate).")]
    [SerializeField] float intervalGrowth = 0.15f;

    [Tooltip("Fallback quake window when triggered without an explicit duration. DisasterManager passes the survival time instead.")]
    [SerializeField] float defaultDuration = 120f;

    [Tooltip("Seconds an aftershock shakes for.")]
    [SerializeField] float aftershockShakeDuration = 5f;

    [Tooltip("Typical aftershock strength as a fraction of the main shock, before decay and randomness.")]
    [Range(0f, 1f)]
    [SerializeField] float aftershockMagnitudeScale = 0.78f;

    [Tooltip("Chance any aftershock is a big one, nearly as strong as the main shock.")]
    [Range(0f, 1f)]
    [SerializeField] float bigAftershockChance = 0.4f;

    [Tooltip("An aftershock must reach this fraction of the main shock to bring a building down.")]
    [Range(0f, 1f)]
    [SerializeField] float collapseThreshold = 0.3f;

    [Header("Feel")]
    [Tooltip("Horizontal stumble pushed onto the player per metre of shake, in m/s.")]
    [SerializeField] float stumblePerMetre = 7f;

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
    private float facadeDebrisAccumulator;
    private Transform facadeRoot;
    private readonly List<GameObject> facadeChunks = new List<GameObject>();
    private readonly List<BuildingCollapse> nearbyBuildings = new List<BuildingCollapse>();

    public int FacadeChunkCount => facadeChunks.Count;

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
                yield return CollapseNext(bigOne ? 3 : 1);
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

        UpdateFacadeDebris();
    }

    /// <summary>
    /// While the ground shakes, the standing buildings nearest the player shed chunks from their
    /// rooflines that fall towards them. Standing beside a building is the dangerous place to be.
    /// </summary>
    private void UpdateFacadeDebris()
    {
        facadeChunks.RemoveAll(c => c == null);

        if (playerController == null || currentShake < facadeDebrisShakeThreshold)
        {
            facadeDebrisAccumulator = 0f;
            return;
        }

        float shake = Mathf.InverseLerp(facadeDebrisShakeThreshold, 1f, currentShake);
        facadeDebrisAccumulator += facadeDebrisPerSecond * shake * Time.deltaTime;
        if (facadeDebrisAccumulator < 1f)
            return;
        facadeDebrisAccumulator -= 1f;

        if (facadeChunks.Count >= maxFacadeChunks)
            return;

        var player = playerController.transform.position;
        nearbyBuildings.Clear();
        foreach (var b in standing)
        {
            if (b == null || b.HasCollapsed || !b.gameObject.activeInHierarchy)
                continue;
            if (FootprintDistance(b, player) <= facadeDebrisRadius)
                nearbyBuildings.Add(b);
        }
        if (nearbyBuildings.Count == 0)
            return;

        SpawnFacadeChunk(nearbyBuildings[Random.Range(0, nearbyBuildings.Count)], player);
    }

    private void SpawnFacadeChunk(BuildingCollapse building, Vector3 player)
    {
        var renderer = building.GetComponent<Renderer>();
        if (renderer == null)
            return;
        var bounds = renderer.bounds;

        // Shed from the roofline on the side facing the player, so the chunk clears the wall.
        var edge = bounds.ClosestPoint(new Vector3(player.x, bounds.max.y, player.z));
        var outward = new Vector3(edge.x - bounds.center.x, 0f, edge.z - bounds.center.z);
        if (outward.sqrMagnitude < 0.01f)
            outward = new Vector3(player.x - bounds.center.x, 0f, player.z - bounds.center.z);
        outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;

        float size = Random.Range(facadeChunkSize.x, facadeChunkSize.y);
        var spawn = new Vector3(edge.x, bounds.max.y + size, edge.z)
            + outward * (size * 0.75f + 0.4f)
            + Vector3.Cross(outward, Vector3.up) * Random.Range(-2.5f, 2.5f);

        if (facadeRoot == null)
            facadeRoot = new GameObject("FacadeDebris").transform;

        var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chunk.name = "FacadeChunk";
        chunk.transform.SetParent(facadeRoot, true);
        chunk.transform.position = spawn;
        chunk.transform.rotation = Random.rotation;
        chunk.transform.localScale = new Vector3(size, size * Random.Range(0.5f, 1f), size * Random.Range(0.6f, 1f));

        var mr = chunk.GetComponent<MeshRenderer>();
        mr.sharedMaterial = renderer.sharedMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var rb = chunk.AddComponent<Rigidbody>();
        rb.mass = 40f * size;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.angularVelocity = Random.insideUnitSphere * 4f;

        // Aim: the horizontal speed that would land the chunk on the player, blended with a plain drop.
        float drop = Mathf.Max(spawn.y - player.y, 1f);
        float fallTime = Mathf.Sqrt(2f * drop / Mathf.Abs(Physics.gravity.y));
        var toPlayer = new Vector3(player.x - spawn.x, 0f, player.z - spawn.z) / fallTime;
        var scatter = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
        rb.linearVelocity = Vector3.Lerp(outward * 1.5f, toPlayer, facadeChunkAim) + scatter;

        chunk.AddComponent<DebrisDamage>().Configure(facadeChunkDamage, 1.5f, 0.3f);

        Destroy(chunk, facadeChunkLifetime);
        facadeChunks.Add(chunk);
    }

    /// <summary>Horizontal distance from a point to a building's footprint (0 when inside it).</summary>
    private static float FootprintDistance(BuildingCollapse building, Vector3 point)
    {
        var renderer = building.GetComponent<Renderer>();
        if (renderer == null)
            return Vector3.Distance(building.transform.position, point);
        var b = renderer.bounds;
        var closest = b.ClosestPoint(new Vector3(point.x, Mathf.Clamp(point.y, b.min.y, b.max.y), point.z));
        return Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(point.x, point.z));
    }

    /// <summary>
    /// Pull the next building to collapse out of the standing list: usually the one nearest the
    /// player, otherwise one of the nearest few, so debris actually reaches them.
    /// </summary>
    private BuildingCollapse TakeNextBuilding()
    {
        standing.RemoveAll(b => b == null);
        if (standing.Count == 0)
            return null;

        if (playerController == null)
        {
            var first = standing[0];
            standing.RemoveAt(0);
            return first;
        }

        var player = playerController.transform.position;
        standing.Sort((a, b) => FootprintDistance(a, player).CompareTo(FootprintDistance(b, player)));

        int index = Random.value < nearestCollapseChance
            ? 0
            : Random.Range(0, Mathf.Min(Mathf.Max(nearestPoolSize, 1), standing.Count));
        var pick = standing[index];
        standing.RemoveAt(index);
        return pick;
    }

    /// <summary>A building coming down right beside the player hurts, falling off with distance.</summary>
    private void ApplyCollapseDamage(BuildingCollapse building)
    {
        if (playerController == null || collapseDamage <= 0f)
            return;

        float d = FootprintDistance(building, playerController.transform.position);
        if (d >= collapseDamageRadius)
            return;

        playerController.TakeDamage(collapseDamage * (1f - d / collapseDamageRadius));
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
            // Wait for fragment budget headroom instead of fracturing into an already-full sim.
            var budget = DebrisBudget.Ensure();
            int spins = 0;
            while (budget.IsFull && spins++ < 40)
            {
                yield return new WaitForSeconds(0.25f);
                budget = DebrisBudget.Ensure();
            }
            if (budget.IsFull || !budget.CanCollapse())
                yield break;

            var building = TakeNextBuilding();

            if (building == null)
                continue;

            // Collapse() may no-op if the budget filled between the check and the call.
            if (building.HasCollapsed)
                continue;

            var renderer = building.GetComponent<Renderer>();
            var at = renderer != null ? renderer.bounds : new Bounds(building.transform.position, Vector3.one * 6f);

            building.Collapse();
            if (!building.HasCollapsed)
            {
                // Budget refused — put it back and stop trying this tremor.
                standing.Insert(0, building);
                yield break;
            }

            ApplyCollapseDamage(building);
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

    void OnDestroy()
    {
        if (facadeRoot != null)
            Destroy(facadeRoot.gameObject);
    }
}
