using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    private Image imageComponent;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        imageComponent = GetComponent<Image>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void ChangeHealth(float value)
    {
        imageComponent.fillAmount += value;
        Debug.Log(imageComponent.fillAmount);
    }
}
