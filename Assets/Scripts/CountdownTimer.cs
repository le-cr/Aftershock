using UnityEngine;
using UnityEngine.Events;
using TMPro; // Required for TextMesh Pro

/// <summary>
/// A reusable "00:00" countdown driven by an external owner (see DisasterManager).
/// Raises <see cref="onTimerEnd"/> when it reaches zero rather than hard-coding
/// what happens next, so one timer prefab can serve the warning and survival phases.
/// </summary>
public class CountdownTimer : MonoBehaviour
{
    [Header("Constants")]
    [SerializeField] private float duration = 30f; // Fallback when StartTimer() is called with no argument
    [SerializeField] private bool startOnEnable = false;

    [Header("References")]
    [SerializeField] private TextMeshProUGUI timerText; // Drag your UI text object here

    [Header("Events")]
    public UnityEvent onTimerEnd;

    private float timeRemaining;
    private bool isTimerRunning = false;

    public float TimeRemaining => timeRemaining;
    public bool IsRunning => isTimerRunning;

    private void OnEnable()
    {
        if (startOnEnable)
            StartTimer();
    }

    /// <summary>Restart the countdown using the inspector-authored duration.</summary>
    public void StartTimer()
    {
        StartTimer(duration);
    }

    /// <summary>Restart the countdown from <paramref name="seconds"/>.</summary>
    public void StartTimer(float seconds)
    {
        duration = seconds;
        timeRemaining = seconds;
        isTimerRunning = true;
        DisplayTime(timeRemaining);
    }

    public void StopTimer()
    {
        isTimerRunning = false;
    }

    private void Update()
    {
        if (!isTimerRunning)
            return;

        if (timeRemaining > 0)
        {
            // Subtract the time passed since the last frame
            timeRemaining -= Time.deltaTime;
            DisplayTime(Mathf.Max(timeRemaining, 0f));
        }
        else
        {
            timeRemaining = 0;
            isTimerRunning = false;
            DisplayTime(timeRemaining);
            onTimerEnd.Invoke();
        }
    }

    private void DisplayTime(float timeToDisplay)
    {
        if (timerText == null)
            return;

        // Math to calculate minutes and seconds
        float minutes = Mathf.FloorToInt(timeToDisplay / 60);
        float seconds = Mathf.FloorToInt(timeToDisplay % 60);

        // Formats the text as "00:00"
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}
