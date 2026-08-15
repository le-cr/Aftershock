using UnityEngine;
using TMPro;

public class PlayerController : MonoBehaviour
{
    public bool inWater;
    public int timesTouchedSnow = 0;
    public float timeSpentInWater = 0f;
    [SerializeField] float maxTimeSpentInWater = 30f;
    [SerializeField] float floodTimeMultiplier = 1.5f;
    [SerializeField] TMP_Text timeInWaterText;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Debug.Log(timeSpentInWater);
        if (inWater)
        {
            timeSpentInWater += Time.deltaTime * floodTimeMultiplier;
            timeInWaterText.text = "Time spent in water: " + ((int)Mathf.Round(timeSpentInWater)).ToString();
        }
        if (timesTouchedSnow > 10000)
        {
            Debug.Log("player takes damage");
        }
        if (timeSpentInWater > maxTimeSpentInWater)
        {
            Debug.Log("player takes damage");
        }
    }
}
