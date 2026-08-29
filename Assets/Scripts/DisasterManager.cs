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

    private DisasterType chosenDisaster;

    public DisasterType ChosenDisaster => chosenDisaster;

    void Start()
    {
        chosenDisaster = overrideRandomPick
            ? forcedDisaster
            : (DisasterType)Random.Range(0, System.Enum.GetValues(typeof(DisasterType)).Length);

        BeginWarning();
    }

    private void BeginWarning()
    {
        if (blizzard != null)
            blizzard.SetActive(false);

        if (surviveGroup != null)
            surviveGroup.SetActive(false);

        if (disasterText != null)
            disasterText.text = chosenDisaster + " in";

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
