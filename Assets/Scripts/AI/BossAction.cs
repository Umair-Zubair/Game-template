/// <summary>
/// Available boss action types for the AI Decision Engine.
/// Extend this enum as new boss abilities are added.
/// </summary>
public enum BossActionType
{
    None,              // No preference — continue current behavior
    Chase,             // Move toward the player
    MeleeAttack,       // Transition to melee attack state
    ArtilleryAttack,   // Transition to artillery attack state
    SpawnCultist,      // Summon a TwistedCultist in front of the boss

    // Retreat,           // Move away from player (future)
    // Dodge,             // Dodge player attack (future)
    // Dash               // Dash attack (future)
}

/// <summary>
/// Output of a decision module: which action to take and how confident.
/// Passed between layers of the AI Decision Engine.
/// </summary>
public struct BossDecision
{
    /// <summary>The recommended action.</summary>
    public BossActionType action;

    /// <summary>0-1, how confident the module is in this recommendation.</summary>
    public float confidence;

    /// <summary>If true, skip lower-priority modules (Layer 1 critical override).</summary>
    public bool isCriticalOverride;

    /// <summary>"Heuristic", "Weighted", "ML" — identifies which layer produced this.</summary>
    public string source;

    public static BossDecision Default => new BossDecision
    {
        action = BossActionType.Chase,
        confidence = 0f,
        isCriticalOverride = false,
        source = "Default"
    };
}
