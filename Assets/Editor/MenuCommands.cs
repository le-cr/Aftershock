using System.Collections.Generic;
using TMPro;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Aftershock.Editor
{
    /// <summary>
    /// Builds the Aftershock title / settings menu scene from the Unity CLI.
    /// Idempotent: safe to re-run after tweaking layout values.
    /// </summary>
    public static class MenuCommands
    {
        const string k_ScenePath = "Assets/Scenes/Menu.unity";
        const string k_MainPath = "Assets/Scenes/Main.unity";
        const string k_HudMaterialPath = "Assets/Materials/HUD_TextOutline.mat";
        const string k_SkyPath = "Assets/Materials/Skyboxes/Deep Dusk/Deep Dusk.mat";

        static readonly Color Amber = new Color(1f, 0.78f, 0.25f, 1f);
        static readonly Color PanelDark = new Color(0.05f, 0.07f, 0.1f, 0.82f);
        static readonly Color ButtonFace = new Color(0.12f, 0.14f, 0.18f, 0.92f);
        static readonly Color ButtonBorder = new Color(1f, 0.78f, 0.25f, 0.55f);
        static readonly Color AmberText = new Color(0.78f, 0.82f, 0.88f, 0.9f);
        static readonly Color ToggleOn = new Color(0.55f, 0.28f, 0.08f, 0.95f);

        [CliCommand("setup_main_menu", "Create or rebuild the Menu scene: title screen, settings panel (disaster choice + volume/prep/survive sliders), and put Menu first in Build Settings.")]
        public static object SetupMainMenu()
        {
            var log = new List<string>();

            EnsureScene(log);
            ClearRootExceptEssentials(log);
            EnsureCameraAndLight(log);
            EnsureEventSystem(log);

            var canvas = EnsureCanvas(log);
            var controller = canvas.GetComponent<MainMenuController>() ?? Undo.AddComponent<MainMenuController>(canvas);

            // Wipe previous menu content under the canvas so re-runs are clean.
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(canvas.transform.GetChild(i).gameObject);

            var mainPanel = CreatePanel(canvas.transform, "MainPanel", stretch: true, color: Color.clear);
            var settingsPanel = CreatePanel(canvas.transform, "SettingsPanel", stretch: true, color: new Color(0f, 0f, 0f, 0.55f));
            settingsPanel.SetActive(false);

            BuildMainPanel(mainPanel.transform, log);
            BuildSettingsPanel(settingsPanel.transform, log);
            WireController(controller, mainPanel, settingsPanel, log);

            ConfigureBuildSettings(log);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            log.Add("Menu scene saved");
            return new { scene = k_ScenePath, changes = log };
        }

        static void EnsureScene(List<string> log)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath);
            if (existing == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, k_ScenePath);
                log.Add($"created {k_ScenePath}");
            }
            else
            {
                EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
                log.Add($"opened {k_ScenePath}");
            }
        }

        static void ClearRootExceptEssentials(List<string> log)
        {
            var keep = new HashSet<string> { "Main Camera", "Directional Light", "EventSystem", "Canvas" };
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var go in roots)
            {
                if (keep.Contains(go.name)) continue;
                Undo.DestroyObjectImmediate(go);
                log.Add($"removed root {go.name}");
            }
        }

        static void EnsureCameraAndLight(List<string> log)
        {
            var camGo = GameObject.Find("Main Camera");
            if (camGo == null)
            {
                camGo = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(camGo, "Menu camera");
                camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                log.Add("created Main Camera");
            }

            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = new Vector3(0f, 4f, -10f);
            cam.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(k_SkyPath);
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                log.Add("applied Deep Dusk skybox");
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.32f, 0.4f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.2f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.07f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.18f, 0.14f, 0.16f);
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 90f;

            var lightGo = GameObject.Find("Directional Light");
            if (lightGo == null)
            {
                lightGo = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(lightGo, "Menu light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                log.Add("created Directional Light");
            }

            var sun = lightGo.GetComponent<Light>();
            sun.color = new Color(1f, 0.72f, 0.45f);
            sun.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(28f, -35f, 0f);
        }

        static void EnsureEventSystem(List<string> log)
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "Menu EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            log.Add("created EventSystem");
        }

        static GameObject EnsureCanvas(List<string> log)
        {
            var canvasGo = GameObject.Find("Canvas");
            if (canvasGo != null && canvasGo.GetComponent<Canvas>() == null)
            {
                Undo.DestroyObjectImmediate(canvasGo);
                canvasGo = null;
                log.Add("removed incomplete Canvas");
            }

            if (canvasGo == null)
            {
                canvasGo = new GameObject("Canvas");
                Undo.RegisterCreatedObjectUndo(canvasGo, "Menu Canvas");
                canvasGo.AddComponent<RectTransform>();
                canvasGo.AddComponent<Canvas>();
                canvasGo.AddComponent<CanvasScaler>();
                canvasGo.AddComponent<GraphicRaycaster>();
                log.Add("created Canvas");
            }

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>() ?? canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (canvasGo.GetComponent<GraphicRaycaster>() == null)
                canvasGo.AddComponent<GraphicRaycaster>();

            return canvasGo;
        }

        static void BuildMainPanel(Transform parent, List<string> log)
        {
            var dim = CreatePanel(parent, "Dimmer", stretch: true, color: new Color(0.02f, 0.03f, 0.05f, 0.45f));
            dim.transform.SetAsFirstSibling();

            var title = CreateText(parent, "Title", "AFTERSHOCK", 96, Amber, FontStyles.Bold);
            Stretch(title.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(900f, 120f));
            ApplyOutline(title);

            var tagline = CreateText(parent, "Tagline", "Survive the disaster. Find shelter. Outlast the clock.", 28, AmberText, FontStyles.Normal);
            Stretch(tagline.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(900f, 48f));
            ApplyOutline(tagline);

            var buttonColumn = new GameObject("Buttons", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(buttonColumn, "Menu buttons");
            var colRt = buttonColumn.GetComponent<RectTransform>();
            colRt.SetParent(parent, false);
            Stretch(colRt, new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f), new Vector2(360f, 260f));
            var vlg = buttonColumn.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 18f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateMenuButton(colRt, "PlayButton", "PLAY");
            CreateMenuButton(colRt, "SettingsButton", "SETTINGS");
            CreateMenuButton(colRt, "QuitButton", "QUIT");
            log.Add("main panel built");
        }

        static void BuildSettingsPanel(Transform parent, List<string> log)
        {
            var card = CreatePanel(parent, "SettingsCard", stretch: false, color: PanelDark);
            var cardRt = card.GetComponent<RectTransform>();
            Stretch(cardRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(820f, 720f));
            var cardOutline = card.AddComponent<Outline>();
            cardOutline.effectColor = ButtonBorder;
            cardOutline.effectDistance = new Vector2(2f, -2f);

            var title = CreateText(cardRt, "SettingsTitle", "SETTINGS", 48, Amber, FontStyles.Bold);
            Stretch(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(700f, 70f), new Vector2(0f, -48f));
            ApplyOutline(title);

            var disasterLabel = CreateText(cardRt, "DisasterLabel", "NATURAL DISASTER", 22, Amber, FontStyles.Bold);
            Stretch(disasterLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(700f, 36f), new Vector2(0f, -110f));

            var disasterRow = new GameObject("DisasterRow", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(disasterRow, "Disaster row");
            var rowRt = disasterRow.GetComponent<RectTransform>();
            rowRt.SetParent(cardRt, false);
            Stretch(rowRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(760f, 56f), new Vector2(0f, -160f));
            var hlg = disasterRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            var group = disasterRow.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            CreateDisasterToggle(rowRt, group, "DisasterRandom", "RANDOM");
            CreateDisasterToggle(rowRt, group, "DisasterEarthquake", "QUAKE");
            CreateDisasterToggle(rowRt, group, "DisasterBlizzard", "BLIZZARD");
            CreateDisasterToggle(rowRt, group, "DisasterFlood", "FLOOD");
            CreateDisasterToggle(rowRt, group, "DisasterWildfire", "FIRE");

            CreateSliderRow(cardRt, "MusicVolume", "MUSIC VOLUME", 0f, 1f, GameSettings.DefaultMusicVolume, false, new Vector2(0f, -250f));
            CreateSliderRow(cardRt, "PrepTime", "PREPARATION TIME", GameSettings.MinPrepSeconds, GameSettings.MaxPrepSeconds, GameSettings.DefaultPrepSeconds, true, new Vector2(0f, -350f));
            CreateSliderRow(cardRt, "SurviveTime", "TIME TO SURVIVE", GameSettings.MinSurviveSeconds, GameSettings.MaxSurviveSeconds, GameSettings.DefaultSurviveSeconds, true, new Vector2(0f, -450f));

            var back = CreateMenuButton(cardRt, "BackButton", "BACK");
            var backRt = back.GetComponent<RectTransform>();
            Stretch(backRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(280f, 64f), new Vector2(0f, 48f));
            // Detach from any layout on the card (there is none) — keep absolute placement.
            Object.DestroyImmediate(back.GetComponent<LayoutElement>());

            log.Add("settings panel built");
        }

        static void WireController(MainMenuController controller, GameObject mainPanel, GameObject settingsPanel, List<string> log)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("mainPanel").objectReferenceValue = mainPanel;
            so.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;

            so.FindProperty("playButton").objectReferenceValue = FindButton(mainPanel, "PlayButton");
            so.FindProperty("settingsButton").objectReferenceValue = FindButton(mainPanel, "SettingsButton");
            so.FindProperty("quitButton").objectReferenceValue = FindButton(mainPanel, "QuitButton");
            so.FindProperty("settingsBackButton").objectReferenceValue = FindButton(settingsPanel, "BackButton");

            so.FindProperty("disasterRandom").objectReferenceValue = FindToggle(settingsPanel, "DisasterRandom");
            so.FindProperty("disasterEarthquake").objectReferenceValue = FindToggle(settingsPanel, "DisasterEarthquake");
            so.FindProperty("disasterBlizzard").objectReferenceValue = FindToggle(settingsPanel, "DisasterBlizzard");
            so.FindProperty("disasterFlood").objectReferenceValue = FindToggle(settingsPanel, "DisasterFlood");
            so.FindProperty("disasterWildfire").objectReferenceValue = FindToggle(settingsPanel, "DisasterWildfire");

            WireSliderProps(so, settingsPanel, "MusicVolume", "musicVolumeSlider", "musicVolumeValue");
            WireSliderProps(so, settingsPanel, "PrepTime", "prepTimeSlider", "prepTimeValue");
            WireSliderProps(so, settingsPanel, "SurviveTime", "surviveTimeSlider", "surviveTimeValue");

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            log.Add("MainMenuController wired");
        }

        static void WireSliderProps(SerializedObject so, GameObject root, string rowName, string sliderProp, string valueProp)
        {
            var row = root.transform.Find($"SettingsCard/{rowName}");
            if (row == null) row = FindDeep(root.transform, rowName);
            if (row == null) return;
            so.FindProperty(sliderProp).objectReferenceValue = row.GetComponentInChildren<Slider>(true);
            var value = row.Find("Value");
            so.FindProperty(valueProp).objectReferenceValue = value != null ? value.GetComponent<TMP_Text>() : null;
        }

        static Button FindButton(GameObject root, string name)
        {
            var t = FindDeep(root.transform, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        static Toggle FindToggle(GameObject root, string name)
        {
            var t = FindDeep(root.transform, name);
            return t != null ? t.GetComponent<Toggle>() : null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static void ConfigureBuildSettings(List<string> log)
        {
            // Menu first, then a single Main entry. Drop demo / duplicate / NGO scenes from the play list.
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(k_ScenePath, true),
                new EditorBuildSettingsScene(k_MainPath, true),
            };
            EditorBuildSettings.scenes = scenes.ToArray();
            log.Add("Build Settings: Menu (0), Main (1)");
        }

        static GameObject CreatePanel(Transform parent, string name, bool stretch, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, name);
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = stretch;
            return go;
        }

        static GameObject CreateMenuButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, name);
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(320f, 64f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 64f;
            le.minHeight = 64f;

            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            img.color = ButtonFace;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = ButtonBorder;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.05f, 0.9f, 1f);
            colors.pressedColor = new Color(0.85f, 0.8f, 0.7f, 1f);
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;
            btn.targetGraphic = img;

            var text = CreateText(rt, "Label", label, 28, Amber, FontStyles.Bold);
            Stretch(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(300f, 56f));
            text.raycastTarget = false;
            return go;
        }

        static Toggle CreateDisasterToggle(Transform parent, ToggleGroup group, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, name);
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            go.AddComponent<CanvasRenderer>();
            var bg = go.AddComponent<Image>();
            bg.color = ButtonFace;

            var toggle = go.AddComponent<Toggle>();
            toggle.group = group;
            toggle.targetGraphic = bg;

            var colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.05f, 0.95f, 1f);
            colors.pressedColor = new Color(0.9f, 0.85f, 0.75f, 1f);
            colors.selectedColor = new Color(1.2f, 1.0f, 0.8f, 1f);
            toggle.colors = colors;

            // Selected look via a child check graphic tinted amber.
            var checkGo = new GameObject("Check", typeof(RectTransform));
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.SetParent(rt, false);
            StretchFull(checkRt);
            checkGo.AddComponent<CanvasRenderer>();
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = ToggleOn;
            checkImg.raycastTarget = false;
            toggle.graphic = checkImg;

            var text = CreateText(rt, "Label", label, 16, Amber, FontStyles.Bold);
            StretchFull(text.rectTransform);
            text.raycastTarget = false;

            if (name == "DisasterRandom")
                toggle.isOn = true;

            return toggle;
        }

        static void CreateSliderRow(Transform parent, string name, string label, float min, float max, float value, bool whole, Vector2 anchoredPos)
        {
            var row = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(row, name);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.SetParent(parent, false);
            Stretch(rowRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(700f, 70f), anchoredPos);

            var labelTmp = CreateText(rowRt, "Label", label, 20, Amber, FontStyles.Bold);
            Stretch(labelTmp.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(420f, 28f), new Vector2(210f, -8f));
            labelTmp.horizontalAlignment = HorizontalAlignmentOptions.Left;

            var valueTmp = CreateText(rowRt, "Value", "", 20, AmberText, FontStyles.Normal);
            Stretch(valueTmp.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(120f, 28f), new Vector2(-60f, -8f));
            valueTmp.horizontalAlignment = HorizontalAlignmentOptions.Right;

            var sliderGo = new GameObject("Slider", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(sliderGo, name + " slider");
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.SetParent(rowRt, false);
            Stretch(sliderRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(700f, 28f), new Vector2(0f, 12f));

            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.value = value;

            var bg = CreatePanel(sliderRt, "Background", stretch: true, color: new Color(0.08f, 0.09f, 0.12f, 1f));
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.offsetMin = new Vector2(0f, 8f);
            bgRt.offsetMax = new Vector2(0f, -8f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.SetParent(sliderRt, false);
            StretchFull(fillAreaRt);
            fillAreaRt.offsetMin = new Vector2(5f, 8f);
            fillAreaRt.offsetMax = new Vector2(-5f, -8f);

            var fill = CreatePanel(fillAreaRt, "Fill", stretch: true, color: Amber);
            var fillImg = fill.GetComponent<Image>();

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            var handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.SetParent(sliderRt, false);
            StretchFull(handleAreaRt);
            handleAreaRt.offsetMin = new Vector2(10f, 0f);
            handleAreaRt.offsetMax = new Vector2(-10f, 0f);

            var handle = CreatePanel(handleAreaRt, "Handle", stretch: false, color: Color.white);
            var handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(22f, 22f);
            var handleImg = handle.GetComponent<Image>();
            handleImg.color = Amber;

            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
        }

        static TMP_Text CreateText(Transform parent, string name, string content, float size, Color color, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, name);
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;

            var font = TMP_Settings.defaultFontAsset;
            if (font != null) tmp.font = font;
            return tmp;
        }

        static void ApplyOutline(TMP_Text tmp)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_HudMaterialPath);
            if (mat != null && tmp.font != null)
                tmp.fontSharedMaterial = mat;
        }

        static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2? anchoredPos = null)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos ?? Vector2.zero;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
