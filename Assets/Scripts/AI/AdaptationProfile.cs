/// <summary>
/// Classifies the player's current combat style based on observed behavior.
/// </summary>
public enum PlayerStyle
{
    Balanced,    // Default — no strong pattern detected
    Aggressive,  // High attack frequency, closes distance constantly
    Defensive,   // High block rate, low aggression
    Aerial,      // Frequent jump attacks / wall jumps
    Ranged       // Prefers distance, uses ranged attacks (future-proofing)
}

/// <summary>
/// Multiplier-based overrides applied to EnemyData values at runtime.
/// All values default to 1.0 (no change). The HeuristicAdaptationManager
/// sets these based on the detected PlayerStyle.
/// </summary>
public class AdaptationProfile
{
    // --- Movement ---
    public float chaseSpeedMultiplier      = 1f;
    public float retreatRangeMultiplier    = 1f;
    public float retreatSpeedMultiplier    = 1f;

    // --- Combat ---
    public float attackCooldownMultiplier  = 1f;  // < 1 = faster attacks
    public float dodgeCooldownMultiplier   = 1f;  // < 1 = dodges more often

    // --- Priority Bonuses (added to base score in ChaseState) ---
    public float dashPriorityBonus         = 0f;
    public float artilleryPriorityBonus    = 0f;

    // --- Clamp bounds (applied by EnemyController) ---
    public const float MinCooldownMultiplier = 0.5f;
    public const float MaxCooldownMultiplier = 2.0f;
    public const float MinSpeedMultiplier    = 0.5f;
    public const float MaxSpeedMultiplier    = 2.0f;

    // --- Pre-built profiles ---

    public static AdaptationProfile Default() => new AdaptationProfile();

    public static AdaptationProfile AntiAggressive() => new AdaptationProfile
    {
        retreatRangeMultiplier   = 1.5f,  // Retreat earlier
        retreatSpeedMultiplier   = 1.3f,
        attackCooldownMultiplier = 0.75f, // Counter-attack faster
        dodgeCooldownMultiplier  = 0.7f,  // Dodge more
        artilleryPriorityBonus   = 1.5f,  // Prefer artillery to create space
    };

    public static AdaptationProfile AntiDefensive() => new AdaptationProfile
    {
        retreatRangeMultiplier   = 0.6f,  // Don't retreat as much — stay in face
        attackCooldownMultiplier = 0.8f,  // Attack more frequently
        dashPriorityBonus        = 2.0f,  // Prefer dash to break guard
        artilleryPriorityBonus   = 0.5f,  // Less artillery (player just blocks it)
    };

    public static AdaptationProfile AntiAerial() => new AdaptationProfile
    {
        artilleryPriorityBonus   = 2.5f,  // Artillery hits aerial players
        retreatRangeMultiplier   = 1.2f,  // Stay back, let artillery do work
        attackCooldownMultiplier = 1.1f,  // Slightly slower direct attacks
    };

    public static AdaptationProfile AntiRanged() => new AdaptationProfile
    {
        chaseSpeedMultiplier     = 1.4f,  // Close gap faster
        dashPriorityBonus        = 1.5f,  // Dash to close distance
        dodgeCooldownMultiplier  = 0.6f,  // Dodge projectiles more
        retreatRangeMultiplier   = 0.7f,  // Don't retreat — push forward
    };
}
