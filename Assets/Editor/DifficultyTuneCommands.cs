using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aftershock.Editor
{
    /// <summary>
    /// Pushes the harder disaster tuning into the open scene: the blizzard particle system is
    /// rebuilt so snow actually reaches and lands on the player, and every disaster's damage /
    /// pacing values are raised. Idempotent; calls amp_earthquake for the quake side.
    /// </summary>
    public static class DifficultyTuneCommands
    {
        [CliCommand("tune_difficulty", "Make every disaster harder in the open scene: fix the blizzard snow so it lands on the player, and raise damage / pacing on blizzard, flood, wildfire, earthquake and the player's regen.")]
        public static object Tune()
        {
            var log = new List<string>();

            // --- Blizzard ---------------------------------------------------------------------
            var bliz = Object.FindFirstObjectByType<BlizzardManager>(FindObjectsInactive.Include);
            if (bliz != null)
            {
                var so = new SerializedObject(bliz);
                Set(so, "exposureGraceSeconds", 1f);
                Set(so, "coverCheckHeight", 40f);
                var mask = so.FindProperty("coverMask");
                if (mask != null) mask.intValue = ~0;
                Set(so, "secondsToFreeze", 60f);
                Set(so, "secondsToThaw", 30f);
                var range = so.FindProperty("damageMultiplierRange");
                if (range != null) range.vector2Value = new Vector2(0.15f, 1.2f);
                Set(so, "hypothermiaThreshold", 0.75f);
                Set(so, "hypothermiaCoverDamageScale", 0.4f);
                Set(so, "playerPushFactor", 0.12f);
                var emission = so.FindProperty("emissionRange");
                if (emission != null) emission.vector2Value = new Vector2(900f, 2000f);
                Set(so, "emitterHeight", 14f);
                Set(so, "meanFallSpeed", 6.5f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bliz);

                // The particle system: snow must live long enough to reach the ground from the
                // (now lower) emitter, fall fast enough to land near where it was aimed, be dense
                // enough that hits on the player register continuously, and die on contact so it
                // doesn't bounce off a roof into the shelter beneath.
                var ps = bliz.GetComponent<ParticleSystem>();
                Undo.RecordObject(ps, "Tune blizzard snow");
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 5.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5f);
                main.maxParticles = 11000;
                main.gravityModifier = 0f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(28f, 28f, 1f);
                shape.rotation = new Vector3(90f, 0f, 0f);

                var col = ps.collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.World;
                col.mode = ParticleSystemCollisionMode.Collision3D;
                col.sendCollisionMessages = true;
                col.collidesWith = ~0;
                col.quality = ParticleSystemCollisionQuality.High;
                col.enableDynamicColliders = true;
                col.lifetimeLoss = 1f;
                col.bounce = 0f;
                col.dampen = 1f;
                col.radiusScale = 1f;

                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.y = new ParticleSystem.MinMaxCurve(-3f, -2f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                EditorUtility.SetDirty(ps);
                log.Add("BlizzardManager + snow particle system retuned");
            }
            else log.Add("no BlizzardManager");

            // --- Player -----------------------------------------------------------------------
            var player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (player != null)
            {
                var so = new SerializedObject(player);
                Set(so, "hazardDamagePerSecond", 0.06f);
                Set(so, "drowningMultiplier", 4f);
                Set(so, "regenDelaySeconds", 9f);
                Set(so, "regenPerSecond", 0.02f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(player);
                log.Add("PlayerController: more hazard damage, slower regen");
            }

            // --- Flood ------------------------------------------------------------------------
            var flood = Object.FindFirstObjectByType<Flood>(FindObjectsInactive.Include);
            if (flood != null)
            {
                var so = new SerializedObject(flood);
                Set(so, "floodSpeed", 0.65f);
                Set(so, "endSpeedMultiplier", 3f);
                Set(so, "rampSeconds", 70f);
                Set(so, "currentStrength", 2f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(flood);
                log.Add("Flood: faster rise, stronger current");
            }

            // --- Wildfire ---------------------------------------------------------------------
            var fire = Object.FindFirstObjectByType<WildfireManager>(FindObjectsInactive.Include);
            if (fire != null)
            {
                var so = new SerializedObject(fire);
                Set(so, "cellBurnSeconds", 20f);
                Set(so, "spreadCompletionFraction", 0.75f);
                Set(so, "minOriginDistanceFromPlayer", 6f);
                Set(so, "damageRadius", 12f);
                Set(so, "maxDamagePerSecond", 0.3f);
                Set(so, "buildingIgniteRadius", 14f);
                Set(so, "maxConcurrentBuildingFires", 6);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(fire);
                log.Add("WildfireManager: wider, hotter, faster front");
            }

            // --- Earthquake (shared tuner, also saves the scene) -------------------------------
            var quake = EarthquakeTuneCommands.AmpEarthquake();
            log.Add("amp_earthquake: " + string.Join("; ", (List<string>)quake.GetType().GetProperty("changes").GetValue(quake)));

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add($"saved {scene.path}");
            return new { changes = log };
        }

        static void Set(SerializedObject so, string prop, float value) => EarthquakeTuneCommands.Set(so, prop, value);
        static void Set(SerializedObject so, string prop, int value) => EarthquakeTuneCommands.Set(so, prop, value);
    }
}
