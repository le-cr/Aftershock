using UnityEngine;

/// <summary>
/// Persistent player options shared by the main menu and a run of Main.
/// Disaster choice of Random leaves <see cref="DisasterManager"/> to pick at runtime.
///
/// Menu values only override <see cref="DisasterManager"/> Inspector constants when a run is
/// started with <see cref="ArmMenuOverrides"/> (Play from the title screen). Playing Main
/// directly in the Editor uses the Inspector values as authored.
/// </summary>
public static class GameSettings
{
    public enum DisasterChoice
    {
        Random = -1,
        Flood = 0,
        Blizzard = 1,
        Earthquake = 2,
        Wildfire = 3,
    }

    const string PrefDisaster = "aftershock.disaster";
    const string PrefMusicVolume = "aftershock.musicVolume";
    const string PrefPrepSeconds = "aftershock.prepSeconds";
    const string PrefSurviveSeconds = "aftershock.surviveSeconds";

    public const float DefaultMusicVolume = 0.75f;
    public const float DefaultPrepSeconds = 30f;
    public const float DefaultSurviveSeconds = 120f;

    public const float MinPrepSeconds = 10f;
    public const float MaxPrepSeconds = 120f;
    public const float MinSurviveSeconds = 30f;
    public const float MaxSurviveSeconds = 300f;

    /// <summary>
    /// When true, <see cref="DisasterManager"/> should apply menu prep/survive/disaster values
    /// over its Inspector constants. Armed by the title-screen Play button.
    /// </summary>
    public static bool MenuOverridesArmed { get; private set; }

    public static void ArmMenuOverrides() => MenuOverridesArmed = true;

    public static void ClearMenuOverrides() => MenuOverridesArmed = false;

    public static DisasterChoice Disaster
    {
        get => (DisasterChoice)PlayerPrefs.GetInt(PrefDisaster, (int)DisasterChoice.Random);
        set
        {
            PlayerPrefs.SetInt(PrefDisaster, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(PrefMusicVolume, DefaultMusicVolume);
        set
        {
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(PrefMusicVolume, clamped);
            PlayerPrefs.Save();
            ApplyAudioVolume();
        }
    }

    public static float PrepSeconds
    {
        get => PlayerPrefs.GetFloat(PrefPrepSeconds, DefaultPrepSeconds);
        set
        {
            PlayerPrefs.SetFloat(PrefPrepSeconds, Mathf.Clamp(value, MinPrepSeconds, MaxPrepSeconds));
            PlayerPrefs.Save();
        }
    }

    public static float SurviveSeconds
    {
        get => PlayerPrefs.GetFloat(PrefSurviveSeconds, DefaultSurviveSeconds);
        set
        {
            PlayerPrefs.SetFloat(PrefSurviveSeconds, Mathf.Clamp(value, MinSurviveSeconds, MaxSurviveSeconds));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Push the saved music volume into the AudioListener (and any active mixer-less SFX).</summary>
    public static void ApplyAudioVolume()
    {
        AudioListener.volume = Mathf.Clamp01(MusicVolume);
    }
}
