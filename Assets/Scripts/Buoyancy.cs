using UnityEngine;

/// <summary>
/// Applies real Archimedes-principle buoyancy to a building using its existing fitted BoxCollider.
/// Samples submersion independently at the box's 4 local bottom corners (transformed to world space
/// each frame) so tilted or toppled buildings get correct per-corner depth and real torque, rather
/// than a single center-point lift. Auto-attached by <see cref="FloodSimulator"/> to every building
/// rigidbody under the city's "Buildings" container.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class Buoyancy : MonoBehaviour
{
    private static readonly Vector2[] CornerSigns =
    {
        new Vector2(-1f, -1f),
        new Vector2(-1f, 1f),
        new Vector2(1f, -1f),
        new Vector2(1f, 1f),
    };

    private BoxCollider box;
    private Rigidbody rb;
    private float originalLinearDamping;
    private float originalAngularDamping;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        rb = GetComponent<Rigidbody>();
        originalLinearDamping = rb.linearDamping;
        originalAngularDamping = rb.angularDamping;
    }

    private void FixedUpdate()
    {
        FloodSimulator flood = FloodSimulator.Instance;
        if (flood == null)
        {
            return;
        }

        float waterLevel = flood.WaterLevel;
        Vector3 scale = transform.lossyScale;

        float worldHeight = Mathf.Max(0.01f, box.size.y * scale.y);
        float totalVolume = Mathf.Max(0.0001f,
            box.size.x * scale.x * box.size.y * scale.y * box.size.z * scale.z);
        float volumePerCorner = totalVolume * 0.25f;

        float bottomLocalY = box.center.y - box.size.y * 0.5f;
        float halfX = box.size.x * 0.5f;
        float halfZ = box.size.z * 0.5f;
        float gravity = Mathf.Abs(Physics.gravity.y);

        float submersionSum = 0f;

        foreach (Vector2 sign in CornerSigns)
        {
            var localCorner = new Vector3(box.center.x + sign.x * halfX, bottomLocalY, box.center.z + sign.y * halfZ);
            Vector3 worldCorner = transform.TransformPoint(localCorner);

            float depth = Mathf.Clamp(waterLevel - worldCorner.y, 0f, worldHeight);
            float fraction = depth / worldHeight;
            submersionSum += fraction;

            if (fraction > 0f)
            {
                float force = flood.WaterDensity * gravity * volumePerCorner * fraction;
                if (flood.ClampBuoyantForce)
                {
                    force = Mathf.Min(force, rb.mass * gravity * flood.MaxForceMultiplier);
                }
                rb.AddForceAtPosition(Vector3.up * force, worldCorner, ForceMode.Force);
            }
        }

        float avgSubmersion = submersionSum * 0.25f;
        rb.linearDamping = Mathf.Lerp(originalLinearDamping, flood.WaterLinearDamping, avgSubmersion);
        rb.angularDamping = Mathf.Lerp(originalAngularDamping, flood.WaterAngularDamping, avgSubmersion);

        if (avgSubmersion > 0f && flood.CurrentDirection.sqrMagnitude > 0.0001f)
        {
            rb.AddForce(flood.CurrentDirection.normalized * flood.CurrentStrength * avgSubmersion, ForceMode.Force);
        }
    }
}
