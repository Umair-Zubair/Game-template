using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Layer 2: Data-driven weighted strategy brain.
/// Tracks action-outcome pairs per player style and adjusts weights
/// using an exponential moving average.
///
/// Implements SDS Algorithm 2:
///   W_new = W_old + α(R - W_old)
///   W_new = Clamp(W_new, 0.05, 1.0)
///
/// Weights are persisted to JSON between sessions so the boss "remembers"
/// what worked against different player styles across encounters.
/// </summary>
public class WeightedStrategyBrain : IBossDecisionModule
{
    public string ModuleName => "Weighted";

    // ---- Hyperparameters (SDS Algorithm 2) ----
    private const float ALPHA = 0.1f;         // Learning rate
    private const float MIN_WEIGHT = 0.05f;   // Prevent zero-out
    private const float MAX_WEIGHT = 1.0f;

    // Weights per player style per action type
    private Dictionary<PlayerStyle, Dictionary<BossActionType, float>> weights;

    // Action types the boss can currently choose between
    private static readonly BossActionType[] ActionTypes =
    {
        BossActionType.MeleeAttack,
        BossActionType.ArtilleryAttack,
    };

    public WeightedStrategyBrain()
    {
        InitializeWeights();
    }

    // =========================================================
    // IBossDecisionModule
    // =========================================================

    public BossDecision Evaluate(GameContext ctx)
    {
        var styleWeights = weights[ctx.currentPlayerStyle];

        // Collect feasible actions and their weights
        float bestWeight = -1f;
        float secondBest = -1f;
        BossActionType bestAction = BossActionType.Chase;
        int feasibleCount = 0;

        foreach (var action in ActionTypes)
        {
            if (!IsFeasible(action, ctx)) continue;

            float w = styleWeights[action];
            feasibleCount++;

            if (w > bestWeight)
            {
                secondBest = bestWeight;
                bestWeight = w;
                bestAction = action;
            }
            else if (w > secondBest)
            {
                secondBest = w;
            }
        }

        // No feasible attack actions → chase (low confidence so Layer 3 can override)
        if (feasibleCount == 0)
        {
            return new BossDecision
            {
                action = BossActionType.Chase,
                confidence = 0f,
                source = ModuleName
            };
        }

        // Compute confidence: how dominant the best action's weight is
        float confidence;
        if (feasibleCount == 1)
        {
            // Only one feasible action — confidence is its weight directly
            confidence = bestWeight;
        }
        else
        {
            // Confidence = how much better the best is vs the runner-up
            // 0 when equal, approaches 1 when one dominates
            confidence = secondBest > 0f ? 1f - (secondBest / bestWeight) : 1f;
        }
        confidence = Mathf.Clamp01(confidence);

        return new BossDecision
        {
            action = bestAction,
            confidence = confidence,
            isCriticalOverride = false,
            source = ModuleName
        };
    }

    // =========================================================
    // Weight Update (SDS Algorithm 2)
    // =========================================================

    /// <summary>
    /// Update the weight for an action after seeing its outcome.
    /// Called by AIDecisionEngine when a boss action completes.
    /// </summary>
    /// <param name="action">The action that was taken.</param>
    /// <param name="style">The player style when the action was taken.</param>
    /// <param name="reward">Outcome reward, roughly in [-1, 1] range.</param>
    public void UpdateWeight(BossActionType action, PlayerStyle style, float reward)
    {
        if (!weights.ContainsKey(style) || !weights[style].ContainsKey(action))
            return;

        float oldWeight = weights[style][action];
        float newWeight = oldWeight + ALPHA * (reward - oldWeight);
        weights[style][action] = Mathf.Clamp(newWeight, MIN_WEIGHT, MAX_WEIGHT);

        Debug.Log($"[WeightedBrain] {style}/{action}: {oldWeight:F3} → {weights[style][action]:F3} (reward={reward:F2})");
    }

    // =========================================================
    // Feasibility Check
    // =========================================================

    private bool IsFeasible(BossActionType action, GameContext ctx)
    {
        switch (action)
        {
            case BossActionType.MeleeAttack:
                return ctx.canMeleeAttack && ctx.isPlayerInAttackRange;
            case BossActionType.ArtilleryAttack:
                return ctx.canUseArtillery;
            default:
                return false;
        }
    }

    // =========================================================
    // Initialization
    // =========================================================

    private void InitializeWeights()
    {
        weights = new Dictionary<PlayerStyle, Dictionary<BossActionType, float>>();
        foreach (PlayerStyle style in System.Enum.GetValues(typeof(PlayerStyle)))
        {
            weights[style] = new Dictionary<BossActionType, float>();
            foreach (var action in ActionTypes)
            {
                weights[style][action] = 0.5f; // Neutral starting weight
            }
        }
    }

    // =========================================================
    // Debug API
    // =========================================================

    /// <summary>
    /// Get a copy of the current weight values for a specific player style.
    /// Used by the debug HUD.
    /// </summary>
    public Dictionary<BossActionType, float> GetWeightsForStyle(PlayerStyle style)
    {
        if (weights.ContainsKey(style))
            return new Dictionary<BossActionType, float>(weights[style]);
        return null;
    }

    // =========================================================
    // Persistence (JSON serialization)
    // =========================================================

    [System.Serializable]
    private struct WeightEntry
    {
        public string style;
        public string action;
        public float weight;
    }

    [System.Serializable]
    private struct WeightData
    {
        public WeightEntry[] entries;
    }

    /// <summary>Serialize all weights to JSON string for saving.</summary>
    public string SerializeWeights()
    {
        var entries = new List<WeightEntry>();
        foreach (var styleKvp in weights)
        {
            foreach (var actionKvp in styleKvp.Value)
            {
                entries.Add(new WeightEntry
                {
                    style = styleKvp.Key.ToString(),
                    action = actionKvp.Key.ToString(),
                    weight = actionKvp.Value
                });
            }
        }
        return JsonUtility.ToJson(new WeightData { entries = entries.ToArray() }, true);
    }

    /// <summary>Deserialize weights from a JSON string (loaded from disk).</summary>
    public void DeserializeWeights(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var data = JsonUtility.FromJson<WeightData>(json);
            foreach (var entry in data.entries)
            {
                if (System.Enum.TryParse<PlayerStyle>(entry.style, out var style) &&
                    System.Enum.TryParse<BossActionType>(entry.action, out var action))
                {
                    if (weights.ContainsKey(style) && weights[style].ContainsKey(action))
                    {
                        weights[style][action] = Mathf.Clamp(entry.weight, MIN_WEIGHT, MAX_WEIGHT);
                    }
                }
            }
            Debug.Log("[WeightedBrain] Weights loaded from save.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[WeightedBrain] Failed to load weights: {e.Message}");
        }
    }
}
