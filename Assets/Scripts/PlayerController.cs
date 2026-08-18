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

    [Header("References")]
    [SerializeField] HealthBar healthBar;

    private float timeSinceDamage = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        timeSinceDamage += Time.deltaTime;
        if (touchingSnow || inWater)
        {
            if (timeSinceDamage > 1f) {
                healthBar.ChangeHealth(damage);
                timeSinceDamage = 0f;
            }
        }
    }
}
