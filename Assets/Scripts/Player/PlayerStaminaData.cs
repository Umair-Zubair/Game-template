using UnityEngine;

/// <summary>
/// ScriptableObject holding all stamina tuning parameters.
/// Create via Assets > Create > Player > Stamina Data.
/// Designers can tweak values without touching code.
/// </summary>
[CreateAssetMenu(fileName = "PlayerStaminaData", menuName = "Player/Stamina Data")]
public class PlayerStaminaData : ScriptableObject
{
    // ──────────────────────────────────────────────
    //  Core Pool
    // ──────────────────────────────────────────────

    [Header("Stamina Pool")]
    [Tooltip("Maximum stamina the player can have.")]
    public float maxStamina = 100f;

    [Tooltip("Stamina points recovered per second during regeneration.")]
    public float regenRatePerSecond = 15f;

    [Tooltip("Seconds of inactivity (no stamina use) before regen begins.")]
    public float regenDelayAfterUse = 1.5f;

    // ──────────────────────────────────────────────
    //  Action Costs
    // ──────────────────────────────────────────────

    [Header("Action Costs")]
    [Tooltip("Stamina consumed per jump.")]
    public float jumpCost = 15f;

    [Tooltip("Stamina consumed per ground attack.")]
    public float attackCost = 20f;

    [Tooltip("Stamina consumed per air attack (jump attack / uppercut while airborne).")]
    public float airAttackCost = 25f;

    [Tooltip("Stamina consumed per ranged (dragon) attack.")]
    public float rangedAttackCost = 18f;

    [Tooltip("Stamina consumed per jump-attack special move (F key).")]
    public float jumpAttackCost = 30f;

    [Tooltip("Stamina consumed per uppercut.")]
    public float uppercutCost = 25f;

    // ──────────────────────────────────────────────
    //  Exhaustion
    // ──────────────────────────────────────────────

    [Header("Exhaustion")]
    [Tooltip("Speed multiplier applied when stamina is depleted (0-1). e.g. 0.5 = half speed.")]
    [Range(0.1f, 1f)]
    public float exhaustedSpeedMultiplier = 0.5f;

    [Tooltip("Stamina must regenerate above this threshold to exit exhausted state.")]
    public float exhaustionRecoveryThreshold = 20f;

    [Tooltip("If true, allow stamina actions even at 0 stamina after a cooldown penalty.")]
    public bool allowActionsOnCooldownWhenExhausted = false;

    [Tooltip("Extra cooldown (seconds) added to actions performed while exhausted (if allowed).")]
    public float exhaustedActionCooldownPenalty = 1.0f;

    // ──────────────────────────────────────────────
    //  UI Thresholds
    // ──────────────────────────────────────────────

    [Header("UI Thresholds")]
    [Tooltip("Stamina percentage below which the bar shows 'low' color (0-1).")]
    [Range(0f, 1f)]
    public float lowStaminaThreshold = 0.3f;
}
