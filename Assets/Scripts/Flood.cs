using UnityEngine;

public class Flood : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerController playerController;
    [SerializeField] GameObject waterTintPanel;

    [Header("Constants")]
    [SerializeField] float floodSpeed = 1f;
    [SerializeField] float maxFloodHeight = 10f;

    private bool isFlooding;

    public bool IsFlooding => isFlooding;

    /// <summary>Start raising the water. Called by DisasterManager when Flood is the chosen disaster.</summary>
    public void BeginFlood()
    {
        isFlooding = true;
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
            transform.Translate(Vector3.up * floodSpeed * Time.deltaTime, Space.World);
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
