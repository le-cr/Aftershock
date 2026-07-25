using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Raises a WaterWorks water plane through a rise/hold/recede envelope to simulate a flood, and
/// wires up <see cref="Buoyancy"/> on every building under the city's "Buildings" container so
/// they respond with real Archimedes-principle physics as the water rises.
///
/// Exposes <see cref="WaterLevel"/> (current world-space Y of the water surface) so
/// <see cref="Buoyancy"/> instances can compute submersion each frame, mirroring how
/// <see cref="EarthquakeSimulator"/> exposes <c>ShakeStrength</c> for <see cref="BuildingDebris"/>.
/// Press F to trigger, or enable Auto Start.
/// </summary>
[DisallowMultipleComponent]
public class FloodSimulator : MonoBehaviour
{
    /// <summary>The most recently enabled simulator, so buoyant bodies can find the active flood.</summary>
    public static FloodSimulator Instance { get; private set; }

    /// <summary>True while the flood is rising, holding, or receding.</summary>
    public bool IsFlooding => phase != FloodPhase.Idle;

    /// <summary>Current world-space Y of the water surface.</summary>
    public float WaterLevel { get; private set; }

    public float WaterDensity => waterDensity;
    public Vector3 CurrentDirection => currentDirection;
    public float CurrentStrength => currentStrength;
    public float WaterLinearDamping => waterLinearDamping;
    public float WaterAngularDamping => waterAngularDamping;
    public bool ClampBuoyantForce => clampBuoyantForce;
    public float MaxForceMultiplier => maxForceMultiplier;

    [Header("Water Plane")]
    [Tooltip("The WaterWorks Water_Plane prefab. Use 'Auto-Find Water Plane Prefab' in the inspector.")]
    [SerializeField] private GameObject waterPlanePrefab;
    [Tooltip("Optional explicit override for the buildings container. Auto-resolved to this " +
             "GameObject's 'Buildings' child (as built by EarthquakeCityBuilder) if left empty.")]
    [SerializeField] private Transform buildingsContainerOverride;

    [Header("Water Levels")]
    [Tooltip("World Y the water surface sits at when idle, safely below the ground (top face is at Y=0).")]
    [SerializeField] private float dryLevel = -6f;
    [Tooltip("World Y the water surface rises to at peak flood. Tune against building height in Play mode.")]
    [SerializeField] private float peakLevel = 10f;

    [Header("Timing")]
    [SerializeField] private float riseDuration = 20f;
    [SerializeField] private float holdDuration = 15f;
    [Tooltip("Receding slower than rising reads as more natural.")]
    [SerializeField] private float recedeDuration = 25f;
    [Tooltip("Small bobbing amplitude applied while holding at peak.")]
    [SerializeField] private float waveAmplitude = 0.15f;
    [SerializeField] private float waveFrequency = 0.5f;

    [Header("Input")]
    [Tooltip("Key that triggers/re-triggers a flood.")]
    [SerializeField] private Key triggerKey = Key.F;
    [SerializeField] private bool autoStart = false;
    [SerializeField] private float startDelay = 3f;

    [Header("Buoyancy Tuning")]
    [Tooltip("Real fresh water density in kg/m^3, used for Archimedes' principle buoyant force.")]
    [SerializeField] private float waterDensity = 1000f;
    [Tooltip("Linear/angular damping a fully-submerged building is lerped toward, simulating water drag.")]
    [SerializeField] private float waterLinearDamping = 6f;
    [SerializeField] private float waterAngularDamping = 6f;
    [Tooltip("Direction of the flood current, pushes submerged/floating bodies downstream.")]
    [SerializeField] private Vector3 currentDirection = Vector3.right;
    [SerializeField] private float currentStrength = 5000f;
    [Tooltip("Caps buoyant force so a building can't be launched at unbounded velocity.")]
    [SerializeField] private bool clampBuoyantForce = true;
    [Tooltip("Max buoyant force as a multiple of the building's own weight, when clamped.")]
    [SerializeField] private float maxForceMultiplier = 40f;

    private enum FloodPhase { Idle, Rising, Holding, Receding }

    private FloodPhase phase = FloodPhase.Idle;
    private float elapsed;
    private float levelAtRecedeStart;
    private GameObject waterPlaneInstance;
    private Transform buildingsContainer;

    private void OnEnable()
    {
        Instance = this;
    }

    private void Start()
    {
        WaterLevel = dryLevel;
        EnsureWaterPlane();
        ResolveBuildingsContainer();

        if (autoStart)
        {
            Invoke(nameof(TriggerFlood), startDelay);
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[triggerKey].wasPressedThisFrame)
        {
            TriggerFlood();
        }
    }

    private void FixedUpdate()
    {
        if (phase == FloodPhase.Idle)
        {
            return;
        }

        elapsed += Time.fixedDeltaTime;

        switch (phase)
        {
            case FloodPhase.Rising:
                WaterLevel = Mathf.SmoothStep(dryLevel, peakLevel, elapsed / riseDuration);
                if (elapsed >= riseDuration)
                {
                    phase = FloodPhase.Holding;
                    elapsed = 0f;
                }
                break;

            case FloodPhase.Holding:
                WaterLevel = peakLevel + Mathf.Sin(elapsed * waveFrequency * Mathf.PI * 2f) * waveAmplitude;
                if (elapsed >= holdDuration)
                {
                    levelAtRecedeStart = WaterLevel;
                    phase = FloodPhase.Receding;
                    elapsed = 0f;
                }
                break;

            case FloodPhase.Receding:
                WaterLevel = Mathf.SmoothStep(levelAtRecedeStart, dryLevel, elapsed / recedeDuration);
                if (elapsed >= recedeDuration)
                {
                    StopFlood();
                    return;
                }
                break;
        }

        if (waterPlaneInstance != null)
        {
            Vector3 pos = waterPlaneInstance.transform.position;
            pos.y = WaterLevel;
            waterPlaneInstance.transform.position = pos;
        }
    }

    /// <summary>Starts (or restarts) a flood from the dry water level.</summary>
    [ContextMenu("Trigger Flood")]
    public void TriggerFlood()
    {
        EnsureWaterPlane();
        ResolveBuildingsContainer();
        WireBuoyancy();

        elapsed = 0f;
        phase = FloodPhase.Rising;
    }

    /// <summary>Immediately stops the flood and lowers the water back to the dry level.</summary>
    [ContextMenu("Stop Flood")]
    public void StopFlood()
    {
        phase = FloodPhase.Idle;
        elapsed = 0f;
        WaterLevel = dryLevel;

        if (waterPlaneInstance != null)
        {
            Vector3 pos = waterPlaneInstance.transform.position;
            pos.y = WaterLevel;
            waterPlaneInstance.transform.position = pos;
        }
    }

    private void EnsureWaterPlane()
    {
        if (waterPlaneInstance != null)
        {
            return;
        }

        if (waterPlanePrefab == null)
        {
            Debug.LogWarning("FloodSimulator: no water plane prefab assigned. " +
                             "Use 'Auto-Find Water Plane Prefab' in the inspector.", this);
            return;
        }

        // Instantiated at scene root (not parented under this transform) so EarthquakeCityBuilder's
        // "Clear City" - which destroys all direct children of the City GameObject - can't delete it.
        waterPlaneInstance = Instantiate(waterPlanePrefab);
        waterPlaneInstance.name = "Flood Water";
        waterPlaneInstance.transform.position = new Vector3(transform.position.x, WaterLevel, transform.position.z);

        // The water is purely visual here; Buoyancy drives all physical interaction via WaterLevel.
        // Leaving the plane's collider enabled would double up on physics (PhysX contact resolution
        // fighting the script-driven buoyant forces) and be expensive to move every frame.
        var planeCollider = waterPlaneInstance.GetComponent<Collider>();
        if (planeCollider != null)
        {
            planeCollider.enabled = false;
        }
    }

    private void ResolveBuildingsContainer()
    {
        buildingsContainer = buildingsContainerOverride != null
            ? buildingsContainerOverride
            : transform.Find("Buildings");

        if (buildingsContainer == null)
        {
            Debug.LogWarning("FloodSimulator: could not find a 'Buildings' container. " +
                             "Build the city first, or assign an explicit override.", this);
        }
    }

    private void WireBuoyancy()
    {
        if (buildingsContainer == null)
        {
            return;
        }

        foreach (Rigidbody rb in buildingsContainer.GetComponentsInChildren<Rigidbody>())
        {
            if (rb.GetComponent<Buoyancy>() == null)
            {
                rb.gameObject.AddComponent<Buoyancy>();
            }
        }
    }
}
