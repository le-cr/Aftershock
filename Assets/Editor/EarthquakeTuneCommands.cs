using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aftershock.Editor
{
    /// <summary>
    /// Pushes the harder earthquake / debris defaults into the open scene and lowers OpenFracture
    /// fragment counts so rubble stays within the DebrisBudget cap.
    /// </summary>
    public static class EarthquakeTuneCommands
    {
        [CliCommand("amp_earthquake", "Apply stronger earthquake + debris damage values, enable fragment despawn, and lower Fracture.fragmentCount on every collapsing building in the open scene.")]
        public static object AmpEarthquake(
            [CliArg("fragment_count", "OpenFracture fragments per building.")] int fragmentCount = 36)
        {
            var log = new List<string>();
            fragmentCount = Mathf.Clamp(fragmentCount, 12, 80);

            var quake = Object.FindFirstObjectByType<EarthquakeManager>(FindObjectsInactive.Include);
            if (quake != null)
            {
                var so = new SerializedObject(quake);
                Set(so, "foreshockSeconds", 3.5f);
                Set(so, "shakeDuration", 10f);
                Set(so, "shakeMagnitude", 0.62f);
                Set(so, "leadIn", 0.45f);
                Set(so, "initialCollapseCount", 4);
                Set(so, "firstInterval", 8f);
                Set(so, "intervalGrowth", 0.22f);
                Set(so, "aftershockShakeDuration", 5f);
                Set(so, "aftershockMagnitudeScale", 0.78f);
                Set(so, "bigAftershockChance", 0.28f);
                Set(so, "collapseThreshold", 0.35f);
                Set(so, "stumblePerMetre", 5.5f);
                Set(so, "dustRateAtFullShake", 90f);
                Set(so, "collapseDustBurst", 130);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(quake);
                log.Add("EarthquakeManager amped");
            }
            else
            {
                log.Add("no EarthquakeManager");
            }

            int buildings = 0;
            int fractures = 0;
            foreach (var collapse in Object.FindObjectsByType<BuildingCollapse>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(collapse);
                Set(so, "explosionForce", 6.5f);
                Set(so, "explosionRadius", 28f);
                Set(so, "upwardsModifier", 0.65f);
                Set(so, "lateralScatter", 2.2f);
                Set(so, "randomTorque", 2f);
                Set(so, "fragmentDrag", 0.35f);
                Set(so, "fragmentAngularDrag", 2.5f);
                Set(so, "debrisDamage", 0.2f);
                Set(so, "debrisMinImpactSpeed", 1.8f);
                Set(so, "debrisRearmSeconds", 0.35f);
                so.FindProperty("despawnFragments").boolValue = true;
                Set(so, "fragmentLifetime", 28f);
                Set(so, "speculativeSeconds", 2.5f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(collapse);
                buildings++;

                var fracture = collapse.GetComponent<Fracture>();
                if (fracture == null) continue;
                var fso = new SerializedObject(fracture);
                var count = fso.FindProperty("fractureOptions.fragmentCount");
                if (count != null)
                {
                    count.intValue = fragmentCount;
                    fso.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(fracture);
                    fractures++;
                }
            }
            log.Add($"BuildingCollapse updated: {buildings}");
            log.Add($"Fracture.fragmentCount -> {fragmentCount} on {fractures}");

            foreach (var shake in Object.FindObjectsByType<CameraShake>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(shake);
                Set(so, "defaultDuration", 10f);
                Set(so, "defaultMagnitude", 0.62f);
                Set(so, "frequency", 16f);
                Set(so, "rotationDegreesPerMetre", 12f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(shake);
            }
            log.Add("CameraShake defaults updated");

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add($"saved {scene.path}");
            return new { changes = log };
        }

        static void Set(SerializedObject so, string prop, float value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.floatValue = value;
        }

        static void Set(SerializedObject so, string prop, int value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = value;
        }
    }
}
