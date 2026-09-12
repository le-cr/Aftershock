using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hard cap on live building-fragment Rigidbodies. New collapses are refused when the cap is
/// full, and any pile that would push past the limit is trimmed with DestroyImmediate so the
/// physics step never sees more bodies than <see cref="HardLimit"/>.
/// </summary>
public class DebrisBudget : MonoBehaviour
{
    public static DebrisBudget Instance { get; private set; }

    /// <summary>Absolute maximum fragment Rigidbodies allowed in the scene at once.</summary>
    public const int HardLimit = 96;

    [Tooltip("Hard cap on live fragment Rigidbodies. Enforced immediately with DestroyImmediate.")]
    [SerializeField] int maxLiveFragments = HardLimit;

    [Tooltip("How often to sleep / simplify settled fragments.")]
    [SerializeField] float settleInterval = 0.35f;

    [Tooltip("Speed below which a fragment is treated as settled.")]
    [SerializeField] float settleSpeed = 0.4f;

    [Tooltip("Destroy entire piles older than this many seconds.")]
    [SerializeField] float maxPileAge = 22f;

    struct Pile
    {
        public Transform root;
        public float born;
        public List<Rigidbody> bodies;
    }

    readonly List<Pile> piles = new List<Pile>(16);
    float nextSettleTime;
    int cachedLive;

    public int LiveCount => cachedLive;
    public int MaxLive => Mathf.Max(8, maxLiveFragments);
    public int FreeSlots => Mathf.Max(0, MaxLive - LiveCount);
    public bool IsFull => LiveCount >= MaxLive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Ensure();
    }

    public static DebrisBudget Ensure()
    {
        if (Instance != null) return Instance;
        var existing = FindFirstObjectByType<DebrisBudget>();
        if (existing != null) return Instance = existing;
        return new GameObject("DebrisBudget").AddComponent<DebrisBudget>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        maxLiveFragments = HardLimit;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>True when at least one more collapse is allowed under the hard cap.</summary>
    public bool CanCollapse(int estimatedFragments = 24)
    {
        RefreshLiveCount();
        return FreeSlots >= Mathf.Max(8, estimatedFragments / 3);
    }

    /// <summary>
    /// Track a freshly fractured pile and immediately destroy excess fragments until under the
    /// hard cap. Uses DestroyImmediate so the physics engine never simulates over-budget bodies.
    /// </summary>
    public void RegisterPile(Transform fragmentRoot)
    {
        if (fragmentRoot == null) return;

        var bodies = new List<Rigidbody>(64);
        fragmentRoot.GetComponentsInChildren(true, bodies);
        // Drop nulls / already-dead.
        bodies.RemoveAll(b => b == null);

        piles.Add(new Pile
        {
            root = fragmentRoot,
            born = Time.time,
            bodies = bodies,
        });

        RefreshLiveCount();
        EnforceHardLimitImmediate();
    }

    void Update()
    {
        if (piles.Count == 0)
        {
            cachedLive = 0;
            return;
        }

        CullExpiredPiles();
        RefreshLiveCount();

        if (Time.time >= nextSettleTime)
        {
            nextSettleTime = Time.time + settleInterval;
            SettleFragments();
            if (cachedLive > MaxLive)
                EnforceHardLimitImmediate();
        }
    }

    void LateUpdate()
    {
        // Catch anything that slipped through Destroy() schedules or async fracture runners.
        if (cachedLive > MaxLive)
            EnforceHardLimitImmediate();
    }

    void CullExpiredPiles()
    {
        float now = Time.time;
        for (int i = piles.Count - 1; i >= 0; i--)
        {
            var pile = piles[i];
            if (pile.root == null)
            {
                piles.RemoveAt(i);
                continue;
            }

            if (now - pile.born >= maxPileAge)
            {
                DestroyImmediateSafe(pile.root.gameObject);
                piles.RemoveAt(i);
            }
        }
    }

    void SettleFragments()
    {
        float speedSq = settleSpeed * settleSpeed;
        for (int i = 0; i < piles.Count; i++)
        {
            var bodies = piles[i].bodies;
            if (bodies == null) continue;

            for (int b = 0; b < bodies.Count; b++)
            {
                var rb = bodies[b];
                if (rb == null) continue;
                if (rb.isKinematic || rb.IsSleeping()) continue;

                if (rb.linearVelocity.sqrMagnitude <= speedSq &&
                    rb.angularVelocity.sqrMagnitude <= speedSq)
                {
                    if (rb.collisionDetectionMode != CollisionDetectionMode.Discrete)
                        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    rb.Sleep();
                }
            }
        }
    }

    void EnforceHardLimitImmediate()
    {
        RefreshLiveCount();
        int guard = 0;
        while (cachedLive > MaxLive && piles.Count > 0 && guard++ < 512)
        {
            // Prefer destroying whole oldest piles — cheapest way to free MeshColliders.
            int victim = OldestPileIndex();
            if (victim < 0)
                break;

            var doomed = piles[victim];
            int before = cachedLive;
            if (doomed.root != null)
                DestroyImmediateSafe(doomed.root.gameObject);
            piles.RemoveAt(victim);
            RefreshLiveCount();

            // If destroying the root didn't free enough (or root was already gone), thin bodies.
            if (cachedLive >= before)
                ThinAnyFragments(cachedLive - MaxLive);
        }

        // Final trim in case only partial piles remain over budget.
        if (cachedLive > MaxLive)
            ThinAnyFragments(cachedLive - MaxLive);
    }

    int OldestPileIndex()
    {
        int victim = -1;
        float oldest = float.PositiveInfinity;
        for (int i = 0; i < piles.Count; i++)
        {
            if (piles[i].root == null && CountValid(piles[i].bodies) == 0)
                continue;
            if (piles[i].born < oldest)
            {
                oldest = piles[i].born;
                victim = i;
            }
        }
        return victim;
    }

    void ThinAnyFragments(int needToRemove)
    {
        if (needToRemove <= 0) return;

        // Flatten into a temp list of (pileIndex, bodyIndex, mass) and kill lightest first.
        var entries = new List<(int pile, int body, float mass)>(needToRemove + 8);
        for (int p = 0; p < piles.Count; p++)
        {
            var bodies = piles[p].bodies;
            if (bodies == null) continue;
            for (int b = 0; b < bodies.Count; b++)
            {
                var rb = bodies[b];
                if (rb == null) continue;
                entries.Add((p, b, rb.mass));
            }
        }

        entries.Sort((a, b) => a.mass.CompareTo(b.mass));

        int removed = 0;
        for (int i = 0; i < entries.Count && removed < needToRemove; i++)
        {
            var e = entries[i];
            var bodies = piles[e.pile].bodies;
            var rb = bodies[e.body];
            if (rb == null) continue;
            DestroyImmediateSafe(rb.gameObject);
            bodies[e.body] = null;
            removed++;
        }

        RefreshLiveCount();
    }

    void RefreshLiveCount()
    {
        int n = 0;
        for (int i = piles.Count - 1; i >= 0; i--)
        {
            if (piles[i].root == null)
            {
                // Root gone — drop stale body refs.
                piles.RemoveAt(i);
                continue;
            }
            n += CountValid(piles[i].bodies);
        }
        cachedLive = n;
    }

    static int CountValid(List<Rigidbody> bodies)
    {
        if (bodies == null) return 0;
        int n = 0;
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i] != null) n++;
        return n;
    }

    static void DestroyImmediateSafe(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Object.DestroyImmediate(obj);
        else
            Object.DestroyImmediate(obj);
    }
}
