using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public int timesTouchedSnow = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Debug.Log(timesTouchedSnow);
        if (timesTouchedSnow > 10000)
        {
            Debug.Log("player takes damage");
        }
    }
}
