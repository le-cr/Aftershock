using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the earthquake for the whole survival window: a main shock, then aftershocks every
/// 15-30 seconds until time is up. Buildings are held back rather than all dropped at once, so
/// later tremors still bring something down instead of being pure camera rattle.
/// Driven by DisasterManager rather than a key press.
/// </summary>
public class EarthquakeManager : MonoBehaviour
{
    [Header("Main shock")]
    [Tooltip("Seconds the camera shakes on the main shock. The shake decays to nothing over this time.")]
    [SerializeField] float shakeDuration = 8f;

    [Tooltip("Peak camera offset in metres on the main shock.")]
    [SerializeField] float shakeMagnitude = 0.35f;

    [Tooltip("Seconds of shaking before the first building gives way.")]
    [SerializeField] float leadIn = 0.6f;

    [Tooltip("Buildings brought down by the main shock.")]
    [SerializeField] int initialCollapseCount = 2;

    [Header("Aftershocks")]
    [Tooltip("Shortest gap between tremors, in seconds.")]
    [SerializeField] float minInterval = 15f;

    [Tooltip("Longest gap between tremors, in seconds.")]
    [SerializeField] float maxInterval = 30f;

    [Tooltip("Fallback quake window when triggered without an explicit duration. DisasterManager passes the survival time instead.")]
    [SerializeField] float defaultDuration = 120f;

    [Tooltip("Seconds an aftershock shakes for.")]
    [SerializeField] float aftershockShakeDuration = 4f;

    [Tooltip("Aftershock strength as a fraction of the main shock.")]
    [Range(0f, 1f)]
    [SerializeField] float aftershockMagnitudeScale = 0.6f;

    [Tooltip("Buildings brought down by each aftershock, while any are still standing.")]
    [SerializeField] int aftershockCollapseCount = 1;

    [Header("Constants")]
    [Tooltip("Seconds between one building coming down and the next within a single tremor.")]
    [SerializeField] float collapseStagger = 0.45f;

    [Header("References")]
    [Tooltip("Leave empty to collapse every BuildingCollapse in the scene, so new buildings need no rewiring.")]
    [SerializeField] BuildingCollapse[] buildings;

    [Tooltip("Leave empty to shake every CameraShake in the scene.")]
    [SerializeField] CameraShake[] cameraShakes;

    private bool triggered;
    private int quakeCount;
    private readonly List<BuildingCollapse> standing = new List<BuildingCollapse>();

    public bool HasTriggered => triggered;

    /// <summary>Number of tremors so far, main shock included. Useful for testing.</summary>
    public int QuakeCount => quakeCount;

    public int StandingBuildings => standing.Count;

    void Awake()
    {
        if (buildings == null || buildings.Length == 0)
            buildings = FindObjectsByType<BuildingCollapse>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (cameraShakes == null || cameraShakes.Length == 0)
            cameraShakes = FindObjectsByType<CameraShake>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>Start the quake using the inspector-authored window.</summary>
    public void TriggerEarthquake()
    {
        TriggerEarthquake(defaultDuration);
    }

    /// <summary>
    /// Start the quake and keep it going for <paramref name="duration"/> seconds.
    /// DisasterManager passes the survival time so tremors last exactly as long as the run.
    /// </summary>
    public void TriggerEarthquake(float duration)
    {
        if (triggered)
            return;

        triggered = true;

        standing.Clear();
        foreach (var b in buildings)
        {
            if (b != null && !b.HasCollapsed)
                standing.Add(b);
        }

        StartCoroutine(Run(duration));
    }

    private IEnumerator Run(float duration)
    {
        float endTime = Time.time + duration;

        // Main shock.
        quakeCount++;
        ShakeAll(shakeDuration, shakeMagnitude);
        yield return new WaitForSeconds(leadIn);
        yield return CollapseNext(initialCollapseCount);

        // Aftershocks, until the survival window closes.
        while (Time.time < endTime)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));

            if (Time.time >= endTime)
                break;

            quakeCount++;
            ShakeAll(aftershockShakeDuration, shakeMagnitude * aftershockMagnitudeScale);
            yield return CollapseNext(aftershockCollapseCount);
        }
    }

    private void ShakeAll(float duration, float magnitude)
    {
        foreach (var shake in cameraShakes)
        {
            if (shake != null)
                shake.Shake(duration, magnitude);
        }
    }

    /// <summary>Bring down up to <paramref name="count"/> of the buildings still standing.</summary>
    private IEnumerator CollapseNext(int count)
    {
        for (int i = 0; i < count && standing.Count > 0; i++)
        {
            var building = standing[0];
            standing.RemoveAt(0);

            if (building == null)
                continue;

            building.Collapse();
            yield return new WaitForSeconds(collapseStagger);
        }
    }
}
