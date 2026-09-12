using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    [Header("Boolean Values")]
    public bool inWater;
    public bool touchingSnow;

    [Header("Hazard damage")]
    [Tooltip("Health lost per second while exposed to snow or water. Health runs 0-1.")]
    [SerializeField] float hazardDamagePerSecond = 0.06f;

    [Tooltip("Multiplier on hazard damage once the head is under water.")]
    [SerializeField] float drowningMultiplier = 4f;

    [Tooltip("Submersion (0-1) above which the player is drowning rather than wading.")]
    [SerializeField] float drownSubmersion = 0.97f;

    /// <summary>
    /// Scales the per-second hazard damage. Hazards set this to express severity: a blizzard
    /// that has chilled the player for a minute hurts more than the first flurry. 1 = normal.
    /// </summary>
    public float HazardDamageMultiplier { get; set; } = 1f;

    /// <summary>True while the head is under water. Read by the HUD and by Flood for audio muffling.</summary>
    public bool IsDrowning { get; private set; }

    [Header("Regeneration")]
    [Tooltip("Seconds without taking damage before health starts regenerating.")]
    [SerializeField] float regenDelaySeconds = 9f;

    [Tooltip("Health regained per second once regeneration kicks in.")]
    [SerializeField] float regenPerSecond = 0.02f;

    [Header("Environmental slow")]
    [Tooltip("Move-speed multiplier while wading through water or exposed to blizzard snow.")]
    [Range(0.1f, 1f)]
    [SerializeField] float hazardSpeedMultiplier = 0.6f;

    [Header("References")]
    [SerializeField] HealthBar healthBar;
    [SerializeField] GameObject deathScreen;
    [SerializeField] GameObject winScreen;

    private DamageTint damageTint;
    private FirstPersonController firstPersonController;
    private float lastDamageTime = float.NegativeInfinity;
    private float damageTickAccumulator;
    private bool isGameOver = false;

    void Awake()
    {
        firstPersonController = GetComponent<FirstPersonController>();
        damageTint = FindFirstObjectByType<DamageTint>(FindObjectsInactive.Include);
    }

    void Update()
    {
        if (isGameOver)
            return;

        float submersion = firstPersonController != null ? firstPersonController.Submersion : 0f;
        IsDrowning = submersion >= drownSubmersion;
        bool exposed = touchingSnow || inWater || submersion > 0.3f;

        // Snow and deep water make movement heavy; shelter (or dry land) restores it.
        // Swimming is slower still.
        if (firstPersonController != null)
        {
            float mult = exposed ? hazardSpeedMultiplier : 1f;
            if (firstPersonController.IsSwimming)
                mult *= 0.75f;
            firstPersonController.EnvironmentSpeedMultiplier = mult;
        }

        if (exposed)
        {
            // Tick damage once per second so each hit is big enough to register on the tint.
            damageTickAccumulator += Time.deltaTime;
            if (damageTickAccumulator >= 1f)
            {
                damageTickAccumulator = 0f;

                float damage = hazardDamagePerSecond * HazardDamageMultiplier;
                if (IsDrowning)
                    damage *= drowningMultiplier;            // no air: this is what actually kills in a flood
                else if (!touchingSnow && submersion > 0f)
                    damage *= Mathf.Lerp(0.3f, 1f, submersion); // wading is only mildly harmful; deep cold water more so

                TakeDamage(damage);
            }
        }
        else
        {
            damageTickAccumulator = 0f;

            if (Time.time - lastDamageTime >= regenDelaySeconds)
                healthBar.ChangeHealth(regenPerSecond * Time.deltaTime);
        }
    }

    /// <summary>All hazards (snow, water, fire, falling debris) route damage through here.</summary>
    public void TakeDamage(float amount)
    {
        if (isGameOver)
            return;

        lastDamageTime = Time.time;
        healthBar.ChangeHealth(-Mathf.Abs(amount));

        if (damageTint != null)
            damageTint.Flash(Mathf.Clamp01(Mathf.Abs(amount) * 8f + 0.4f));
    }

    public void Respawn()
    {
        deathScreen.SetActive(false);
        winScreen.SetActive(false);
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    public void Die()
    {
        // Guard: HealthBar polls fillAmount every frame, and a win may already have landed.
        if (isGameOver)
            return;

        isGameOver = true;
        deathScreen.SetActive(true);
        ReleaseCursor();
    }

    public void Win()
    {
        if (isGameOver)
            return;

        isGameOver = true;
        winScreen.SetActive(true);
        ReleaseCursor();
    }

    private void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
