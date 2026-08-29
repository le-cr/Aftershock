using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Runs the wildfire: ignites beside the player and spreads outward as an advancing front for
/// the whole survival window, burning the collapsing buildings down on its way through.
///
/// The simulation and the visuals are deliberately separate. Spread is a lightweight logical
/// grid of cells; the fire VFX are a small pooled set attached only to burning cells near the
/// camera. Memory is therefore bounded by the pool, not by how much of the map is alight —
/// the whole level can be burning while only ~20 instances exist.
///
/// Driven by DisasterManager rather than a key press.
/// </summary>
public class WildfireManager : MonoBehaviour
{
    enum CellState { Unburnt, Burning, Burnt }

    class FireCell
    {
        public Vector3 position;
        public CellState state;
        public float ignitedAt;
        public float jitter;          // delays ignition so the front edge isn't a perfect circle
        public float distanceToOrigin;
        public FireInstance vfx;
    }

    class BurningBuilding
    {
        public BuildingCollapse building;
        public float ignitedAt;
        public FireInstance vfx;
    }

    [Header("Area")]
    [Tooltip("Centre of the burnable area, in world space.")]
    [SerializeField] Vector3 areaCenter = new Vector3(-19f, 0f, 18f);

    [Tooltip("Width (X) and depth (Z) of the burnable area.")]
    [SerializeField] Vector2 areaSize = new Vector2(104f, 88f);

    [Tooltip("Metres between ignition cells. Larger = fewer cells and a coarser front.")]
    [SerializeField] float cellSpacing = 10f;

    [Header("Spread")]
    [Tooltip("Seconds a single cell burns before going out and freeing its VFX slot.")]
    [SerializeField] float cellBurnSeconds = 14f;

    [Tooltip("Fraction of the survival window by which the furthest cell has ignited. " +
             "Spread speed is derived from this, so the fire always paces itself to the run length.")]
    [Range(0.5f, 1f)]
    [SerializeField] float spreadCompletionFraction = 0.9f;

    [Tooltip("Metres of random ignition delay per cell, so the front reads organically.")]
    [SerializeField] float ignitionJitter = 4f;

    [Tooltip("Fire ignites at least this far from the player, so it starts beside them not on them.")]
    [SerializeField] float minOriginDistanceFromPlayer = 8f;

    [Header("Damage")]
    [Tooltip("Damage stops entirely beyond this distance from the nearest burning cell.")]
    [SerializeField] float damageRadius = 6f;

    [Tooltip("Health lost per second when standing in the middle of a fire. Health runs 0-1.")]
    [SerializeField] float maxDamagePerSecond = 0.2f;

    [Header("Buildings")]
    [Tooltip("The front ignites a building once it comes this close to it.")]
    [SerializeField] float buildingIgniteRadius = 8f;

    [Tooltip("Seconds a building burns before it collapses.")]
    [SerializeField] float buildingBurnSeconds = 10f;

    [Header("VFX")]
    [SerializeField] FireInstance groundFirePrefab;
    [SerializeField] FireInstance buildingFirePrefab;

    [Tooltip("Hard ceiling on simultaneous ground-fire instances. This is the memory ceiling.")]
    [SerializeField] int maxConcurrentVfx = 20;

    [Tooltip("Burning cells further than this from the camera are simulated but not drawn.")]
    [SerializeField] float vfxVisualRange = 55f;

    [Tooltip("Particle buffer cap applied to every child system. The prefabs ship at 1000.")]
    [SerializeField] int maxParticlesPerSystem = 50;

    [Tooltip("How many of the nearest fires carry a real-time point light.")]
    [SerializeField] int litFireCount = 2;

    [Tooltip("Turn off heat-distortion children. Needed if running an RP asset without Require Opaque Texture.")]
    [SerializeField] bool disableDistortion = false;

    [Header("Audio")]
    [Tooltip("One shared looping fire bed, volume driven by distance to the nearest fire.")]
    [SerializeField] AudioSource fireAmbience;
    [SerializeField] float ambienceFalloffDistance = 30f;

    [Header("References")]
    [SerializeField] PlayerController playerController;
    [SerializeField] Terrain terrain;

    [Tooltip("Leave empty to find every Shelter under /Scene. Cells inside these never ignite.")]
    [SerializeField] Transform[] shelters;

    [Tooltip("Metres of clearance kept around each shelter.")]
    [SerializeField] float shelterMargin = 2f;

    [Tooltip("Leave empty to burn every BuildingCollapse in the scene.")]
    [SerializeField] BuildingCollapse[] buildings;

    [Header("Constants")]
    [Tooltip("Seconds between simulation ticks. The whole sim runs here, not in Update.")]
    [SerializeField] float tickInterval = 0.25f;

    private readonly List<FireCell> cells = new List<FireCell>();
    private readonly List<BurningBuilding> burningBuildings = new List<BurningBuilding>();
    private readonly List<FireCell> visibleBurning = new List<FireCell>();
    private readonly List<Bounds> shelterBounds = new List<Bounds>();

    private ObjectPool<FireInstance> pool;
    private Vector3 origin;
    private float startTime;
    private float duration;
    private float spreadSpeed;
    private float maxCellDistance;
    private float frontRadius;
    private float lastIgnitionElapsed;
    private int activeVfxCount;
    private bool triggered;
    private Transform viewer;

    // --- stats, used by the CLI verification pass ---
    public bool HasTriggered => triggered;
    public int CellCount => cells.Count;
    public float FrontRadius => frontRadius;
    public float MaxCellDistance => maxCellDistance;
    public float LastIgnitionElapsed => lastIgnitionElapsed;
    public int ActiveVfxCount => activeVfxCount;
    public int MaxConcurrentVfx => maxConcurrentVfx;
    public float VfxVisualRange => vfxVisualRange;

    public int BurningCount => CountState(CellState.Burning);
    public int BurntCount => CountState(CellState.Burnt);
    public int UnburntCount => CountState(CellState.Unburnt);

    /// <summary>Burning cells too far from the camera to be drawn. Proves sim and view are separate.</summary>
    public int BurningWithoutVfx
    {
        get
        {
            int n = 0;
            foreach (var c in cells)
                if (c.state == CellState.Burning && c.vfx == null) n++;
            return n;
        }
    }

    private int CountState(CellState state)
    {
        int n = 0;
        foreach (var c in cells)
            if (c.state == state) n++;
        return n;
    }

    void Awake()
    {
        if (terrain == null)
            terrain = FindFirstObjectByType<Terrain>();

        if (buildings == null || buildings.Length == 0)
            buildings = FindObjectsByType<BuildingCollapse>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (shelters == null || shelters.Length == 0)
            shelters = FindShelters();
    }

    private Transform[] FindShelters()
    {
        var found = new List<Transform>();
        var scene = GameObject.Find("/Scene");
        if (scene == null)
            return found.ToArray();

        foreach (Transform child in scene.transform)
            if (child.name.StartsWith("Shelter"))
                found.Add(child);

        return found.ToArray();
    }

    /// <summary>Start the wildfire and spread it across <paramref name="runDuration"/> seconds.</summary>
    public void TriggerWildfire(float runDuration)
    {
        if (triggered)
            return;

        triggered = true;
        duration = runDuration;
        startTime = Time.time;

        CacheShelterBounds();
        BuildGrid();
        CreatePool();
        ChooseOrigin();

        if (fireAmbience != null)
        {
            fireAmbience.loop = true;
            fireAmbience.volume = 0f;
            fireAmbience.Play();
        }

        StartCoroutine(Run());
    }

    private void CacheShelterBounds()
    {
        shelterBounds.Clear();
        foreach (var shelter in shelters)
        {
            if (shelter == null) continue;

            var renderers = shelter.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            b.Expand(new Vector3(shelterMargin * 2f, 0f, shelterMargin * 2f));
            shelterBounds.Add(b);
        }
    }

    private void BuildGrid()
    {
        cells.Clear();

        int nx = Mathf.Max(1, Mathf.RoundToInt(areaSize.x / cellSpacing));
        int nz = Mathf.Max(1, Mathf.RoundToInt(areaSize.y / cellSpacing));
        Vector3 min = areaCenter - new Vector3(areaSize.x * 0.5f, 0f, areaSize.y * 0.5f);

        for (int ix = 0; ix <= nx; ix++)
        {
            for (int iz = 0; iz <= nz; iz++)
            {
                var p = min + new Vector3(ix * cellSpacing, 0f, iz * cellSpacing);

                // Shelters are safe havens: they never catch, so they stay a viable strategy.
                if (IsInsideShelter(p))
                    continue;

                if (terrain != null)
                    p.y = terrain.SampleHeight(p) + terrain.transform.position.y;

                cells.Add(new FireCell { position = p, jitter = Random.Range(0f, ignitionJitter) });
            }
        }
    }

    private bool IsInsideShelter(Vector3 p)
    {
        foreach (var b in shelterBounds)
            if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z)
                return true;

        return false;
    }

    private bool IsPlayerSheltered()
    {
        if (playerController == null)
            return false;

        var p = playerController.transform.position;
        foreach (var b in shelterBounds)
            if (b.Contains(new Vector3(p.x, b.center.y, p.z)))
                return true;

        return false;
    }

    private void CreatePool()
    {
        pool = new ObjectPool<FireInstance>(
            createFunc: () =>
            {
                var instance = Instantiate(groundFirePrefab, Vector3.zero, Quaternion.identity, transform);
                instance.Tame(maxParticlesPerSystem, disableDistortion);
                instance.gameObject.SetActive(false);
                return instance;
            },
            actionOnGet: null,
            actionOnRelease: instance => instance.Stop(),
            actionOnDestroy: instance => Destroy(instance.gameObject),
            collectionCheck: false,
            defaultCapacity: Mathf.Min(8, maxConcurrentVfx),
            maxSize: maxConcurrentVfx);
    }

    private void ChooseOrigin()
    {
        var playerPos = playerController != null ? playerController.transform.position : Vector3.zero;

        FireCell best = null;
        float bestDistance = float.MaxValue;
        FireCell fallback = null;
        float fallbackDistance = float.MaxValue;

        foreach (var c in cells)
        {
            float d = Vector2.Distance(new Vector2(c.position.x, c.position.z), new Vector2(playerPos.x, playerPos.z));

            if (d < fallbackDistance) { fallbackDistance = d; fallback = c; }

            // Nearest cell that is still far enough away to not ignite on top of the player.
            if (d >= minOriginDistanceFromPlayer && d < bestDistance) { bestDistance = d; best = c; }
        }

        var originCell = best ?? fallback;
        origin = originCell != null ? originCell.position : playerPos;

        // Precompute each cell's ignition distance, jitter included, and derive the speed that
        // lands the furthest cell at spreadCompletionFraction of the run.
        maxCellDistance = 0.01f;
        foreach (var c in cells)
        {
            c.distanceToOrigin = Vector2.Distance(new Vector2(c.position.x, c.position.z), new Vector2(origin.x, origin.z)) + c.jitter;
            if (c.distanceToOrigin > maxCellDistance) maxCellDistance = c.distanceToOrigin;
        }

        spreadSpeed = maxCellDistance / Mathf.Max(duration * spreadCompletionFraction, 0.01f);
    }

    private IEnumerator Run()
    {
        var wait = new WaitForSeconds(tickInterval);

        while (true)
        {
            Tick();

            bool windowClosed = Time.time - startTime > duration;
            if (windowClosed && BurningCount == 0 && burningBuildings.Count == 0)
                break;

            yield return wait;
        }

        ReleaseAllVfx();

        if (fireAmbience != null)
            fireAmbience.Stop();
    }

    private void Tick()
    {
        float elapsed = Time.time - startTime;
        frontRadius = spreadSpeed * elapsed;

        bool stillSpreading = elapsed <= duration;

        // --- spread and burn-out ---
        foreach (var c in cells)
        {
            if (c.state == CellState.Unburnt)
            {
                if (stillSpreading && c.distanceToOrigin <= frontRadius)
                {
                    c.state = CellState.Burning;
                    c.ignitedAt = Time.time;
                    lastIgnitionElapsed = elapsed;
                }
            }
            else if (c.state == CellState.Burning && Time.time - c.ignitedAt >= cellBurnSeconds)
            {
                c.state = CellState.Burnt;
                ReleaseVfx(c);
            }
        }

        UpdateVfx();
        UpdateBuildings(stillSpreading);
        ApplyDamage();
        UpdateAmbience();
    }

    /// <summary>
    /// Attach the pooled instances to the burning cells nearest the camera, releasing any that
    /// have drifted out of range. Burning cells beyond the range stay simulated but undrawn.
    /// </summary>
    private void UpdateVfx()
    {
        if (viewer == null && Camera.main != null)
            viewer = Camera.main.transform;

        Vector3 eye = viewer != null ? viewer.position
                    : playerController != null ? playerController.transform.position
                    : transform.position;

        visibleBurning.Clear();

        foreach (var c in cells)
        {
            if (c.state != CellState.Burning)
                continue;

            if (Vector3.Distance(c.position, eye) > vfxVisualRange)
                ReleaseVfx(c);        // out of sight: give the slot back
            else
                visibleBurning.Add(c);
        }

        visibleBurning.Sort((a, b) =>
            (a.position - eye).sqrMagnitude.CompareTo((b.position - eye).sqrMagnitude));

        for (int i = 0; i < visibleBurning.Count; i++)
        {
            var c = visibleBurning[i];

            if (c.vfx == null && activeVfxCount < maxConcurrentVfx)
            {
                c.vfx = pool.Get();
                c.vfx.transform.position = c.position;
                c.vfx.Play();
                activeVfxCount++;
            }

            // Only the closest few carry a real-time light; the project allows very few.
            if (c.vfx != null)
                c.vfx.SetLightEnabled(i < litFireCount);
        }
    }

    private void ReleaseVfx(FireCell c)
    {
        if (c.vfx == null)
            return;

        pool.Release(c.vfx);
        c.vfx = null;
        activeVfxCount--;
    }

    private void ReleaseAllVfx()
    {
        foreach (var c in cells)
            ReleaseVfx(c);

        foreach (var b in burningBuildings)
            if (b.vfx != null) Destroy(b.vfx.gameObject);

        burningBuildings.Clear();
    }

    private void UpdateBuildings(bool stillSpreading)
    {
        if (stillSpreading)
        {
            foreach (var building in buildings)
            {
                if (building == null || building.HasCollapsed || IsBurning(building))
                    continue;

                float d = Vector2.Distance(
                    new Vector2(building.transform.position.x, building.transform.position.z),
                    new Vector2(origin.x, origin.z));

                if (d - buildingIgniteRadius > frontRadius)
                    continue;

                var entry = new BurningBuilding { building = building, ignitedAt = Time.time };

                if (buildingFirePrefab != null)
                {
                    var renderer = building.GetComponent<Renderer>();
                    var at = renderer != null ? renderer.bounds.center : building.transform.position;
                    entry.vfx = Instantiate(buildingFirePrefab, at, Quaternion.identity, transform);
                    entry.vfx.Tame(maxParticlesPerSystem, disableDistortion);
                    entry.vfx.Play();
                }

                burningBuildings.Add(entry);
            }
        }

        // Burnt-through buildings come down via the existing fracture path.
        for (int i = burningBuildings.Count - 1; i >= 0; i--)
        {
            var entry = burningBuildings[i];
            if (Time.time - entry.ignitedAt < buildingBurnSeconds)
                continue;

            if (entry.vfx != null)
                Destroy(entry.vfx.gameObject);

            if (entry.building != null)
                entry.building.Collapse();

            burningBuildings.RemoveAt(i);
        }
    }

    private bool IsBurning(BuildingCollapse building)
    {
        foreach (var b in burningBuildings)
            if (b.building == building) return true;

        return false;
    }

    private float NearestBurningDistance()
    {
        if (playerController == null)
            return float.MaxValue;

        var p = playerController.transform.position;
        float nearest = float.MaxValue;

        foreach (var c in cells)
        {
            if (c.state != CellState.Burning)
                continue;

            float d = Vector3.Distance(c.position, p);
            if (d < nearest) nearest = d;
        }

        return nearest;
    }

    /// <summary>
    /// Proximity damage, scaled by distance. Done here rather than with colliders on each fire:
    /// one pass over the cells is far cheaper than N trigger volumes.
    /// </summary>
    private void ApplyDamage()
    {
        if (playerController == null || IsPlayerSheltered())
            return;

        float d = NearestBurningDistance();
        if (d >= damageRadius)
            return;

        float intensity = 1f - (d / damageRadius);
        playerController.TakeDamage(maxDamagePerSecond * intensity * tickInterval);
    }

    private void UpdateAmbience()
    {
        if (fireAmbience == null)
            return;

        float d = NearestBurningDistance();
        fireAmbience.volume = d >= ambienceFalloffDistance
            ? 0f
            : 1f - (d / ambienceFalloffDistance);
    }
}
