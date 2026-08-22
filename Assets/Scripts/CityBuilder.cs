using UnityEngine;

/// <summary>
/// Procedurally builds a grid of destructible buildings from the Kenney
/// commercial/suburban kits, each equipped with an OpenFracture Fracture
/// component so it can be fractured later (e.g. by Earthquake).
/// </summary>
public class CityBuilder : MonoBehaviour
{
    [Header("Building Pools")]
    [SerializeField] private GameObject[] commercialBuildings;
    [SerializeField] private GameObject[] suburbanBuildings;

    [Header("Grid Layout")]
    [SerializeField] private int gridRows = 6;
    [SerializeField] private int gridColumns = 6;
    [SerializeField] private float cellSpacing = 20f;
    [SerializeField] private Vector3 originOffset = new Vector3(150f, 0f, 150f);

    [Header("Terrain")]
    [SerializeField] private Terrain terrain;

    [Header("Fracture Setup")]
    [SerializeField] private Material insideMaterial;
    [SerializeField] private int fragmentCountMin = 8;
    [SerializeField] private int fragmentCountMax = 15;

    [Header("Output")]
    [SerializeField] private Transform cityRoot;

    // Standing buildings live on a dedicated layer (not Default, where Terrain
    // and the Player stay) so Earthquake can make thrown fragments ignore
    // collisions with OTHER, still-standing buildings. At 20m grid spacing a
    // thrown fragment easily reaches a neighboring building within a second or
    // two; slamming into that neighbor's solid, non-convex collider caused
    // severe tunneling/depenetration blowups. This layer index doesn't need a
    // registered name in Project Settings to function - gameObject.layer is a
    // raw index either way. Must match Earthquake.BuildingLayer.
    public const int BuildingLayer = 9;

    private void Awake()
    {
        if (cityRoot == null)
        {
            var root = new GameObject("CityRoot");
            cityRoot = root.transform;
        }

        BuildCity();
    }

    private void BuildCity()
    {
        for (int row = 0; row < gridRows; row++)
        {
            for (int col = 0; col < gridColumns; col++)
            {
                var buildingAsset = PickRandomBuilding();
                if (buildingAsset == null)
                    continue;

                float worldX = originOffset.x + col * cellSpacing;
                float worldZ = originOffset.z + row * cellSpacing;
                float worldY = originOffset.y;

                if (terrain != null)
                    worldY += terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrain.transform.position.y;

                var rotation = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f);
                var building = Instantiate(buildingAsset, new Vector3(worldX, worldY, worldZ), rotation, cityRoot);
                building.name = buildingAsset.name;
                building.layer = BuildingLayer;

                SetupFracturable(building);
            }
        }
    }

    private GameObject PickRandomBuilding()
    {
        int commercialCount = commercialBuildings != null ? commercialBuildings.Length : 0;
        int suburbanCount = suburbanBuildings != null ? suburbanBuildings.Length : 0;
        int total = commercialCount + suburbanCount;
        if (total == 0)
            return null;

        int index = Random.Range(0, total);
        return index < commercialCount ? commercialBuildings[index] : suburbanBuildings[index - commercialCount];
    }

    private void SetupFracturable(GameObject building)
    {
        var meshFilter = building.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        var rigidbody = building.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = building.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;

        var meshCollider = building.GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = building.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.sharedMesh;
        meshCollider.convex = false;

        var fracture = building.GetComponent<Fracture>();
        if (fracture == null)
            fracture = building.AddComponent<Fracture>();

        // AddComponent<Fracture>() at runtime does not auto-construct these nested
        // [Serializable] option objects (Unity only does that via Editor/scene
        // deserialization), so they must be created explicitly here.
        if (fracture.fractureOptions == null)
            fracture.fractureOptions = new FractureOptions();
        if (fracture.triggerOptions == null)
            fracture.triggerOptions = new TriggerOptions();
        if (fracture.callbackOptions == null)
            fracture.callbackOptions = new CallbackOptions();
        if (fracture.callbackOptions.onCompleted == null)
            fracture.callbackOptions.onCompleted = new UnityEngine.Events.UnityEvent();
        if (fracture.refractureOptions == null)
            fracture.refractureOptions = new RefractureOptions();

        fracture.fractureOptions.fragmentCount = Random.Range(fragmentCountMin, fragmentCountMax + 1);
        fracture.fractureOptions.xAxis = true;
        fracture.fractureOptions.yAxis = true;
        fracture.fractureOptions.zAxis = true;
        // Synchronous: fragments for a building are all created and physics-fixed-up
        // (see Earthquake.DelayedFracture) within a single frame, before any physics
        // step can process a partially-created, overlapping fragment cluster. Building
        // collapses are already staggered in time by Earthquake, so this is safe.
        fracture.fractureOptions.asynchronous = false;
        fracture.fractureOptions.insideMaterial = insideMaterial;

        fracture.triggerOptions.triggerType = TriggerType.Keyboard;
        fracture.triggerOptions.triggerKey = KeyCode.None;
    }
}