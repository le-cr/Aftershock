using UnityEngine;

/// <summary>
/// Attached to every building fragment by <see cref="BuildingCollapse"/>. Hurts the player
/// when the fragment strikes them hard enough, with a short re-arm so a single chunk grinding
/// along the player doesn't drain the whole health bar in one frame.
/// </summary>
public class DebrisDamage : MonoBehaviour
{
    private float damage = 0.08f;
    private float minImpactSpeed = 2.5f;
    private float rearmSeconds = 0.5f;
    private float lastHitTime = float.NegativeInfinity;

    public void Configure(float damagePerHit, float minimumImpactSpeed, float rearm)
    {
        damage = damagePerHit;
        minImpactSpeed = minimumImpactSpeed;
        rearmSeconds = rearm;
    }

    void OnCollisionEnter(Collision collision)
    {
        // The hit may land on the Player root (CharacterController) or the Capsule child.
        var player = collision.collider.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        if (collision.relativeVelocity.magnitude < minImpactSpeed)
            return;

        if (Time.time - lastHitTime < rearmSeconds)
            return;

        lastHitTime = Time.time;
        player.TakeDamage(damage);
    }
}
