using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    private Image imageComponent;
    [SerializeField] PlayerController playerController;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        imageComponent = GetComponent<Image>();
    }

    // Update is called once per frame
    void Update()
    {
        if (imageComponent.fillAmount <= 0f)
        {
            playerController.Die();
        }
    }

    public void ChangeHealth(float value)
    {
        imageComponent.fillAmount += value;
    }
}
