using System.Collections;
using UnityEngine;

/// <summary>
/// Runs the earthquake: rattles the cameras and brings every fracturable building down in a
/// staggered sequence, so the collapse rolls across the level instead of happening all at once.
/// Driven by DisasterManager rather than a key press.
/// </summary>
public class EarthquakeManager : MonoBehaviour
{
    [Header("Constants")]
    [Tooltip("Seconds the camera keeps shaking. The shake decays to nothing over this time.")]
    [SerializeField] float shakeDuration = 8f;

    [Tooltip("Peak camera offset in metres at the start of the quake.")]
    [SerializeField] float shakeMagnitude = 0.35f;

    [Tooltip("Seconds between one building coming down and the next.")]
    [SerializeField] float collapseStagger = 0.45f;

    [Tooltip("Seconds of shaking before the first building gives way.")]
    [SerializeField] float leadIn = 0.6f;

    [Header("References")]
    [Tooltip("Leave empty to collapse every BuildingCollapse in the scene, so new buildings need no rewiring.")]
    [SerializeField] BuildingCollapse[] buildings;

    [Tooltip("Leave empty to shake every CameraShake in the scene.")]
    [SerializeField] CameraShake[] cameraShakes;

    private bool triggered;

    public bool HasTriggered => triggered;

    void Awake()
    {
        if (buildings == null || buildings.Length == 0)
            buildings = FindObjectsByType<BuildingCollapse>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (cameraShakes == null || cameraShakes.Length == 0)
            cameraShakes = FindObjectsByType<CameraShake>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>Start the quake. Called by DisasterManager when Earthquake is the chosen disaster.</summary>
    public void TriggerEarthquake()
    {
        if (triggered)
            return;

        triggered = true;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        foreach (var shake in cameraShakes)
        {
            if (shake != null)
                shake.Shake(shakeDuration, shakeMagnitude);
        }

        yield return new WaitForSeconds(leadIn);

        foreach (var building in buildings)
        {
            if (building != null)
                building.Collapse();

            yield return new WaitForSeconds(collapseStagger);
        }
    }
}
