using UnityEngine;

/// <summary>
/// Reads PlayerBehaviorTracker data, classifies the player's combat style,
/// and applies the appropriate AdaptationProfile to the boss's EnemyController.
///
/// Attach to: Boss GameObject (same one that has EnemyController).
/// Requires: EnemyController on same GameObject, PlayerBehaviorTracker in scene (on Player).
/// </summary>
public class HeuristicAdaptationManager : MonoBehaviour
{
    [Header("Evaluation Settings")]
    [Tooltip("How often (seconds) to re-evaluate the player's style and update the boss profile.")]
    [SerializeField] private float evaluationInterval = 5f;

    [Tooltip("How long (seconds) to smoothly lerp between old and new profile multipliers.")]
    [SerializeField] private float transitionDuration = 2f;

    [Header("Classification Thresholds")]
    [Tooltip("Aggression score above this = AGGRESSIVE player style.")]
    [SerializeField] private float aggressiveThreshold = 0.65f;

    [Tooltip("Block rate above this = DEFENSIVE player style.")]
    [SerializeField] private float defensiveThreshold = 0.35f;

    [Tooltip("Jump attack ratio above this = AERIAL player style.")]
    [SerializeField] private float aerialThreshold = 0.4f;

    [Tooltip("Ranged ratio above this = RANGED player style.")]
    [SerializeField] private float rangedThreshold = 0.5f;

    [Header("Debug")]
    public bool DebugMode = true;

    // ---- References ----
    private EnemyController boss;
    private PlayerBehaviorTracker tracker;

    // ---- State ----
    private float evaluationTimer;
    private PlayerStyle currentStyle = PlayerStyle.Balanced;
    private AdaptationProfile currentProfile;
    private AdaptationProfile targetProfile;
    private AdaptationProfile previousProfile;
    private float transitionTimer;
    private bool isTransitioning;

    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void Awake()
    {
        boss = GetComponent<EnemyController>();
        if (boss == null)
            Debug.LogError("[HeuristicAdaptation] No EnemyController found on this GameObject!");

        currentProfile  = AdaptationProfile.Default();
        targetProfile   = AdaptationProfile.Default();
        previousProfile = AdaptationProfile.Default();
    }

    private void Start()
    {
        // Find tracker on player
        tracker = FindFirstObjectByType<PlayerBehaviorTracker>();
        if (tracker == null)
            Debug.LogWarning("[HeuristicAdaptation] No PlayerBehaviorTracker found in scene. Adaptation disabled.");

        // Apply default profile immediately
        boss?.ApplyAdaptationProfile(currentProfile);
    }

    private void Update()
    {
        if (tracker == null || boss == null) return;

        // --- Periodic re-evaluation ---
        evaluationTimer += Time.deltaTime;
        if (evaluationTimer >= evaluationInterval)
        {
            evaluationTimer = 0f;
            EvaluateAndAdapt();
        }

        // --- Smooth profile transition ---
        if (isTransitioning)
        {
            transitionTimer += Time.deltaTime;
            float t = Mathf.Clamp01(transitionTimer / transitionDuration);
            currentProfile = LerpProfile(previousProfile, targetProfile, t);
            boss.ApplyAdaptationProfile(currentProfile);

            if (t >= 1f)
                isTransitioning = false;
        }
    }

    // =========================================================
    // Core Logic
    // =========================================================

    private void EvaluateAndAdapt()
    {
        PlayerProfile profile = tracker.Profile;
        PlayerStyle newStyle = ClassifyPlayerStyle(profile);

        if (newStyle != currentStyle)
        {
            if (DebugMode)
                Debug.Log($"[HeuristicAdaptation] Style change: {currentStyle} → {newStyle} " +
                          $"| Aggression: {profile.aggressionScore:F2} | BlockRate: {profile.blockRate:F2} " +
                          $"| AerialRatio: {profile.aerialRatio:F2} | AttackFreq: {profile.attackFrequency:F2}/s");

            currentStyle = newStyle;
            BeginTransitionTo(SelectProfile(newStyle));
        }
        // No log when style is unchanged — keeps console clean
    }

    /// <summary>
    /// Classifies the player's current style using a priority-ordered if/else tree.
    /// Order matters: more specific/dangerous patterns take priority.
    /// </summary>
    private PlayerStyle ClassifyPlayerStyle(PlayerProfile p)
    {
        // 1. Aggressive: high aggression score (attacks often + stays close)
        if (p.aggressionScore >= aggressiveThreshold)
            return PlayerStyle.Aggressive;

        // 2. Defensive: high block rate
        if (p.blockRate >= defensiveThreshold)
            return PlayerStyle.Defensive;

        // 3. Aerial: lots of jump attacks
        if (p.aerialRatio >= aerialThreshold && p.attackFrequency > 0.2f)
            return PlayerStyle.Aerial;

        // 4. Ranged: lots of ranged attacks from distance
        if (p.rangedRatio >= rangedThreshold && p.averageDistance > 5f)
            return PlayerStyle.Ranged;

        // 5. Default
        return PlayerStyle.Balanced;
    }

    private AdaptationProfile SelectProfile(PlayerStyle style)
    {
        switch (style)
        {
            case PlayerStyle.Aggressive: return AdaptationProfile.AntiAggressive();
            case PlayerStyle.Defensive:  return AdaptationProfile.AntiDefensive();
            case PlayerStyle.Aerial:     return AdaptationProfile.AntiAerial();
            case PlayerStyle.Ranged:     return AdaptationProfile.AntiRanged();
            default:                     return AdaptationProfile.Default();
        }
    }

    private void BeginTransitionTo(AdaptationProfile newProfile)
    {
        previousProfile = currentProfile;
        targetProfile   = newProfile;
        transitionTimer = 0f;
        isTransitioning = true;
    }

    // =========================================================
    // Profile Lerp
    // =========================================================

    private AdaptationProfile LerpProfile(AdaptationProfile a, AdaptationProfile b, float t)
    {
        return new AdaptationProfile
        {
            chaseSpeedMultiplier     = Mathf.Lerp(a.chaseSpeedMultiplier,     b.chaseSpeedMultiplier,     t),
            retreatRangeMultiplier   = Mathf.Lerp(a.retreatRangeMultiplier,   b.retreatRangeMultiplier,   t),
            retreatSpeedMultiplier   = Mathf.Lerp(a.retreatSpeedMultiplier,   b.retreatSpeedMultiplier,   t),
            attackCooldownMultiplier = Mathf.Lerp(a.attackCooldownMultiplier, b.attackCooldownMultiplier, t),
            dodgeCooldownMultiplier  = Mathf.Lerp(a.dodgeCooldownMultiplier,  b.dodgeCooldownMultiplier,  t),
            dashPriorityBonus        = Mathf.Lerp(a.dashPriorityBonus,        b.dashPriorityBonus,        t),
            artilleryPriorityBonus   = Mathf.Lerp(a.artilleryPriorityBonus,   b.artilleryPriorityBonus,   t),
        };
    }

    // =========================================================
    // Public API
    // =========================================================

    public PlayerStyle CurrentStyle => currentStyle;

    /// <summary>Force a re-evaluation immediately (e.g. on phase change).</summary>
    public void ForceEvaluate()
    {
        evaluationTimer = evaluationInterval;
    }
}
