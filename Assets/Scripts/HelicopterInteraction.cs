using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player walk up to the helicopter and press H to enter/exit it,
/// switching the active camera/audio listener and gating helicopter input
/// to only respond while the player is aboard.
/// </summary>
public class HelicopterInteraction : MonoBehaviour
{
    [Header("Player references")]
    [SerializeField] private FirstPersonController playerController;
    [SerializeField] private CharacterController playerCharacterController;
    [SerializeField] private MeshRenderer playerMeshRenderer;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener playerAudioListener;

    [Header("Helicopter references")]
    [SerializeField] private Camera helicopterCamera;
    [SerializeField] private AudioListener helicopterAudioListener;
    [SerializeField] private ControlPanel controlPanel;
    [SerializeField] private HelicopterController helicopterController;

    [Header("Exit")]
    [SerializeField] private Transform exitPoint;

    [Header("UI")]
    [SerializeField] private GameObject cockpitUI;

    private bool playerInRange;
    private bool isPiloting;

    private void Awake()
    {
        helicopterCamera.enabled = false;
        helicopterAudioListener.enabled = false;
        controlPanel.enabled = false;
        cockpitUI.SetActive(false);

        playerCamera.enabled = true;
        playerAudioListener.enabled = true;
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.hKey.wasPressedThisFrame)
        {
            if (!isPiloting && playerInRange)
                EnterHelicopter();
            else if (isPiloting)
                ExitHelicopter();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<CharacterController>() != null)
            playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<CharacterController>() != null)
            playerInRange = false;
    }

    private void EnterHelicopter()
    {
        isPiloting = true;

        playerController.enabled = false;
        playerCharacterController.enabled = false;
        playerMeshRenderer.enabled = false;
        playerCamera.enabled = false;
        playerAudioListener.enabled = false;

        helicopterCamera.enabled = true;
        helicopterAudioListener.enabled = true;
        controlPanel.enabled = true;

        cockpitUI.SetActive(true);
        UIGameController.runtime.ShowInfo();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ExitHelicopter()
    {
        isPiloting = false;

        controlPanel.enabled = false;
        helicopterCamera.enabled = false;
        helicopterAudioListener.enabled = false;
        helicopterController.ResetControls();
        cockpitUI.SetActive(false);

        playerCharacterController.enabled = false;
        playerCharacterController.transform.SetPositionAndRotation(exitPoint.position, exitPoint.rotation);
        playerCharacterController.enabled = true;

        playerController.enabled = true;
        playerMeshRenderer.enabled = true;
        playerCamera.enabled = true;
        playerAudioListener.enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}