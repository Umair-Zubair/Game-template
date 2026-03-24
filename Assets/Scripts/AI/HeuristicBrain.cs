/// <summary>
/// Layer 1: Rule-based heuristic brain.
/// Reads the current adaptation profile bonuses and player style to recommend actions.
/// Provides critical overrides for obvious tactical situations that should always fire.
///
/// This wraps the existing heuristic adaptation system's profile bonuses into
/// action-level decisions for the AIDecisionEngine.
/// </summary>
public class HeuristicBrain : IBossDecisionModule
{
    public string ModuleName => "Heuristic";

    public BossDecision Evaluate(GameContext ctx)
    {
        // =============================================================
        // CRITICAL OVERRIDES — these bypass Layer 2 and Layer 3
        // =============================================================

        // Anti-Aerial: if player is aerial and artillery is available with high bonus,
        // artillery is the optimal counter (hits mid-air players reliably)
        if (ctx.currentPlayerStyle == PlayerStyle.Aerial
            && ctx.canUseArtillery
            && ctx.artilleryPriorityBonus >= 2.0f)
        {
            return new BossDecision
            {
                action = BossActionType.ArtilleryAttack,
                confidence = 0.9f,
                isCriticalOverride = true,
                source = ModuleName
            };
        }

        // Anti-Ranged: if ranged player is finally in melee range, punish immediately
        // (ranged players rarely get close — when they do, it's a critical window)
        if (ctx.currentPlayerStyle == PlayerStyle.Ranged
            && ctx.isPlayerInAttackRange
            && ctx.canMeleeAttack)
        {
            return new BossDecision
            {
                action = BossActionType.MeleeAttack,
                confidence = 0.85f,
                isCriticalOverride = true,
                source = ModuleName
            };
        }

        // =============================================================
        // NON-CRITICAL RECOMMENDATIONS — Layer 2 can override these
        // =============================================================

        // Artillery with significant profile bonus (adaptation says we should use it)
        if (ctx.canUseArtillery && ctx.artilleryPriorityBonus > 1.0f)
        {
            return new BossDecision
            {
                action = BossActionType.ArtilleryAttack,
                confidence = 0.4f + ctx.artilleryPriorityBonus * 0.1f,
                isCriticalOverride = false,
                source = ModuleName
            };
        }

        // Default artillery when available (no strong profile preference)
        if (ctx.canUseArtillery)
        {
            return new BossDecision
            {
                action = BossActionType.ArtilleryAttack,
                confidence = 0.35f,
                isCriticalOverride = false,
                source = ModuleName
            };
        }

        // Melee when in range and ready
        if (ctx.isPlayerInAttackRange && ctx.canMeleeAttack)
        {
            return new BossDecision
            {
                action = BossActionType.MeleeAttack,
                confidence = 0.6f,
                isCriticalOverride = false,
                source = ModuleName
            };
        }

        // Default: keep chasing
        return new BossDecision
        {
            action = BossActionType.Chase,
            confidence = 0.3f,
            isCriticalOverride = false,
            source = ModuleName
        };
    }
}
