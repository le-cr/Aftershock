using UnityEngine;

/// <summary>
/// Wraps one pooled fire VFX and defuses the three things that make the Vefects prefabs
/// expensive to spawn in bulk:
///
///   1. every child ParticleSystem ships with maxParticles = 1000 (the untouched Shuriken
///      default) while only ~10-35 are ever alive, so each instance reserves ~30x what it uses;
///   2. a child "Light"/"Lights" system spawns a real-time point light per instance, and the
///      project runs pixelLightCount = 2 with additionalLightsPerObjectLimit = 4;
///   3. the root AudioSource is PlayOnAwake + Loop, so N instances means N overlapping loops.
///
/// Taming happens once, the first time the instance is created by the pool.
/// </summary>
public class FireInstance : MonoBehaviour
{
    private ParticleSystem[] systems;
    private AudioSource audioSource;
    private GameObject lightChild;
    private bool tamed;

    /// <summary>Cell or building this instance is currently attached to. Managed by WildfireManager.</summary>
    public object Owner { get; set; }

    public void Tame(int maxParticlesPerSystem, bool disableDistortion)
    {
        if (tamed)
            return;

        tamed = true;
        systems = GetComponentsInChildren<ParticleSystem>(true);

        foreach (var ps in systems)
        {
            var main = ps.main;
            main.maxParticles = Mathf.Min(main.maxParticles, maxParticlesPerSystem);
        }

        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform)
                continue;

            if (lightChild == null && (child.name == "Light" || child.name == "Lights"))
            {
                lightChild = child.gameObject;
                lightChild.SetActive(false);
            }
            else if (disableDistortion && (child.name == "Distortion" || child.name == "Heat Distortion"))
            {
                // Samples the opaque texture; costs a scene-colour copy and breaks on RP assets
                // that have Require Opaque Texture off (Mobile_RPAsset does).
                child.gameObject.SetActive(false);
            }
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.Stop();
            audioSource.enabled = false;
        }
    }

    /// <summary>Only the handful of fires nearest the player carry a real-time light.</summary>
    public void SetLightEnabled(bool enabled)
    {
        if (lightChild != null)
            lightChild.SetActive(enabled);
    }

    public void Play()
    {
        gameObject.SetActive(true);

        foreach (var ps in systems)
            ps.Play(true);
    }

    public void Stop()
    {
        foreach (var ps in systems)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        SetLightEnabled(false);
        gameObject.SetActive(false);
    }
}
