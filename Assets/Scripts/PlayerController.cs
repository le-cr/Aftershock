using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

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
    [SerializeField] GameObject deathScreen;

    private float timeSinceDamage = 0f;

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

    public void Respawn()
    {
        deathScreen.SetActive(false);
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    public void Die()
    {
        deathScreen.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Win()
    {
        
    }
}
