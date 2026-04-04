using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Three-layer AI Decision Engine for boss action selection.
/// Implements SDS Algorithm 1: Decision Selector Logic.
///
///   Layer 1: HeuristicBrain        — critical rule overrides, always first
///   Layer 2: MLBrain               — neural network, wins if confidence >= mlConfidenceThreshold
///   Layer 3: WeightedStrategyBrain — EMA weight fallback when ML absent or uncertain
///
/// Also owns the FairnessGuardian safety net and outcome tracking.
///
/// Attach to: Boss GameObject (same one with BossController + VoidbornAdaptationManager).
/// </summary>
public class AIDecisionEngine : MonoBehaviour
{
    [Header("Decision Engine Settings")]
    [Tooltip("How often (seconds) to re-evaluate the optimal action.")]
    [SerializeField] private float evaluationInterval = 0.3f;

    [Tooltip("Confidence threshold for Layer 3 weighted brain (θ = 0.4).")]
    [SerializeField] private float confidenceThreshold = 0.4f;

    [Tooltip("Save/load weighted brain data between sessions.")]
    [SerializeField] private bool persistWeights = true;

    [Header("ML Brain")]
    [Tooltip("Drag the VoidbornGoddess GameObject here (it has MLBrain on it).")]
    [SerializeField] private MLBrain mlBrain;

    [Tooltip("ML wins decisions when its confidence >= this. " +
             "Set to 1.1 during development (ML never wins). Drop to 0.6 after training.")]
    [SerializeField] private float mlConfidenceThreshold = 1.1f;

    [Header("Debug")]
    public bool DebugMode = true;

    // ---- Decision Modules ----
    private HeuristicBrain heuristicBrain;
    private WeightedStrategyBrain weightedBrain;

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
    private bool _prevFairnessActive;
    private bool playerDeathSubscribed;

    // =========================================================
    // Public API
    // =========================================================

    public BossDecision CurrentDecision { get; private set; }
    public GameContext  CurrentContext  { get; private set; }

    public string ActiveLayer          => CurrentDecision.source ?? "None";
    public bool   IsFairnessActive     => fairnessGuardian?.IsRelaxationActive ?? false;
    public float  FairnessCooldownMultiplier => fairnessGuardian?.CooldownMultiplier ?? 1f;

    // ---- ML Brain public state (for debug HUD) ----
    public bool  MLBrainActive => mlBrain != null && mlBrain.IsModelLoaded;
    public float MLConfidence  { get; private set; }

    // ---- Decision counters (for AISessionLogger) ----
    public int   TotalDecisions    { get; private set; }
    public float ConfidenceSum     { get; private set; }
    public int   LowConfidenceCount{ get; private set; }
    public int   L1DecisionCount   { get; private set; }  // Heuristic
    public int   L2DecisionCount   { get; private set; }  // MLBrain
    public int   L3DecisionCount   { get; private set; }  // Weighted

    // ---- Events ----
    public event System.Action<BossDecision>          OnDecisionChanged;
    public event System.Action                         OnFairnessActivated;
    public event System.Action<GameContext,BossDecision> OnDecisionEvaluated;

    public Dictionary<BossActionType, float> GetWeightsForStyle(PlayerStyle style)
        => weightedBrain?.GetWeightsForStyle(style);

    /// <summary>
    /// Resets transient per-episode state so the next ML-Agents episode starts clean.
    /// Persistent data (weighted brain, decision counters) is intentionally kept across episodes.
    /// </summary>
    public void ResetForNewEpisode()
    {
        evaluationTimer     = 0f;
        trackingOutcome     = false;
        _prevFairnessActive = false;
        CurrentDecision     = BossDecision.Default;

        fairnessGuardian?.Reset();
        FindPlayerReferences();
    }

    // ---- Outcome tracking ----
    private BossActionType lastActionTaken = BossActionType.None;
    private float bossHealthAtActionStart;
    private float playerHealthAtActionStart;
    private PlayerStyle styleAtActionStart;
    private bool trackingOutcome;

    private string SavePath => Path.Combine(Application.persistentDataPath, "boss_ai_weights.json");

    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void Awake()
    {
        bossController    = GetComponent<BossController>();
        goddess           = bossController as VoidbornGoddessController;
        adaptationManager = GetComponent<VoidbornAdaptationManager>();
        bossHealth        = GetComponent<Health>();

        heuristicBrain   = new HeuristicBrain();
        weightedBrain    = new WeightedStrategyBrain();
        fairnessGuardian = new FairnessGuardian();

        CurrentDecision  = BossDecision.Default;
    }

    private void Start()
    {
        FindPlayerReferences();

        if (bossController != null)
            bossController.OnDied += HandleBossDiedForML;

        if (persistWeights) LoadWeights();
    }

    private void Update()
    {
        if (bossController == null || bossController.IsDead) return;
        if (playerHealth == null) FindPlayerReferences();

        evaluationTimer += Time.deltaTime;
        if (evaluationTimer < evaluationInterval) return;

        evaluationTimer = 0f;
        CurrentContext  = BuildContext();

        fairnessGuardian.Evaluate(
            CurrentContext.bossHealthNormalized,
            CurrentContext.playerHealthNormalized);

        BossDecision previous = CurrentDecision;
        CurrentDecision = RunDecisionCascade(CurrentContext);

        // ---- Update counters ----
        TotalDecisions++;
        ConfidenceSum += CurrentDecision.confidence;
        if (CurrentDecision.confidence < confidenceThreshold) LowConfidenceCount++;

        if (CurrentDecision.source != null)
        {
            if      (CurrentDecision.source.StartsWith("Heuristic")) L1DecisionCount++;
            else if (CurrentDecision.source.StartsWith("MLBrain"))   L2DecisionCount++;
            else if (CurrentDecision.source.StartsWith("Weighted"))  L3DecisionCount++;
        }

        // ---- Fairness event ----
        bool fairNow = fairnessGuardian?.IsRelaxationActive ?? false;
        if (fairNow && !_prevFairnessActive) OnFairnessActivated?.Invoke();
        _prevFairnessActive = fairNow;

        OnDecisionEvaluated?.Invoke(CurrentContext, CurrentDecision);
        if (CurrentDecision.action != previous.action)
            OnDecisionChanged?.Invoke(CurrentDecision);
    }

    private void OnDestroy()
    {
        if (bossController != null)
            bossController.OnDied -= HandleBossDiedForML;
        if (playerHealth != null && playerDeathSubscribed)
            playerHealth.OnDamageTaken -= HandlePlayerDamagedForML;

        if (persistWeights) SaveWeights();
    }

    // =========================================================
    // Three-Layer Decision Cascade
    // =========================================================

    private BossDecision RunDecisionCascade(GameContext ctx)
    {
        // ---- Layer 1: Heuristic critical overrides ----
        BossDecision heuristic = heuristicBrain.Evaluate(ctx);
        if (heuristic.isCriticalOverride)
        {
            var final1 = fairnessGuardian.ApplyOverride(heuristic);
            if (DebugMode && final1.action != CurrentDecision.action)
                Debug.Log($"[AIEngine] L1 OVERRIDE → {final1.action} (conf={final1.confidence:F2})");
            return final1;
        }

        // ---- Layer 2: ML Brain ----
        // mlConfidenceThreshold defaults to 1.1 so ML never wins until
        // you lower it to 0.6 after training a model and assigning the .onnx
        if (mlBrain != null && mlBrain.IsModelLoaded)
        {
            mlBrain.CurrentContext = ctx;
            BossDecision ml = mlBrain.Evaluate(ctx);
            MLConfidence = ml.confidence;

            if (ml.confidence >= mlConfidenceThreshold)
            {
                var finalML = fairnessGuardian.ApplyOverride(ml);
                if (DebugMode && finalML.action != CurrentDecision.action)
                    Debug.Log($"[AIEngine] L2 ML → {finalML.action} (conf={finalML.confidence:F2})");
                return finalML;
            }

            if (DebugMode)
                Debug.Log($"[AIEngine] ML conf {ml.confidence:F2} < threshold {mlConfidenceThreshold:F2} — falling to L3");
        }

        // ---- Layer 3: Weighted strategy fallback ----
        BossDecision weighted = weightedBrain.Evaluate(ctx);
        if (weighted.confidence > confidenceThreshold)
        {
            var final3 = fairnessGuardian.ApplyOverride(weighted);
            if (DebugMode && final3.action != CurrentDecision.action)
                Debug.Log($"[AIEngine] L3 Weighted → {final3.action} (conf={final3.confidence:F2})");
            return final3;
        }

        // ---- Fallback: heuristic recommendation ----
        var fallback = fairnessGuardian.ApplyOverride(heuristic);
        if (DebugMode && fallback.action != CurrentDecision.action)
            Debug.Log($"[AIEngine] Fallback heuristic → {fallback.action}");
        return fallback;
    }

    // =========================================================
    // Context Building
    // =========================================================

    private GameContext BuildContext()
    {
        PlayerProfile playerProfile = tracker != null ? tracker.Profile : new PlayerProfile();
        PlayerStyle style = adaptationManager != null
            ? adaptationManager.CurrentStyle : PlayerStyle.Balanced;

        AdaptationProfile profile = bossController.ActiveProfile;
        float artilleryBonus = profile?.artilleryPriorityBonus ?? 0f;
        float dashBonus      = profile?.dashPriorityBonus ?? 0f;

        float bossHP = 1f;
        if (bossHealth != null && bossHealth.MaxHealth > 0f)
            bossHP = bossHealth.currentHealth / bossHealth.MaxHealth;

        float playerHP = 1f;
        if (playerHealth != null && playerHealth.MaxHealth > 0f)
            playerHP = playerHealth.currentHealth / playerHealth.MaxHealth;

        return new GameContext
        {
            bossHealthNormalized     = bossHP,
            playerHealthNormalized   = playerHP,
            distanceToPlayer         = bossController.GetDistanceToPlayer(),
            canMeleeAttack           = goddess != null && goddess.CanAttack,
            canUseArtillery          = goddess != null && goddess.CanUseArtillery && !fairnessGuardian.BlockArtillery,
            isPlayerInAttackRange    = bossController.PlayerInAttackRange(),
            isPlayerInDetectionRange = bossController.PlayerInDetectionRange(),
            playerProfile            = playerProfile,
            currentPlayerStyle       = style,
            artilleryPriorityBonus   = artilleryBonus,
            dashPriorityBonus        = dashBonus,
        };
    }

    // =========================================================
    // Outcome Tracking
    // =========================================================

    public void OnBossActionStarted(BossActionType action)
    {
        lastActionTaken           = action;
        bossHealthAtActionStart   = bossHealth   != null ? bossHealth.currentHealth   : 0f;
        playerHealthAtActionStart = playerHealth != null ? playerHealth.currentHealth : 0f;
        styleAtActionStart        = adaptationManager != null
            ? adaptationManager.CurrentStyle : PlayerStyle.Balanced;
        trackingOutcome = true;

        AISessionLogger.Instance?.RecordAction(action, CurrentDecision.source);

        if (DebugMode)
            Debug.Log($"[AIEngine] Action started: {action} | BossHP={bossHealthAtActionStart:F0} PlayerHP={playerHealthAtActionStart:F0}");
    }

    public void OnBossActionCompleted(BossActionType action)
    {
        if (!trackingOutcome || action != lastActionTaken) return;
        trackingOutcome = false;

        float bossHealthNow   = bossHealth   != null ? bossHealth.currentHealth   : 0f;
        float playerHealthNow = playerHealth != null ? playerHealth.currentHealth : 0f;

        float damageDealt = playerHealthAtActionStart - playerHealthNow;
        float damageTaken = bossHealthAtActionStart   - bossHealthNow;

        // Reward for weighted brain: binary hit/miss table
        float weightedReward = (damageDealt > 0f ? 1f : -0.2f) + (damageTaken > 0f ? -0.3f : 0f);
        weightedBrain.UpdateWeight(action, styleAtActionStart, weightedReward);

        // Reward for ML brain: +1 per full-HP dealt, −1 per full-HP taken
        float playerMax = playerHealth != null ? playerHealth.MaxHealth : 1f;
        float bossMax   = bossHealth   != null ? bossHealth.MaxHealth   : 1f;
        mlBrain?.ReceiveActionOutcome(damageDealt / playerMax, damageTaken / bossMax);

        if (playerHealthNow <= 0f) mlBrain?.OnFightWon();
        if (bossHealthNow   <= 0f) mlBrain?.OnFightLost();

        if (DebugMode)
            Debug.Log($"[AIEngine] Action completed: {action} | Dealt={damageDealt:F0} Taken={damageTaken:F0} → wReward={weightedReward:F2}");
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

        if (playerHealth != null && !playerDeathSubscribed)
        {
            playerHealth.OnDamageTaken += HandlePlayerDamagedForML;
            playerDeathSubscribed = true;
        }
    }

    // =========================================================
    // ML Episode — death event handlers
    // =========================================================

    private void HandleBossDiedForML()
    {
        mlBrain?.OnFightLost();
    }

    private void HandlePlayerDamagedForML(float damage)
    {
        if (playerHealth != null && playerHealth.currentHealth <= 0f)
            mlBrain?.OnFightWon();
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
