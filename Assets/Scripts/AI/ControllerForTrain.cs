using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.Barracuda;
using UnityEngine;

/// <summary>
/// Meta-controller ML-Agents agent that learns when to switch between two
/// pre-trained ONNX combat brains for the boss.
///
/// This agent does NOT make combat decisions itself — it only decides WHICH
/// of the two existing brains should be active at any given moment. The active
/// brain then makes the actual Chase / Melee / Artillery decisions through MLBrain.
///
/// Place on a SEPARATE empty GameObject (not on the boss) so its BehaviorParameters
/// does not conflict with MLBrain's BehaviorParameters on the boss.
///
/// ─── Scene Setup ───────────────────────────────────────────────────────
///   1. Create an empty GameObject named "BrainController"
///   2. Add this component (auto-adds BehaviorParameters)
///   3. BehaviorParameters on BrainController:
///        • Behavior Name  = "ControllerForTrain"
///        • Vector Observation Size = 22
///        • Discrete Branches = 1 branch of size 2
///        • Behavior Type = Default (training) or InferenceOnly (play)
///   4. Drag both ONNX files from "Onnx Folder" into brainA / brainB
///   5. On the boss's MLBrain BehaviorParameters:
///        • Behavior Type = InferenceOnly  (NOT Default)
///   6. On the boss's AIDecisionEngine:
///        • isTrainingMode = true
///
/// ─── Training ──────────────────────────────────────────────────────────
///   mlagents-learn controller_config.yaml --run-id=controller_v1
///
/// ─── Observations (22 floats) ──────────────────────────────────────────
///   [0-2]   Boss HP, Player HP, Distance
///   [3-5]   Feasibility (inRange, canMelee, canArtillery)
///   [6-10]  Player profile (aggression, blockRate, ranged, melee, jumpAttack)
///   [11]    Active brain index (0 or 1)
///   [12-13] Per-brain net score (damage dealt − taken, normalized)
///   [14-15] Per-brain time allocation ratio
///   [16-17] Health deltas since last controller decision
///   [18-19] Switch dynamics (time since switch, stagnation timer)
///   [20-21] Episode progress (switch count, time elapsed)
///
/// ─── Actions ───────────────────────────────────────────────────────────
///   1 discrete branch, size 2:  0 = Brain A,  1 = Brain B
///
/// ─── Reward Signals ────────────────────────────────────────────────────
///   • Damage dealt to player        → positive (scaled)
///   • Damage taken by boss          → negative (scaled)
///   • Win (player dies)             → +winReward
///   • Lose (boss dies)              → +loseReward
///   • Timeout                       → +timeoutReward
///   • Each brain switch             → switchPenalty (small negative)
///   • Damage dealt shortly after switch → postSwitchBonus (validates switch)
///   • No damage dealt for too long  → stagnationPenalty (encourages trying other brain)
/// </summary>
public class ControllerForTrain : Agent
{
    // ════════════════════════════════════════════════════════════════════
    // Inspector
    // ════════════════════════════════════════════════════════════════════

    [Header("Brain Models (drag ONNX files from 'Onnx Folder')")]
    [Tooltip("First pre-trained ONNX brain")]
    [SerializeField] private NNModel brainA;
    [Tooltip("Second pre-trained ONNX brain")]
    [SerializeField] private NNModel brainB;

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

    // Per-brain performance within the current episode
    private readonly float[] brainDamageDealt = new float[2];
    private readonly float[] brainDamageTaken = new float[2];
    private readonly float[] brainActiveTime  = new float[2];

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
    public string ActiveBrainName  => activeBrainIndex == 0 ? "BrainA" : "BrainB";

    // ════════════════════════════════════════════════════════════════════
    // ML-Agents Lifecycle
    // ════════════════════════════════════════════════════════════════════

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

        FindPlayerHealth();
        CacheMaxHP();
    }

    public override void OnEpisodeBegin()
    {
        episodeTimer  = 0f;
        evalTimer     = 0f;
        episodeEnding = false;

        brainDamageDealt[0] = brainDamageDealt[1] = 0f;
        brainDamageTaken[0] = brainDamageTaken[1] = 0f;
        brainActiveTime[0]  = brainActiveTime[1]  = 0f;

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
            Debug.Log("[ControllerForTrain] Episode begin — starting with BrainA");
    }

    /// <summary>22 observations for the brain-selection network.</summary>
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

        // ── Active brain (1) ────────────────────────────────────────
        sensor.AddObservation((float)activeBrainIndex);                           // 11

        // ── Per-brain net performance: (dealt − taken) / playerMaxHP (2) ─
        float pMax = Mathf.Max(playerMaxHP, 1f);
        float netA = (brainDamageDealt[0] - brainDamageTaken[0]) / pMax;
        float netB = (brainDamageDealt[1] - brainDamageTaken[1]) / pMax;
        sensor.AddObservation(Mathf.Clamp(netA, -1f, 1f));                       // 12
        sensor.AddObservation(Mathf.Clamp(netB, -1f, 1f));                       // 13

        // ── Per-brain time allocation (2) ───────────────────────────
        float totalTime = Mathf.Max(episodeTimer, 0.1f);
        sensor.AddObservation(Mathf.Clamp01(brainActiveTime[0] / totalTime));     // 14
        sensor.AddObservation(Mathf.Clamp01(brainActiveTime[1] / totalTime));     // 15

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
        int chosen = Mathf.Clamp(actions.DiscreteActions[0], 0, 1);

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
                Debug.Log($"[ControllerForTrain] Switched → Brain" +
                          $"{(chosen == 0 ? "A" : "B")} (switch #{switchCount})");
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
        if (Input.GetKeyDown(KeyCode.B))
            action = 1 - activeBrainIndex;
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
                      $"Active: {ActiveBrainName} | " +
                      $"A dealt={brainDamageDealt[0]:F0} B dealt={brainDamageDealt[1]:F0}");

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
                          $"Active: {ActiveBrainName} | " +
                          $"A dealt={brainDamageDealt[0]:F0} B dealt={brainDamageDealt[1]:F0}");

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
        activeBrainIndex = brainIndex;
        NNModel selected = brainIndex == 0 ? brainA : brainB;

        if (selected == null)
        {
            Debug.LogWarning($"[ControllerForTrain] Brain " +
                             $"{(brainIndex == 0 ? "A" : "B")} model is null! " +
                             "Drag the ONNX file into the inspector slot.");
            return;
        }

        if (mlBrain == null) return;

        try
        {
            mlBrain.SetModel("VoidbornBoss", selected);
            if (DebugMode)
                Debug.Log($"[ControllerForTrain] Model set → Brain{(brainIndex == 0 ? "A" : "B")} ({selected.name})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ControllerForTrain] SetModel failed: {e.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

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
