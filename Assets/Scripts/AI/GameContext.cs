/// <summary>
/// Snapshot of all game state that AI decision modules need to make decisions.
/// Computed once per evaluation cycle by AIDecisionEngine and passed to all modules.
/// </summary>
public struct GameContext
{
    // ---- Boss state ----
    public float bossHealthNormalized;     // 0 = dead, 1 = full
    public float distanceToPlayer;
    public bool canMeleeAttack;            // Cooldown ready + not mid-attack
    public bool canUseArtillery;           // Cooldown ready + not mid-attack + not blocked by fairness
    public bool isPlayerInAttackRange;
    public bool isPlayerInDetectionRange;

    // ---- Player metrics (from PlayerBehaviorTracker) ----
    public PlayerProfile playerProfile;

    // ---- Player style (from VoidbornAdaptationManager / HeuristicAdaptationManager) ----
    public PlayerStyle currentPlayerStyle;

    // ---- Player state ----
    public float playerHealthNormalized;   // 0 = dead, 1 = full

    // ---- Adaptation profile bonuses (from heuristic layer) ----
    public float artilleryPriorityBonus;
    public float dashPriorityBonus;
}
