using UnityEngine;
using TMPro;

public class PlayerController : MonoBehaviour
{
    [Header("Boolean Values")]
    public bool inWater;
    public bool touchingSnow;

    [Header("Constants")]
    [SerializeField] float floodTimeMultiplier = 1.5f;
    [SerializeField] float damage = -0.05f;

    [Tooltip("Health lost each time falling debris hits the player. Negative values remove health.")]
    [SerializeField] float debrisDamage = -0.05f;

    [Tooltip("Minimum seconds between debris hits. A collapse throws dozens of fragments at once, so without this the whole health bar would empty in about a second.")]
    [SerializeField] float debrisDamageCooldown = 1f;

    [Header("References")]
    [SerializeField] HealthBar healthBar;

    private float timeSinceDamage = 0f;
    private float timeSinceDebrisDamage = float.PositiveInfinity;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        timeSinceDamage += Time.deltaTime;
        timeSinceDebrisDamage += Time.deltaTime;
        if (touchingSnow || inWater)
        {
            if (timeSinceDamage > 1f) {
                healthBar.ChangeHealth(damage);
                timeSinceDamage = 0f;
            }
        }
    }

    /// <summary>
    /// Called by a piece of falling debris that has hit the player. Rate limited on its own
    /// timer, separate from the snow/water timer, so environmental damage and being buried in
    /// rubble don't cancel each other out.
    /// </summary>
    public void TakeDebrisHit()
    {
        if (timeSinceDebrisDamage < debrisDamageCooldown)
        {
            return;
        }

        timeSinceDebrisDamage = 0f;
        healthBar.ChangeHealth(debrisDamage);
    }
}
