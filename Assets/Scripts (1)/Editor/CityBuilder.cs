using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Aftershock city generator.
// Builds a fancy city block around the existing "Structure 01" building using the
// LowPolyRoadPack (highway + local streets) and the Kenney suburban kit (houses, fences,
// planters, trees, driveways, paths).  Everything is generated under a single parent so it is
// non-destructive and Undo-able.  The existing Structure 01 and Player are never moved.
//
// Menu:  Aftershock ▸ Build City Around Structure 01   (and a Clear command to remove it again)
//
// How it stays coherent across three asset packs that were authored at different scales:
// every prefab is measured (real renderer bounds) at import scale, then uniformly rescaled so
// its footprint matches a target derived from Structure 01's measured footprint.  Road tiles
// are all scaled by one shared factor so they still snap edge-to-edge.
public static class CityBuilder
{
    const string CityRoot = "Aftershock City";
    const string StructureName = "Structure 01";

    // Asset paths (verified to exist in this project).
    const string Roads = "Assets/LowPolyRoadPack/Prefabs/";
    const string Kenney = "Assets/Models/kenney_city-kit-suburban_20/Models/FBX format/";

    const string P_HighwayStraight = Roads + "Highway Straight 1.prefab";
    const string P_HighwayCurve    = Roads + "Highway Curve 1.prefab";
    const string P_HighwayJoin     = Roads + "Highway Join Road.prefab";
    const string P_RoadStraight    = Roads + "Road Straight 1.prefab";
    const string P_RoadCurve       = Roads + "Road Curve.prefab";
    const string P_Car1            = Roads + "Car 1.prefab";
    const string P_Car2            = Roads + "Car 2.prefab";

    static readonly string[] KenneyBuildings =
    {
        Kenney + "building-type-a.fbx", Kenney + "building-type-b.fbx",
        Kenney + "building-type-c.fbx", Kenney + "building-type-e.fbx",
        Kenney + "building-type-g.fbx", Kenney + "building-type-h.fbx",
        Kenney + "building-type-j.fbx", Kenney + "building-type-l.fbx",
    };
    const string P_Fence    = Kenney + "fence-1x4.fbx";
    const string P_Planter  = Kenney + "planter.fbx";
    const string P_TreeBig  = Kenney + "tree-large.fbx";
    const string P_TreeSmall= Kenney + "tree-small.fbx";
    const string P_Path     = Kenney + "path-stones-long.fbx";
    const string P_Driveway = Kenney + "driveway-long.fbx";

    static Transform s_root;
    static float s_groundY;
    static readonly Dictionary<string, Vector3> s_sizeCache = new Dictionary<string, Vector3>();

    [MenuItem("Aftershock/Build City Around Structure 01")]
    public static void Build()
    {
        var structure = GameObject.Find(StructureName);
        if (structure == null)
        {
            EditorUtility.DisplayDialog("City Builder",
                "Could not find a GameObject named \"" + StructureName + "\" in the open scene.\n\n" +
                "Open SampleScene first, then run this again.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Build City");
        int group = Undo.GetCurrentGroup();

        // Fresh start: remove any previously generated city so re-runs don't stack.
        var existing = GameObject.Find(CityRoot);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var rootGo = new GameObject(CityRoot);
        Undo.RegisterCreatedObjectUndo(rootGo, "Create City Root");
        s_root = rootGo.transform;
        s_sizeCache.Clear();

        // --- Establish scale + anchor from the existing building (which never moves) ---
        Bounds sb = WorldBounds(structure);
        s_groundY = sb.min.y;                          // top of the ground / base of buildings
        Vector2 center = new Vector2(sb.center.x, sb.center.z);
        float structFoot = Mathf.Max(sb.size.x, sb.size.z);
        if (structFoot < 0.01f) structFoot = 10f;      // safety if the building had no renderers

        // A "plot" is one grid cell big enough for a building + its yard + fence.
        float plot = structFoot * 1.9f;
        float streetWidth = structFoot * 0.9f;

        // --- Local street loop around a 3x3 block of plots (Structure 01 sits in the centre) ---
        float ringHalf = plot * 1.5f;                  // street centre-line distance from block centre
        BuildStreetLoop(center, ringHalf, streetWidth);

        // --- Buildings: keep Structure 01 in the centre plot, fill the other 8 plots ---
        int b = 0;
        for (int gx = -1; gx <= 1; gx++)
        for (int gz = -1; gz <= 1; gz++)
        {
            Vector2 p = center + new Vector2(gx, gz) * plot;
            bool isCentre = gx == 0 && gz == 0;

            if (!isCentre)
            {
                string bp = KenneyBuildings[b % KenneyBuildings.Length];
                b++;
                float yaw = FaceCentre(gx, gz);        // front door toward the block centre / street
                var house = Place(bp, p, yaw, FitScale(bp, plot * 0.5f));
                if (house != null) house.name = "House " + b;
            }

            // Every plot (including Structure 01's) gets a fence ring + landscaping = "fancier".
            DressPlot(p, plot, isCentre ? 0f : FaceCentre(gx, gz));
        }

        // --- Sophisticated highway running along the north edge, past the block ---
        BuildHighway(center, ringHalf, plot);

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = rootGo;
        EditorSceneManager.MarkSceneDirty(structure.scene);
        Debug.Log("[CityBuilder] Built '" + CityRoot + "': street loop, highway, " + b +
                  " new buildings with fences + landscaping. Structure 01 and Player untouched.");
    }

    [MenuItem("Aftershock/Clear Generated City")]
    public static void Clear()
    {
        var existing = GameObject.Find(CityRoot);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(existing.scene);
        }
    }

    // ------------------------------------------------------------------ roads

    static void BuildStreetLoop(Vector2 c, float half, float width)
    {
        Vector3 rs = ScaledSize(P_RoadStraight, RoadScale(width));
        if (rs == Vector3.zero) return;
        float tile = Mathf.Max(rs.x, rs.z);            // length along the run
        float scale = RoadScale(width);

        // Four straight edges (leave the corners for curve pieces).
        FillLine(P_RoadStraight, new Vector2(c.x - half, c.y + half), new Vector2(c.x + half, c.y + half), tile, scale, true);   // north
        FillLine(P_RoadStraight, new Vector2(c.x - half, c.y - half), new Vector2(c.x + half, c.y - half), tile, scale, true);   // south
        FillLine(P_RoadStraight, new Vector2(c.x - half, c.y - half), new Vector2(c.x - half, c.y + half), tile, scale, false);  // west
        FillLine(P_RoadStraight, new Vector2(c.x + half, c.y - half), new Vector2(c.x + half, c.y + half), tile, scale, false);  // east

        // Corner curves.
        Place(P_RoadCurve, new Vector2(c.x - half, c.y + half), 0f,   scale);
        Place(P_RoadCurve, new Vector2(c.x + half, c.y + half), 90f,  scale);
        Place(P_RoadCurve, new Vector2(c.x + half, c.y - half), 180f, scale);
        Place(P_RoadCurve, new Vector2(c.x - half, c.y - half), 270f, scale);
    }

    static void BuildHighway(Vector2 c, float ringHalf, float plot)
    {
        float width = plot * 0.5f;                     // highways read wider than local streets
        float scale = RoadScale(width, P_HighwayStraight);
        Vector3 hs = ScaledSize(P_HighwayStraight, scale);
        if (hs == Vector3.zero) return;
        float tile = Mathf.Max(hs.x, hs.z);

        float z = c.y + ringHalf + plot * 1.1f;        // north of the street loop, with a gap
        float span = ringHalf * 2f + plot * 2f;
        var a = new Vector2(c.x - span * 0.5f, z);
        var b = new Vector2(c.x + span * 0.5f, z);
        FillLine(P_HighwayStraight, a, b, tile, scale, true);

        // Curves turning the highway southward at each end, and an on-ramp toward the block.
        Place(P_HighwayCurve, new Vector2(a.x - tile * 0.5f, z), 270f, scale);
        Place(P_HighwayCurve, new Vector2(b.x + tile * 0.5f, z), 180f, scale);
        Place(P_HighwayJoin,  new Vector2(c.x, z - tile * 0.6f), 180f, scale);   // ramp down to the streets

        // A little traffic so the city feels alive.
        Place(P_Car1, new Vector2(c.x - tile, z), 90f, scale * 0.8f);
        Place(P_Car2, new Vector2(c.x + tile * 0.4f, z), 90f, scale * 0.8f);
        Place(P_Car1, new Vector2(c.x + ringHalf, c.y - ringHalf), 0f, scale * 0.8f);
    }

    // Fill the segment A->B with as many tiles as fit, snapped edge to edge. The yaw is chosen
    // so each tile's LONG axis lies along the run, whatever the mesh's native orientation is.
    static void FillLine(string prefab, Vector2 a, Vector2 b, float tile, float scale, bool alongX)
    {
        float len = Vector2.Distance(a, b);
        int n = Mathf.Max(1, Mathf.RoundToInt(len / tile));
        float step = len / n;
        Vector2 dir = (b - a).normalized;
        float yaw = AlignYaw(prefab, alongX);
        for (int i = 0; i < n; i++)
        {
            Vector2 p = a + dir * (step * (i + 0.5f));
            Place(prefab, p, yaw, scale);
        }
    }

    // Yaw that puts the prefab's longest horizontal axis along the run direction.
    static float AlignYaw(string prefab, bool alongX)
    {
        Vector3 s = ScaledSize(prefab, 1f);
        bool nativeLongIsX = s.x >= s.z;
        if (alongX) return nativeLongIsX ? 0f : 90f;   // want long axis along X
        return nativeLongIsX ? 90f : 0f;               // want long axis along Z
    }

    // ------------------------------------------------------------------ plots / fences / props

    static void DressPlot(Vector2 c, float plot, float frontYaw)
    {
        float half = plot * 0.34f;                     // fence sits inside the plot, around the yard
        float fenceScale = FitScale(P_Fence, half * 0.9f, true);   // scale by length, not footprint
        Vector3 fs = ScaledSize(P_Fence, fenceScale);
        float fenceLen = Mathf.Max(fs.x, fs.z, 0.1f);

        // Ring the four sides; leave a gap on the front (street-facing) side for a gate.
        FenceEdge(new Vector2(c.x - half, c.y + half), new Vector2(c.x + half, c.y + half), fenceLen, fenceScale, true,  frontYaw == 0f);
        FenceEdge(new Vector2(c.x - half, c.y - half), new Vector2(c.x + half, c.y - half), fenceLen, fenceScale, true,  frontYaw == 180f);
        FenceEdge(new Vector2(c.x - half, c.y - half), new Vector2(c.x - half, c.y + half), fenceLen, fenceScale, false, frontYaw == 270f);
        FenceEdge(new Vector2(c.x + half, c.y - half), new Vector2(c.x + half, c.y + half), fenceLen, fenceScale, false, frontYaw == 90f);

        // Landscaping: planters at the four corners, a couple of trees, a path + driveway.
        float pScale = FitScale(P_Planter, plot * 0.12f);
        Place(P_Planter, new Vector2(c.x - half, c.y - half), 0f, pScale);
        Place(P_Planter, new Vector2(c.x + half, c.y - half), 0f, pScale);
        Place(P_TreeBig,   new Vector2(c.x - half * 0.6f, c.y + half * 0.6f), 0f, FitScale(P_TreeBig,   plot * 0.22f));
        Place(P_TreeSmall, new Vector2(c.x + half * 0.6f, c.y + half * 0.6f), 0f, FitScale(P_TreeSmall, plot * 0.16f));

        Vector2 front = c + Dir(frontYaw) * half;      // toward the street
        Place(P_Path,     Vector2.Lerp(c, front, 0.7f), frontYaw, FitScale(P_Path,     half * 0.5f, true));
        Place(P_Driveway, front + Dir(frontYaw) * (half * 0.4f), frontYaw, FitScale(P_Driveway, half * 0.6f, true));
    }

    static void FenceEdge(Vector2 a, Vector2 b, float tile, float scale, bool alongX, bool hasGate)
    {
        float len = Vector2.Distance(a, b);
        int n = Mathf.Max(1, Mathf.RoundToInt(len / tile));
        float step = len / n;
        Vector2 dir = (b - a).normalized;
        float yaw = AlignYaw(P_Fence, alongX);
        int gate = hasGate ? n / 2 : -1;               // skip the middle tile for an entrance
        for (int i = 0; i < n; i++)
        {
            if (i == gate) continue;
            Vector2 p = a + dir * (step * (i + 0.5f));
            Place(P_Fence, p, yaw, scale);
        }
    }

    // ------------------------------------------------------------------ helpers

    // Yaw so a plot on the grid faces the block centre (and thus the nearest street).
    static float FaceCentre(int gx, int gz)
    {
        if (gz > 0) return 180f;
        if (gz < 0) return 0f;
        if (gx > 0) return 270f;
        return 90f;
    }

    static Vector2 Dir(float yaw)
    {
        float r = yaw * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
    }

    static float RoadScale(float targetWidth, string prefab = P_RoadStraight)
    {
        Vector3 s = ScaledSize(prefab, 1f);
        float w = Mathf.Min(s.x, s.z);
        return w < 0.001f ? 1f : targetWidth / w;
    }

    // Uniform scale so the prefab's footprint (or longest axis, if byLength) hits target size.
    static float FitScale(string prefab, float target, bool byLength = false)
    {
        Vector3 s = ScaledSize(prefab, 1f);
        float dim = byLength ? Mathf.Max(s.x, s.z) : Mathf.Max(s.x, s.z);
        return dim < 0.001f ? 1f : target / dim;
    }

    // Size of a prefab's renderer bounds at a given uniform scale (measured once, cached).
    static Vector3 ScaledSize(string path, float scale)
    {
        if (!s_sizeCache.TryGetValue(path, out Vector3 native))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { s_sizeCache[path] = Vector3.zero; return Vector3.zero; }
            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            tmp.transform.position = Vector3.zero;
            tmp.transform.rotation = Quaternion.identity;
            tmp.transform.localScale = Vector3.one;
            native = WorldBounds(tmp).size;
            Object.DestroyImmediate(tmp);
            s_sizeCache[path] = native;
        }
        return native * scale;
    }

    // Instantiate a prefab, scale it, face it, and drop it so its base rests on the ground.
    static GameObject Place(string path, Vector2 xz, float yaw, float scale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogWarning("[CityBuilder] Missing asset: " + path); return null; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Place " + go.name);
        go.transform.SetParent(s_root, true);
        go.transform.localScale = Vector3.one * scale;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Bounds bb = WorldBounds(go);
        Vector3 pos = go.transform.position;
        pos.x += xz.x - bb.center.x;
        pos.z += xz.y - bb.center.z;
        pos.y += s_groundY - bb.min.y;
        go.transform.position = pos;
        return go;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }
}
