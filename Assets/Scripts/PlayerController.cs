using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    [Header("Boolean Values")]
    public bool inWater;
    public bool touchingSnow;

    [Header("Hazard damage")]
    [Tooltip("Health lost per second while exposed to snow or water. Health runs 0-1.")]
    [SerializeField] float hazardDamagePerSecond = 0.05f;

    [Header("Regeneration")]
    [Tooltip("Seconds without taking damage before health starts regenerating.")]
    [SerializeField] float regenDelaySeconds = 6f;

    [Tooltip("Health regained per second once regeneration kicks in.")]
    [SerializeField] float regenPerSecond = 0.03f;

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

        bool exposed = touchingSnow || inWater;

        // Snow and deep water make movement heavy; shelter (or dry land) restores it.
        if (firstPersonController != null)
            firstPersonController.EnvironmentSpeedMultiplier = exposed ? hazardSpeedMultiplier : 1f;

        if (exposed)
        {
            // Tick damage once per second so each hit is big enough to register on the tint.
            damageTickAccumulator += Time.deltaTime;
            if (damageTickAccumulator >= 1f)
            {
                damageTickAccumulator = 0f;
                TakeDamage(hazardDamagePerSecond);
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
