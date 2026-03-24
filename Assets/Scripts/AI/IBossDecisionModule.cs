/// <summary>
/// Interface for AI decision modules (brains) in the three-layer architecture.
/// Each module evaluates the current game context and recommends an action.
///
/// Layer 1: HeuristicBrain     — rule-based critical overrides
/// Layer 2: WeightedStrategyBrain — data-driven probability adjustment
/// Layer 3: (future) MLBrain   — neural network inference via ML-Agents
/// </summary>
public interface IBossDecisionModule
{
    /// <summary>
    /// Evaluate the current game state and return an action recommendation.
    /// </summary>
    BossDecision Evaluate(GameContext context);

    /// <summary>
    /// Display name for debug HUD and logging.
    /// </summary>
    string ModuleName { get; }
}
