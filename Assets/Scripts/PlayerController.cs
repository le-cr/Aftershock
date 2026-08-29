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
    [SerializeField] GameObject winScreen;

    private float timeSinceDamage = 0f;
    private bool isGameOver = false;

    // Update is called once per frame
    void Update()
    {
        if (isGameOver)
            return;

        timeSinceDamage += Time.deltaTime;
        if (touchingSnow || inWater)
        {
            if (timeSinceDamage > 1f) {
                healthBar.ChangeHealth(damage);
                timeSinceDamage = 0f;
            }
        }
    }

    /// <summary>External hazards (falling debris, etc.) route damage through here.</summary>
    public void TakeDamage(float amount)
    {
        if (isGameOver)
            return;

        healthBar.ChangeHealth(-Mathf.Abs(amount));
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
