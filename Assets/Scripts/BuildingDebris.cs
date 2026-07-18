using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// Spawns small debris chunks attached to a building. While an earthquake plays, chunks break off
/// (in proportion to the quake's current strength), gain physics, and drop as rubble — simulating
/// parts of the structure crumbling. Attached chunks are plain child transforms (no Rigidbody) so
/// they ride with the building until released, avoiding nested-rigidbody instability.
/// </summary>
public class BuildingDebris : MonoBehaviour
{
    private readonly Queue<Transform> attached = new Queue<Transform>();
    private Material debrisMaterial;
    private float pieceMass;
    private float releaseRatePerSecond;
    private float parentScale;

    /// <summary>
    /// Creates the debris chunks inside the given building-local bounds.
    /// </summary>
    /// <param name="localBounds">Building bounds in this transform's local (pre-scale) space.</param>
    public void Initialize(Bounds localBounds, Material material, int count, float pieceWorldSize,
                           float mass, float releaseRate)
    {
        debrisMaterial = material;
        pieceMass = mass;
        releaseRatePerSecond = releaseRate;
        parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);

        // Debris is created at a fixed local size so it ends up ~pieceWorldSize in world units.
        float localSize = pieceWorldSize / parentScale;

        for (int i = 0; i < count; i++)
        {
            // Bias placement toward the upper part of the building, where facades shed first.
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            var localPos = new Vector3(
                Random.Range(min.x, max.x),
                Random.Range(Mathf.Lerp(min.y, max.y, 0.35f), max.y),
                Random.Range(min.z, max.z));

            Transform piece = CreatePiece(localSize);
            piece.SetParent(transform, worldPositionStays: false);
            piece.localPosition = localPos;
            piece.localRotation = Random.rotationUniform;
            attached.Enqueue(piece);
        }
    }

    private Transform CreatePiece(float localSize)
    {
        ProBuilderMesh mesh = ShapeGenerator.GenerateCube(
            PivotLocation.Center, Vector3.one * localSize * Random.Range(0.7f, 1.3f));
        mesh.ToMesh();
        mesh.Refresh();

        GameObject go = mesh.gameObject;
        go.name = "Debris";

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null && debrisMaterial != null)
        {
            renderer.sharedMaterial = debrisMaterial;
        }

        return go.transform;
    }

    private void Update()
    {
        if (attached.Count == 0)
        {
            return;
        }

        EarthquakeSimulator quake = EarthquakeSimulator.Instance;
        if (quake == null || !quake.IsShaking)
        {
            return;
        }

        // Expected releases this frame scale with quake strength; use it as a probability.
        float chance = quake.ShakeStrength * releaseRatePerSecond * Time.deltaTime;
        if (Random.value < chance)
        {
            ReleasePiece();
        }
    }

    private void ReleasePiece()
    {
        Transform piece = attached.Dequeue();

        // Detach so it no longer follows the building; keep its current world pose. Reparent to
        // the building's container (not null) so "Clear City" still cleans it up.
        Vector3 worldPos = piece.position;
        piece.SetParent(transform.parent, worldPositionStays: true);

        var box = piece.gameObject.AddComponent<BoxCollider>();
        // Fit the collider to the cube mesh (default size 1 would be far too large once scaled).
        var mf = piece.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
        }

        var rb = piece.gameObject.AddComponent<Rigidbody>();
        rb.mass = pieceMass;
        rb.linearDamping = 0.15f;
        rb.angularDamping = 0.2f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Kick it outward from the building center and give it a tumble.
        Vector3 outward = worldPos - transform.position;
        outward.y = 0f;
        outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Random.insideUnitSphere;
        Vector3 impulse = (outward * Random.Range(0.5f, 1.5f) + Vector3.up * Random.Range(0.2f, 0.6f))
                          * pieceMass;
        rb.AddForce(impulse, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * pieceMass * 0.5f, ForceMode.Impulse);
    }
}
