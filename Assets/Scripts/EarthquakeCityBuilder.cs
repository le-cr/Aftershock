using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// Builds a large ProBuilder ground and lays out a grid of real building models (the Kenney
/// city-kit meshes). Each building gets a convex MeshCollider + Rigidbody so it topples under
/// physics when the ground shakes. The ground receives an <see cref="EarthquakeSimulator"/>.
///
/// Assign the building models to <see cref="buildingModels"/> (use the "Auto-Find Building
/// Models" button in the inspector to populate them from Assets/Models). Then press Play, or
/// right-click the component and choose "Build City".
/// </summary>
[DisallowMultipleComponent]
public class EarthquakeCityBuilder : MonoBehaviour
{
    [Header("Ground")]
    [Tooltip("Width/depth of the square ProBuilder ground in world units.")]
    [SerializeField] private float groundSize = 120f;
    [SerializeField] private float groundThickness = 2f;
    [SerializeField] private Color groundColor = new Color(0.35f, 0.37f, 0.4f);

    [Header("Building Grid")]
    [SerializeField] private int rows = 6;
    [SerializeField] private int columns = 6;
    [Tooltip("Distance between building centers (models are ~1 unit before scaling).")]
    [SerializeField] private float spacing = 6f;
    [Tooltip("Random horizontal jitter added to each building's grid position.")]
    [SerializeField] private float positionJitter = 1f;

    [Header("Buildings")]
    [Tooltip("The actual building models to place. Use 'Auto-Find Building Models' to fill this.")]
    [SerializeField] private GameObject[] buildingModels;
    [Tooltip("Uniform scale applied to each model (Kenney buildings import at ~1 unit).")]
    [SerializeField] private float buildingScale = 4f;
    [Tooltip("Mass of each building. Heavy so they stay grounded and don't bounce around.")]
    [SerializeField] private float buildingMass = 400f;
    [Tooltip("Linear/angular damping that resists sliding and rocking under the shake.")]
    [SerializeField] private float buildingLinearDamping = 1.5f;
    [SerializeField] private float buildingAngularDamping = 2f;
    [SerializeField] private bool randomYRotation = true;

    [Header("Debris")]
    [Tooltip("How many breakable chunks to attach to each building.")]
    [SerializeField] private int debrisPerBuilding = 10;
    [Tooltip("Approximate world size of each debris chunk.")]
    [SerializeField] private float debrisSize = 0.5f;
    [SerializeField] private float debrisMass = 3f;
    [Tooltip("At full quake strength, expected debris pieces released per building per second.")]
    [SerializeField] private float debrisReleaseRate = 1.5f;
    [SerializeField] private Color debrisColor = new Color(0.55f, 0.53f, 0.5f);

    [Header("Generation")]
    [Tooltip("Seed for model choice / placement. Same seed = same city.")]
    [SerializeField] private int randomSeed = 12345;
    [SerializeField] private bool buildOnAwake = true;

    private void Awake()
    {
        if (buildOnAwake && Application.isPlaying)
        {
            Build();
        }
    }

    [ContextMenu("Build City")]
    public void Build()
    {
        Clear();
        Transform ground = BuildGround();
        BuildBuildings(ground);
    }

    [ContextMenu("Clear City")]
    public void Clear()
    {
        var children = new List<GameObject>();
        foreach (Transform child in transform)
        {
            children.Add(child.gameObject);
        }

        foreach (GameObject child in children)
        {
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private Transform BuildGround()
    {
        Vector3 size = new Vector3(groundSize, groundThickness, groundSize);

        ProBuilderMesh mesh = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        mesh.ToMesh();
        mesh.Refresh();

        GameObject ground = mesh.gameObject;
        ground.name = "Ground";
        ground.transform.SetParent(transform, worldPositionStays: false);
        ground.transform.localPosition = new Vector3(0f, -groundThickness * 0.5f, 0f); // top at y = 0

        var collider = ground.AddComponent<BoxCollider>();
        collider.size = size;

        var renderer = ground.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetMaterial(groundColor);
        }

        // Kinematic rigidbody lets the shaking impart contact forces to resting buildings.
        Rigidbody rb = ground.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        ground.AddComponent<EarthquakeSimulator>();
        return ground.transform;
    }

    private void BuildBuildings(Transform ground)
    {
        if (buildingModels == null || buildingModels.Length == 0)
        {
            Debug.LogWarning("EarthquakeCityBuilder: no building models assigned. " +
                             "Use 'Auto-Find Building Models' in the inspector.", this);
            return;
        }

        var container = new GameObject("Buildings").transform;
        container.SetParent(transform, worldPositionStays: false);

        Random.InitState(randomSeed);

        float gridWidth = (columns - 1) * spacing;
        float gridDepth = (rows - 1) * spacing;
        Vector3 origin = new Vector3(-gridWidth * 0.5f, 0f, -gridDepth * 0.5f);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                GameObject model = buildingModels[Random.Range(0, buildingModels.Length)];
                if (model == null)
                {
                    continue;
                }

                Vector3 jitter = new Vector3(
                    Random.Range(-positionJitter, positionJitter),
                    0f,
                    Random.Range(-positionJitter, positionJitter));
                Vector3 pos = origin + new Vector3(c * spacing, 0f, r * spacing) + jitter;

                PlaceBuilding(model, container, pos, r * columns + c);
            }
        }
    }

    private void PlaceBuilding(GameObject model, Transform parent, Vector3 localPos, int index)
    {
        GameObject go = Instantiate(model, parent);
        go.name = $"{model.name}_{index}";
        go.transform.localPosition = localPos; // model pivot is at its base, so it rests on the ground
        go.transform.localScale = Vector3.one * buildingScale;
        if (randomYRotation)
        {
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f);
        }

        Bounds localBounds = SetupBuildingPhysics(go, buildingMass);

        // Attach breakable debris that sheds off during the quake.
        if (debrisPerBuilding > 0)
        {
            var debris = go.AddComponent<BuildingDebris>();
            debris.Initialize(localBounds, GetMaterial(debrisColor), debrisPerBuilding,
                              debrisSize, debrisMass, debrisReleaseRate);
        }
    }

    /// <summary>
    /// Adds a BoxCollider fitted to the building's combined mesh bounds and a Rigidbody so it
    /// can topple. A box (rather than a convex mesh collider) keeps physics stable and avoids
    /// the model-import "Read/Write" requirement, since Mesh.bounds is always accessible.
    /// Returns the fitted bounds in the building's local space.
    /// </summary>
    private Bounds SetupBuildingPhysics(GameObject go, float mass)
    {
        Transform root = go.transform;
        bool hasBounds = false;
        Bounds bounds = default;

        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null)
            {
                continue;
            }

            // Encapsulate each mesh's local-space corners, expressed in the root's local space.
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 min = mb.min;
            Vector3 max = mb.max;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                Vector3 local = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!hasBounds)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        var box = go.AddComponent<BoxCollider>();
        if (hasBounds)
        {
            box.center = bounds.center;
            box.size = bounds.size;
        }

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = go.AddComponent<Rigidbody>();
        }
        rb.mass = mass;
        rb.linearDamping = buildingLinearDamping;
        rb.angularDamping = buildingAngularDamping;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        return hasBounds ? bounds : new Bounds(Vector3.up, Vector3.one);
    }

    private readonly Dictionary<Color, Material> materialCache = new Dictionary<Color, Material>();

    private Material GetMaterial(Color color)
    {
        if (materialCache.TryGetValue(color, out Material cached) && cached != null)
        {
            return cached;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        var material = new Material(shader) { name = $"Ground_{ColorUtility.ToHtmlStringRGB(color)}" };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        materialCache[color] = material;
        return material;
    }
}
