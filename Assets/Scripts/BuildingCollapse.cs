using UnityEngine;

/// <summary>
/// Drives an OpenFracture <see cref="Fracture"/> component from a key press and then throws the
/// resulting fragments apart, so the building reads as a collapsing structure rather than a mesh
/// that quietly falls into a neat pile.
/// </summary>
[RequireComponent(typeof(Fracture))]
public class BuildingCollapse : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Key that collapses the building.")]
    public KeyCode collapseKey = KeyCode.T;

    [Header("Separation")]
    [Tooltip("Outward impulse applied to every fragment from the blast origin.")]
    public float explosionForce = 3.5f;

    [Tooltip("Radius of the blast. Fragments further than this from the origin get no impulse.")]
    public float explosionRadius = 25f;

    [Tooltip("Metres the blast origin is pushed below the building, which adds lift to the debris.")]
    public float upwardsModifier = 0.5f;

    [Tooltip("Height of the blast origin as a fraction of the building height. 0 = base, 1 = roof.")]
    [Range(0f, 1f)]
    public float blastHeightFraction = 0.05f;

    [Tooltip("Extra sideways scatter applied to a fragment at the roof. Base fragments get none, so the building splays outward as it comes down.")]
    public float lateralScatter = 1.2f;

    [Tooltip("Random spin applied to each fragment.")]
    public float randomTorque = 1.2f;

    [Header("Settling")]
    [Tooltip("Linear damping on the fragments so the pile settles instead of skating.")]
    public float fragmentDrag = 0.15f;

    [Tooltip("Angular damping on the fragments.")]
    public float fragmentAngularDrag = 1.5f;

    [Header("Damage")]
    [Tooltip("Health removed when a fragment strikes the player. Health runs 0-1.")]
    public float debrisDamage = 0.08f;

    [Tooltip("Minimum impact speed before a fragment hurts. Stops resting rubble from grinding the player down.")]
    public float debrisMinImpactSpeed = 2.5f;

    [Tooltip("Seconds before the same fragment can hurt the player again.")]
    public float debrisRearmSeconds = 0.5f;

    [Header("Cleanup")]
    [Tooltip("Destroy the rubble after it has settled.")]
    public bool despawnFragments = false;

    [Tooltip("Seconds before the rubble is destroyed, if despawning is enabled.")]
    public float fragmentLifetime = 30f;

    bool collapsed;

    void Update()
    {
        if (!collapsed && Input.GetKeyDown(collapseKey))
        {
            Collapse();
        }
    }

    [ContextMenu("Collapse")]
    public void Collapse()
    {
        if (collapsed) return;
        collapsed = true;

        // Cache the bounds before fracturing; the source object is deactivated by CauseFracture.
        var bounds = GetComponent<MeshRenderer>().bounds;
        var parent = transform.parent;
        var rootName = $"{name}Fragments";

        GetComponent<Fracture>().CauseFracture();

        // Fracture deactivates this GameObject, so nothing further can run on it: no coroutine, no
        // Update. A synchronous fracture is already finished here, so separate straight away.
        var fragmentRoot = FindFragmentRoot(parent, rootName);
        if (fragmentRoot != null && fragmentRoot.childCount > 0)
        {
            Separate(fragmentRoot, bounds);
            if (despawnFragments)
            {
                Destroy(fragmentRoot.gameObject, fragmentLifetime);
            }
            return;
        }

        // An asynchronous fracture spreads the fragments over the next few frames, so hand the job
        // to a separate live object that can still tick.
        var runner = new GameObject($"{name}CollapseRunner").AddComponent<DeferredFragmentSeparator>();
        runner.Begin(this, parent, rootName, bounds);
    }

    internal void SeparateAndCleanUp(Transform fragmentRoot, Bounds bounds)
    {
        Separate(fragmentRoot, bounds);
        if (despawnFragments)
        {
            Destroy(fragmentRoot.gameObject, fragmentLifetime);
        }
    }

    void Separate(Transform fragmentRoot, Bounds bounds)
    {
        var origin = new Vector3(
            bounds.center.x,
            Mathf.Lerp(bounds.min.y, bounds.max.y, blastHeightFraction),
            bounds.center.z);

        float height = Mathf.Max(bounds.size.y, 0.001f);

        foreach (var body in fragmentRoot.GetComponentsInChildren<Rigidbody>())
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.linearDamping = fragmentDrag;
            body.angularDamping = fragmentAngularDrag;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Flying debris hurts. Attached here because this is where the fragment bodies
            // are first handled; they are created at runtime by the fracture.
            body.gameObject.AddComponent<DebrisDamage>()
                .Configure(debrisDamage, debrisMinImpactSpeed, debrisRearmSeconds);

            body.AddExplosionForce(
                explosionForce,
                origin - Vector3.up * upwardsModifier,
                explosionRadius,
                0f,
                ForceMode.Impulse);

            // Fragments higher up the building are thrown further sideways, which is what makes
            // the debris splay out into a rubble field instead of dropping straight down.
            float heightFraction = Mathf.Clamp01((body.worldCenterOfMass.y - bounds.min.y) / height);

            var outward = body.worldCenterOfMass - origin;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f)
            {
                var scatter = Random.insideUnitCircle.normalized;
                outward = new Vector3(scatter.x, 0f, scatter.y);
            }

            body.AddForce(outward.normalized * (lateralScatter * heightFraction), ForceMode.Impulse);
            body.AddTorque(Random.insideUnitSphere * randomTorque, ForceMode.Impulse);
        }
    }

    static Transform FindFragmentRoot(Transform parent, string rootName)
    {
        if (parent != null)
        {
            return parent.Find(rootName);
        }

        var found = GameObject.Find(rootName);
        return found != null ? found.transform : null;
    }
}

/// <summary>
/// Waits for an asynchronous fracture to finish producing fragments and then hands them back to
/// <see cref="BuildingCollapse"/> to be thrown apart. Lives on its own GameObject because the
/// fractured object is deactivated the moment the fracture starts.
/// </summary>
class DeferredFragmentSeparator : MonoBehaviour
{
    public void Begin(BuildingCollapse owner, Transform parent, string rootName, Bounds bounds)
    {
        StartCoroutine(Run(owner, parent, rootName, bounds));
    }

    System.Collections.IEnumerator Run(BuildingCollapse owner, Transform parent, string rootName, Bounds bounds)
    {
        Transform fragmentRoot = null;

        for (int frame = 0; frame < 600; frame++)
        {
            fragmentRoot = parent != null ? parent.Find(rootName) : GameObject.Find(rootName)?.transform;

            if (fragmentRoot != null && fragmentRoot.childCount > 0)
            {
                // Give the fracture one more frame; if no new fragments arrived, it is done.
                int before = fragmentRoot.childCount;
                yield return null;
                if (fragmentRoot.childCount == before) break;
            }
            else
            {
                yield return null;
            }
        }

        if (fragmentRoot == null)
        {
            Debug.LogWarning($"{rootName}: fracture produced no fragments, nothing to separate.");
        }
        else if (owner != null)
        {
            owner.SeparateAndCleanUp(fragmentRoot, bounds);
        }

        Destroy(gameObject);
    }
}
