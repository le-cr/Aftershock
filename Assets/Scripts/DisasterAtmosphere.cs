using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Shifts the whole environment to match the disaster: sky, fog, sun, ambient light and colour
/// grading. Each disaster has three looks: a Warning look that creeps in during the countdown
/// (clouds gather, haze builds), an Active look at the moment it hits, and a Peak look the
/// disaster blends towards as it intensifies via <see cref="SetIntensity"/>.
///
/// Skies are six-sided skybox materials, which can't be lerped, so a sky change fades the
/// current sky's exposure to black, swaps the material, then fades the new one up.
///
/// Everything touched here is an instance (skybox copies, the volume's instanced profile), so
/// nothing is written back to project assets while playing in the editor.
/// </summary>
public class DisasterAtmosphere : MonoBehaviour
{
    [System.Serializable]
    public class Look
    {
        public Material skybox;
        public float skyExposure = 1f;
        public Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        public Color fogColor = new Color(0.74f, 0.84f, 0.95f);
        public float fogStart = 80f;
        public float fogEnd = 320f;
        public Color sunColor = new Color(1f, 0.96f, 0.84f);
        public float sunIntensity = 1.74f;
        public float ambientIntensity = 1f;
        [Range(-100f, 100f)] public float saturation = 6f;
        [Range(-100f, 100f)] public float temperature = 0f;
        [Range(-5f, 5f)] public float exposure = 0.35f;
        [Range(0f, 1f)] public float vignette = 0.22f;

        public static Look Lerp(Look a, Look b, float t)
        {
            return new Look
            {
                skybox = t < 0.5f ? a.skybox : b.skybox,
                skyExposure = Mathf.Lerp(a.skyExposure, b.skyExposure, t),
                skyTint = Color.Lerp(a.skyTint, b.skyTint, t),
                fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
                fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t),
                fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t),
                sunColor = Color.Lerp(a.sunColor, b.sunColor, t),
                sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
                ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t),
                saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                temperature = Mathf.Lerp(a.temperature, b.temperature, t),
                exposure = Mathf.Lerp(a.exposure, b.exposure, t),
                vignette = Mathf.Lerp(a.vignette, b.vignette, t),
            };
        }
    }

    [System.Serializable]
    public class DisasterLooks
    {
        public Look warning;
        public Look active;
        public Look peak;
    }

    [Header("References")]
    [SerializeField] Light sun;
    [SerializeField] Volume volume;

    [Header("Looks")]
    [SerializeField] Look clear;
    [SerializeField] DisasterLooks flood;
    [SerializeField] DisasterLooks blizzard;
    [SerializeField] DisasterLooks earthquake;
    [SerializeField] DisasterLooks wildfire;

    [Header("Constants")]
    [Tooltip("Seconds a look change takes by default.")]
    [SerializeField] float defaultTransitionSeconds = 8f;

    /// <summary>Extra multiplier on fog distances, for momentary effects such as whiteout gusts. 1 = none.</summary>
    public float FogDistanceScale { get; set; } = 1f;

    /// <summary>Extra sun intensity added this frame, for lightning. Reset every frame by the caller.</summary>
    public float SunFlash { get; set; }

    private Look current;
    private Look target;
    private float transitionSeconds;
    private DisasterLooks activeSet;
    private Material skyInstance;
    private Material skySource;
    private ColorAdjustments colorAdjustments;
    private WhiteBalance whiteBalance;
    private Vignette vignetteFx;
    private Coroutine skySwap;

    public Look Clear => clear;

    void Awake()
    {
        if (sun == null)
            sun = RenderSettings.sun;

        if (volume == null)
            volume = FindFirstObjectByType<Volume>();

        if (volume != null)
        {
            // .profile (not sharedProfile) instances the asset so runtime edits never touch disk.
            var profile = volume.profile;
            profile.TryGet(out colorAdjustments);
            profile.TryGet(out vignetteFx);
            if (!profile.TryGet(out whiteBalance))
                whiteBalance = profile.Add<WhiteBalance>(true);
        }

        // Default the "clear" look's sky to whatever the scene is already using.
        if (clear.skybox == null)
            clear.skybox = RenderSettings.skybox;

        current = Look.Lerp(clear, clear, 0f);
        target = current;
        transitionSeconds = defaultTransitionSeconds;
        ApplySky(clear.skybox, immediate: true);
        Apply(current);
    }

    public DisasterLooks LooksFor(DisasterManager.DisasterType type)
    {
        switch (type)
        {
            case DisasterManager.DisasterType.Flood: return flood;
            case DisasterManager.DisasterType.Blizzard: return blizzard;
            case DisasterManager.DisasterType.Earthquake: return earthquake;
            default: return wildfire;
        }
    }

    /// <summary>Countdown phase: the first signs of what is coming.</summary>
    public void BeginWarning(DisasterManager.DisasterType type, float seconds)
    {
        activeSet = LooksFor(type);
        TransitionTo(activeSet.warning, seconds);
    }

    /// <summary>The disaster hits.</summary>
    public void BeginActive(DisasterManager.DisasterType type, float seconds = -1f)
    {
        activeSet = LooksFor(type);
        TransitionTo(activeSet.active, seconds < 0f ? defaultTransitionSeconds : seconds);
    }

    /// <summary>Blend from the Active look towards the Peak look. 0 = just started, 1 = worst it gets.</summary>
    public void SetIntensity(float t, float seconds = 6f)
    {
        if (activeSet == null)
            return;

        TransitionTo(Look.Lerp(activeSet.active, activeSet.peak, Mathf.Clamp01(t)), seconds);
    }

    public void ReturnToClear(float seconds = -1f)
    {
        TransitionTo(clear, seconds < 0f ? defaultTransitionSeconds : seconds);
    }

    /// <summary>
    /// Head towards a look. Safe to call every frame with a moving target: the blend is an
    /// exponential approach, so re-targeting never stalls it.
    /// </summary>
    public void TransitionTo(Look look, float seconds)
    {
        target = look;
        transitionSeconds = Mathf.Max(seconds, 0.01f);

        if (look.skybox != null && look.skybox != skySource)
            ApplySky(look.skybox, immediate: false, fadeSeconds: Mathf.Min(seconds, 6f));
    }

    void Update()
    {
        // Reaches ~95% of the way in transitionSeconds.
        float k = 1f - Mathf.Exp(-3f * Time.deltaTime / transitionSeconds);
        current = Look.Lerp(current, target, k);
        current.skybox = target.skybox;
        Apply(current);
    }

    private void Apply(Look look)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = look.fogColor;
        RenderSettings.fogStartDistance = look.fogStart * FogDistanceScale;
        RenderSettings.fogEndDistance = Mathf.Max(look.fogEnd * FogDistanceScale, look.fogStart * FogDistanceScale + 5f);
        RenderSettings.ambientIntensity = look.ambientIntensity;

        if (sun != null)
        {
            sun.color = look.sunColor;
            sun.intensity = look.sunIntensity + SunFlash;
        }

        if (skyInstance != null && !swapping)
        {
            skyInstance.SetFloat("_Exposure", look.skyExposure);
            skyInstance.SetColor("_Tint", look.skyTint);
        }

        if (colorAdjustments != null)
        {
            colorAdjustments.saturation.Override(look.saturation);
            colorAdjustments.postExposure.Override(look.exposure);
        }

        if (whiteBalance != null)
            whiteBalance.temperature.Override(look.temperature);

        if (vignetteFx != null)
            vignetteFx.intensity.Override(look.vignette);
    }

    private bool swapping;

    private void ApplySky(Material source, bool immediate, float fadeSeconds = 3f)
    {
        if (source == null)
            return;

        if (immediate)
        {
            SetSkyInstance(source);
            return;
        }

        if (skySwap != null)
            StopCoroutine(skySwap);
        skySwap = StartCoroutine(SwapSky(source, fadeSeconds));
    }

    private void SetSkyInstance(Material source)
    {
        if (skyInstance != null)
            Destroy(skyInstance);

        skySource = source;
        skyInstance = new Material(source);
        RenderSettings.skybox = skyInstance;
        DynamicGI.UpdateEnvironment();
    }

    private IEnumerator SwapSky(Material next, float seconds)
    {
        swapping = true;
        float half = Mathf.Max(seconds * 0.5f, 0.1f);

        // Fade the old sky to black.
        float startExposure = skyInstance != null ? skyInstance.GetFloat("_Exposure") : 1f;
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            if (skyInstance != null)
                skyInstance.SetFloat("_Exposure", Mathf.Lerp(startExposure, 0f, t / half));
            yield return null;
        }

        SetSkyInstance(next);
        skyInstance.SetColor("_Tint", current.skyTint);

        // Fade the new sky up to whatever the transition currently wants.
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            skyInstance.SetFloat("_Exposure", Mathf.Lerp(0f, current.skyExposure, t / half));
            skyInstance.SetColor("_Tint", current.skyTint);
            yield return null;
        }

        swapping = false;
        skySwap = null;
    }

    void OnDestroy()
    {
        if (skyInstance != null)
            Destroy(skyInstance);
    }
}
