using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using Unity.Barracuda;
using UnityEngine;

/// <summary>
/// Meta-controller ML-Agents agent that learns when to switch between
/// N pre-trained ONNX combat brains for the boss.
///
/// This agent does NOT make combat decisions itself — it only decides WHICH
/// of the configured brains should be active at any given moment. The active
/// brain then makes the actual Chase / Melee / Artillery decisions through MLBrain.
///
/// The list of brains is fully configurable via the inspector. Empty (null)
/// slots are skipped at startup, and the action / observation space is sized
/// to match the count of valid entries. You can add or remove brains freely
/// without touching BehaviorParameters — they are reconfigured at Awake().
///
/// Place on a SEPARATE empty GameObject (not on the boss) so its BehaviorParameters
/// does not conflict with MLBrain's BehaviorParameters on the boss.
///
/// ─── Scene Setup ───────────────────────────────────────────────────────
///   1. Create an empty GameObject named "BrainController"
///   2. Add this component (auto-adds BehaviorParameters)
///   3. BehaviorParameters on BrainController:
///        • Behavior Name  = "ControllerForTrain"
///        • Behavior Type  = Default (training) or InferenceOnly (play)
///        • VectorObservationSize / BranchSizes are overwritten at Awake()
///   4. Populate the "brains" list with ONNX models from "Onnx Files"
///   5. On the boss's MLBrain BehaviorParameters:
///        • Behavior Type = InferenceOnly  (NOT Default)
///   6. On the boss's AIDecisionEngine:
///        • isTrainingMode = true
///
/// ─── Training ──────────────────────────────────────────────────────────
///   mlagents-learn controller_config.yaml --run-id=controller_v2
///   Note: any previously trained ControllerForTrain.onnx is shape-incompatible
///   once the brain count changes — retrain from scratch.
///
/// ─── Observations (18 + 2*N floats) ────────────────────────────────────
///   [0-2]      Boss HP, Player HP, Distance
///   [3-5]      Feasibility (inRange, canMelee, canArtillery)
///   [6-10]     Player profile (aggression, blockRate, ranged, melee, jumpAttack)
///   [11]       Active brain index, normalized to [0,1]
///   [12..11+N] Per-brain net score (damage dealt − taken, normalized)
///   [12+N..]   Per-brain time allocation ratio (N values)
///   then       Health deltas (2), switch dynamics (2), episode progress (2)
///
/// ─── Actions ───────────────────────────────────────────────────────────
///   1 discrete branch of size N:  index → brains[index]
///
/// ─── Reward Signals ────────────────────────────────────────────────────
///   • Damage dealt to player        → positive (scaled)
///   • Damage taken by boss          → negative (scaled)
///   • Win (player dies)             → +winReward
///   • Lose (boss dies)              → +loseReward
///   • Timeout                       → +timeoutReward
///   • Each brain switch             → switchPenalty (small negative)
///   • Damage dealt shortly after switch → postSwitchBonus (validates switch)
///   • No damage dealt for too long  → stagnationPenalty (encourages trying another brain)
/// </summary>
public class ControllerForTrain : Agent
{
    // ════════════════════════════════════════════════════════════════════
    // Inspector
    // ════════════════════════════════════════════════════════════════════

    [Header("Brain Models (drag ONNX files from 'Onnx Files'; empty slots are ignored)")]
    [Tooltip("Configurable list of pre-trained ONNX brains. The agent's discrete " +
             "action space and observation size are sized to match the count of " +
             "non-null entries at Awake().")]
    [SerializeField] private List<NNModel> brains = new List<NNModel>();

    [Header("References (auto-found if left empty)")]
    [SerializeField] private MLBrain mlBrain;
    [SerializeField] private AIDecisionEngine decisionEngine;

    [Header("Evaluation")]
    [Tooltip("Seconds between brain-selection decisions. Keep higher than " +
             "AIDecisionEngine.evaluationInterval so each brain has time to act.")]
    [SerializeField] private float evaluationInterval = 3f;
    [Tooltip("Episode auto-ends after this many seconds.")]
    [SerializeField] private float maxEpisodeDuration = 120f;

    [Header("Reward Tuning")]
    [SerializeField] private float winReward            =  5f;
    [SerializeField] private float loseReward           = -5f;
    [SerializeField] private float timeoutReward        = -1f;
    [SerializeField] private float switchPenalty         = -0.02f;
    [SerializeField] private float damageDealtScale     =  1.5f;
    [SerializeField] private float damageTakenScale     =  1.0f;
    [SerializeField] private float stagnationPenalty    = -0.01f;
    [SerializeField] private float stagnationThreshold  =  8f;
    [SerializeField] private float postSwitchBonusWindow =  5f;
    [SerializeField] private float postSwitchBonus      =  0.15f;

    [Header("Debug")]
    public bool DebugMode = true;

    // ════════════════════════════════════════════════════════════════════
    // Cached references
    // ════════════════════════════════════════════════════════════════════

    private BossController bossController;
    private Health         bossHealth;
    private Health         playerHealth;
    private RestartManager restartManager;

    // ════════════════════════════════════════════════════════════════════
    // Episode state
    // ════════════════════════════════════════════════════════════════════

    private int   activeBrainIndex;
    private float episodeTimer;
    private float evalTimer;
    private bool  episodeEnding;

    // Filtered (non-null) view of the inspector list. Populated in Awake().
    private List<NNModel> activeBrains;
    private int BrainCount => activeBrains?.Count ?? 0;

    // Per-brain performance within the current episode (sized to BrainCount).
    private float[] brainDamageDealt;
    private float[] brainDamageTaken;
    private float[] brainActiveTime;

    // Switch tracking
    private int   switchCount;
    private float timeSinceLastSwitch;
    private bool  awaitingPostSwitchBonus;
    private float postSwitchTimer;

    // Health delta tracking (between controller decisions)
    private float prevBossHP;
    private float prevPlayerHP;

    // Lethal-damage detection (robust against race with RestartManager)
    private float lastKnownPlayerHP;

    // Stagnation
    private float timeSinceLastDamageDealt;

    // Max HP caches for normalization
    private float bossMaxHP   = 1f;
    private float playerMaxHP = 1f;

    // Prevent double subscription to player health
    private bool playerHealthSubscribed;

    // Deferred model swap (must happen outside Academy step)
    private int pendingBrainSwitch = -1;

    // ════════════════════════════════════════════════════════════════════
    // Public API
    // ════════════════════════════════════════════════════════════════════

    public int    ActiveBrainIndex => activeBrainIndex;
    public string ActiveBrainName  =>
        (activeBrains != null && activeBrainIndex >= 0 && activeBrainIndex < activeBrains.Count
         && activeBrains[activeBrainIndex] != null)
            ? $"Brain[{activeBrainIndex}] ({activeBrains[activeBrainIndex].name})"
            : $"Brain[{activeBrainIndex}]";

    // ════════════════════════════════════════════════════════════════════
    // ML-Agents Lifecycle
    // ════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (!BuildActiveBrainList())
        {
            Debug.LogError("[ControllerForTrain] No NNModel assigned in the 'brains' " +
                           "list — disabling agent. Populate the list in the inspector.");
            enabled = false;
            return;
        }

        ConfigureBehaviorParameters();
    }

    private bool BuildActiveBrainList()
    {
        activeBrains = new List<NNModel>();
        if (brains == null) return false;

        for (int i = 0; i < brains.Count; i++)
        {
            if (brains[i] != null)
                activeBrains.Add(brains[i]);
        }

        return activeBrains.Count > 0;
    }

    private void ConfigureBehaviorParameters()
    {
        var behaviorParameters = GetComponent<BehaviorParameters>();
        if (behaviorParameters == null)
        {
            Debug.LogError("[ControllerForTrain] BehaviorParameters component missing " +
                           "on this GameObject.");
            return;
        }

        behaviorParameters.BrainParameters.VectorObservationSize = 18 + 2 * BrainCount;
        behaviorParameters.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(BrainCount);

        if (DebugMode)
            Debug.Log($"[ControllerForTrain] Configured BehaviorParameters: " +
                      $"obs={18 + 2 * BrainCount}, discrete branch size={BrainCount}");
    }

    public override void Initialize()
    {
        if (mlBrain == null)
            mlBrain = FindFirstObjectByType<MLBrain>();
        if (decisionEngine == null)
            decisionEngine = FindFirstObjectByType<AIDecisionEngine>();

        if (mlBrain == null)
        {
            Debug.LogError("[ControllerForTrain] MLBrain not found! " +
                           "Make sure the boss has an MLBrain component.");
            return;
        }

        bossController = mlBrain.GetComponent<BossController>();
        bossHealth     = mlBrain.GetComponent<Health>();
        restartManager = FindFirstObjectByType<RestartManager>();

        // Size per-brain arrays now that we know the brain count.
        brainDamageDealt = new float[BrainCount];
        brainDamageTaken = new float[BrainCount];
        brainActiveTime  = new float[BrainCount];

        FindPlayerHealth();
        CacheMaxHP();
    }

    public override void OnEpisodeBegin()
    {
        episodeTimer  = 0f;
        evalTimer     = 0f;
        episodeEnding = false;

        for (int i = 0; i < BrainCount; i++)
        {
            brainDamageDealt[i] = 0f;
            brainDamageTaken[i] = 0f;
            brainActiveTime[i]  = 0f;
        }

        switchCount             = 0;
        timeSinceLastSwitch     = 0f;
        awaitingPostSwitchBonus = false;
        postSwitchTimer         = 0f;
        timeSinceLastDamageDealt = 0f;

        prevBossHP   = 1f;
        prevPlayerHP = 1f;

        // Cancel any running death coroutines before resetting
        CancelDeathSequences();

        // Reset the encounter (respawn boss + player)
        if (restartManager != null)
            restartManager.ResetEncounter();
        else if (bossController != null)
            bossController.Respawn(bossController.transform.position);

        decisionEngine?.ResetForNewEpisode();

        FindPlayerHealth();
        CacheMaxHP();
        lastKnownPlayerHP = playerMaxHP;

        // Defer brain swap to LateUpdate (unsafe during Academy step)
        activeBrainIndex = 0;
        pendingBrainSwitch = 0;

        if (DebugMode)
            Debug.Log($"[ControllerForTrain] Episode begin — starting with {ActiveBrainName}");
    }

    /// <summary>(18 + 2*N) observations for the brain-selection network.</summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        GameContext ctx = decisionEngine != null
            ? decisionEngine.CurrentContext
            : default;

        // ── Core game state (3) ─────────────────────────────────────
        sensor.AddObservation(ctx.bossHealthNormalized);                          // 0
        sensor.AddObservation(ctx.playerHealthNormalized);                        // 1
        sensor.AddObservation(Mathf.Clamp01(ctx.distanceToPlayer / 20f));         // 2

        // ── Feasibility flags (3) ───────────────────────────────────
        sensor.AddObservation(ctx.isPlayerInAttackRange ? 1f : 0f);               // 3
        sensor.AddObservation(ctx.canMeleeAttack        ? 1f : 0f);               // 4
        sensor.AddObservation(ctx.canUseArtillery       ? 1f : 0f);               // 5

        // ── Player behaviour profile (5) ────────────────────────────
        PlayerProfile p = ctx.playerProfile;
        sensor.AddObservation(Mathf.Clamp01(p.aggressionScore));                  // 6
        sensor.AddObservation(Mathf.Clamp01(p.blockRate));                        // 7
        sensor.AddObservation(Mathf.Clamp01(p.rangedRatio));                      // 8
        sensor.AddObservation(Mathf.Clamp01(p.meleeRatio));                       // 9
        sensor.AddObservation(Mathf.Clamp01(p.jumpAttackRatio));                  // 10

        // ── Active brain (1) — normalized to [0,1] across N brains ──
        float activeNorm = BrainCount > 1
            ? activeBrainIndex / (float)(BrainCount - 1)
            : 0f;
        sensor.AddObservation(activeNorm);                                        // 11

        // ── Per-brain net performance: (dealt − taken) / playerMaxHP (N) ─
        float pMax = Mathf.Max(playerMaxHP, 1f);
        for (int i = 0; i < BrainCount; i++)
        {
            float net = (brainDamageDealt[i] - brainDamageTaken[i]) / pMax;
            sensor.AddObservation(Mathf.Clamp(net, -1f, 1f));
        }

        // ── Per-brain time allocation (N) ───────────────────────────
        float totalTime = Mathf.Max(episodeTimer, 0.1f);
        for (int i = 0; i < BrainCount; i++)
            sensor.AddObservation(Mathf.Clamp01(brainActiveTime[i] / totalTime));

        // ── Health deltas since last controller decision (2) ────────
        float curBossHP = bossHealth != null
            ? bossHealth.currentHealth / Mathf.Max(bossMaxHP, 1f) : 1f;
        float curPlayerHP = playerHealth != null
            ? playerHealth.currentHealth / Mathf.Max(playerMaxHP, 1f) : 1f;
        sensor.AddObservation(Mathf.Clamp(curBossHP   - prevBossHP,   -1f, 1f)); // 16
        sensor.AddObservation(Mathf.Clamp(curPlayerHP  - prevPlayerHP, -1f, 1f)); // 17

        // ── Switch dynamics (2) ─────────────────────────────────────
        sensor.AddObservation(
            Mathf.Clamp01(timeSinceLastSwitch / maxEpisodeDuration));              // 18
        sensor.AddObservation(
            Mathf.Clamp01(timeSinceLastDamageDealt / stagnationThreshold));        // 19

        // ── Episode progress (2) ────────────────────────────────────
        sensor.AddObservation(Mathf.Clamp01(switchCount / 10f));                  // 20
        sensor.AddObservation(Mathf.Clamp01(episodeTimer / maxEpisodeDuration));  // 21
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (BrainCount == 0) return;

        int chosen = Mathf.Clamp(actions.DiscreteActions[0], 0, BrainCount - 1);

        if (chosen != activeBrainIndex)
        {
            // Defer the actual model swap to LateUpdate (unsafe during Academy step)
            activeBrainIndex = chosen;
            pendingBrainSwitch = chosen;
            AddReward(switchPenalty);
            switchCount++;
            timeSinceLastSwitch     = 0f;
            awaitingPostSwitchBonus = true;
            postSwitchTimer         = 0f;

            if (DebugMode)
            {
                string name = activeBrains[chosen] != null ? activeBrains[chosen].name : "<null>";
                Debug.Log($"[ControllerForTrain] Switched → Brain[{chosen}] ({name}) " +
                          $"(switch #{switchCount})");
            }
        }

        // Snapshot HP for delta tracking on the NEXT observation
        prevBossHP = bossHealth != null
            ? bossHealth.currentHealth / Mathf.Max(bossMaxHP, 1f) : 1f;
        prevPlayerHP = playerHealth != null
            ? playerHealth.currentHealth / Mathf.Max(playerMaxHP, 1f) : 1f;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        int action = activeBrainIndex;
        if (BrainCount > 0 && Input.GetKeyDown(KeyCode.B))
            action = (activeBrainIndex + 1) % BrainCount;
        actionsOut.DiscreteActions.Array[0] = action;
    }

    // ════════════════════════════════════════════════════════════════════
    // Unity Lifecycle
    // ════════════════════════════════════════════════════════════════════

    private void Start()
    {
        SubscribeEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
    }

    private void LateUpdate()
    {
        if (pendingBrainSwitch < 0) return;
        int idx = pendingBrainSwitch;
        pendingBrainSwitch = -1;
        ApplyBrainSelection(idx);
    }

    private void Update()
    {
        if (episodeEnding || mlBrain == null) return;

        float dt = Time.deltaTime;
        episodeTimer             += dt;
        evalTimer                += dt;
        timeSinceLastSwitch      += dt;
        timeSinceLastDamageDealt += dt;
        brainActiveTime[activeBrainIndex] += dt;

        // Keep player HP tracking fresh for lethal-damage detection
        if (playerHealth != null && playerHealth.currentHealth > 0f)
            lastKnownPlayerHP = playerHealth.currentHealth;

        // Post-switch bonus window tracking
        if (awaitingPostSwitchBonus)
        {
            postSwitchTimer += dt;
            if (postSwitchTimer >= postSwitchBonusWindow)
                awaitingPostSwitchBonus = false;
        }

        // Continuous stagnation penalty when no damage dealt for too long
        if (timeSinceLastDamageDealt > stagnationThreshold)
            AddReward(stagnationPenalty * dt);

        // Timeout
        if (episodeTimer >= maxEpisodeDuration)
        {
            episodeEnding = true;
            AddReward(timeoutReward);
            if (DebugMode)
                Debug.Log("[ControllerForTrain] Timeout — ending episode");
            EndEpisode();
            return;
        }

        // Periodic brain-selection decision
        if (evalTimer >= evaluationInterval)
        {
            evalTimer = 0f;
            RequestDecision();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Event Handling
    // ════════════════════════════════════════════════════════════════════

    private void SubscribeEvents()
    {
        if (bossController != null)
            bossController.OnDied += OnBossDied;

        if (bossHealth != null)
            bossHealth.OnDamageTaken += OnBossTookDamage;

        SafeSubscribePlayerHealth();
    }

    private void UnsubscribeEvents()
    {
        if (bossController != null)
            bossController.OnDied -= OnBossDied;

        if (bossHealth != null)
            bossHealth.OnDamageTaken -= OnBossTookDamage;

        if (playerHealth != null)
        {
            playerHealth.OnDamageTaken -= OnPlayerTookDamage;
            playerHealthSubscribed = false;
        }
    }

    private void SafeSubscribePlayerHealth()
    {
        if (playerHealth != null && !playerHealthSubscribed)
        {
            playerHealth.OnDamageTaken += OnPlayerTookDamage;
            playerHealthSubscribed = true;
        }
    }

    // ── Boss died → LOSE ────────────────────────────────────────────
    private void OnBossDied()
    {
        if (episodeEnding) return;
        episodeEnding = true;

        AddReward(loseReward);

        if (DebugMode)
            Debug.Log($"[ControllerForTrain] Boss died (lose). " +
                      $"Active: {ActiveBrainName} | {BuildPerBrainSummary()}");

        EndEpisode();
    }

    // ── Boss took damage → negative reward ──────────────────────────
    private void OnBossTookDamage(float damage)
    {
        if (episodeEnding) return;

        brainDamageTaken[activeBrainIndex] += damage;
        float normalized = damage / Mathf.Max(bossMaxHP, 1f);
        AddReward(-damageTakenScale * normalized);
    }

    // ── Player took damage → positive reward / win detection ────────
    private void OnPlayerTookDamage(float damage)
    {
        if (episodeEnding) return;

        // Death detection using both direct check and our tracked HP.
        // The tracked-HP approach guards against RestartManager resetting
        // the encounter before this delegate fires.
        bool lethal = lastKnownPlayerHP > 0f
                   && (lastKnownPlayerHP - damage) <= 0.01f;

        if (lethal || (playerHealth != null && playerHealth.currentHealth <= 0f))
        {
            episodeEnding = true;
            AddReward(winReward);

            if (DebugMode)
                Debug.Log($"[ControllerForTrain] Player died (win). " +
                          $"Active: {ActiveBrainName} | {BuildPerBrainSummary()}");

            EndEpisode();
            return;
        }

        // Track damage for the active brain
        brainDamageDealt[activeBrainIndex] += damage;
        timeSinceLastDamageDealt = 0f;

        float normalized = damage / Mathf.Max(playerMaxHP, 1f);
        AddReward(damageDealtScale * normalized);

        // Post-switch bonus: dealing damage shortly after switching validates
        // that the switch was a good decision.
        if (awaitingPostSwitchBonus)
        {
            AddReward(postSwitchBonus);
            awaitingPostSwitchBonus = false;

            if (DebugMode)
                Debug.Log("[ControllerForTrain] Post-switch damage bonus awarded!");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Brain Swapping
    // ════════════════════════════════════════════════════════════════════

    private void ApplyBrainSelection(int brainIndex)
    {
        if (brainIndex < 0 || brainIndex >= BrainCount)
        {
            Debug.LogWarning($"[ControllerForTrain] Brain index {brainIndex} out of range " +
                             $"[0, {BrainCount - 1}].");
            return;
        }

        activeBrainIndex = brainIndex;
        NNModel selected = activeBrains[brainIndex];

        if (selected == null)
        {
            Debug.LogWarning($"[ControllerForTrain] Brain[{brainIndex}] model is null! " +
                             "Re-check the inspector list.");
            return;
        }

        if (mlBrain == null) return;

        try
        {
            mlBrain.SetModel("VoidbornBoss", selected);
            if (DebugMode)
                Debug.Log($"[ControllerForTrain] Model set → Brain[{brainIndex}] ({selected.name})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ControllerForTrain] SetModel failed: {e.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private string BuildPerBrainSummary()
    {
        if (BrainCount == 0) return "no brains";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < BrainCount; i++)
        {
            if (i > 0) sb.Append(", ");
            string name = activeBrains[i] != null ? activeBrains[i].name : "<null>";
            sb.Append($"[{i}] {name} dealt={brainDamageDealt[i]:F0} taken={brainDamageTaken[i]:F0}");
        }
        return sb.ToString();
    }

    private void CancelDeathSequences()
    {
        if (bossHealth != null) bossHealth.StopAllCoroutines();

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            var ph = playerObj.GetComponent<Health>();
            if (ph != null) ph.StopAllCoroutines();
        }

        if (restartManager != null) restartManager.StopAllCoroutines();
    }

    private void FindPlayerHealth()
    {
        if (playerHealth != null) return;

        var obj = GameObject.FindGameObjectWithTag("Player");
        if (obj != null)
        {
            playerHealth = obj.GetComponent<Health>();
            SafeSubscribePlayerHealth();
        }
    }

    private void CacheMaxHP()
    {
        if (bossHealth   != null && bossHealth.MaxHealth   > 0f) bossMaxHP   = bossHealth.MaxHealth;
        if (playerHealth != null && playerHealth.MaxHealth > 0f) playerMaxHP = playerHealth.MaxHealth;
    }
}