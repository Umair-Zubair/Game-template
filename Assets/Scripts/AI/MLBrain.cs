using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// Layer 2: ML-Agents neural network brain.
/// Observations: normalized PlayerProfile floats + health + feasibility (13 total).
/// Actions: 1 discrete branch, size 3 (Chase / Melee / Artillery).
/// Set BehaviorParameters → Space Size = 13, Branches = {3}.
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
    private const float InfeasiblePenalty = -0.05f;

    private const float ArtilleryDamageBonus  = 0.5f;
    private const float ConsecutiveMeleeBonus = 0.25f;
    private int consecutiveMeleeHits;

    public bool IsModelLoaded =>
        behaviorParams != null &&
        behaviorParams.BehaviorType != BehaviorType.HeuristicOnly &&
        behaviorParams.Model != null;

    public override void Initialize()
    {
        behaviorParams    = GetComponent<BehaviorParameters>();
        decisionEngine    = GetComponent<AIDecisionEngine>();
        adaptationManager = GetComponent<VoidbornAdaptationManager>();
        restartManager    = FindFirstObjectByType<RestartManager>();
    }

    // ── Episode lifecycle ──────────────────────────────────────

    public override void OnEpisodeBegin()
    {
        AISessionLogger.Instance?.EndFight(false);
        AIDecisionLogger.Instance?.EndFight(false);

        CancelDeathSequences();

        if (restartManager != null)
            restartManager.ResetEncounter();
        else
        {
            GetComponent<BossController>()?.Respawn(transform.position);
            var p = FindFirstObjectByType<PlayerController>();
            p?.Respawn(p.transform.position);
        }

        decisionEngine?.ResetForNewEpisode();
        adaptationManager?.ResetForNewEpisode();

        episodeEnding = false;
        episodeTimer  = 0f;
        lastDecision  = BossDecision.Default;
        consecutiveMeleeHits = 0;
    }

    // ── Called by AIDecisionEngine each eval tick ───────────────

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

    // ── Observations — 13 floats (raw metrics, no style enum) ──

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

        // Raw PlayerProfile — network learns style implicitly (7)
        PlayerProfile p = CurrentContext.playerProfile;
        sensor.AddObservation(Mathf.Clamp01(p.attackFrequency / 5f));
        sensor.AddObservation(p.meleeRatio);
        sensor.AddObservation(p.jumpAttackRatio);
        sensor.AddObservation(p.rangedRatio);
        sensor.AddObservation(Mathf.Clamp01(p.averageDistance / 20f));
        sensor.AddObservation(p.aggressionScore);
        sensor.AddObservation(Mathf.Clamp01(p.jumpFrequency / 3f));
    }

    // ── Actions ────────────────────────────────────────────────

    public override void OnActionReceived(ActionBuffers actions)
    {
        int idx = actions.DiscreteActions[0];
        BossActionType chosen = ActionMap[Mathf.Clamp(idx, 0, ActionMap.Length - 1)];

        if (chosen == BossActionType.MeleeAttack
            && (!CurrentContext.canMeleeAttack || !CurrentContext.isPlayerInAttackRange))
            AddReward(InfeasiblePenalty);

        if (chosen == BossActionType.ArtilleryAttack && !CurrentContext.canUseArtillery)
            AddReward(InfeasiblePenalty);

        float dynamicConf = CalculateDynamicConfidence(chosen, CurrentContext);

        lastDecision = new BossDecision
        {
            action     = chosen,
            confidence = dynamicConf, 
            source     = ModuleName
        };


    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[0] = 0;
    }

    // ── Rewards ────────────────────────────────────────────────

    public void ReceiveActionOutcome(BossActionType action, float normalizedDealt, float normalizedTaken)
    {
        float adjustedDealt = normalizedDealt;

        if (action == BossActionType.ArtilleryAttack && normalizedDealt > 0f)
            adjustedDealt *= 1f + ArtilleryDamageBonus;

        if (action == BossActionType.MeleeAttack && normalizedDealt > 0f)
        {
            consecutiveMeleeHits++;
            if (consecutiveMeleeHits > 1)
                adjustedDealt *= 1f + ConsecutiveMeleeBonus * (consecutiveMeleeHits - 1);
        }
        else
        {
            consecutiveMeleeHits = 0;
        }

        AddReward(adjustedDealt - normalizedTaken);
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

    public void ForceEndEpisode()
    {
        if (episodeEnding) return;
        episodeEnding = true;
        CancelDeathSequences();
        EndEpisode();
    }

    // ── Safety ─────────────────────────────────────────────────

    private void CancelDeathSequences()
    {
        var bh = GetComponent<Health>();
        if (bh != null) bh.StopAllCoroutines();

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            var ph = playerObj.GetComponent<Health>();
            if (ph != null) ph.StopAllCoroutines();
        }

        if (restartManager != null) restartManager.StopAllCoroutines();
    }

    private float CalculateDynamicConfidence(BossActionType chosenAction, GameContext ctx)
    {
        float baseConfidence = 0.5f; // Start neutral

        switch (chosenAction)
        {
            case BossActionType.MeleeAttack:
                if (!ctx.canMeleeAttack) return 0.0f; // Literally impossible, 0 confidence
                if (ctx.isPlayerInAttackRange) baseConfidence += 0.4f; // Perfect situation
                else baseConfidence -= 0.3f; // Risky, player isn't close enough
                break;

            case BossActionType.ArtilleryAttack:
                if (!ctx.canUseArtillery) return 0.0f; // On cooldown, 0 confidence
                if (!ctx.isPlayerInAttackRange) baseConfidence += 0.4f; // Great for long range
                if (ctx.playerHealthNormalized < 0.3f) baseConfidence += 0.2f; // Good for finishing a weak player
                break;

            case BossActionType.Chase:
                if (ctx.distanceToPlayer > 15f) baseConfidence += 0.4f; // Makes sense to close gap
                if (ctx.isPlayerInAttackRange) baseConfidence -= 0.3f; // Why chase if already in melee range?
                break;
        }

        // Clamp the result between 0.0 and 1.0 just to be safe
        return Mathf.Clamp01(baseConfidence);
    }
}


