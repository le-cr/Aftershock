using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Caps live building rubble so earthquake debris can't spawn thousands of MeshCollider
/// Rigidbodies and stall or crash the physics step. Buildings register fragment roots here
/// after they collapse; this manager culls the oldest piles when over budget, sleeps settled
/// chunks, and downgrades their collision mode once they've stopped flying.
/// </summary>
public class DebrisBudget : MonoBehaviour
{
    public static DebrisBudget Instance { get; private set; }

    [Tooltip("Hard cap on live fragment Rigidbodies. Oldest piles are destroyed first when exceeded.")]
    [SerializeField] int maxLiveFragments = 220;

    [Tooltip("Seconds after a pile is registered before it becomes eligible for budget culls.")]
    [SerializeField] float graceSeconds = 4f;

    [Tooltip("How often to sleep / simplify settled fragments.")]
    [SerializeField] float settleInterval = 0.5f;

    [Tooltip("Speed below which a fragment is treated as settled.")]
    [SerializeField] float settleSpeed = 0.35f;

    [Tooltip("Destroy entire piles older than this many seconds (in addition to per-building lifetime).")]
    [SerializeField] float maxPileAge = 40f;

    struct Pile
    {
        public Transform root;
        public float born;
        public List<Rigidbody> bodies;
    }

    readonly List<Pile> piles = new List<Pile>(16);
    float nextSettleTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // Recreate per loaded scene so rubble state never survives a Main reload.
        if (Instance != null) return;
        if (Object.FindFirstObjectByType<DebrisBudget>() != null) return;
        new GameObject("DebrisBudget").AddComponent<DebrisBudget>();
    }

    /// <summary>Ensure a budget manager exists in the active scene.</summary>
    public static DebrisBudget Ensure()
    {
        if (Instance != null) return Instance;
        var existing = Object.FindFirstObjectByType<DebrisBudget>();
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
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Track a freshly fractured building so it can be culled / settled.</summary>
    public void RegisterPile(Transform fragmentRoot)
    {
        if (fragmentRoot == null) return;

        var bodies = new List<Rigidbody>(64);
        fragmentRoot.GetComponentsInChildren(true, bodies);
        piles.Add(new Pile
        {
            root = fragmentRoot,
            born = Time.time,
            bodies = bodies,
        });

        EnforceBudget();
    }

    void Update()
    {
        if (piles.Count == 0) return;

        CullExpiredPiles();

        if (Time.time >= nextSettleTime)
        {
            nextSettleTime = Time.time + settleInterval;
            SettleFragments();
            EnforceBudget();
        }
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
                Destroy(pile.root.gameObject);
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
                    // Discrete is far cheaper once chunks are on the ground; ContinuousSpeculative
                    // was only needed while they were flying through the player.
                    if (rb.collisionDetectionMode != CollisionDetectionMode.Discrete)
                        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                    rb.Sleep();
                }
            }
        }
    }

    void EnforceBudget()
    {
        int live = CountLive();
        if (live <= maxLiveFragments) return;

        // Destroy oldest piles first (skip those still in the grace window so the current
        // collapse still feels violent).
        float now = Time.time;
        while (live > maxLiveFragments && piles.Count > 0)
        {
            int victim = -1;
            float oldest = float.PositiveInfinity;
            for (int i = 0; i < piles.Count; i++)
            {
                var pile = piles[i];
                if (pile.root == null) continue;
                if (now - pile.born < graceSeconds) continue;
                if (pile.born < oldest)
                {
                    oldest = pile.born;
                    victim = i;
                }
            }

            // Everything is still in grace — thin the oldest pile's farthest fragments instead.
            if (victim < 0)
            {
                ThinLargestPile(live - maxLiveFragments);
                return;
            }

            var doomed = piles[victim];
            int removed = doomed.bodies != null ? CountValid(doomed.bodies) : 0;
            if (doomed.root != null)
                Destroy(doomed.root.gameObject);
            piles.RemoveAt(victim);
            live -= removed;
        }
    }

    void ThinLargestPile(int needToRemove)
    {
        if (needToRemove <= 0 || piles.Count == 0) return;

        int best = 0;
        int bestCount = 0;
        for (int i = 0; i < piles.Count; i++)
        {
            int c = piles[i].bodies != null ? CountValid(piles[i].bodies) : 0;
            if (c > bestCount)
            {
                bestCount = c;
                best = i;
            }
        }

        var pile = piles[best];
        if (pile.bodies == null || pile.root == null) return;

        // Drop the smallest / lowest fragments first — they read least as flying debris.
        pile.bodies.Sort((a, b) =>
        {
            float ma = a != null ? a.mass : 0f;
            float mb = b != null ? b.mass : 0f;
            return ma.CompareTo(mb);
        });

        int removed = 0;
        for (int i = 0; i < pile.bodies.Count && removed < needToRemove; i++)
        {
            var rb = pile.bodies[i];
            if (rb == null) continue;
            Destroy(rb.gameObject);
            pile.bodies[i] = null;
            removed++;
        }
    }

    int CountLive()
    {
        int n = 0;
        for (int i = 0; i < piles.Count; i++)
            n += piles[i].bodies != null ? CountValid(piles[i].bodies) : 0;
        return n;
    }

    static int CountValid(List<Rigidbody> bodies)
    {
        int n = 0;
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i] != null) n++;
        return n;
    }
}
