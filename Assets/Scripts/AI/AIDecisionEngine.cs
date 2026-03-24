using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Three-layer AI Decision Engine for boss action selection.
/// Implements SDS Algorithm 1: Decision Selector Logic.
///
///   Step 1: Check HeuristicBrain → IF critical override, return immediately
///   Step 2: Check WeightedStrategyBrain → IF confidence > threshold, return
///   Step 3: ML inference (placeholder) → falls back to heuristic recommendation
///
/// Also owns the FairnessGuardian safety net and outcome tracking for Layer 2.
///
/// Attach to: Boss GameObject (same one with BossController + VoidbornAdaptationManager).
/// </summary>
public class AIDecisionEngine : MonoBehaviour
{
    [Header("Decision Engine Settings")]
    [Tooltip("How often (seconds) to re-evaluate the optimal action.")]
    [SerializeField] private float evaluationInterval = 0.3f;

    [Tooltip("Confidence threshold for Layer 2 (SDS Algorithm 1: θ = 0.4). " +
             "If weighted brain confidence is below this, fall through to Layer 3.")]
    [SerializeField] private float confidenceThreshold = 0.4f;

    [Tooltip("Save/load weighted brain data between sessions.")]
    [SerializeField] private bool persistWeights = true;

    [Header("Debug")]
    public bool DebugMode = true;

    // ---- Decision Modules (Brains) ----
    private HeuristicBrain heuristicBrain;
    private WeightedStrategyBrain weightedBrain;
    // Future: private IBossDecisionModule mlBrain;

    // ---- Safety Net ----
    private FairnessGuardian fairnessGuardian;

    // ---- References ----
    private BossController bossController;
    private VoidbornGoddessController goddess;
    private VoidbornAdaptationManager adaptationManager;
    private PlayerBehaviorTracker tracker;
    private Health bossHealth;
    private Health playerHealth;

    // ---- State ----
    private float evaluationTimer;

    /// <summary>The current recommended action from the last evaluation cycle.</summary>
    public BossDecision CurrentDecision { get; private set; }

    /// <summary>The game context from the last evaluation cycle.</summary>
    public GameContext CurrentContext { get; private set; }

    // ---- Outcome tracking (feeds into WeightedBrain) ----
    private BossActionType lastActionTaken = BossActionType.None;
    private float bossHealthAtActionStart;
    private float playerHealthAtActionStart;
    private PlayerStyle styleAtActionStart;
    private bool trackingOutcome;

    // ---- Persistence ----
    private string SavePath => Path.Combine(Application.persistentDataPath, "boss_ai_weights.json");

    // =========================================================
    // Public API
    // =========================================================

    /// <summary>The name of the layer that produced the current decision.</summary>
    public string ActiveLayer => CurrentDecision.source ?? "None";

    /// <summary>Whether the FairnessGuardian is currently throttling the boss.</summary>
    public bool IsFairnessActive => fairnessGuardian?.IsRelaxationActive ?? false;

    /// <summary>
    /// Cooldown multiplier from the FairnessGuardian.
    /// Boss should multiply its effective cooldowns by this (1.0 = normal, 1.3 = relaxation).
    /// </summary>
    public float FairnessCooldownMultiplier => fairnessGuardian?.CooldownMultiplier ?? 1f;

    /// <summary>Get current weighted brain weights for a style (debug display).</summary>
    public Dictionary<BossActionType, float> GetWeightsForStyle(PlayerStyle style)
    {
        return weightedBrain?.GetWeightsForStyle(style);
    }

    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void Awake()
    {
        bossController = GetComponent<BossController>();
        goddess = bossController as VoidbornGoddessController;
        adaptationManager = GetComponent<VoidbornAdaptationManager>();
        bossHealth = GetComponent<Health>();

        // Initialize brains
        heuristicBrain = new HeuristicBrain();
        weightedBrain = new WeightedStrategyBrain();
        fairnessGuardian = new FairnessGuardian();

        CurrentDecision = BossDecision.Default;
    }

    private void Start()
    {
        FindPlayerReferences();

        // Load persisted weights from previous sessions
        if (persistWeights) LoadWeights();
    }

    private void Update()
    {
        if (bossController == null || bossController.IsDead) return;

        // Retry finding player references if not yet found
        if (playerHealth == null) FindPlayerReferences();

        // Periodic evaluation
        evaluationTimer += Time.deltaTime;
        if (evaluationTimer >= evaluationInterval)
        {
            evaluationTimer = 0f;
            CurrentContext = BuildContext();

            // Run fairness guardian
            fairnessGuardian.Evaluate(
                CurrentContext.bossHealthNormalized,
                CurrentContext.playerHealthNormalized
            );

            // Run three-layer decision cascade
            CurrentDecision = RunDecisionCascade(CurrentContext);
        }
    }

    private void OnDestroy()
    {
        if (persistWeights) SaveWeights();
    }

    // =========================================================
    // Three-Layer Decision Cascade (SDS Algorithm 1)
    // =========================================================

    private BossDecision RunDecisionCascade(GameContext ctx)
    {
        // ---- Phase 1: Heuristic Rules (critical override check) ----
        BossDecision heuristic = heuristicBrain.Evaluate(ctx);
        if (heuristic.isCriticalOverride)
        {
            var final1 = fairnessGuardian.ApplyOverride(heuristic);
            if (DebugMode && final1.action != CurrentDecision.action)
                Debug.Log($"[AIEngine] Layer 1 OVERRIDE → {final1.action} (conf={final1.confidence:F2}) [{final1.source}]");
            return final1;
        }

        // ---- Phase 2: Weighted Strategy (confidence threshold check) ----
        BossDecision weighted = weightedBrain.Evaluate(ctx);
        if (weighted.confidence > confidenceThreshold)
        {
            var final2 = fairnessGuardian.ApplyOverride(weighted);
            if (DebugMode && final2.action != CurrentDecision.action)
                Debug.Log($"[AIEngine] Layer 2 → {final2.action} (conf={final2.confidence:F2}) [{final2.source}]");
            return final2;
        }

        // ---- Phase 3: ML Inference (placeholder — falls back to heuristic) ----
        // When ML-Agents is integrated, this will call mlBrain.Evaluate(ctx).
        // For now, use the heuristic recommendation as the ML placeholder.
        var fallback = heuristic;
        fallback.source = "Heuristic (ML placeholder)";
        var final3 = fairnessGuardian.ApplyOverride(fallback);

        if (DebugMode && final3.action != CurrentDecision.action)
            Debug.Log($"[AIEngine] Layer 3 (placeholder) → {final3.action} (conf={final3.confidence:F2})");

        return final3;
    }

    // =========================================================
    // Context Building
    // =========================================================

    private GameContext BuildContext()
    {
        PlayerProfile playerProfile = tracker != null ? tracker.Profile : new PlayerProfile();
        PlayerStyle style = adaptationManager != null
            ? adaptationManager.CurrentStyle
            : PlayerStyle.Balanced;

        // Read adaptation profile bonuses
        AdaptationProfile profile = bossController.ActiveProfile;
        float artilleryBonus = profile?.artilleryPriorityBonus ?? 0f;
        float dashBonus = profile?.dashPriorityBonus ?? 0f;

        // Boss health (normalized)
        float bossHP = 1f;
        if (bossHealth != null && bossHealth.MaxHealth > 0f)
            bossHP = bossHealth.currentHealth / bossHealth.MaxHealth;

        // Player health (normalized)
        float playerHP = 1f;
        if (playerHealth != null && playerHealth.MaxHealth > 0f)
            playerHP = playerHealth.currentHealth / playerHealth.MaxHealth;

        return new GameContext
        {
            bossHealthNormalized = bossHP,
            playerHealthNormalized = playerHP,
            distanceToPlayer = bossController.GetDistanceToPlayer(),
            canMeleeAttack = goddess != null && goddess.CanAttack,
            canUseArtillery = goddess != null && goddess.CanUseArtillery && !fairnessGuardian.BlockArtillery,
            isPlayerInAttackRange = bossController.PlayerInAttackRange(),
            isPlayerInDetectionRange = bossController.PlayerInDetectionRange(),
            playerProfile = playerProfile,
            currentPlayerStyle = style,
            artilleryPriorityBonus = artilleryBonus,
            dashPriorityBonus = dashBonus,
        };
    }

    // =========================================================
    // Outcome Tracking (feeds into Weighted Brain — Layer 2)
    // =========================================================

    /// <summary>
    /// Called when the boss begins an attack action (from ChaseState before transition).
    /// Snapshots health values for outcome computation.
    /// </summary>
    public void OnBossActionStarted(BossActionType action)
    {
        lastActionTaken = action;
        bossHealthAtActionStart = bossHealth != null ? bossHealth.currentHealth : 0f;
        playerHealthAtActionStart = playerHealth != null ? playerHealth.currentHealth : 0f;
        styleAtActionStart = adaptationManager != null
            ? adaptationManager.CurrentStyle
            : PlayerStyle.Balanced;
        trackingOutcome = true;

        if (DebugMode)
            Debug.Log($"[AIEngine] Action started: {action} | BossHP={bossHealthAtActionStart:F0} PlayerHP={playerHealthAtActionStart:F0}");
    }

    /// <summary>
    /// Called when an attack action completes (from state OnExit).
    /// Computes reward and updates weighted brain.
    /// </summary>
    public void OnBossActionCompleted(BossActionType action)
    {
        if (!trackingOutcome || action != lastActionTaken) return;
        trackingOutcome = false;

        float bossHealthNow = bossHealth != null ? bossHealth.currentHealth : 0f;
        float playerHealthNow = playerHealth != null ? playerHealth.currentHealth : 0f;

        float damageDealt = playerHealthAtActionStart - playerHealthNow; // Positive = boss hurt player
        float damageTaken = bossHealthAtActionStart - bossHealthNow;     // Positive = player hurt boss

        // Reward function:
        //   Hit player    → +1.0  (strong positive — the primary goal)
        //   Missed player → -0.2  (mild penalty — encouraged to find better openings)
        //   Got hit       → -0.3  (moderate penalty — trading isn't always bad)
        //
        // Possible outcomes:
        //   Hit + safe      = +1.0
        //   Hit + got hit   = +0.7  (won the trade)
        //   Miss + safe     = -0.2  (wasted cooldown)
        //   Miss + got hit  = -0.5  (worst case)
        float reward = (damageDealt > 0f ? 1f : -0.2f) + (damageTaken > 0f ? -0.3f : 0f);

        weightedBrain.UpdateWeight(action, styleAtActionStart, reward);

        if (DebugMode)
            Debug.Log($"[AIEngine] Action completed: {action} | Dealt={damageDealt:F0} Taken={damageTaken:F0} → reward={reward:F2}");
    }

    // =========================================================
    // Helpers
    // =========================================================

    private void FindPlayerReferences()
    {
        if (tracker == null)
            tracker = FindFirstObjectByType<PlayerBehaviorTracker>();

        if (playerHealth == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                playerHealth = playerObj.GetComponent<Health>();
        }
    }

    // =========================================================
    // Persistence
    // =========================================================

    private void SaveWeights()
    {
        try
        {
            string json = weightedBrain.SerializeWeights();
            File.WriteAllText(SavePath, json);
            if (DebugMode) Debug.Log($"[AIEngine] Weights saved to {SavePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AIEngine] Failed to save weights: {e.Message}");
        }
    }

    private void LoadWeights()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                string json = File.ReadAllText(SavePath);
                weightedBrain.DeserializeWeights(json);
                if (DebugMode) Debug.Log($"[AIEngine] Weights loaded from {SavePath}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AIEngine] Failed to load weights: {e.Message}");
        }
    }
}
