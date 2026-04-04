using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// Layer 2: ML-Agents neural network brain.
/// Observes only raw PlayerProfile metrics + health + feasibility flags.
/// The network must implicitly learn to classify player behavior from
/// raw data to maximize net damage (dealt − taken).
///
/// Observation space: 15 floats.  Action space: 1 discrete branch, size 3.
/// Set BehaviorParameters → Space Size = 15, Branches = {3}.
/// </summary>
public class MLBrain : Agent, IBossDecisionModule
{
    public string ModuleName => "MLBrain";
    public GameContext CurrentContext { get; set; }

    [SerializeField] private float maxEpisodeDuration = 120f;

    private BehaviorParameters behaviorParams;
    private BossDecision lastDecision;
    private float episodeTimer;
    private bool episodeEnding;

    private RestartManager restartManager;
    private AIDecisionEngine decisionEngine;
    private VoidbornAdaptationManager adaptationManager;

    private static readonly BossActionType[] ActionMap =
    {
        BossActionType.Chase,
        BossActionType.MeleeAttack,
        BossActionType.ArtilleryAttack
    };

    private const float WinReward         = +5f;
    private const float LoseReward        = -5f;
    private const float TimeoutReward     = -1f;
    private const float InfeasiblePenalty = -0.1f;

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
    // Episode lifecycle
    // ═══════════════════════════════════════════

    public override void OnEpisodeBegin()
    {
        AISessionLogger.Instance?.EndFight(false);
        AIDecisionLogger.Instance?.EndFight(false);

        if (restartManager != null)
            restartManager.ResetEncounter();
        else
        {
            var boss = GetComponent<BossController>();
            boss?.Respawn(transform.position);
            var player = FindFirstObjectByType<PlayerController>();
            player?.Respawn(player.transform.position);
        }

        decisionEngine?.ResetForNewEpisode();
        adaptationManager?.ResetForNewEpisode();

        episodeEnding = false;
        episodeTimer  = 0f;
        lastDecision  = BossDecision.Default;
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
    // Observations — 15 floats (raw metrics only)
    // ═══════════════════════════════════════════

    public override void CollectObservations(VectorSensor sensor)
    {
        // Health & spatial (3)
        sensor.AddObservation(CurrentContext.bossHealthNormalized);
        sensor.AddObservation(CurrentContext.playerHealthNormalized);
        sensor.AddObservation(Mathf.Clamp01(CurrentContext.distanceToPlayer / 20f));

        // Feasibility flags (3)
        sensor.AddObservation(CurrentContext.isPlayerInAttackRange ? 1f : 0f);
        sensor.AddObservation(CurrentContext.canMeleeAttack        ? 1f : 0f);
        sensor.AddObservation(CurrentContext.canUseArtillery       ? 1f : 0f);

        // Raw PlayerProfile — the network learns style classification implicitly (9)
        PlayerProfile p = CurrentContext.playerProfile;
        sensor.AddObservation(Mathf.Clamp01(p.attackFrequency / 5f));
        sensor.AddObservation(p.meleeRatio);
        // sensor.AddObservation(p.uppercutRatio);
        sensor.AddObservation(p.jumpAttackRatio);
        sensor.AddObservation(p.rangedRatio);
        sensor.AddObservation(Mathf.Clamp01(p.averageDistance / 20f));
        sensor.AddObservation(p.aggressionScore);
        
        
        sensor.AddObservation(Mathf.Clamp01(p.jumpFrequency / 3f));
    }

    // ═══════════════════════════════════════════
    // Actions — discrete branch size 3
    // ═══════════════════════════════════════════

    public override void OnActionReceived(ActionBuffers actions)
    {
        int idx = actions.DiscreteActions[0];
        BossActionType chosen = ActionMap[Mathf.Clamp(idx, 0, ActionMap.Length - 1)];

        if (chosen == BossActionType.MeleeAttack
            && (!CurrentContext.canMeleeAttack || !CurrentContext.isPlayerInAttackRange))
            AddReward(InfeasiblePenalty);

        if (chosen == BossActionType.ArtilleryAttack && !CurrentContext.canUseArtillery)
            AddReward(InfeasiblePenalty);

        lastDecision = new BossDecision
        {
            action     = chosen,
            confidence = 0.8f,
            source     = ModuleName
        };
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[0] = 0;
    }

    // ═══════════════════════════════════════════
    // Rewards: +1 per HP-normalized damage dealt
    //          −1 per HP-normalized damage taken
    // ═══════════════════════════════════════════

    public void ReceiveActionOutcome(float normalizedDealt, float normalizedTaken)
    {
        AddReward(normalizedDealt - normalizedTaken);
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

    private void CancelDeathSequences()
    {
        var bossHealth = GetComponent<Health>();
        if (bossHealth != null) bossHealth.StopAllCoroutines();

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            var ph = playerObj.GetComponent<Health>();
            if (ph != null) ph.StopAllCoroutines();
        }

        if (restartManager != null) restartManager.StopAllCoroutines();
    }
}
