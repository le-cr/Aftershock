using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Bar color at full health.")]
    [SerializeField] Color fullColor = new Color(0.30f, 0.85f, 0.35f);

    [Tooltip("Bar color at half health.")]
    [SerializeField] Color midColor = new Color(0.95f, 0.80f, 0.20f);

    [Tooltip("Bar color when nearly dead.")]
    [SerializeField] Color emptyColor = new Color(0.90f, 0.15f, 0.10f);

    [Header("References")]
    [SerializeField] PlayerController playerController;

    private Image imageComponent;

    public float Health => imageComponent != null ? imageComponent.fillAmount : 1f;

    void Awake()
    {
        imageComponent = GetComponent<Image>();
    }

    void Update()
    {
        ApplyColor();

        if (imageComponent.fillAmount <= 0f)
        {
            playerController.Die();
        }
    }

    public void ChangeHealth(float value)
    {
        imageComponent.fillAmount = Mathf.Clamp01(imageComponent.fillAmount + value);
    }

    /// <summary>Green at full, yellow at half, red near empty — readable at a glance.</summary>
    private void ApplyColor()
    {
        float h = imageComponent.fillAmount;
        imageComponent.color = h > 0.5f
            ? Color.Lerp(midColor, fullColor, (h - 0.5f) * 2f)
            : Color.Lerp(emptyColor, midColor, h * 2f);
    }
}
