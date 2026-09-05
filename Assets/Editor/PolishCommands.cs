using System.Collections.Generic;
using TMPro;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Aftershock.Editor
{
    /// <summary>
    /// One-shot HUD layout and graphics polish for the open scene, driven from the Unity CLI.
    /// Idempotent: safe to run again after tweaking values.
    /// </summary>
    public static class PolishCommands
    {
        const string k_ProfilePath = "Assets/Settings/MainSceneProfile.asset";
        const string k_HudMaterialPath = "Assets/Materials/HUD_TextOutline.mat";
        const string k_TerrainMaterialPath = "Assets/Materials/TerrainGrass.mat";

        [CliCommand("polish_hud", "Re-lay out the disaster warning HUD so the label and timer no longer overlap, and give HUD text an outline.")]
        public static object PolishHud()
        {
            var log = new List<string>();
            var canvas = GameObject.Find("/Canvas");
            if (canvas == null) throw new System.InvalidOperationException("No /Canvas in the open scene.");

            // --- Warning phase: "DISASTER WARNING" on top; below it the label and timer sit side by
            //     side in a horizontal layout row, so they are sized to their text and never overlap
            //     whatever the disaster name is.
            var warning = Place(canvas, "Disaster/DisasterWarning", new Vector2(0f, 100f), new Vector2(700f, 60f), HorizontalAlignmentOptions.Center, log);
            if (warning != null)
            {
                warning.text = "DISASTER WARNING";
                warning.fontStyle = FontStyles.Bold;
                warning.color = new Color(1f, 0.78f, 0.25f);
            }

            var row = EnsureRow(canvas.transform.Find("Disaster") as RectTransform, log);
            var label = Place(canvas, "Disaster/Row/DisasterText", Vector2.zero, new Vector2(320f, 60f), HorizontalAlignmentOptions.Right, log);
            var timer = Place(canvas, "Disaster/Row/DisasterTimer", Vector2.zero, new Vector2(320f, 60f), HorizontalAlignmentOptions.Left, log);
            if (label != null) label.fontStyle = FontStyles.Bold;
            if (timer != null) timer.fontStyle = FontStyles.Bold;
            if (row != null) UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(row);

            // --- Survival phase: same widths, centred.
            var survive = Place(canvas, "Survive/SurviveText", new Vector2(0f, 100f), new Vector2(700f, 60f), HorizontalAlignmentOptions.Center, log);
            if (survive != null)
            {
                survive.fontStyle = FontStyles.Bold;
                survive.color = new Color(1f, 0.78f, 0.25f);
            }
            Place(canvas, "Survive/CountdownTimer", new Vector2(0f, 50f), new Vector2(700f, 60f), HorizontalAlignmentOptions.Center, log);

            // --- Outlined text material so the HUD reads against sky, snow and fire alike.
            var anyText = label ?? timer ?? warning;
            if (anyText != null)
            {
                var mat = EnsureOutlineMaterial(anyText.font, log);
                foreach (var path in new[] {
                    "Disaster/DisasterWarning", "Disaster/Row/DisasterText", "Disaster/Row/DisasterTimer",
                    "Survive/SurviveText", "Survive/CountdownTimer",
                    "DeathScreen/DeathText", "WinScreen/WinText" })
                {
                    var t = canvas.transform.Find(path);
                    var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
                    if (tmp == null || tmp.font != anyText.font) continue;
                    Undo.RecordObject(tmp, "HUD outline");
                    tmp.fontSharedMaterial = mat;
                    EditorUtility.SetDirty(tmp);
                }
                log.Add("outline material applied");
            }

            SaveScene();
            return new { changes = log };
        }

        /// <summary>Find or build the Disaster/Row layout container and move the label + timer into it.</summary>
        static RectTransform EnsureRow(RectTransform disasterGroup, List<string> log)
        {
            if (disasterGroup == null) { log.Add("missing Disaster group"); return null; }

            var row = disasterGroup.Find("Row") as RectTransform;
            if (row == null)
            {
                var go = new GameObject("Row", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "HUD row");
                row = go.GetComponent<RectTransform>();
                row.SetParent(disasterGroup, false);
                log.Add("created Disaster/Row");
            }

            row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.anchoredPosition = new Vector2(0f, 50f);
            row.sizeDelta = new Vector2(700f, 60f);

            var layout = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() ?? Undo.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>(row.gameObject);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            EditorUtility.SetDirty(layout);

            foreach (var name in new[] { "DisasterText", "DisasterTimer" })
            {
                var child = disasterGroup.Find(name) ?? row.Find(name);
                if (child == null) { log.Add($"missing {name}"); continue; }
                if (child.parent != row)
                {
                    Undo.SetTransformParent(child, row, "HUD row");
                    log.Add($"moved {name} into Row");
                }
                child.SetSiblingIndex(name == "DisasterText" ? 0 : 1);
            }

            EditorUtility.SetDirty(row);
            return row;
        }

        static TMP_Text Place(GameObject canvas, string path, Vector2 pos, Vector2 size, HorizontalAlignmentOptions align, List<string> log)
        {
            var t = canvas.transform.Find(path) as RectTransform;
            if (t == null) { log.Add($"missing {path}"); return null; }

            Undo.RecordObject(t, "HUD layout");
            t.anchoredPosition = pos;
            t.sizeDelta = size;

            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                Undo.RecordObject(tmp, "HUD layout");
                tmp.horizontalAlignment = align;
                tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(tmp);
            }

            EditorUtility.SetDirty(t);
            log.Add($"{path} -> pos {pos} size {size} {align}");
            return tmp;
        }

        static Material EnsureOutlineMaterial(TMP_FontAsset font, List<string> log)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_HudMaterialPath);
            if (mat == null)
            {
                mat = new Material(font.material);
                AssetDatabase.CreateAsset(mat, k_HudMaterialPath);
                log.Add($"created {k_HudMaterialPath}");
            }

            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0.02f);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.9f));
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.05f);
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.6f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.2f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        [CliCommand("polish_graphics", "Enable post-processing + SMAA on the cameras, add a global volume (tonemapping, bloom, vignette, colour), fog, soft sun shadows, longer shadow distance, and matte terrain.")]
        public static object PolishGraphics(
            [CliArg("shadow_distance", "URP main-light shadow distance in metres.")] float shadowDistance = 110f,
            [CliArg("fog_start", "Linear fog start distance.")] float fogStart = 80f,
            [CliArg("fog_end", "Linear fog end distance.")] float fogEnd = 320f)
        {
            var log = new List<string>();

            // --- Cameras: post-processing + SMAA.
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data == null) continue;
                Undo.RecordObject(data, "Camera polish");
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.dithering = true;
                EditorUtility.SetDirty(data);
                log.Add($"camera {cam.name}: post-processing + SMAA");
            }

            // --- Global volume with a scene-specific profile.
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, k_ProfilePath);
                log.Add($"created {k_ProfilePath}");
            }

            // Neutral rather than ACES: the low-poly palette reads flat and dark under ACES.
            var tone = GetOrAdd<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.Override(0.35f);
            color.contrast.Override(8f);
            color.saturation.Override(6f);

            var smh = GetOrAdd<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.95f, 0.97f, 1.05f, 0f));     // slightly cool shadows
            smh.highlights.Override(new Vector4(1.03f, 1.01f, 0.97f, 0f));  // slightly warm highlights

            EditorUtility.SetDirty(profile);

            var volumeGo = GameObject.Find("/Global Volume");
            if (volumeGo == null)
            {
                volumeGo = new GameObject("Global Volume");
                Undo.RegisterCreatedObjectUndo(volumeGo, "Global Volume");
                log.Add("created /Global Volume");
            }
            var volume = volumeGo.GetComponent<Volume>() ?? Undo.AddComponent<Volume>(volumeGo);
            volume.isGlobal = true;
            volume.priority = 0;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);

            // --- Fog and sun.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.fogColor = new Color(0.74f, 0.84f, 0.95f);
            log.Add($"fog linear {fogStart}-{fogEnd}");

            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }

            if (sun != null)
            {
                Undo.RecordObject(sun, "Sun polish");
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.9f;
                sun.shadowNormalBias = 0.6f;
                RenderSettings.sun = sun;
                EditorUtility.SetDirty(sun);
                log.Add($"sun {sun.name}: soft shadows");
            }

            // --- URP asset: longer shadow reach so distant buildings are grounded.
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp != null)
            {
                rp.shadowDistance = shadowDistance;
                rp.shadowCascadeCount = 4;
                EditorUtility.SetDirty(rp);
                log.Add($"{rp.name}: shadow distance {shadowDistance}, 4 cascades");
            }

            // --- Terrain: kill the plastic sheen. The terrain uses a plain Lit material shared with a
            //     skybox demo asset, so give it its own matte copy instead of editing the demo material.
            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var src = terrain.materialTemplate;
                if (src != null)
                {
                    var matte = AssetDatabase.LoadAssetAtPath<Material>(k_TerrainMaterialPath);
                    if (matte == null)
                    {
                        matte = new Material(src);
                        AssetDatabase.CreateAsset(matte, k_TerrainMaterialPath);
                        log.Add($"created {k_TerrainMaterialPath}");
                    }
                    matte.SetFloat("_Smoothness", 0.05f);
                    matte.SetFloat("_Metallic", 0f);
                    if (matte.HasProperty("_BaseColor"))
                        matte.SetColor("_BaseColor", new Color(0.19f, 0.55f, 0.20f));
                    EditorUtility.SetDirty(matte);

                    Undo.RecordObject(terrain, "Terrain material");
                    terrain.materialTemplate = matte;
                    EditorUtility.SetDirty(terrain);
                    log.Add($"terrain {terrain.name}: matte material");
                }

                var data = terrain.terrainData;
                if (data == null) continue;
                foreach (var layer in data.terrainLayers)
                {
                    if (layer == null) continue;
                    log.Add($"terrain layer {layer.name}: smoothness {layer.smoothness} -> 0, metallic {layer.metallic} -> 0");
                    layer.smoothness = 0f;
                    layer.metallic = 0f;
                    EditorUtility.SetDirty(layer);
                }
            }

            AssetDatabase.SaveAssets();
            SaveScene();
            return new { changes = log };
        }

        [CliCommand("capture_game", "In play mode, capture the Game view including the overlay HUD to a PNG under the project (via ScreenCapture). Poll the file: it lands a frame later.")]
        public static object CaptureGame(
            [CliArg("path", "Project-relative output path, e.g. Temp/hud.png")] string path = "Temp/hud.png",
            [CliArg("disaster", "Optional disaster name to force into the HUD label first, e.g. EARTHQUAKE.")] string disaster = null)
        {
            if (!string.IsNullOrEmpty(disaster))
            {
                var canvas = GameObject.Find("/Canvas");
                var t = canvas != null ? canvas.transform.Find("Disaster/DisasterText") : null;
                var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
                if (tmp != null) tmp.text = disaster.ToUpperInvariant() + " in";
            }

            ScreenCapture.CaptureScreenshot(path);
            return new { path, playing = Application.isPlaying };
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var c))
                c = profile.Add<T>(true);
            c.active = true;
            return c;
        }

        static void SaveScene()
        {
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
