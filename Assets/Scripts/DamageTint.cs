using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen red wash for fire damage, built on the same idea as the underwater WaterTint panel
/// but driven in code: a sustained tint that deepens as the hazard closes in, plus a brighter
/// flash on every tick of damage so hits register even at low intensity.
///
/// The image is left enabled and driven purely through its alpha, so there is no per-frame
/// SetActive churn on the Canvas.
/// </summary>
[RequireComponent(typeof(Image))]
public class DamageTint : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] Color tintColor = new Color(1f, 0.13f, 0.05f);

    [Tooltip("Alpha of the steady wash at full hazard intensity.")]
    [Range(0f, 1f)]
    [SerializeField] float sustainedAlpha = 0.34f;

    [Tooltip("Alpha added by the flash when damage lands.")]
    [Range(0f, 1f)]
    [SerializeField] float flashAlpha = 0.5f;

    [Header("Timing")]
    [Tooltip("How fast a flash fades, in alpha units per second.")]
    [SerializeField] float flashFadeSpeed = 2.2f;

    [Tooltip("How fast the steady wash fades once the hazard stops refreshing it.")]
    [SerializeField] float sustainedFadeSpeed = 1.5f;

    private Image image;
    private float sustained;
    private float flash;

    void Awake()
    {
        image = GetComponent<Image>();
        image.raycastTarget = false;     // never eat clicks meant for the buttons underneath
        Apply(0f);
    }

    /// <summary>Steady wash strength, 0-1. Refresh every tick while the player is in danger.</summary>
    public void SetSustained(float intensity)
    {
        sustained = Mathf.Max(sustained, Mathf.Clamp01(intensity));
    }

    /// <summary>Punch the screen red. Called each time damage actually lands.</summary>
    public void Flash(float intensity = 1f)
    {
        flash = Mathf.Max(flash, Mathf.Clamp01(intensity));
    }

    void Update()
    {
        // Both decay on their own; the hazard has to keep refreshing them to stay visible.
        flash = Mathf.MoveTowards(flash, 0f, flashFadeSpeed * Time.deltaTime);
        sustained = Mathf.MoveTowards(sustained, 0f, sustainedFadeSpeed * Time.deltaTime);

        Apply(Mathf.Clamp01(sustained * sustainedAlpha + flash * flashAlpha));
    }

    private void Apply(float alpha)
    {
        if (image == null)
            return;

        image.color = new Color(tintColor.r, tintColor.g, tintColor.b, alpha);
        image.enabled = alpha > 0.002f;
    }
}
