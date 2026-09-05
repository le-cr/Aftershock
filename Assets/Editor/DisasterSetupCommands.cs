using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Aftershock.Editor
{
    /// <summary>
    /// Wires the disaster-realism components into the open scene: the atmosphere controller and
    /// its per-disaster looks, the cold screen tint, and the particle materials / smoke-variant
    /// fire prefabs each disaster needs. Idempotent.
    /// </summary>
    public static class DisasterSetupCommands
    {
        const string k_Sky = "Assets/Materials/Skyboxes/";
        const string k_Vfx = "Assets/Vefects/Free Fire VFX URP/";

        [CliCommand("setup_disaster_realism", "Add DisasterAtmosphere with per-disaster looks, a cold tint panel, and wire particle materials and smoke fire prefabs into the disaster managers.")]
        public static object Setup()
        {
            var log = new List<string>();

            var dmGo = GameObject.Find("/DisasterManager");
            if (dmGo == null) throw new System.InvalidOperationException("No /DisasterManager in the open scene.");

            // --- Atmosphere -----------------------------------------------------------------
            var atmosphere = dmGo.GetComponent<DisasterAtmosphere>() ?? Undo.AddComponent<DisasterAtmosphere>(dmGo);
            var so = new SerializedObject(atmosphere);

            var sun = RenderSettings.sun;
            if (sun == null) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            so.FindProperty("sun").objectReferenceValue = sun;
            so.FindProperty("volume").objectReferenceValue = Object.FindFirstObjectByType<Volume>();

            var skyClear = Load<Material>(k_Sky + "Cartoon Base BlueSky/Day_BlueSky_Nothing.mat");
            var skyOvercast = Load<Material>(k_Sky + "Overcast Low/AllSky_Overcast4_Low.mat");
            var skySunset = Load<Material>(k_Sky + "Cold Sunset/Cold Sunset.mat");
            var skyDusk = Load<Material>(k_Sky + "Deep Dusk/Deep Dusk.mat");

            // Clear: the scene's normal look.
            SetLook(so.FindProperty("clear"), skyClear, 1f, Grey(0.5f), new Color(0.74f, 0.84f, 0.95f), 80f, 320f,
                new Color(1f, 0.96f, 0.84f), 1.74f, 1f, 6f, 0f, 0.35f, 0.22f);

            // Flood: storm clouds roll in, grey rain-light, then near-dark at the peak.
            var flood = so.FindProperty("flood");
            SetLook(flood.FindPropertyRelative("warning"), skyOvercast, 0.9f, Grey(0.55f), new Color(0.62f, 0.68f, 0.74f), 60f, 260f,
                new Color(0.85f, 0.88f, 0.95f), 1.1f, 0.85f, -10f, -10f, 0.2f, 0.28f);
            SetLook(flood.FindPropertyRelative("active"), skyOvercast, 0.6f, Grey(0.42f), new Color(0.45f, 0.50f, 0.56f), 30f, 170f,
                new Color(0.75f, 0.80f, 0.92f), 0.7f, 0.7f, -25f, -20f, 0.05f, 0.35f);
            SetLook(flood.FindPropertyRelative("peak"), skyOvercast, 0.4f, Grey(0.32f), new Color(0.33f, 0.37f, 0.42f), 20f, 120f,
                new Color(0.65f, 0.72f, 0.9f), 0.45f, 0.55f, -35f, -30f, -0.1f, 0.42f);

            // Blizzard: overcast, then whiteout. Fog goes bright and close; light goes cold and flat.
            var bliz = so.FindProperty("blizzard");
            SetLook(bliz.FindPropertyRelative("warning"), skyOvercast, 1.1f, Grey(0.6f), new Color(0.80f, 0.84f, 0.90f), 50f, 220f,
                new Color(0.88f, 0.92f, 1f), 1.2f, 0.9f, -15f, -20f, 0.3f, 0.25f);
            SetLook(bliz.FindPropertyRelative("active"), skyOvercast, 1.3f, Grey(0.7f), new Color(0.86f, 0.89f, 0.93f), 12f, 90f,
                new Color(0.82f, 0.88f, 1f), 0.9f, 0.8f, -45f, -35f, 0.35f, 0.3f);
            SetLook(bliz.FindPropertyRelative("peak"), skyOvercast, 1.4f, Grey(0.75f), new Color(0.90f, 0.92f, 0.95f), 6f, 55f,
                new Color(0.80f, 0.86f, 1f), 0.75f, 0.75f, -60f, -40f, 0.4f, 0.35f);

            // Earthquake: the sky stays, but the air fills with tan dust as buildings come down.
            var quake = so.FindProperty("earthquake");
            SetLook(quake.FindPropertyRelative("warning"), skyClear, 1f, Grey(0.5f), new Color(0.74f, 0.82f, 0.90f), 80f, 320f,
                new Color(1f, 0.96f, 0.84f), 1.74f, 1f, 6f, 0f, 0.35f, 0.22f);
            SetLook(quake.FindPropertyRelative("active"), skyClear, 0.95f, Grey(0.5f), new Color(0.72f, 0.68f, 0.60f), 60f, 260f,
                new Color(1f, 0.93f, 0.80f), 1.6f, 0.95f, -5f, 8f, 0.3f, 0.26f);
            SetLook(quake.FindPropertyRelative("peak"), skyClear, 0.8f, Grey(0.48f), new Color(0.66f, 0.60f, 0.50f), 25f, 150f,
                new Color(1f, 0.88f, 0.72f), 1.3f, 0.85f, -15f, 15f, 0.2f, 0.32f);

            // Wildfire: haze first, then an orange smoke sky with a dim red sun at the peak.
            var fire = so.FindProperty("wildfire");
            SetLook(fire.FindPropertyRelative("warning"), skyClear, 0.95f, new Color(0.55f, 0.5f, 0.45f), new Color(0.80f, 0.74f, 0.62f), 60f, 260f,
                new Color(1f, 0.90f, 0.72f), 1.6f, 0.95f, 0f, 12f, 0.3f, 0.26f);
            SetLook(fire.FindPropertyRelative("active"), skySunset, 0.9f, new Color(0.6f, 0.45f, 0.35f), new Color(0.70f, 0.52f, 0.36f), 30f, 170f,
                new Color(1f, 0.72f, 0.45f), 1.2f, 0.85f, 5f, 30f, 0.2f, 0.32f);
            SetLook(fire.FindPropertyRelative("peak"), skyDusk, 0.8f, new Color(0.6f, 0.35f, 0.25f), new Color(0.48f, 0.30f, 0.20f), 12f, 100f,
                new Color(1f, 0.45f, 0.25f), 0.8f, 0.65f, -5f, 45f, 0.05f, 0.4f);

            so.ApplyModifiedProperties();
            log.Add("DisasterAtmosphere configured on /DisasterManager");

            // DisasterManager -> atmosphere
            var dm = dmGo.GetComponent<DisasterManager>();
            var dmSo = new SerializedObject(dm);
            dmSo.FindProperty("atmosphere").objectReferenceValue = atmosphere;
            dmSo.ApplyModifiedProperties();

            // --- Cold tint panel: a copy of FireTint in blue --------------------------------
            var canvas = GameObject.Find("/Canvas");
            var fireTint = canvas != null ? canvas.transform.Find("FireTint") : null;
            DamageTint coldTint = null;
            if (fireTint != null)
            {
                var existing = canvas.transform.Find("ColdTint");
                GameObject coldGo;
                if (existing == null)
                {
                    coldGo = Object.Instantiate(fireTint.gameObject, canvas.transform);
                    coldGo.name = "ColdTint";
                    Undo.RegisterCreatedObjectUndo(coldGo, "ColdTint");
                    // Just above FireTint so both composite.
                    coldGo.transform.SetSiblingIndex(fireTint.GetSiblingIndex() + 1);
                    log.Add("created /Canvas/ColdTint");
                }
                else coldGo = existing.gameObject;

                coldTint = coldGo.GetComponent<DamageTint>();
                var tintSo = new SerializedObject(coldTint);
                tintSo.FindProperty("tintColor").colorValue = new Color(0.55f, 0.75f, 1f);
                tintSo.FindProperty("sustainedAlpha").floatValue = 0.30f;
                tintSo.FindProperty("flashAlpha").floatValue = 0f;
                tintSo.FindProperty("sustainedFadeSpeed").floatValue = 0.15f;
                tintSo.ApplyModifiedProperties();

                var img = coldGo.GetComponent<Image>();
                if (img != null) img.color = new Color(0.55f, 0.75f, 1f, 0f);
            }

            // --- Materials for the procedural particle systems ------------------------------
            var dustMat = Load<Material>(k_Vfx + "Materials/M_VFX_Dust_02.mat");
            var glowMat = Load<Material>(k_Vfx + "Materials/M_VFX_Glow_01.mat");
            var smokeMat = Load<Material>(k_Vfx + "Materials/M_VFX_Smoke_01.mat");

            var quakeMgr = Object.FindFirstObjectByType<EarthquakeManager>(FindObjectsInactive.Include);
            if (quakeMgr != null)
            {
                var q = new SerializedObject(quakeMgr);
                q.FindProperty("dustMaterial").objectReferenceValue = smokeMat != null ? smokeMat : dustMat;
                q.FindProperty("atmosphere").objectReferenceValue = atmosphere;
                q.ApplyModifiedProperties();
                log.Add("EarthquakeManager: dust material");
            }

            var floodMgr = Object.FindFirstObjectByType<Flood>(FindObjectsInactive.Include);
            if (floodMgr != null)
            {
                var f = new SerializedObject(floodMgr);
                f.FindProperty("rainMaterial").objectReferenceValue = glowMat;
                f.FindProperty("atmosphere").objectReferenceValue = atmosphere;
                f.ApplyModifiedProperties();
                log.Add("Flood: rain material");
            }

            var blizMgr = Object.FindFirstObjectByType<BlizzardManager>(FindObjectsInactive.Include);
            if (blizMgr != null)
            {
                var b = new SerializedObject(blizMgr);
                b.FindProperty("atmosphere").objectReferenceValue = atmosphere;
                b.FindProperty("coldTint").objectReferenceValue = coldTint;
                b.FindProperty("terrain").objectReferenceValue = Object.FindFirstObjectByType<Terrain>();
                b.ApplyModifiedProperties();
                log.Add("BlizzardManager: atmosphere, cold tint, terrain");
            }

            var fireMgr = Object.FindFirstObjectByType<WildfireManager>(FindObjectsInactive.Include);
            if (fireMgr != null)
            {
                var w = new SerializedObject(fireMgr);
                w.FindProperty("atmosphere").objectReferenceValue = atmosphere;
                w.FindProperty("emberMaterial").objectReferenceValue = glowMat;

                // Smoke variants of the same fires, so the wildfire actually smokes.
                var ground = Load<GameObject>(k_Vfx + "Particles/VFX_Fire_Floor_01_Smoke.prefab");
                var building = Load<GameObject>(k_Vfx + "Particles/VFX_Fire_01_Big_Smoke.prefab");
                if (ground != null) w.FindProperty("groundFirePrefab").objectReferenceValue = EnsureFireInstance(ground, log);
                if (building != null) w.FindProperty("buildingFirePrefab").objectReferenceValue = EnsureFireInstance(building, log);
                w.ApplyModifiedProperties();
                log.Add("WildfireManager: atmosphere, embers, smoke prefabs");
            }

            // Skies are only visible if the cameras actually draw the skybox.
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.clearFlags != CameraClearFlags.Skybox)
                {
                    Undo.RecordObject(cam, "Skybox clear");
                    cam.clearFlags = CameraClearFlags.Skybox;
                    EditorUtility.SetDirty(cam);
                    log.Add($"camera {cam.name}: clear flags -> Skybox");
                }
            }

            AssetDatabase.SaveAssets();
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return new { changes = log };
        }

        [CliCommand("disaster_state", "Report live disaster state in play mode: render settings, atmosphere, player submersion, flood/blizzard/quake/fire stats.")]
        public static object State()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            var flood = Object.FindFirstObjectByType<Flood>();
            var bliz = Object.FindFirstObjectByType<BlizzardManager>();
            var quake = Object.FindFirstObjectByType<EarthquakeManager>();
            var fire = Object.FindFirstObjectByType<WildfireManager>();
            var pc = Object.FindFirstObjectByType<PlayerController>();
            var cam = Camera.main;
            return new
            {
                playing = Application.isPlaying,
                fog = new { color = RenderSettings.fogColor.ToString(), RenderSettings.fogStartDistance, RenderSettings.fogEndDistance },
                skybox = RenderSettings.skybox != null ? RenderSettings.skybox.name : "none",
                skyExposure = RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure") ? RenderSettings.skybox.GetFloat("_Exposure") : -1f,
                sun = RenderSettings.sun != null ? new { RenderSettings.sun.intensity, color = RenderSettings.sun.color.ToString() } : null,
                cameraClear = cam != null ? cam.clearFlags.ToString() : "none",
                player = fpc != null ? new { pos = fpc.transform.position.ToString(), fpc.Submersion, fpc.IsSwimming, fpc.WaterSurfaceY, external = fpc.ExternalVelocity.ToString() } : null,
                health = pc != null ? Object.FindFirstObjectByType<HealthBar>()?.Health : null,
                flood = flood != null ? new { flood.IsFlooding, flood.StormActive, flood.SurfaceHeight } : null,
                blizzard = bliz != null && bliz.isActiveAndEnabled ? new { bliz.Cold, bliz.Gust, wind = bliz.Wind.ToString() } : null,
                quake = quake != null ? new { quake.HasTriggered, quake.QuakeCount, quake.StandingBuildings } : null,
                fire = fire != null ? new { fire.HasTriggered, fire.BurningCount, fire.BurntCount, fire.BurningBuildingCount, wind = fire.WindDirection.ToString() } : null,
            };
        }

        /// <summary>The smoke prefabs ship without our FireInstance wrapper; add it to the prefab asset.</summary>
        static FireInstance EnsureFireInstance(GameObject prefab, List<string> log)
        {
            var fi = prefab.GetComponent<FireInstance>();
            if (fi != null) return fi;

            var path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            root.AddComponent<FireInstance>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            log.Add($"added FireInstance to {path}");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FireInstance>();
        }

        static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) Debug.LogWarning($"setup_disaster_realism: missing {path}");
            return a;
        }

        static Color Grey(float v) => new Color(v, v, v);

        static void SetLook(SerializedProperty look, Material sky, float skyExposure, Color skyTint, Color fogColor, float fogStart, float fogEnd,
            Color sunColor, float sunIntensity, float ambient, float saturation, float temperature, float exposure, float vignette)
        {
            look.FindPropertyRelative("skybox").objectReferenceValue = sky;
            look.FindPropertyRelative("skyExposure").floatValue = skyExposure;
            look.FindPropertyRelative("skyTint").colorValue = skyTint;
            look.FindPropertyRelative("fogColor").colorValue = fogColor;
            look.FindPropertyRelative("fogStart").floatValue = fogStart;
            look.FindPropertyRelative("fogEnd").floatValue = fogEnd;
            look.FindPropertyRelative("sunColor").colorValue = sunColor;
            look.FindPropertyRelative("sunIntensity").floatValue = sunIntensity;
            look.FindPropertyRelative("ambientIntensity").floatValue = ambient;
            look.FindPropertyRelative("saturation").floatValue = saturation;
            look.FindPropertyRelative("temperature").floatValue = temperature;
            look.FindPropertyRelative("exposure").floatValue = exposure;
            look.FindPropertyRelative("vignette").floatValue = vignette;
        }
    }
}
