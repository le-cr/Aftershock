using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shakes the camera (selling the ground shaking) and fractures and throws
/// around every building under cityRoot when triggered (G key). Does not
/// physically move Terrain's own transform: TerrainCollider is a static
/// heightfield collider that PhysX does not support moving at runtime, even
/// via a kinematic Rigidbody + MovePosition() - doing so was found to inject
/// enormous spurious contact velocity into anything resting on/near it.
/// </summary>
public class Earthquake : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Transform cityRoot;
    [SerializeField] private Transform cameraA;
    [SerializeField] private Transform cameraB;

    [Header("Shake")]
    [SerializeField] private float shakeDuration = 4f;
    [SerializeField] private float cameraShakeMagnitude = 0.25f;
    [SerializeField] private float shakeFrequency = 25f;

    [Header("Building Collapse")]
    [SerializeField] private float collapseInterval = 0.6f;
    [SerializeField] private float throwSpeed = 8f;
    [SerializeField] private float throwUpwardSpeed = 6f;
    [SerializeField] private float tumbleSpeed = 4f;
    [SerializeField] private float maxFragmentSpeed = 15f;
    [SerializeField] private float velocityClampDuration = 2f;

    private Vector3 cameraAOrigin;
    private Vector3 cameraBOrigin;
    private bool originCaptured;
    private readonly HashSet<Fracture> queuedThisQuake = new HashSet<Fracture>();

    // Re-scanned fresh every physics step (not a one-time captured list of
    // Rigidbody references): some fragments were observed to be missing from
    // a one-time GetComponentsInChildren snapshot taken right in the
    // onCompleted callback (root cause not fully pinned down), leaving them
    // with OpenFracture's raw near-zero auto mass and no velocity cap. Always
    // re-querying the fragment root's current children each step is a
    // self-healing catch-all: nothing under a tracked root can escape being
    // normalized, however it got there. FixedUpdate (not a coroutine) is used
    // because Unity only resumes a coroutine once per rendered frame even
    // with WaitForFixedUpdate, so during a hitch (e.g. from fracture
    // computation) where several physics steps run back to back to catch up,
    // a coroutine-based clamp would miss all but the last step.
    private readonly List<Transform> watchedFragmentRoots = new List<Transform>();
    private readonly List<float> watchedFragmentRootsExpiry = new List<float>();

    // Thrown fragments live here so they ignore collisions with OTHER, still-
    // standing buildings (CityBuilder.BuildingLayer). At 20m grid spacing a
    // fragment thrown at throwSpeed easily reaches a neighboring building
    // within a second or two; slamming into that neighbor's solid, non-convex
    // collider caused severe tunneling/depenetration blowups (fragments
    // reaching thousands of units/sec, once even hanging the Editor). Ignoring
    // fragment-vs-fragment here too avoids the same problem between two
    // different buildings' debris landing near each other.
    private const int FragmentLayer = 8;

    private void Start()
    {
        if (cameraA != null)
            cameraAOrigin = cameraA.localPosition;
        if (cameraB != null)
            cameraBOrigin = cameraB.localPosition;

        Physics.IgnoreLayerCollision(FragmentLayer, FragmentLayer, true);
        Physics.IgnoreLayerCollision(FragmentLayer, CityBuilder.BuildingLayer, true);

        originCaptured = true;
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.gKey.wasPressedThisFrame)
            Trigger();
    }

    private void FixedUpdate()
    {
        for (int i = watchedFragmentRoots.Count - 1; i >= 0; i--)
        {
            var fragmentsRoot = watchedFragmentRoots[i];

            if (fragmentsRoot == null || Time.time > watchedFragmentRootsExpiry[i])
            {
                watchedFragmentRoots.RemoveAt(i);
                watchedFragmentRootsExpiry.RemoveAt(i);
                continue;
            }

            foreach (var fragmentBody in fragmentsRoot.GetComponentsInChildren<Rigidbody>())
            {
                if (fragmentBody.mass < 0.01f)
                    fragmentBody.mass = 1f;
                fragmentBody.maxDepenetrationVelocity = 1f;
                fragmentBody.maxLinearVelocity = maxFragmentSpeed;
                fragmentBody.gameObject.layer = FragmentLayer;

                if (fragmentBody.linearVelocity.magnitude > maxFragmentSpeed)
                    fragmentBody.linearVelocity = fragmentBody.linearVelocity.normalized * maxFragmentSpeed;
            }
        }
    }

    public void Trigger()
    {
        StartCoroutine(ShakeRoutine());
        StartCoroutine(CollapseBuildings());
    }

    private IEnumerator ShakeRoutine()
    {
        if (!originCaptured)
            yield break;

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;

            float noiseX = (Mathf.PerlinNoise(Time.time * shakeFrequency, 0f) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(0f, Time.time * shakeFrequency) - 0.5f) * 2f;
            float noiseZ = (Mathf.PerlinNoise(Time.time * shakeFrequency, Time.time * shakeFrequency) - 0.5f) * 2f;

            if (cameraA != null)
                cameraA.localPosition = cameraAOrigin + new Vector3(noiseX, noiseY, noiseZ) * cameraShakeMagnitude;
            if (cameraB != null)
                cameraB.localPosition = cameraBOrigin + new Vector3(noiseX, noiseY, noiseZ) * cameraShakeMagnitude;

            yield return null;
        }

        if (cameraA != null)
            cameraA.localPosition = cameraAOrigin;
        if (cameraB != null)
            cameraB.localPosition = cameraBOrigin;
    }

    private IEnumerator CollapseBuildings()
    {
        if (cityRoot == null)
            yield break;

        // Fracture buildings strictly one at a time, waiting collapseInterval
        // between each: only one building's worth of fresh fragments is ever
        // mid-creation/settling against the terrain at once. Fracturing many
        // buildings' fragments in overlapping windows was found to overload
        // PhysX's contact solver (too many simultaneous new contacts for its
        // iteration budget to resolve), producing runaway velocity severe
        // enough to hang the Editor even with per-building safety fixes in
        // place - this eliminates that class of problem at the source.
        var buildings = cityRoot.GetComponentsInChildren<Fracture>();
        foreach (var fracture in buildings)
        {
            if (fracture == null || !fracture.gameObject.activeSelf)
                continue;
            if (queuedThisQuake.Contains(fracture))
                continue;

            queuedThisQuake.Add(fracture);
            FractureBuilding(fracture);

            yield return new WaitForSeconds(collapseInterval);
        }
    }

    private void FractureBuilding(Fracture fracture)
    {
        var parent = fracture.transform.parent;
        var fragmentsName = fracture.gameObject.name + "Fragments";
        var origin = fracture.transform.position;

        UnityEngine.Events.UnityAction onCompleted = null;
        onCompleted = () =>
        {
            fracture.callbackOptions.onCompleted.RemoveListener(onCompleted);

            var fragmentsRoot = parent != null ? parent.Find(fragmentsName) : null;
            if (fragmentsRoot == null)
                return;

            // OpenFracture's fragment Rigidbodies can end up with a near-zero auto mass
            // for tiny convex fracture pieces (observed ~1e-07). Normalize mass, move
            // to FragmentLayer (see Start) so they ignore other buildings/fragments,
            // and set velocity directly (mass-independent) so the throw speed stays
            // predictable. FixedUpdate re-applies this every step as a self-healing
            // catch-all (see watchedFragmentRoots), so this pass just gives fragments
            // their initial throw impulse immediately rather than waiting a step.
            foreach (var fragmentBody in fragmentsRoot.GetComponentsInChildren<Rigidbody>())
            {
                fragmentBody.mass = 1f;
                fragmentBody.maxDepenetrationVelocity = 1f;
                fragmentBody.maxLinearVelocity = maxFragmentSpeed;
                fragmentBody.gameObject.layer = FragmentLayer;

                var outward = fragmentBody.position - origin;
                if (outward.sqrMagnitude < 0.0001f)
                    outward = Random.onUnitSphere;
                outward.Normalize();

                fragmentBody.linearVelocity = outward * throwSpeed + Vector3.up * throwUpwardSpeed;
                fragmentBody.angularVelocity = Random.insideUnitSphere * tumbleSpeed;
            }

            watchedFragmentRoots.Add(fragmentsRoot);
            watchedFragmentRootsExpiry.Add(Time.time + velocityClampDuration);
        };

        fracture.callbackOptions.onCompleted.AddListener(onCompleted);
        fracture.CauseFracture();
    }
}