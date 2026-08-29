using UnityEngine;

/// <summary>
/// Drives the blizzard hazard. Snow particles physically collide with the world
/// (so a shelter roof actually blocks them), and the player takes damage only while
/// snow is still landing on them — step under cover and the exposure lapses.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class BlizzardManager : MonoBehaviour
{
    [Header("Constants")]
    [Tooltip("How long the player stays 'in the snow' after the last particle hit. " +
             "Acts as a grace period so stepping under a roof clears exposure.")]
    [SerializeField] float exposureGraceSeconds = 0.5f;

    [Header("References")]
    [SerializeField] PlayerController playerController;

    private ParticleSystem ps;
    private float lastSnowHitTime = float.NegativeInfinity;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
    }

    void OnDisable()
    {
        // The blizzard is over (or never started) — never leave the player stuck taking damage.
        lastSnowHitTime = float.NegativeInfinity;
        if (playerController != null)
            playerController.touchingSnow = false;
    }

    void Update()
    {
        if (playerController == null)
            return;

        playerController.touchingSnow = Time.time - lastSnowHitTime < exposureGraceSeconds;
    }

    /// <summary>
    /// Sent by the collision module (Send Collision Messages) with the GameObject that was hit.
    /// Unlike the trigger module this identifies the actual collider, so it can't confuse the
    /// player with terrain the way indexing into the trigger collider list did.
    /// </summary>
    private void OnParticleCollision(GameObject other)
    {
        // The hit may land on the Player root (CharacterController) or the Capsule child.
        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        lastSnowHitTime = Time.time;
    }
}
