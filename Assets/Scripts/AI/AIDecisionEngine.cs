using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Three-layer AI Decision Engine for boss action selection.
///
///   Layer 1 (L1): HeuristicBrain        — critical rule overrides
///   Layer 2 (L2): MLBrain               — neural network inference
///   Layer 3 (L3): WeightedStrategyBrain — EMA weight fallback
///
/// isTrainingMode = true  → bypass cascade, return mlBrain.Evaluate() directly.
/// isTrainingMode = false → run cascade with per-layer enable toggles.
/// </summary>
public class AIDecisionEngine : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────
    [Header("Mode")]
    [Tooltip("True during mlagents-learn. ML owns every decision, no cascade.")]
    public bool isTrainingMode;

    [Header("Layer Toggles (ignored in training mode)")]
    public bool enableL1 = true;
    public bool enableL2 = true;
    public bool enableL3 = true;

    [Header("Fairness Guardian")]
    [Tooltip("Master switch — disable to let the boss act without any fairness throttling.")]
    public bool enableFairnessGuardian = true;

    [Header("Decision Engine Settings")]
    [SerializeField] private float evaluationInterval = 0.3f;
    [SerializeField] private float confidenceThreshold = 0.4f; // for weighted conf
    [SerializeField] private bool  persistWeights = true;

    [Header("ML Brain")]
    [SerializeField] private MLBrain mlBrain;
    [SerializeField] private float mlConfidenceThreshold = 0.7f; // for ml conf

    [Header("Debug")]
    public bool DebugMode = true;

    // ── Modules ────────────────────────────────────────────────
    private HeuristicBrain heuristicBrain;
    private WeightedStrategyBrain weightedBrain;
    private FairnessGuardian fairnessGuardian;

    // ── References ─────────────────────────────────────────────
    private BossController bossController;
    private VoidbornGoddessController goddess;
    private VoidbornAdaptationManager adaptationManager;
    private PlayerBehaviorTracker tracker;
    private Health bossHealth;
    private Health playerHealth;

    // ── State ──────────────────────────────────────────────────
    private float evaluationTimer;
    private bool _prevFairnessActive;

    // ── Outcome tracking ───────────────────────────────────────
    private BossActionType lastActionTaken = BossActionType.None;
    private float bossHealthAtActionStart;
    private float playerHealthAtActionStart;
    private PlayerStyle styleAtActionStart;
    private bool trackingOutcome;

    private string SavePath => Path.Combine(Application.persistentDataPath, "boss_ai_weights.json");

    // ── Public API ─────────────────────────────────────────────
    public BossDecision CurrentDecision { get; private set; }
    public GameContext  CurrentContext  { get; private set; }

    public string ActiveLayer          => CurrentDecision.source ?? "None";
    public bool   IsFairnessActive     => enableFairnessGuardian && (fairnessGuardian?.IsRelaxationActive ?? false);
    public float  FairnessCooldownMultiplier => enableFairnessGuardian
        ? (fairnessGuardian?.CooldownMultiplier ?? 1f) : 1f;

    public bool  MLBrainActive => mlBrain != null && mlBrain.IsModelLoaded;
    public float MLConfidence  { get; private set; }

    public int   TotalDecisions    { get; private set; }
    public float ConfidenceSum     { get; private set; }
    public int   LowConfidenceCount{ get; private set; }
    public int   L1DecisionCount   { get; private set; }
    public int   L2DecisionCount   { get; private set; }
    public int   L3DecisionCount   { get; private set; }

    public event System.Action<BossDecision>            OnDecisionChanged;
    public event System.Action                           OnFairnessActivated;
    public event System.Action<GameContext, BossDecision> OnDecisionEvaluated;

    public Dictionary<BossActionType, float> GetWeightsForStyle(PlayerStyle style)
        => weightedBrain?.GetWeightsForStyle(style);

    public void ResetForNewEpisode()
    {
        evaluationTimer     = 0f;
        trackingOutcome     = false;
        _prevFairnessActive = false;
        CurrentDecision     = BossDecision.Default;
        fairnessGuardian?.Reset();
        FindPlayerReferences();
    }

    // ── Unity Lifecycle ────────────────────────────────────────
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
        if (persistWeights) LoadWeights();
    }

    private void Update()
    {
        if (bossController == null || bossController.IsDead) return;
        if (playerHealth == null) FindPlayerReferences();

        evaluationTimer += Time.deltaTime;
        if (evaluationTimer < evaluationInterval) return;
        evaluationTimer = 0f;

        if (fairnessGuardian != null)
            fairnessGuardian.Enabled = enableFairnessGuardian;

        CurrentContext = BuildContext();

        if (!isTrainingMode)
            fairnessGuardian.Evaluate(CurrentContext.bossHealthNormalized,
                                      CurrentContext.playerHealthNormalized);

        BossDecision previous = CurrentDecision;
        CurrentDecision = isTrainingMode ? RunTrainingMode(CurrentContext)
                                         : RunDecisionCascade(CurrentContext);

        TotalDecisions++;
        ConfidenceSum += CurrentDecision.confidence;
        if (CurrentDecision.confidence < confidenceThreshold) LowConfidenceCount++;

        if (CurrentDecision.source != null)
        {
            if      (CurrentDecision.source.StartsWith("Heuristic")) L1DecisionCount++;
            else if (CurrentDecision.source.StartsWith("MLBrain"))   L2DecisionCount++;
            else if (CurrentDecision.source.StartsWith("Weighted"))  L3DecisionCount++;
        }

        bool fairNow = fairnessGuardian?.IsRelaxationActive ?? false;
        if (fairNow && !_prevFairnessActive) OnFairnessActivated?.Invoke();
        _prevFairnessActive = fairNow;

        OnDecisionEvaluated?.Invoke(CurrentContext, CurrentDecision);
        if (CurrentDecision.action != previous.action)
            OnDecisionChanged?.Invoke(CurrentDecision);
    }

    private void OnDestroy()
    {
        if (persistWeights) SaveWeights();
    }

    // ── Training Mode (ML owns everything) ─────────────────────
    private BossDecision RunTrainingMode(GameContext ctx)
    {
        if (mlBrain == null) return BossDecision.Default;
        mlBrain.CurrentContext = ctx;
        BossDecision ml = mlBrain.Evaluate(ctx);
        MLConfidence = ml.confidence;
        return ml;
    }

    // ── Normal Cascade (with layer toggles) ────────────────────
    private BossDecision RunDecisionCascade(GameContext ctx)
    {
        BossDecision heuristic = BossDecision.Default;

        // L1: Heuristic critical overrides
        if (enableL1)
        {
            heuristic = heuristicBrain.Evaluate(ctx);
            if (heuristic.isCriticalOverride)
            {
                var f = fairnessGuardian.ApplyOverride(heuristic);
                if (DebugMode) Debug.Log($"[AIEngine] L1 OVERRIDE → {f.action}");
                return f;
            }
        }

        // L2: ML Brain
        if (enableL2 && mlBrain != null && mlBrain.IsModelLoaded)
        {
            mlBrain.CurrentContext = ctx;
            BossDecision ml = mlBrain.Evaluate(ctx);
            MLConfidence = ml.confidence;

            if (ml.confidence >= mlConfidenceThreshold)
            {
                var f = fairnessGuardian.ApplyOverride(ml);
                if (DebugMode) Debug.Log($"[AIEngine] L2 ML → {f.action} (conf={ml.confidence:F2})");
                return f;
            }
        }

        // L3: Weighted strategy
        if (enableL3)
        {
            BossDecision w = weightedBrain.Evaluate(ctx);
            if (w.confidence > confidenceThreshold)
            {
                var f = fairnessGuardian.ApplyOverride(w);
                if (DebugMode) Debug.Log($"[AIEngine] L3 Weighted → {f.action} (conf={w.confidence:F2})");
                return f;
            }
        }

        // Fallback
        var fallback = fairnessGuardian.ApplyOverride(
            enableL1 ? heuristic : BossDecision.Default);
        if (DebugMode) Debug.Log($"[AIEngine] Fallback → {fallback.action}");
        return fallback;
    }

    // ── Context Building ───────────────────────────────────────
    private GameContext BuildContext()
    {
        PlayerProfile pp = tracker != null ? tracker.Profile : new PlayerProfile();
        PlayerStyle style = adaptationManager != null
            ? adaptationManager.CurrentStyle : PlayerStyle.Balanced;

        AdaptationProfile profile = bossController.ActiveProfile;

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
            canUseArtillery          = goddess != null && goddess.CanUseArtillery,
            isPlayerInAttackRange    = bossController.PlayerInAttackRange(),
            isPlayerInDetectionRange = bossController.PlayerInDetectionRange(),
            playerProfile            = pp,
            currentPlayerStyle       = style,
            artilleryPriorityBonus   = profile?.artilleryPriorityBonus ?? 0f,
            dashPriorityBonus        = profile?.dashPriorityBonus ?? 0f,
        };
    }

    // ── Outcome Tracking ───────────────────────────────────────
    public void OnBossActionStarted(BossActionType action)
    {
        lastActionTaken           = action;
        bossHealthAtActionStart   = bossHealth   != null ? bossHealth.currentHealth   : 0f;
        playerHealthAtActionStart = playerHealth != null ? playerHealth.currentHealth : 0f;
        styleAtActionStart        = adaptationManager != null
            ? adaptationManager.CurrentStyle : PlayerStyle.Balanced;
        trackingOutcome = true;

        AISessionLogger.Instance?.RecordAction(action, CurrentDecision.source);
    }

    public void OnBossActionCompleted(BossActionType action)
    {
        if (!trackingOutcome || action != lastActionTaken) return;
        trackingOutcome = false;

        float bossNow   = bossHealth   != null ? bossHealth.currentHealth   : 0f;
        float playerNow = playerHealth != null ? playerHealth.currentHealth : 0f;

        float dealt = playerHealthAtActionStart - playerNow;
        float taken = bossHealthAtActionStart   - bossNow;

        float wReward = (dealt > 0f ? 1f : -0.2f) + (taken > 0f ? -0.3f : 0f);
        weightedBrain.UpdateWeight(action, styleAtActionStart, wReward);

        float pMax = playerHealth != null ? playerHealth.MaxHealth : 1f;
        float bMax = bossHealth   != null ? bossHealth.MaxHealth   : 1f;
        mlBrain?.ReceiveActionOutcome(action, dealt / pMax, taken / bMax);

        if (DebugMode)
            Debug.Log($"[AIEngine] {action} done | dealt={dealt:F0} taken={taken:F0} wR={wReward:F2}");
    }

    // ── Helpers ────────────────────────────────────────────────
    private void FindPlayerReferences()
    {
        if (tracker == null)
            tracker = FindFirstObjectByType<PlayerBehaviorTracker>();

        if (playerHealth == null)
        {
            var obj = GameObject.FindGameObjectWithTag("Player");
            if (obj != null) playerHealth = obj.GetComponent<Health>();
        }
    }

    // ── Persistence ────────────────────────────────────────────
    private void SaveWeights()
    {
        try
        {
            File.WriteAllText(SavePath, weightedBrain.SerializeWeights());
            if (DebugMode) Debug.Log($"[AIEngine] Weights saved.");
        }
        catch (System.Exception e) { Debug.LogWarning($"[AIEngine] Save failed: {e.Message}"); }
    }

    private void LoadWeights()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            weightedBrain.DeserializeWeights(File.ReadAllText(SavePath));
            if (DebugMode) Debug.Log($"[AIEngine] Weights loaded.");
        }
        catch (System.Exception e) { Debug.LogWarning($"[AIEngine] Load failed: {e.Message}"); }
    }
}
