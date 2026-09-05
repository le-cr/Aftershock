using UnityEngine;

/// <summary>
/// Builds the simple ambient particle systems the disasters need (rain, dust, embers) in code,
/// so they need no prefab: just a material. Every system starts stopped; the owner drives its
/// emission rate and follows the player with it.
/// </summary>
public static class ParticleFactory
{
    public static ParticleSystem Create(string name, Transform parent, Material material, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    /// <summary>Rain: fast, thin streaks falling from a box above the player.</summary>
    public static ParticleSystem Rain(Transform parent, Material material)
    {
        var ps = Create("Rain", parent, material, 3000);

        var main = ps.main;
        main.startLifetime = 1.6f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(16f, 20f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
        main.startColor = new Color(0.80f, 0.87f, 0.97f, 0.75f);
        main.gravityModifier = 0.3f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(44f, 1f, 44f);
        shape.position = new Vector3(0f, 22f, 0f);
        shape.rotation = new Vector3(90f, 0f, 0f);       // emit downward

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = 0f; vel.y = 0f; vel.z = 0f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.09f;
        renderer.lengthScale = 0f;
        renderer.sortingFudge = 10f;

        return ps;
    }

    /// <summary>Dust: slow, soft, large puffs drifting up from ground level.</summary>
    public static ParticleSystem Dust(Transform parent, Material material, Color tint)
    {
        var ps = Create("Dust", parent, material, 400);

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = tint;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(36f, 0.5f, 36f);
        shape.rotation = new Vector3(-90f, 0f, 0f);      // emit upward

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = 5f;

        return ps;
    }

    /// <summary>Embers: small glowing sparks lofted by heat, drifting with the wind, fading out.</summary>
    public static ParticleSystem Embers(Transform parent, Material material)
    {
        var ps = Create("Embers", parent, material, 300);

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 1f), new Color(1f, 0.85f, 0.4f, 1f));
        main.gravityModifier = -0.05f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(30f, 2f, 30f);
        shape.position = new Vector3(0f, 1f, 0f);
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.2f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.5f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        return ps;
    }

    /// <summary>Set a world-space constant wind on a system's velocity-over-lifetime module.</summary>
    public static void SetWind(ParticleSystem ps, Vector3 wind)
    {
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = wind.x;
        vel.y = wind.y;
        vel.z = wind.z;
    }

    public static void SetRate(ParticleSystem ps, float rate)
    {
        var emission = ps.emission;
        emission.rateOverTime = rate;

        if (rate > 0f && !ps.isPlaying)
            ps.Play();
    }
}
