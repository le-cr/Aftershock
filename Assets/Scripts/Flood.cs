using UnityEngine;
using UnityEngine.InputSystem;

public class Flood : MonoBehaviour
{
    [SerializeField] PlayerController playerController;
    [SerializeField] GameObject waterTintPanel;
    [SerializeField] GameObject timeInWaterText;

    bool isFlooding;
    [SerializeField] float floodSpeed = 1f;
    [SerializeField] float maxFloodHeight = 10f;

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
        if (other.CompareTag("Player"))
        {
            playerController.inWater = true;
            waterTintPanel.SetActive(true);
            timeInWaterText.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerController.inWater = false;
            waterTintPanel.SetActive(false);
            timeInWaterText.SetActive(false);
        }
    }
}
