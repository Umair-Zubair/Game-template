using UnityEngine;

/// <summary>
/// Reads PlayerBehaviorTracker data, classifies the player's combat style,
/// and applies the appropriate AdaptationProfile to the VoidbornGoddessController.
///
/// Mirrors HeuristicAdaptationManager but targets BossController instead of
/// EnemyController. Also computes distance locally because PlayerBehaviorTracker
/// only searches for EnemyController when finding the boss.
///
/// Attach to: VoidbornGoddess GameObject (same one that has VoidbornGoddessController).
/// </summary>
public class VoidbornAdaptationManager : MonoBehaviour, IBossAdaptationManager
{
    [Header("Evaluation Settings")]
    [SerializeField] private float evaluationInterval = 3f;
    [SerializeField] private float transitionDuration = 1f;

    [Header("Classification Thresholds")]
    [SerializeField] private float aggressiveThreshold = 0.50f;
    [SerializeField] private float passiveThreshold    = 0.20f;
    [SerializeField] private float aerialThreshold     = 0.40f;
    [SerializeField] private float rangedThreshold     = 0.50f;

    [Header("Debug")]
    public bool DebugMode = true;

    // ---- References ----
    private VoidbornGoddessController boss;
    private PlayerBehaviorTracker tracker;
    private Transform playerTransform;

    // ---- State ----
    private float evaluationTimer;
    [SerializeField, Header("Runtime Info (read-only)")]
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
        boss = GetComponent<VoidbornGoddessController>();
        if (boss == null)
            Debug.LogError("[VoidbornAdaptation] No VoidbornGoddessController found on this GameObject!");

        currentProfile  = AdaptationProfile.Default();
        targetProfile   = AdaptationProfile.Default();
        previousProfile = AdaptationProfile.Default();
    }

    private void Start()
    {
        tracker = FindFirstObjectByType<PlayerBehaviorTracker>();
        if (tracker == null)
            Debug.LogWarning("[VoidbornAdaptation] PlayerBehaviorTracker not found yet — will retry each frame.");
        else
            playerTransform = tracker.transform;

        boss?.ApplyAdaptationProfile(currentProfile);
    }

    private void Update()
    {
        if (tracker == null)
        {
            tracker = FindFirstObjectByType<PlayerBehaviorTracker>();
            if (tracker != null)
            {
                playerTransform = tracker.transform;
                if (DebugMode)
                    Debug.Log("[VoidbornAdaptation] PlayerBehaviorTracker found — adaptation active.");
            }
            else return;
        }

        if (boss == null) return;

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

        // PlayerBehaviorTracker.FindBoss() only finds EnemyController, not BossController,
        // so averageDistance in the profile is unreliable here. Compute it ourselves.
        float distance = playerTransform != null
            ? Vector2.Distance(playerTransform.position, boss.transform.position)
            : 999f;

        if (DebugMode)
            Debug.Log($"[VoidbornAdapt] aggro={profile.aggressionScore:F2} aerial={profile.jumpFrequency:F2} " +
                      $"atkFreq={profile.attackFrequency:F2}/s dist={distance:F1} → {currentStyle}");

        PlayerStyle newStyle = ClassifyPlayerStyle(profile, distance);

        if (newStyle != currentStyle)
        {
            PlayerStyle oldStyle = currentStyle;
            currentStyle = newStyle;
            BeginTransitionTo(SelectProfile(newStyle));

            if (DebugMode)
                Debug.Log($"[VoidbornAdaptation] Style change: {oldStyle} → {newStyle}");

            OnStyleChanged?.Invoke(oldStyle, newStyle);
        }
    }

    private PlayerStyle ClassifyPlayerStyle(PlayerProfile p, float distance)
    {
        if (p.aggressionScore >= aggressiveThreshold)
            return PlayerStyle.Aggressive;

        if (p.aggressionScore <= passiveThreshold && distance > 5f)
            return PlayerStyle.Defensive;

        if (p.jumpFrequency >= aerialThreshold && p.attackFrequency > 0.2f)
            return PlayerStyle.Aerial;

        if (p.rangedRatio >= rangedThreshold && distance > 5f)
            return PlayerStyle.Ranged;

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

    /// <summary>Fired when the detected player style changes. Args: (oldStyle, newStyle). Used by AISessionLogger.</summary>
    public event System.Action<PlayerStyle, PlayerStyle> OnStyleChanged;

    /// <summary>Force a re-evaluation immediately (e.g. on phase change).</summary>
    public void ForceEvaluate()
    {
        evaluationTimer = evaluationInterval;
    }

    /// <summary>
    /// Resets adaptation state for a new ML-Agents episode.
    /// Returns style to Balanced and profile to Default so the boss
    /// starts each episode with a clean slate.
    /// </summary>
    public void ResetForNewEpisode()
    {
        currentStyle    = PlayerStyle.Balanced;
        currentProfile  = AdaptationProfile.Default();
        targetProfile   = AdaptationProfile.Default();
        previousProfile = AdaptationProfile.Default();
        isTransitioning = false;
        evaluationTimer = 0f;

        if (boss != null)
            boss.ApplyAdaptationProfile(currentProfile);
    }
}
