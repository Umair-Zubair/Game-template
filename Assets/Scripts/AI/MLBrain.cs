using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using UnityEngine;

public class MLBrain : Agent, IBossDecisionModule
{
    public string ModuleName => "MLBrain";

    // ═══════════════════════════════════════════
    // AIDecisionEngine integration
    // ═══════════════════════════════════════════
    public GameContext CurrentContext { get; set; }

    [SerializeField] private WeightedStrategyBrain weightedBrain;
    [SerializeField] private float maxEpisodeDuration = 120f;

    private BehaviorParameters behaviorParams;
    private BossDecision lastDecision;
    private float lastReward;
    private float episodeTimer;
    private bool episodeEnding;

    // Cached references for in-place episode reset
    private RestartManager restartManager;
    private AIDecisionEngine decisionEngine;
    private VoidbornAdaptationManager adaptationManager;

    public bool IsModelLoaded =>
        behaviorParams != null &&
        behaviorParams.BehaviorType != BehaviorType.HeuristicOnly;

    public override void Initialize()
    {
        behaviorParams    = GetComponent<BehaviorParameters>();
        decisionEngine    = GetComponent<AIDecisionEngine>();
        adaptationManager = GetComponent<VoidbornAdaptationManager>();
        restartManager    = FindFirstObjectByType<RestartManager>();
    }

    // ═══════════════════════════════════════════
    // Episode lifecycle — in-place reset
    // ═══════════════════════════════════════════

    public override void OnEpisodeBegin()
    {
        // 1. Close any lingering fight sessions from the previous episode
        AISessionLogger.Instance?.EndFight(false);
        AIDecisionLogger.Instance?.EndFight(false);

        // 2. In-place encounter reset (boss + player health, position, FSM)
        if (restartManager != null)
        {
            restartManager.ResetEncounter();
        }
        else
        {
            var boss = GetComponent<BossController>();
            boss?.Respawn(transform.position);

            var player = FindFirstObjectByType<PlayerController>();
            player?.Respawn(player.transform.position);
        }

        // 3. Reset AI subsystems
        decisionEngine?.ResetForNewEpisode();
        adaptationManager?.ResetForNewEpisode();

        // 4. Reset episode-local state
        episodeEnding = false;
        episodeTimer  = 0f;
        lastReward    = 0f;
        lastDecision = new BossDecision
            { action = BossActionType.Chase, confidence = 0f, source = "MLBrain" };

        Debug.Log("[MLBrain] Episode began — encounter reset in-place.");
    }

    // ═══════════════════════════════════════════
    // Called by AIDecisionEngine each eval tick
    // ═══════════════════════════════════════════
    public BossDecision Evaluate(GameContext ctx)
    {
        CurrentContext = ctx;

        episodeTimer += Time.deltaTime;
        if (episodeTimer >= maxEpisodeDuration && !episodeEnding)
        {
            episodeEnding = true;
            CancelDeathSequences();
            AddReward(TimeoutReward);
            EndEpisode();
            return lastDecision;
        }

        RequestDecision();
        return lastDecision;
    }

    // ═══════════════════════════════════════════
    // Observations — 15 floats
    // ═══════════════════════════════════════════
    // Update BehaviorParameters → Vector Observation Space Size to 15
    public override void CollectObservations(VectorSensor sensor)
    {
        // Spatial + health (core decision inputs)
        sensor.AddObservation(Mathf.Clamp01(CurrentContext.distanceToPlayer / 20f)); // normalized distance
        sensor.AddObservation(CurrentContext.bossHealthNormalized);
        sensor.AddObservation(CurrentContext.playerHealthNormalized);

        // Cooldown / feasibility flags
        sensor.AddObservation(CurrentContext.isPlayerInAttackRange ? 1f : 0f);
        sensor.AddObservation(CurrentContext.canMeleeAttack        ? 1f : 0f);
        sensor.AddObservation(CurrentContext.canUseArtillery       ? 1f : 0f);

        // One-hot player style (5 floats)
        for (int i = 0; i < 5; i++)
            sensor.AddObservation((int)CurrentContext.currentPlayerStyle == i ? 1f : 0f);

        // Adaptation profile bonuses
        sensor.AddObservation(CurrentContext.artilleryPriorityBonus);
        sensor.AddObservation(CurrentContext.dashPriorityBonus);

        // Weighted brain hints
        var w = weightedBrain?.GetWeightsForStyle(CurrentContext.currentPlayerStyle);
        float meleeW     = w != null && w.ContainsKey(BossActionType.MeleeAttack)
                           ? w[BossActionType.MeleeAttack]     : 0.5f;
        float artilleryW = w != null && w.ContainsKey(BossActionType.ArtilleryAttack)
                           ? w[BossActionType.ArtilleryAttack] : 0.5f;
        sensor.AddObservation(meleeW);
        sensor.AddObservation(artilleryW);
    }

    // ═══════════════════════════════════════════
    // Discrete action → BossActionType mapping
    // ═══════════════════════════════════════════
    // ML-Agents discrete branch outputs 0..N-1.  BossActionType enum values
    // don't start at 0 for the actions we care about (None=0, Chase=1, …),
    // so we use an explicit lookup instead of a raw cast.
    //
    //   Index 0 → Chase
    //   Index 1 → MeleeAttack
    //   Index 2 → ArtilleryAttack
    private static readonly BossActionType[] ActionMap =
    {
        BossActionType.Chase,
        BossActionType.MeleeAttack,
        BossActionType.ArtilleryAttack
    };

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = 0; // Chase — safe default when no .onnx model is assigned
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int actionIdx = actions.DiscreteActions[0];
        BossActionType chosen = ActionMap[Mathf.Clamp(actionIdx, 0, ActionMap.Length - 1)];

        if (chosen == BossActionType.MeleeAttack
            && (!CurrentContext.canMeleeAttack || !CurrentContext.isPlayerInAttackRange))
            AddReward(-0.1f);

        if (chosen == BossActionType.ArtilleryAttack && !CurrentContext.canUseArtillery)
            AddReward(-0.1f);

        lastDecision = new BossDecision
        {
            action             = chosen,
            confidence         = 0.8f,
            isCriticalOverride = false,
            source             = ModuleName
        };

        weightedBrain?.UpdateWeight(chosen, CurrentContext.currentPlayerStyle, lastReward);
    }

    // ═══════════════════════════════════════════
    // Rewards
    // ═══════════════════════════════════════════
    // Per-action (from AIDecisionEngine.OnBossActionCompleted):
    //   Hit player + safe   = +1.0    Hit + got hit = +0.7
    //   Miss + safe         = -0.2    Miss + got hit = -0.5
    // Per-step: small time penalty to incentivize killing fast
    // Terminal:
    //   Player killed  = +5     Boss killed = -5     Timeout = -1
    private const float StepTimePenalty = -0.005f;
    private const float WinReward      = +5f;
    private const float LoseReward     = -5f;
    private const float TimeoutReward  = -1f;

    public void ReceiveReward(float reward)
    {
        lastReward = reward;
        AddReward(reward);
        AddReward(StepTimePenalty);
    }

    public void OnFightWon()
    {
        if (episodeEnding) return;
        episodeEnding = true;
        CancelDeathSequences();
        AddReward(WinReward);
        EndEpisode();
    }

    public void OnFightLost()
    {
        if (episodeEnding) return;
        episodeEnding = true;
        CancelDeathSequences();
        AddReward(LoseReward);
        EndEpisode();
    }

    /// <summary>
    /// Stops death-animation coroutines (which would deactivate GameObjects
    /// and unregister the Agent) and the RestartManager reset-after-delay
    /// coroutine, so the boss GO stays active for OnEpisodeBegin to fire.
    /// </summary>
    private void CancelDeathSequences()
    {
        var bossHealth = GetComponent<Health>();
        if (bossHealth != null) bossHealth.StopAllCoroutines();

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            var playerHealth = playerObj.GetComponent<Health>();
            if (playerHealth != null) playerHealth.StopAllCoroutines();
        }

        if (restartManager != null) restartManager.StopAllCoroutines();
    }
}