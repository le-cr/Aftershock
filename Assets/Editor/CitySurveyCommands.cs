using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Aftershock.Editor
{
    public static class CitySurveyCommands
    {
        [CliCommand("capture_from", "Render a PNG from a temporary camera at a position looking at a target. Works in edit mode.")]
        public static object CaptureFrom(
            [CliArg("path", "Project-relative PNG path.")] string path = "Temp/view.png",
            [CliArg("x", "Camera X")] float x = -20f, [CliArg("y", "Camera Y")] float y = 120f, [CliArg("z", "Camera Z")] float z = 0f,
            [CliArg("tx", "Target X")] float tx = -20f, [CliArg("ty", "Target Y")] float ty = 0f, [CliArg("tz", "Target Z")] float tz = 1f,
            [CliArg("fov", "Vertical field of view")] float fov = 60f,
            [CliArg("width", "Image width")] int width = 1400, [CliArg("height", "Image height")] int height = 900)
        {
            var go = new GameObject("CaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            var rt = new RenderTexture(width, height, 24);
            try
            {
                cam.transform.position = new Vector3(x, y, z);
                cam.transform.LookAt(new Vector3(tx, ty, tz));
                cam.fieldOfView = fov;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
            }
            return new { path };
        }

        [CliCommand("city_survey", "Report terrain heights over a region, bounds of key scene objects, and model footprint sizes.")]
        public static object Survey(
            [CliArg("min_x","Region min X")] float minX = -80f, [CliArg("max_x","Region max X")] float maxX = 45f,
            [CliArg("min_z","Region min Z")] float minZ = -40f, [CliArg("max_z","Region max Z")] float maxZ = 75f,
            [CliArg("step","Sample spacing")] float step = 16f)
        {
            var terrain = Object.FindFirstObjectByType<Terrain>();
            var rows = new List<string>();
            if (terrain != null)
            {
                for (float z = maxZ; z >= minZ; z -= step)
                {
                    var cells = new List<string>();
                    for (float x = minX; x <= maxX; x += step)
                        cells.Add((terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y).ToString("0.0"));
                    rows.Add($"z={z,5}: " + string.Join(" ", cells));
                }
            }

            var objects = new List<object>();
            foreach (var name in new[] { "Shelter (1)", "Shelter (2)", "Shelter (3)", "HelicopterPlatform", "Player", "Flood" })
            {
                var go = GameObject.Find(name);
                if (go == null) continue;
                var rs = go.GetComponentsInChildren<Renderer>();
                Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                objects.Add(new { name, pos = go.transform.position.ToString(), min = b.min.ToString(), max = b.max.ToString() });
            }

            var models = new List<object>();
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models/Commercial", "Assets/Models/Suburban" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                models.Add(new { name = go.name, size = b.size.ToString("0.00"), min = b.min.ToString("0.00") });
            }

            var terrainInfo = terrain != null ? new
            {
                pos = terrain.transform.position.ToString(),
                size = terrain.terrainData.size.ToString(),
                heightmapRes = terrain.terrainData.heightmapResolution,
                asset = AssetDatabase.GetAssetPath(terrain.terrainData),
            } : null;

            return new { terrain = terrainInfo, heights = rows, objects, models };
        }
    }
}
