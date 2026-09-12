using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the title screen: Play / Settings / Quit, and the settings panel
/// (disaster choice, music volume, prep time, survive time).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    const string MainSceneName = "Main";

    [Header("Panels")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject settingsPanel;

    [Header("Main buttons")]
    [SerializeField] Button playButton;
    [SerializeField] Button settingsButton;
    [SerializeField] Button quitButton;
    [SerializeField] Button settingsBackButton;

    [Header("Disaster choice")]
    [SerializeField] Toggle disasterRandom;
    [SerializeField] Toggle disasterEarthquake;
    [SerializeField] Toggle disasterBlizzard;
    [SerializeField] Toggle disasterFlood;
    [SerializeField] Toggle disasterWildfire;

    [Header("Sliders")]
    [SerializeField] Slider musicVolumeSlider;
    [SerializeField] Slider prepTimeSlider;
    [SerializeField] Slider surviveTimeSlider;
    [SerializeField] TMP_Text musicVolumeValue;
    [SerializeField] TMP_Text prepTimeValue;
    [SerializeField] TMP_Text surviveTimeValue;

    bool suppressCallbacks;

    void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // Back on the title screen — Inspector values on Main win again until Play is pressed.
        GameSettings.ClearMenuOverrides();
        GameSettings.ApplyAudioVolume();

        if (mainPanel != null) mainPanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        WireButtons();
        LoadSettingsIntoUI();
    }

    void WireButtons()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(OnPlay);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(ShowSettings);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuit);
        }

        if (settingsBackButton != null)
        {
            settingsBackButton.onClick.RemoveAllListeners();
            settingsBackButton.onClick.AddListener(HideSettings);
        }

        WireDisasterToggle(disasterRandom, GameSettings.DisasterChoice.Random);
        WireDisasterToggle(disasterEarthquake, GameSettings.DisasterChoice.Earthquake);
        WireDisasterToggle(disasterBlizzard, GameSettings.DisasterChoice.Blizzard);
        WireDisasterToggle(disasterFlood, GameSettings.DisasterChoice.Flood);
        WireDisasterToggle(disasterWildfire, GameSettings.DisasterChoice.Wildfire);

        WireSlider(musicVolumeSlider, v =>
        {
            GameSettings.MusicVolume = v;
            SetValueLabel(musicVolumeValue, $"{Mathf.RoundToInt(v * 100f)}%");
        });

        WireSlider(prepTimeSlider, v =>
        {
            GameSettings.PrepSeconds = v;
            SetValueLabel(prepTimeValue, $"{Mathf.RoundToInt(v)}s");
        });

        WireSlider(surviveTimeSlider, v =>
        {
            GameSettings.SurviveSeconds = v;
            SetValueLabel(surviveTimeValue, $"{Mathf.RoundToInt(v)}s");
        });
    }

    void WireDisasterToggle(Toggle toggle, GameSettings.DisasterChoice choice)
    {
        if (toggle == null) return;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(on =>
        {
            if (suppressCallbacks || !on) return;
            GameSettings.Disaster = choice;
        });
    }

    void WireSlider(Slider slider, System.Action<float> onChanged)
    {
        if (slider == null) return;
        slider.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.AddListener(v =>
        {
            if (suppressCallbacks) return;
            onChanged(v);
        });
    }

    void LoadSettingsIntoUI()
    {
        suppressCallbacks = true;

        SelectDisasterToggle(GameSettings.Disaster);

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.minValue = 0f;
            musicVolumeSlider.maxValue = 1f;
            musicVolumeSlider.value = GameSettings.MusicVolume;
            SetValueLabel(musicVolumeValue, $"{Mathf.RoundToInt(GameSettings.MusicVolume * 100f)}%");
        }

        if (prepTimeSlider != null)
        {
            prepTimeSlider.minValue = GameSettings.MinPrepSeconds;
            prepTimeSlider.maxValue = GameSettings.MaxPrepSeconds;
            prepTimeSlider.wholeNumbers = true;
            prepTimeSlider.value = GameSettings.PrepSeconds;
            SetValueLabel(prepTimeValue, $"{Mathf.RoundToInt(GameSettings.PrepSeconds)}s");
        }

        if (surviveTimeSlider != null)
        {
            surviveTimeSlider.minValue = GameSettings.MinSurviveSeconds;
            surviveTimeSlider.maxValue = GameSettings.MaxSurviveSeconds;
            surviveTimeSlider.wholeNumbers = true;
            surviveTimeSlider.value = GameSettings.SurviveSeconds;
            SetValueLabel(surviveTimeValue, $"{Mathf.RoundToInt(GameSettings.SurviveSeconds)}s");
        }

        suppressCallbacks = false;
        GameSettings.ApplyAudioVolume();
    }

    void SelectDisasterToggle(GameSettings.DisasterChoice choice)
    {
        SetToggle(disasterRandom, choice == GameSettings.DisasterChoice.Random);
        SetToggle(disasterEarthquake, choice == GameSettings.DisasterChoice.Earthquake);
        SetToggle(disasterBlizzard, choice == GameSettings.DisasterChoice.Blizzard);
        SetToggle(disasterFlood, choice == GameSettings.DisasterChoice.Flood);
        SetToggle(disasterWildfire, choice == GameSettings.DisasterChoice.Wildfire);
    }

    static void SetToggle(Toggle toggle, bool on)
    {
        if (toggle == null) return;
        toggle.SetIsOnWithoutNotify(on);
    }

    static void SetValueLabel(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }

    void ShowSettings()
    {
        LoadSettingsIntoUI();
        if (mainPanel != null) mainPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    void HideSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    void OnPlay()
    {
        GameSettings.ApplyAudioVolume();
        GameSettings.ArmMenuOverrides();
        SceneManager.LoadScene(MainSceneName);
    }

    void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
