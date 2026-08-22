using System.Collections.Generic;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aftershock.Editor
{
    /// <summary>
    /// CLI commands for standing up a fracturable building in the open scene. Exposed to the Unity
    /// CLI through the Pipeline package so the setup can be driven from the command line.
    /// </summary>
    public static class CollapsingBuildingCommands
    {
        const string k_GeneratedMeshFolder = "Assets/Models/Generated";

        [CliCommand("setup_collapsing_building",
            "Instantiate a building model in the open scene and set it up to fracture and scatter on a key press.")]
        public static object SetupCollapsingBuilding(
            [CliArg("model", "Project path of the building model, e.g. Assets/Models/Commercial/building-a.fbx", Required = true)]
            string model,
            [CliArg("name", "Name of the created GameObject.")]
            string name = "CollapsingBuilding",
            [CliArg("parent", "Hierarchy path of the parent GameObject. Must be uniformly scaled.")]
            string parent = null,
            [CliArg("x", "World X position.")] float x = 0f,
            [CliArg("y", "World Y position.")] float y = 0f,
            [CliArg("z", "World Z position.")] float z = 0f,
            [CliArg("scale", "Uniform scale applied to the building.")] float scale = 1f,
            [CliArg("fragment_count", "Number of fragments to break the building into.")] int fragmentCount = 60,
            [CliArg("inside_material", "Material for the freshly exposed interior faces.")]
            string insideMaterial = "Assets/Materials/FractureInside.mat",
            [CliArg("key", "KeyCode name that triggers the collapse.")] string key = "T")
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            if (source == null)
                throw new System.ArgumentException($"No model asset found at '{model}'.");

            EnsureMeshIsReadable(model);

            Transform parentTransform = null;
            if (!string.IsNullOrEmpty(parent))
            {
                var parentGo = GameObject.Find(parent);
                if (parentGo == null)
                    throw new System.ArgumentException($"No GameObject found at hierarchy path '{parent}'.");
                parentTransform = parentGo.transform;
            }

            // Pull the geometry out of the model. Kenney kit pieces are a handful of child meshes
            // sharing one atlas material, and Fracture needs a single MeshFilter on one object.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            Mesh mesh;
            Material material;
            try
            {
                mesh = BuildCombinedMesh(instance, source.name, out material);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            var existing = GameObject.Find(parentTransform != null ? $"{parent}/{name}" : $"/{name}");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Collapsing Building");
            if (parentTransform != null)
                go.transform.SetParent(parentTransform, true);
            go.transform.position = new Vector3(x, y, z);
            go.transform.localScale = Vector3.one * scale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            // Kinematic so the intact building stands still; the fragments get their own dynamic
            // bodies from the fracture template.
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.mass = Mathf.Max(1f, mesh.bounds.size.x * mesh.bounds.size.y * mesh.bounds.size.z * scale * scale * scale);

            var inside = AssetDatabase.LoadAssetAtPath<Material>(insideMaterial);

            var fracture = go.AddComponent<Fracture>();
            // The collapse script drives the fracture itself, so leave the component's own trigger
            // inert: Collision with tag filtering off can never fire (see Fracture.OnCollisionEnter).
            fracture.triggerOptions = new TriggerOptions
            {
                triggerType = TriggerType.Collision,
                filterCollisionsByTag = false,
                triggerAllowedTags = new List<string>(),
                minimumCollisionForce = 0f,
                triggerKey = KeyCode.None,
            };
            fracture.fractureOptions = new FractureOptions
            {
                fragmentCount = fragmentCount,
                xAxis = true,
                yAxis = true,
                zAxis = true,
                detectFloatingFragments = false,
                asynchronous = false,
                insideMaterial = inside,
                textureScale = Vector2.one,
                textureOffset = Vector2.zero,
            };
            fracture.refractureOptions = new RefractureOptions();
            fracture.callbackOptions = new CallbackOptions();

            var collapse = go.AddComponent<BuildingCollapse>();
            collapse.collapseKey = (KeyCode)System.Enum.Parse(typeof(KeyCode), key, true);

            EditorSceneManager.MarkSceneDirty(go.scene);

            return new
            {
                created = go.name,
                hierarchyPath = GetHierarchyPath(go.transform),
                instanceId = go.GetInstanceID(),
                meshPath = AssetDatabase.GetAssetPath(mesh),
                vertexCount = mesh.vertexCount,
                bounds = new { size = Vec(mesh.bounds.size), center = Vec(mesh.bounds.center) },
                material = material != null ? material.name : null,
                insideMaterial = inside != null ? inside.name : null,
                fragmentCount,
                collapseKey = collapse.collapseKey.ToString(),
            };
        }

        [CliCommand("collapse_building",
            "Trigger a BuildingCollapse in the open scene without a key press. Intended for testing in play mode.")]
        public static object CollapseBuilding(
            [CliArg("target", "Hierarchy path of the GameObject carrying BuildingCollapse.")]
            string target = "/Scene/CollapsingBuilding")
        {
            var go = GameObject.Find(target);
            if (go == null)
                throw new System.ArgumentException($"No GameObject found at '{target}'.");

            var collapse = go.GetComponent<BuildingCollapse>();
            if (collapse == null)
                throw new System.ArgumentException($"'{target}' has no BuildingCollapse component.");

            collapse.Collapse();

            return new { triggered = target, playing = Application.isPlaying };
        }

        [CliCommand("toggle_camera_shake",
            "Toggle the earthquake camera shake without a key press. Intended for testing in play mode.")]
        public static object ToggleCameraShake(
            [CliArg("target", "Hierarchy path of the GameObject carrying CameraShake.")]
            string target = "/CameraShake")
        {
            var go = GameObject.Find(target);
            if (go == null)
                throw new System.ArgumentException($"No GameObject found at '{target}'.");

            var shake = go.GetComponent<CameraShake>();
            if (shake == null)
                throw new System.ArgumentException($"'{target}' has no CameraShake component.");

            shake.Toggle();

            var camera = Camera.main;
            return new
            {
                shaking = shake.IsShaking,
                playing = Application.isPlaying,
                activeCamera = camera != null ? camera.name : null,
            };
        }

        [CliCommand("set_camera_enabled",
            "Enable or disable a Camera at runtime. Mirrors what HelicopterInteraction does on F, for testing the camera swap in play mode.")]
        public static object SetCameraEnabled(
            [CliArg("target", "Hierarchy path of the GameObject carrying the Camera.", Required = true)]
            string target,
            [CliArg("enabled", "Whether the camera should be on.")]
            bool enabled = true)
        {
            var go = GameObject.Find(target);
            if (go == null)
                throw new System.ArgumentException($"No GameObject found at '{target}'.");

            var camera = go.GetComponent<Camera>();
            if (camera == null)
                throw new System.ArgumentException($"'{target}' has no Camera component.");

            camera.enabled = enabled;

            return new { target, enabled = camera.enabled, playing = Application.isPlaying };
        }

        [CliCommand("toggle_earthquake",
            "Toggle the earthquake (camera shake + staggered building collapse) without a key press. Intended for testing in play mode.")]
        public static object ToggleEarthquake(
            [CliArg("target", "Hierarchy path of the GameObject carrying Earthquake.")]
            string target = "/Earthquake")
        {
            var go = GameObject.Find(target);
            if (go == null)
                throw new System.ArgumentException($"No GameObject found at '{target}'.");

            var quake = go.GetComponent<Earthquake>();
            if (quake == null)
                throw new System.ArgumentException($"'{target}' has no Earthquake component.");

            quake.Toggle();

            return new { quaking = quake.IsQuaking, playing = Application.isPlaying };
        }

        [CliCommand("get_player_health",
            "Read the player's current health bar fill, for verifying debris damage in play mode.")]
        public static object GetPlayerHealth(
            [CliArg("target", "Hierarchy path of the GameObject carrying the HealthBar Image.")]
            string target = null)
        {
            var bar = string.IsNullOrEmpty(target)
                ? Object.FindFirstObjectByType<HealthBar>()
                : GameObject.Find(target)?.GetComponent<HealthBar>();

            if (bar == null)
                throw new System.ArgumentException("No HealthBar found in the open scene.");

            var image = bar.GetComponent<UnityEngine.UI.Image>();
            return new
            {
                healthBar = bar.gameObject.name,
                fillAmount = image != null ? image.fillAmount : -1f,
                playing = Application.isPlaying,
            };
        }

        /// <summary>
        /// Combines every MeshFilter under <paramref name="instance"/> into one mesh in the
        /// instance's local space and saves it as an asset, so it survives a scene save.
        /// </summary>
        static Mesh BuildCombinedMesh(GameObject instance, string sourceName, out Material material)
        {
            var filters = instance.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null)
                .ToArray();

            if (filters.Length == 0)
                throw new System.InvalidOperationException($"Model '{sourceName}' contains no meshes.");

            var renderer = instance.GetComponentInChildren<MeshRenderer>();
            material = renderer != null ? renderer.sharedMaterial : null;

            var combines = filters.Select(f => new CombineInstance
            {
                mesh = f.sharedMesh,
                transform = instance.transform.worldToLocalMatrix * f.transform.localToWorldMatrix,
            }).ToArray();

            var mesh = new Mesh { name = $"{sourceName}_fracture" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(combines, true, true);
            mesh.RecalculateBounds();

            // Fracturing reads vertex data at runtime, which requires a readable mesh.
            mesh.UploadMeshData(false);

            if (!AssetDatabase.IsValidFolder(k_GeneratedMeshFolder))
                AssetDatabase.CreateFolder("Assets/Models", "Generated");

            var path = $"{k_GeneratedMeshFolder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        /// <summary>Model meshes import non-readable by default, which breaks runtime fracturing.</summary>
        static void EnsureMeshIsReadable(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null || importer.isReadable) return;

            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        static string GetHierarchyPath(Transform t)
        {
            var path = "/" + t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = "/" + t.name + path;
            }
            return path;
        }

        static float[] Vec(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
