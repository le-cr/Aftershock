using UnityEngine;
using TMPro;

/// <summary>
/// Owns the run's disaster: picks one at random, names it in the HUD, counts down
/// the warning, then fires the disaster the HUD named and starts the survival timer.
///
/// The HUD text is the single source of truth — whatever <see cref="disasterText"/>
/// says is what actually happens.
/// </summary>
public class DisasterManager : MonoBehaviour
{
    public enum DisasterType
    {
        Flood,
        Blizzard,
        Earthquake,
        Wildfire,
    }

    [Header("Constants")]
    [Tooltip("Seconds of warning before the disaster begins.")]
    [SerializeField] float warningSeconds = 60f;
    [Tooltip("Seconds the player must survive once the disaster begins.")]
    [SerializeField] float surviveSeconds = 120f;
    [Tooltip("Force a specific disaster instead of picking at random. Useful for testing.")]
    [SerializeField] bool overrideRandomPick = false;
    [SerializeField] DisasterType forcedDisaster = DisasterType.Flood;

    [Header("Warning phase")]
    [SerializeField] GameObject disasterGroup;
    [SerializeField] TMP_Text disasterText;
    [SerializeField] CountdownTimer disasterTimer;

    [Header("Survival phase")]
    [SerializeField] GameObject surviveGroup;
    [SerializeField] CountdownTimer surviveTimer;

    [Header("Disasters")]
    [SerializeField] Flood flood;
    [SerializeField] GameObject blizzard;
    [SerializeField] EarthquakeManager earthquake;
    [SerializeField] WildfireManager wildfire;

    [Header("References")]
    [SerializeField] PlayerController playerController;
    [SerializeField] DisasterAtmosphere atmosphere;

    private DisasterType chosenDisaster;

    public DisasterType ChosenDisaster => chosenDisaster;

    void Awake()
    {
        if (atmosphere == null)
            atmosphere = FindFirstObjectByType<DisasterAtmosphere>();

        // Menu Play arms overrides; playing Main directly keeps Inspector constants.
        if (GameSettings.MenuOverridesArmed)
            ApplyMenuSettings();
    }

    void Start()
    {
        // Music volume always comes from saved settings (menu or prior session).
        GameSettings.ApplyAudioVolume();

        chosenDisaster = overrideRandomPick
            ? forcedDisaster
            : (DisasterType)Random.Range(0, System.Enum.GetValues(typeof(DisasterType)).Length);

        BeginWarning();
    }

    /// <summary>
    /// Apply prep / survive times and disaster choice from the main-menu settings.
    /// Only called when the run was started from the title screen.
    /// </summary>
    void ApplyMenuSettings()
    {
        warningSeconds = GameSettings.PrepSeconds;
        surviveSeconds = GameSettings.SurviveSeconds;

        var choice = GameSettings.Disaster;
        if (choice == GameSettings.DisasterChoice.Random)
        {
            overrideRandomPick = false;
        }
        else
        {
            overrideRandomPick = true;
            forcedDisaster = (DisasterType)(int)choice;
        }
    }

    private void BeginWarning()
    {
        if (blizzard != null)
            blizzard.SetActive(false);

        if (surviveGroup != null)
            surviveGroup.SetActive(false);

        // Build the wildfire's grid and pre-instantiate its VFX now, during the countdown,
        // so ignition doesn't stall the frame it happens on.
        if (chosenDisaster == DisasterType.Wildfire && wildfire != null)
            wildfire.Prepare();

        // First signs: clouds gather, haze builds, the light changes over the countdown.
        if (atmosphere != null)
            atmosphere.BeginWarning(chosenDisaster, warningSeconds * 0.8f);

        // The storm itself arrives ahead of the water.
        if (chosenDisaster == DisasterType.Flood && flood != null)
            flood.BeginStorm();

        if (disasterText != null)
            disasterText.text = chosenDisaster.ToString().ToUpperInvariant() + " in";

        if (disasterGroup != null)
            disasterGroup.SetActive(true);

        if (disasterTimer != null)
        {
            disasterTimer.onTimerEnd.RemoveListener(TriggerDisaster);
            disasterTimer.onTimerEnd.AddListener(TriggerDisaster);
            disasterTimer.StartTimer(warningSeconds);
        }
    }

    /// <summary>Fire the disaster named in the HUD, then hand over to the survival timer.</summary>
    public void TriggerDisaster()
    {
        if (atmosphere != null)
            atmosphere.BeginActive(chosenDisaster, chosenDisaster == DisasterType.Earthquake ? 3f : 10f);

        switch (chosenDisaster)
        {
            case DisasterType.Flood:
                if (flood != null)
                    flood.BeginFlood();
                break;

            case DisasterType.Blizzard:
                if (blizzard != null)
                    blizzard.SetActive(true);
                break;

            case DisasterType.Earthquake:
                // Tremors continue for the whole survival window.
                if (earthquake != null)
                    earthquake.TriggerEarthquake(surviveSeconds);
                break;

            case DisasterType.Wildfire:
                // The front is paced to cover the map across the survival window.
                if (wildfire != null)
                    wildfire.TriggerWildfire(surviveSeconds);
                break;
        }

        BeginSurvival();
    }

    private void BeginSurvival()
    {
        if (disasterGroup != null)
            disasterGroup.SetActive(false);

        if (surviveGroup != null)
            surviveGroup.SetActive(true);

        if (surviveTimer != null)
        {
            surviveTimer.onTimerEnd.RemoveListener(OnSurvived);
            surviveTimer.onTimerEnd.AddListener(OnSurvived);
            surviveTimer.StartTimer(surviveSeconds);
        }
    }

    private void OnSurvived()
    {
        if (surviveGroup != null)
            surviveGroup.SetActive(false);

        if (playerController != null)
            playerController.Win();
    }
}
