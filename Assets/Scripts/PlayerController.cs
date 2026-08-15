using UnityEngine;
using TMPro;

public class PlayerController : MonoBehaviour
{
    public bool inWater;
    public bool touchingSnow;
    public int timesTouchedSnow = 0;
    public float timeSpentInWater = 0f;
    [SerializeField] float maxTimeSpentInWater = 10f;
    [SerializeField] float floodTimeMultiplier = 1.5f;
    [SerializeField] float damage = -0.05f;
    [SerializeField] TMP_Text timeInWaterText;
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
        if (touchingSnow)
        {
            if (timeSinceDamage > 1f) {
                healthBar.ChangeHealth(damage);
                timeSinceDamage = 0f;
            }
        }
        if (inWater)
        {
            timeSpentInWater += Time.deltaTime * floodTimeMultiplier;
            timeInWaterText.text = "Time spent in water: " + ((int)Mathf.Round(timeSpentInWater)).ToString();
            if (timeSinceDamage > 1f) {
                healthBar.ChangeHealth(damage);
                timeSinceDamage = 0f;
            }
        }
    }
}
