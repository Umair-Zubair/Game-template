using UnityEngine;

/// <summary>
/// Monitors boss vs player health ratio and prevents unfair difficulty spikes.
/// Implements SDS Algorithm 4: FairnessGuardian.
///
/// Trigger:  PlayerHealth &lt; 20% AND BossHealth &gt; 80%
/// Action:   Slow boss attacks by 30%, block artillery until player recovers above 40%
/// Release:  PlayerHealth &gt; 40%
///
/// The guardian is a passive safety net — it does not make decisions, only
/// modifies decisions made by the three AI layers. It sits between the
/// decision engine and the boss controller.
/// </summary>
public class FairnessGuardian
{
    // ---- Thresholds ----
    private const float PLAYER_DANGER_THRESHOLD   = 0.20f;  // Player below 20% HP
    private const float BOSS_DOMINANT_THRESHOLD    = 0.80f;  // Boss above 80% HP
    private const float PLAYER_RECOVERY_THRESHOLD  = 0.40f;  // Player recovers above 40%
    private const float COOLDOWN_PENALTY           = 1.3f;   // +30% cooldown slowdown

    // ---- Public State ----

    /// <summary>Whether the guardian is currently throttling the boss.</summary>
    public bool IsRelaxationActive { get; private set; }

    /// <summary>
    /// Multiply all boss cooldowns by this value.
    /// 1.0 = normal, 1.3 = 30% slower during relaxation.
    /// </summary>
    public float CooldownMultiplier { get; private set; } = 1f;

    /// <summary>Whether artillery attacks are currently blocked.</summary>
    public bool BlockArtillery { get; private set; }

    // =========================================================
    // Core Evaluation
    // =========================================================

    /// <summary>
    /// Evaluate current health ratios and update fairness overrides.
    /// Called every evaluation cycle from AIDecisionEngine.
    /// </summary>
    public void Evaluate(float bossHealthNormalized, float playerHealthNormalized)
    {
        if (!IsRelaxationActive)
        {
            // Check trigger condition
            if (playerHealthNormalized < PLAYER_DANGER_THRESHOLD
                && bossHealthNormalized > BOSS_DOMINANT_THRESHOLD)
            {
                ActivateRelaxation();
            }
        }
        else
        {
            // Check recovery condition
            if (playerHealthNormalized > PLAYER_RECOVERY_THRESHOLD)
            {
                DeactivateRelaxation();
            }
        }
    }

    // =========================================================
    // Decision Override
    // =========================================================

    /// <summary>
    /// Apply fairness override to a decision produced by the AI layers.
    /// Called by AIDecisionEngine after the three-layer cascade.
    /// </summary>
    public BossDecision ApplyOverride(BossDecision decision)
    {
        if (!IsRelaxationActive) return decision;

        // Block artillery during relaxation — force chase instead
        if (BlockArtillery && decision.action == BossActionType.ArtilleryAttack)
        {
            decision.action = BossActionType.Chase;
            decision.source += " [FG:blocked]";
        }

        return decision;
    }

    // =========================================================
    // Internal
    // =========================================================

    private void ActivateRelaxation()
    {
        IsRelaxationActive = true;
        CooldownMultiplier = COOLDOWN_PENALTY;
        BlockArtillery = true;
        Debug.Log($"[FairnessGuardian] ACTIVATED — player in danger. " +
                  $"Cooldowns +{(COOLDOWN_PENALTY - 1f) * 100f:F0}%, artillery disabled.");
    }

    private void DeactivateRelaxation()
    {
        IsRelaxationActive = false;
        CooldownMultiplier = 1f;
        BlockArtillery = false;
        Debug.Log("[FairnessGuardian] DEACTIVATED — player recovered.");
    }
}
