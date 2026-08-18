using UnityEngine;
using UnityEngine.InputSystem;

public class Flood : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PlayerController playerController;
    [SerializeField] GameObject waterTintPanel;

    [Header("Constants")]
    [SerializeField] float floodSpeed = 1f;
    [SerializeField] float maxFloodHeight = 10f;

    private bool isFlooding;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (transform.position.y >= maxFloodHeight)
        {
            isFlooding = false;
        }
        if (Keyboard.current.fKey.isPressed)
        {
            isFlooding = true;
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
