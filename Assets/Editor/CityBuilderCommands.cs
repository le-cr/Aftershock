using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aftershock.Editor
{
    /// <summary>
    /// Lays out a city on the flat part of the map: a grid of roads with sidewalks, blocks filled
    /// with fracturable Kenney buildings (same pipeline as the hand-placed ones, so the earthquake
    /// brings them down and the wildfire sets them alight), and street trees.
    ///
    /// Roads and sidewalks are generated meshes with generated textures, saved under
    /// Assets/Models/Generated/City so the scene survives a reload. Shelters, the helicopter
    /// platform and the player spawn are treated as obstacles: road segments and lots that would
    /// cross them are skipped.
    /// </summary>
    public static class CityBuilderCommands
    {
        const string k_Folder = "Assets/Models/Generated/City";
        const string k_Commercial = "Assets/Models/Commercial/";
        const string k_Suburban = "Assets/Models/Suburban/";

        class Obstacle
        {
            public string name;
            public Rect rect;       // x/z footprint, already padded
        }

        struct Lot
        {
            public Rect rect;
            public float yaw;       // faces the road
            public bool tall;       // inner city
        }

        [CliCommand("build_city", "Build the road grid, sidewalks, fracturable buildings and street trees on the flat ground. Replaces any previous city and the five hand-placed buildings.")]
        public static object Build(
            [CliArg("seed", "Random seed for building choice and placement.")] int seed = 7,
            [CliArg("road_width", "Asphalt width in metres.")] float roadWidth = 6f,
            [CliArg("sidewalk_width", "Sidewalk width in metres, each side.")] float sidewalkWidth = 1.6f,
            [CliArg("building_scale", "Largest scale a building gets; smaller lots shrink it.")] float buildingScale = 8f,
            [CliArg("fragment_count", "Fragments each building breaks into.")] int fragmentCount = 36,
            [CliArg("flatten_terrain", "Flatten the terrain under the city footprint.")] bool flattenTerrain = true)
        {
            var log = new List<string>();
            var rng = new System.Random(seed);

            // Road centre-lines. Chosen to thread between the shelters and the helicopter pad.
            float[] xRoads = { -49.5f, -26f, 9f, 30f };
            float[] zRoads = { -32f, -8f, 16f, 40f };
            var city = new Rect(xRoads[0] - 14f, zRoads[0] - 8f, xRoads[^1] - xRoads[0] + 28f, zRoads[^1] - zRoads[0] + 24f);

            var scene = GameObject.Find("/Scene");
            if (scene == null) throw new System.InvalidOperationException("No /Scene root in the open scene.");

            // --- Obstacles ------------------------------------------------------------------
            var obstacles = new List<Obstacle>();
            foreach (var name in new[] { "Shelter (1)", "Shelter (2)", "Shelter (3)", "HelicopterPlatform" })
            {
                var go = GameObject.Find(name);
                if (go == null) continue;
                var b = WorldBounds(go);
                float pad = name == "HelicopterPlatform" ? 2f : 1f;
                obstacles.Add(new Obstacle { name = name, rect = Rect.MinMaxRect(b.min.x - pad, b.min.z - pad, b.max.x + pad, b.max.z + pad) });
            }
            var player = GameObject.Find("/Player");
            if (player != null)
            {
                var p = player.transform.position;
                obstacles.Add(new Obstacle { name = "Player", rect = Rect.MinMaxRect(p.x - 2f, p.z - 2f, p.x + 2f, p.z + 2f) });
            }

            // --- Terrain ----------------------------------------------------------------------
            if (flattenTerrain)
            {
                var terrain = Object.FindFirstObjectByType<Terrain>();
                if (terrain != null)
                {
                    Flatten(terrain, city, 14f);
                    log.Add($"terrain flattened over {city}");
                }
            }

            // --- Clear the previous city and the hand-placed buildings --------------------------
            var old = scene.transform.Find("City");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            foreach (var b in Object.FindObjectsByType<BuildingCollapse>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b.transform.parent == scene.transform)
                {
                    log.Add($"removed {b.name}");
                    Undo.DestroyObjectImmediate(b.gameObject);
                }
            }

            var cityGo = new GameObject("City");
            Undo.RegisterCreatedObjectUndo(cityGo, "City");
            cityGo.transform.SetParent(scene.transform, false);
            var roadsGo = Child(cityGo, "Roads");
            var buildingsGo = Child(cityGo, "Buildings");
            var propsGo = Child(cityGo, "Props");

            if (!AssetDatabase.IsValidFolder("Assets/Models/Generated")) AssetDatabase.CreateFolder("Assets/Models", "Generated");
            if (!AssetDatabase.IsValidFolder(k_Folder)) AssetDatabase.CreateFolder("Assets/Models/Generated", "City");

            // --- Roads --------------------------------------------------------------------------
            var asphaltLines = RoadMaterial("RoadAsphalt", withLines: true);
            var asphaltPlain = RoadMaterial("RoadIntersection", withLines: false);
            var concrete = ConcreteMaterial("Sidewalk");

            var roadMesh = new MeshBuilder();
            var crossMesh = new MeshBuilder();
            var walkMesh = new MeshBuilder();
            float half = roadWidth * 0.5f;
            float walkY = 0.12f;
            int segments = 0;

            // Segments along X (between consecutive x nodes) at each z road, and along Z at each x road.
            for (int zi = 0; zi < zRoads.Length; zi++)
                for (int xi = 0; xi + 1 < xRoads.Length; xi++)
                {
                    var seg = Rect.MinMaxRect(xRoads[xi] + half, zRoads[zi] - half, xRoads[xi + 1] - half, zRoads[zi] + half);
                    if (Blocked(seg, obstacles, out var why)) { log.Add($"road x-seg at z={zRoads[zi]} skipped: {why}"); continue; }
                    roadMesh.AddQuad(seg, 0.03f, alongX: true, tile: (seg.width) / 8f);
                    walkMesh.AddQuad(Rect.MinMaxRect(seg.xMin, seg.yMax, seg.xMax, seg.yMax + sidewalkWidth), walkY, true, seg.width / 2f);
                    walkMesh.AddQuad(Rect.MinMaxRect(seg.xMin, seg.yMin - sidewalkWidth, seg.xMax, seg.yMin), walkY, true, seg.width / 2f);
                    walkMesh.AddCurb(seg.xMin, seg.xMax, seg.yMax, walkY, alongX: true, facing: -1);
                    walkMesh.AddCurb(seg.xMin, seg.xMax, seg.yMin, walkY, alongX: true, facing: 1);
                    segments++;
                }
            for (int xi = 0; xi < xRoads.Length; xi++)
                for (int zi = 0; zi + 1 < zRoads.Length; zi++)
                {
                    var seg = Rect.MinMaxRect(xRoads[xi] - half, zRoads[zi] + half, xRoads[xi] + half, zRoads[zi + 1] - half);
                    if (Blocked(seg, obstacles, out var why)) { log.Add($"road z-seg at x={xRoads[xi]} skipped: {why}"); continue; }
                    roadMesh.AddQuad(seg, 0.03f, alongX: false, tile: seg.height / 8f);
                    walkMesh.AddQuad(Rect.MinMaxRect(seg.xMax, seg.yMin, seg.xMax + sidewalkWidth, seg.yMax), walkY, false, seg.height / 2f);
                    walkMesh.AddQuad(Rect.MinMaxRect(seg.xMin - sidewalkWidth, seg.yMin, seg.xMin, seg.yMax), walkY, false, seg.height / 2f);
                    walkMesh.AddCurb(seg.yMin, seg.yMax, seg.xMax, walkY, alongX: false, facing: -1);
                    walkMesh.AddCurb(seg.yMin, seg.yMax, seg.xMin, walkY, alongX: false, facing: 1);
                    segments++;
                }
            foreach (var x in xRoads)
                foreach (var z in zRoads)
                {
                    var node = Rect.MinMaxRect(x - half, z - half, x + half, z + half);
                    if (Blocked(node, obstacles, out _)) continue;
                    crossMesh.AddQuad(node, 0.03f, true, 1f);
                    // Corner sidewalk squares.
                    foreach (var sx in new[] { -1, 1 })
                        foreach (var sz in new[] { -1, 1 })
                        {
                            float cx = x + sx * half, cz = z + sz * half;
                            var corner = Rect.MinMaxRect(Mathf.Min(cx, cx + sx * sidewalkWidth), Mathf.Min(cz, cz + sz * sidewalkWidth), Mathf.Max(cx, cx + sx * sidewalkWidth), Mathf.Max(cz, cz + sz * sidewalkWidth));
                            walkMesh.AddQuad(corner, walkY, true, 1f);
                        }
                }

            MakeMeshObject(roadsGo, "Asphalt", roadMesh.Build("CityRoads"), asphaltLines);
            MakeMeshObject(roadsGo, "Intersections", crossMesh.Build("CityIntersections"), asphaltPlain);
            MakeMeshObject(roadsGo, "Sidewalks", walkMesh.Build("CitySidewalks"), concrete);
            log.Add($"{segments} road segments");

            // --- Lots -----------------------------------------------------------------------------
            float setback = half + sidewalkWidth + 0.8f;       // building edge from road centre
            var lots = new List<Lot>();

            // Inner blocks: as many lots across as fit, in two rows (facing the roads north and
            // south) when the block is deep enough, otherwise one row facing alternating sides.
            const float lotWidth = 7.5f;
            for (int xi = 0; xi + 1 < xRoads.Length; xi++)
                for (int zi = 0; zi + 1 < zRoads.Length; zi++)
                {
                    var block = Rect.MinMaxRect(xRoads[xi] + setback, zRoads[zi] + setback, xRoads[xi + 1] - setback, zRoads[zi + 1] - setback);
                    bool tall = xi >= 1 && zi >= 1;                                       // north-east quarter is downtown
                    int across = Mathf.Max(1, Mathf.FloorToInt(block.width / lotWidth));
                    int rows = block.height >= 13f ? 2 : 1;
                    float lw = block.width / across - 0.4f;
                    float lh = block.height / rows - 0.4f;
                    for (int a = 0; a < across; a++)
                        for (int r = 0; r < rows; r++)
                        {
                            float yaw = rows == 2 ? (r == 0 ? 180f : 0f) : ((xi + zi) % 2 == 0 ? 180f : 0f);
                            lots.Add(new Lot { rect = new Rect(block.xMin + a * block.width / across, block.yMin + r * block.height / rows, lw, lh), yaw = yaw, tall = tall });
                        }
                }

            // Outer ring: a row of lots outside the outermost roads, facing inward.
            float ringDepth = 11f;
            for (int xi = 0; xi + 1 < xRoads.Length; xi++)
            {
                float x0 = xRoads[xi] + setback, x1 = xRoads[xi + 1] - setback;
                int across = Mathf.Max(1, Mathf.FloorToInt((x1 - x0) / lotWidth));
                for (int k = 0; k < across; k++)
                {
                    float lx = x0 + k * (x1 - x0) / across, lw = (x1 - x0) / across - 0.4f;
                    lots.Add(new Lot { rect = new Rect(lx, zRoads[0] - setback - ringDepth, lw, ringDepth), yaw = 0f, tall = false });
                    lots.Add(new Lot { rect = new Rect(lx, zRoads[^1] + setback, lw, ringDepth), yaw = 180f, tall = false });
                }
            }
            for (int zi = 0; zi + 1 < zRoads.Length; zi++)
            {
                float z0 = zRoads[zi] + setback, z1 = zRoads[zi + 1] - setback;
                int across = Mathf.Max(1, Mathf.FloorToInt((z1 - z0) / lotWidth));
                for (int k = 0; k < across; k++)
                {
                    float lz = z0 + k * (z1 - z0) / across, lh = (z1 - z0) / across - 0.4f;
                    lots.Add(new Lot { rect = new Rect(xRoads[0] - setback - ringDepth, lz, ringDepth, lh), yaw = 90f, tall = false });
                    lots.Add(new Lot { rect = new Rect(xRoads[^1] + setback, lz, ringDepth, lh), yaw = -90f, tall = false });
                }
            }

            // --- Buildings ----------------------------------------------------------------------
            string[] lowRise = { "building-a", "building-b", "building-c", "building-d", "building-e", "building-f", "building-g", "building-h", "building-i", "building-k" };
            string[] midRise = { "building-j", "building-l", "building-m", "building-n", "building-f", "building-g", "building-i" };
            string[] highRise = { "building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c", "building-skyscraper-d", "building-skyscraper-e", "building-m", "building-l" };

            int built = 0, skipped = 0;
            foreach (var lot in lots)
            {
                if (Blocked(lot.rect, obstacles, out _)) { skipped++; continue; }
                if (rng.NextDouble() < 0.06) { skipped++; continue; }          // the odd empty lot: parking, a yard

                string[] pool = lot.tall ? (rng.NextDouble() < 0.6 ? highRise : midRise)
                              : (rng.NextDouble() < 0.25 ? midRise : lowRise);
                bool swap = Mathf.Abs(Mathf.Repeat(lot.yaw, 180f) - 90f) < 1f;    // 90/-90 swaps x and z

                // Try a few models from the pool and keep the one that fills the lot best.
                string modelName = null, modelPath = null;
                GameObject model = null;
                Bounds mb = default;
                float fx = 0f, fz = 0f, scale = 0f;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    string candidate = pool[rng.Next(pool.Length)];
                    string candidatePath = k_Commercial + candidate + ".fbx";
                    var candidateModel = AssetDatabase.LoadAssetAtPath<GameObject>(candidatePath);
                    if (candidateModel == null) continue;
                    var cb = WorldBounds(candidateModel);
                    float cfx = swap ? cb.size.z : cb.size.x, cfz = swap ? cb.size.x : cb.size.z;
                    float cs = Mathf.Min(buildingScale, lot.rect.width / cfx * 0.95f, lot.rect.height / cfz * 0.95f);
                    if (cs > scale)
                    {
                        modelName = candidate; modelPath = candidatePath; model = candidateModel; mb = cb; fx = cfx; fz = cfz; scale = cs;
                    }
                    if (scale >= buildingScale * 0.9f) break;
                }
                if (model == null || scale < 4f) { skipped++; continue; }

                // Sit the building on its lot edge nearest the road it faces, centred across.
                float cx = lot.rect.center.x, cz = lot.rect.center.y;
                float depth = fz * scale;
                if (lot.yaw == 180f) cz = lot.rect.yMin + depth * 0.5f;
                else if (lot.yaw == 0f) cz = lot.rect.yMax - depth * 0.5f;
                else if (lot.yaw == 90f) cx = lot.rect.xMax - fx * scale * 0.5f;
                else cx = lot.rect.xMin + fx * scale * 0.5f;

                // Model pivots sit at the footprint centre; offset by the bounds centre so it lands centred.
                var pivotOffset = Quaternion.Euler(0f, lot.yaw, 0f) * new Vector3(mb.center.x, 0f, mb.center.z) * scale;
                var pos = new Vector3(cx, 0f, cz) - pivotOffset;

                string name = $"Building_{built:00}_{modelName.Replace("building-", "")}";
                CollapsingBuildingCommands.Create(modelPath, name, buildingsGo.transform, pos, lot.yaw, scale, fragmentCount, "Assets/Materials/FractureInside.mat", "T", replaceExisting: false);
                built++;
            }
            log.Add($"{built} buildings placed, {skipped} lots left empty");

            // --- Street trees ---------------------------------------------------------------------
            var treeLarge = AssetDatabase.LoadAssetAtPath<GameObject>(k_Suburban + "tree-large.fbx");
            var treeSmall = AssetDatabase.LoadAssetAtPath<GameObject>(k_Suburban + "tree-small.fbx");
            int trees = 0;
            if (treeLarge != null && treeSmall != null)
            {
                float treeOffset = half + sidewalkWidth + 1.1f;     // just inside the lot line, outside the sidewalk
                foreach (var z in zRoads)
                    for (float x = xRoads[0] + 6f; x < xRoads[^1] - 4f; x += 9f)
                        foreach (var side in new[] { -1f, 1f })
                            trees += PlaceTree(propsGo.transform, rng.NextDouble() < 0.6 ? treeLarge : treeSmall, new Vector3(x, 0f, z + side * treeOffset), rng, obstacles, xRoads, zRoads, half + sidewalkWidth) ? 1 : 0;
                foreach (var x in xRoads)
                    for (float z = zRoads[0] + 6f; z < zRoads[^1] - 4f; z += 9f)
                        foreach (var side in new[] { -1f, 1f })
                            trees += PlaceTree(propsGo.transform, rng.NextDouble() < 0.6 ? treeLarge : treeSmall, new Vector3(x + side * treeOffset, 0f, z), rng, obstacles, xRoads, zRoads, half + sidewalkWidth) ? 1 : 0;
            }
            log.Add($"{trees} street trees");

            // Buildings are static scenery until they fracture; trees and roads always are.
            foreach (var t in cityGo.GetComponentsInChildren<Transform>())
                if (t.GetComponent<BuildingCollapse>() == null)
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(cityGo.scene);
            EditorSceneManager.SaveScene(cityGo.scene);
            return new { changes = log, buildings = built, trees, roadSegments = segments };
        }

        // ---------------------------------------------------------------------------------------

        static bool PlaceTree(Transform parent, GameObject model, Vector3 pos, System.Random rng, List<Obstacle> obstacles, float[] xRoads, float[] zRoads, float corridor)
        {
            // Keep out of intersections, obstacles, and off the road corridor of the crossing street.
            foreach (var x in xRoads) if (Mathf.Abs(pos.x - x) < corridor + 0.5f) return false;
            foreach (var z in zRoads) if (Mathf.Abs(pos.z - z) < corridor + 0.5f) return false;
            if (Blocked(new Rect(pos.x - 1f, pos.z - 1f, 2f, 2f), obstacles, out _)) return false;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.localScale = Vector3.one * (7f + (float)rng.NextDouble() * 2.5f);
            Undo.RegisterCreatedObjectUndo(go, "Tree");
            return true;
        }

        static bool Blocked(Rect r, List<Obstacle> obstacles, out string why)
        {
            foreach (var o in obstacles)
                if (r.Overlaps(o.rect)) { why = o.name; return true; }
            why = null;
            return false;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static void MakeMeshObject(GameObject parent, string name, Mesh mesh, Material material)
        {
            var path = $"{k_Folder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = Child(parent, name);
            go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Flatten the terrain to height 0 inside the rect, feathering out over `feather` metres.</summary>
        static void Flatten(Terrain terrain, Rect rect, float feather)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var tpos = terrain.transform.position;
            var size = data.size;

            for (int iy = 0; iy < res; iy++)
                for (int ix = 0; ix < res; ix++)
                {
                    float wx = tpos.x + ix / (float)(res - 1) * size.x;
                    float wz = tpos.z + iy / (float)(res - 1) * size.z;
                    float dx = Mathf.Max(rect.xMin - wx, wx - rect.xMax, 0f);
                    float dz = Mathf.Max(rect.yMin - wz, wz - rect.yMax, 0f);
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d >= feather) continue;
                    float t = d / feather;
                    t = t * t * (3f - 2f * t);
                    heights[iy, ix] = Mathf.Lerp(0f, heights[iy, ix], t);
                }

            Undo.RegisterCompleteObjectUndo(data, "Flatten terrain");
            data.SetHeights(0, 0, heights);
            EditorUtility.SetDirty(data);
        }

        // --- Materials and textures --------------------------------------------------------------

        static Material RoadMaterial(string name, bool withLines)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{k_Folder}/{name}.png");
            if (tex == null)
            {
                tex = AsphaltTexture(withLines);
                var bytes = tex.EncodeToPNG();
                System.IO.File.WriteAllBytes($"{k_Folder}/{name}.png", bytes);
                AssetDatabase.ImportAsset($"{k_Folder}/{name}.png");
                var importer = AssetImporter.GetAtPath($"{k_Folder}/{name}.png") as TextureImporter;
                if (importer != null) { importer.wrapMode = TextureWrapMode.Repeat; importer.SaveAndReimport(); }
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{k_Folder}/{name}.png");
            }

            var matPath = $"{k_Folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material ConcreteMaterial(string name)
        {
            var matPath = $"{k_Folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetColor("_BaseColor", new Color(0.72f, 0.71f, 0.68f));
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// One road tile: dark speckled asphalt, optionally with a dashed yellow centre line and
        /// solid white edge lines. U runs across the road, V along it; the mesh tiles V.
        /// </summary>
        static Texture2D AsphaltTexture(bool withLines)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var rng = new System.Random(3);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = 0.20f + (float)rng.NextDouble() * 0.05f;
                    var c = new Color(n, n, n * 1.03f, 1f);
                    if (withLines)
                    {
                        float u = x / (float)size, v = y / (float)size;
                        bool centre = Mathf.Abs(u - 0.5f) < 0.012f && Mathf.Repeat(v * 4f, 1f) < 0.55f;
                        bool edge = u < 0.035f && u > 0.02f || u > 0.965f && u < 0.98f;
                        if (centre) c = new Color(0.85f, 0.72f, 0.2f, 1f);
                        else if (edge) c = new Color(0.8f, 0.8f, 0.78f, 1f);
                    }
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Accumulates flat quads (and little curb faces) into one mesh.</summary>
        class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> tri = new List<int>();

            /// <summary>Flat quad over an x/z rect. When alongX the texture's V runs along X.</summary>
            public void AddQuad(Rect r, float y, bool alongX, float tile)
            {
                int i = v.Count;
                v.Add(new Vector3(r.xMin, y, r.yMin)); v.Add(new Vector3(r.xMin, y, r.yMax));
                v.Add(new Vector3(r.xMax, y, r.yMax)); v.Add(new Vector3(r.xMax, y, r.yMin));
                if (alongX)
                {
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, tile)); uv.Add(new Vector2(0f, tile));
                }
                else
                {
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, tile)); uv.Add(new Vector2(1f, tile)); uv.Add(new Vector2(1f, 0f));
                }
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            /// <summary>A short vertical face along a line from a to b at the sidewalk edge, facing the road.</summary>
            public void AddCurb(float a, float b, float at, float height, bool alongX, int facing)
            {
                int i = v.Count;
                if (alongX)
                {
                    v.Add(new Vector3(a, 0f, at)); v.Add(new Vector3(a, height, at)); v.Add(new Vector3(b, height, at)); v.Add(new Vector3(b, 0f, at));
                }
                else
                {
                    v.Add(new Vector3(at, 0f, a)); v.Add(new Vector3(at, height, a)); v.Add(new Vector3(at, height, b)); v.Add(new Vector3(at, 0f, b));
                }
                uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one); uv.Add(Vector2.right);
                bool flip = alongX ? facing > 0 : facing < 0;
                if (flip) tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v);
                m.SetUVs(0, uv);
                m.SetTriangles(tri, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
