using UnityEngine;

public class Flood : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerController playerController;
    [SerializeField] GameObject waterTintPanel;

    [Header("Constants")]
    [SerializeField] float floodSpeed = 1f;
    [SerializeField] float maxFloodHeight = 10f;

    [Tooltip("The rise accelerates by this factor over the flood's life, so it starts gentle and ends urgent.")]
    [SerializeField] float endSpeedMultiplier = 2.5f;
    [Tooltip("Seconds over which the rise ramps from base speed to base speed x endSpeedMultiplier.")]
    [SerializeField] float rampSeconds = 90f;

    private bool isFlooding;
    private float floodStartTime;

    public bool IsFlooding => isFlooding;

    /// <summary>Start raising the water. Called by DisasterManager when Flood is the chosen disaster.</summary>
    public void BeginFlood()
    {
        isFlooding = true;
        floodStartTime = Time.time;
    }

    public void StopFlood()
    {
        isFlooding = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (transform.position.y >= maxFloodHeight)
        {
            isFlooding = false;
        }
        if (isFlooding)
        {
            float ramp = Mathf.Clamp01((Time.time - floodStartTime) / Mathf.Max(rampSeconds, 0.01f));
            float speed = floodSpeed * Mathf.Lerp(1f, endSpeedMultiplier, ramp);
            transform.Translate(Vector3.up * speed * Time.deltaTime, Space.World);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("WaterDetection"))
        {
            playerController.inWater = true;
            waterTintPanel.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("WaterDetection"))
        {
            playerController.inWater = false;
            waterTintPanel.SetActive(false);
        }
    }
}
