using UnityEngine;

/// <summary>
/// Monitors boss vs player health ratio and gently slows the boss down when
/// the fight is extremely lopsided.  Does NOT override decision-making — it
/// only widens cooldowns so the player has slightly more breathing room.
///
/// Trigger:  PlayerHealth &lt; 15% AND BossHealth &gt; 85%
/// Action:   Slow all boss cooldowns by 15%
/// Release:  PlayerHealth &gt; 30%
///
/// The guardian can be disabled at runtime via <see cref="Enabled"/>.
/// When disabled every property returns its neutral value (multiplier = 1,
/// no blocking, no overrides) so the rest of the pipeline runs unaffected.
/// </summary>
public class FairnessGuardian
{
    private const float PLAYER_DANGER_THRESHOLD   = 0.15f;
    private const float BOSS_DOMINANT_THRESHOLD    = 0.85f;
    private const float PLAYER_RECOVERY_THRESHOLD  = 0.30f;
    private const float COOLDOWN_PENALTY           = 1.15f;

    /// <summary>Master switch — when false all queries return neutral values.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the guardian is currently throttling the boss.</summary>
    public bool IsRelaxationActive { get; private set; }

    /// <summary>
    /// Cooldown multiplier. 1.0 = normal, 1.15 = 15% slower during relaxation.
    /// Always 1.0 when disabled.
    /// </summary>
    public float CooldownMultiplier => Enabled && IsRelaxationActive ? COOLDOWN_PENALTY : 1f;

    // =========================================================
    // Core Evaluation
    // =========================================================

    public void Evaluate(float bossHealthNormalized, float playerHealthNormalized)
    {
        if (!Enabled)
        {
            if (IsRelaxationActive) DeactivateRelaxation();
            return;
        }

        if (!IsRelaxationActive)
        {
            if (playerHealthNormalized < PLAYER_DANGER_THRESHOLD
                && bossHealthNormalized > BOSS_DOMINANT_THRESHOLD)
            {
                ActivateRelaxation();
            }
        }
        else
        {
            if (playerHealthNormalized > PLAYER_RECOVERY_THRESHOLD)
            {
                DeactivateRelaxation();
            }
        }
    }

    // =========================================================
    // Decision Pass-through
    // =========================================================

    /// <summary>
    /// Previously replaced artillery decisions with chase. Now acts as a
    /// pass-through — the guardian no longer overrides what the boss decides
    /// to do, it only affects how quickly the boss can act again via
    /// <see cref="CooldownMultiplier"/>.
    /// </summary>
    public BossDecision ApplyOverride(BossDecision decision)
    {
        return decision;
    }

    // =========================================================
    // Reset / Internal
    // =========================================================

    public void Reset()
    {
        IsRelaxationActive = false;
    }

    private void ActivateRelaxation()
    {
        IsRelaxationActive = true;
        Debug.Log($"[FairnessGuardian] ACTIVATED — player in danger. " +
                  $"Cooldowns +{(COOLDOWN_PENALTY - 1f) * 100f:F0}%.");
    }

    private void DeactivateRelaxation()
    {
        IsRelaxationActive = false;
        Debug.Log("[FairnessGuardian] DEACTIVATED — player recovered.");
    }
}
