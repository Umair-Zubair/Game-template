using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tracks player combat behavior during a fight and exposes a normalized
/// PlayerProfile for the HeuristicAdaptationManager to read.
///
/// Attach to: Player GameObject.
/// Requires: MeleeAttack (auto-found), PlayerShield (auto-found), Health (auto-found).
/// </summary>
public class PlayerBehaviorTracker : MonoBehaviour
{
    [Header("Tracking Configuration")]
    [Tooltip("Duration of the rolling window in seconds. Older events are discarded.")]
    [SerializeField] private float windowDuration = 3f;

    [Tooltip("How often (seconds) to sample distance and block state.")]
    [SerializeField] private float sampleInterval = 0.5f;

    // ---- References (auto-found) ----
    private MeleeAttack meleeAttack;
    private BlobRangedAttack blobRangedAttack;
    private PlayerShield playerShield;
    private Health playerHealth;
    private Transform bossTransform;

    // ---- Rolling event logs ----
    private List<(float time, string type)> attackLog = new List<(float, string)>();
    private List<float> jumpLog = new List<float>(); // timestamps of Space presses

    // ---- Sampled metrics ----
    private List<float> distanceSamples  = new List<float>();
    private List<float> blockSamples     = new List<float>(); // 1 = blocking, 0 = not
    private float sampleTimer;

    // ---- Damage tracking ----
    private float totalDamageTaken;
    private float totalDamageDealt;

    // ---- Public read-only profile ----
    public PlayerProfile Profile { get; private set; }

    // ---- Debug ----
    [Header("Debug")]
    public bool DebugMode = true;

    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void Awake()
    {
        meleeAttack      = GetComponent<MeleeAttack>();
        blobRangedAttack = GetComponent<BlobRangedAttack>();
        playerShield     = GetComponent<PlayerShield>();
        playerHealth     = GetComponent<Health>();
    }

    private void Start()
    {
        // Subscribe to whichever attack component this character has
        if (meleeAttack != null)
        {
            meleeAttack.OnAttackPerformed += OnPlayerAttack;
            meleeAttack.OnDamageDealt     += RegisterDamageDealt;
        }
        if (blobRangedAttack != null)
            blobRangedAttack.OnAttackPerformed += OnPlayerAttack;

        if (meleeAttack == null && blobRangedAttack == null)
            Debug.LogWarning("[BehaviorTracker] No attack component found on player — attack metrics will be empty.");

        // Subscribe to damage taken event
        if (playerHealth != null)
        {
            playerHealth.OnDamageTaken += OnPlayerDamageTaken;
        }

        // Find boss — look for EnemyController in scene
        FindBoss();
    }

    private void OnDestroy()
    {
        if (meleeAttack != null)
        {
            meleeAttack.OnAttackPerformed -= OnPlayerAttack;
            meleeAttack.OnDamageDealt     -= RegisterDamageDealt;
        }
        if (blobRangedAttack != null)
            blobRangedAttack.OnAttackPerformed -= OnPlayerAttack;

        if (playerHealth != null)
            playerHealth.OnDamageTaken -= OnPlayerDamageTaken;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            jumpLog.Add(Time.time);

        PruneOldEvents();

        sampleTimer += Time.deltaTime;
        if (sampleTimer >= sampleInterval)
        {
            sampleTimer = 0f;
            TakeSample();
        }

        // Rebuild profile every frame (cheap — just reads lists)
        Profile = BuildProfile();
    }

    // =========================================================
    // Event Handlers
    // =========================================================

    private void OnPlayerAttack(string attackType)
    {
        attackLog.Add((Time.time, attackType));

        if (DebugMode)
            Debug.Log($"[BehaviorTracker] Attack logged: {attackType} | Window total: {attackLog.Count}");
    }

    private void OnPlayerDamageTaken(float damage)
    {
        totalDamageTaken += damage;
    }

    /// <summary>
    /// Called by MeleeAttack when it successfully hits the boss.
    /// Wire this up from MeleeAttack.OnDamageDealt.
    /// </summary>
    public void RegisterDamageDealt(float damage)
    {
        totalDamageDealt += damage;
    }

    // =========================================================
    // Sampling
    // =========================================================

    private void TakeSample()
    {
        // Distance sample
        if (bossTransform != null)
        {
            float dist = Vector2.Distance(transform.position, bossTransform.position);
            distanceSamples.Add(dist);
        }
        else
        {
            FindBoss(); // Retry if boss wasn't found yet
        }

        // Block sample
        float blockValue = (playerShield != null && playerShield.IsBlocking()) ? 1f : 0f;
        blockSamples.Add(blockValue);

        // Prune old samples (keep only within window)
        int maxSamples = Mathf.CeilToInt(windowDuration / sampleInterval);
        while (distanceSamples.Count > maxSamples) distanceSamples.RemoveAt(0);
        while (blockSamples.Count > maxSamples)    blockSamples.RemoveAt(0);
    }

    private void PruneOldEvents()
    {
        float cutoff = Time.time - windowDuration;
        attackLog.RemoveAll(e => e.time < cutoff);
        jumpLog.RemoveAll(t => t < cutoff);
    }

    // =========================================================
    // Profile Building
    // =========================================================

    private PlayerProfile BuildProfile()
    {
        var p = new PlayerProfile();

        // --- Attack frequency (attacks per second in window) ---
        p.attackFrequency = windowDuration > 0
            ? attackLog.Count / windowDuration
            : 0f;

        // --- Attack type breakdown ---
        int meleeCount      = 0;
        int uppercutCount   = 0;
        int jumpAttackCount = 0;
        int rangedCount     = 0;

        foreach (var entry in attackLog)
        {
            switch (entry.type)
            {
                case "melee":      meleeCount++;      break;
                case "uppercut":   uppercutCount++;   break;
                case "jumpAttack": jumpAttackCount++; break;
                case "ranged":     rangedCount++;     break;
            }
        }

        int totalAttacks = attackLog.Count;
        p.meleeRatio      = totalAttacks > 0 ? (float)meleeCount      / totalAttacks : 0f;
        p.uppercutRatio   = totalAttacks > 0 ? (float)uppercutCount   / totalAttacks : 0f;
        p.jumpAttackRatio = totalAttacks > 0 ? (float)jumpAttackCount / totalAttacks : 0f;
        p.rangedRatio     = totalAttacks > 0 ? (float)rangedCount     / totalAttacks : 0f;

        // --- Block rate (fraction of samples where player was blocking) ---
        p.blockRate = blockSamples.Count > 0
            ? Average(blockSamples)
            : 0f;

        // --- Average distance to boss ---
        p.averageDistance = distanceSamples.Count > 0
            ? Average(distanceSamples)
            : 999f;

        // --- Aggression score: composite of attack freq + closeness ---
        // Normalize attack freq (assume 1 attack/sec = very aggressive, matching melee cooldown)
        float normalizedFreq = Mathf.Clamp01(p.attackFrequency / 1f);
        // Normalize closeness (0 = far, 1 = very close; assume 3 units = close)
        float normalizedCloseness = Mathf.Clamp01(1f - (p.averageDistance / 10f));
        p.aggressionScore = (normalizedFreq * 0.6f) + (normalizedCloseness * 0.4f);

        // --- Jump attack ratio (aerial tendency) ---
        // --- Jump frequency (Space presses per second in window) ---
        p.jumpFrequency = windowDuration > 0 ? jumpLog.Count / windowDuration : 0f;

        // --- Damage stats ---
        p.totalDamageTaken = totalDamageTaken;
        p.totalDamageDealt = totalDamageDealt;

        return p;
    }

    // =========================================================
    // Helpers
    // =========================================================

    private float Average(List<float> list)
    {
        if (list.Count == 0) return 0f;
        float sum = 0f;
        foreach (float v in list) sum += v;
        return sum / list.Count;
    }

    private void FindBoss()
    {
        // Try EnemyController first (regular enemies)
        EnemyController enemy = FindFirstObjectByType<EnemyController>();
        if (enemy != null)
        {
            bossTransform = enemy.transform;
            return;
        }

        // Try BossController (Voidborn Goddess and other bosses)
        BossController boss = FindFirstObjectByType<BossController>();
        if (boss != null)
            bossTransform = boss.transform;
    }

    /// <summary>
    /// Reset all tracked data (call at start of each encounter).
    /// </summary>
    public void ResetTracking()
    {
        attackLog.Clear();
        jumpLog.Clear();
        distanceSamples.Clear();
        blockSamples.Clear();
        totalDamageTaken = 0f;
        totalDamageDealt = 0f;
        Profile = new PlayerProfile();

        if (DebugMode)
            Debug.Log("[BehaviorTracker] Tracking reset.");
    }
}

/// <summary>
/// Snapshot of the player's current combat behavior.
/// All ratio values are 0-1. Read by HeuristicAdaptationManager.
/// </summary>
public struct PlayerProfile
{
    // Attack behavior
    public float attackFrequency;   // Attacks per second in rolling window
    public float meleeRatio;        // Fraction of attacks that are melee
    public float uppercutRatio;     // Fraction of attacks that are uppercuts
    public float jumpAttackRatio;   // Fraction of attacks that are jump attacks
    public float rangedRatio;       // Fraction of attacks that are ranged

    // Positioning
    public float averageDistance;   // Mean distance to boss (world units)
    public float aggressionScore;   // 0 = passive, 1 = very aggressive

    // Defensive
    public float blockRate;         // 0 = never blocks, 1 = always blocking

    // Jump
    public float jumpFrequency;     // Jumps (Space presses) per second in rolling window

    // Damage
    public float totalDamageTaken;
    public float totalDamageDealt;
}
