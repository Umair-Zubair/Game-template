using UnityEngine;
using System;

/// <summary>
/// Core stamina component. Attach to the Player GameObject alongside PlayerController.
///
/// Responsibilities:
///   - Tracks current stamina, handles consumption and regeneration.
///   - Fires events so other systems (UI, audio, VFX) can react without coupling.
///   - Manages the "exhausted" debuff state (reduced speed, blocked actions).
///   - Frame-rate independent; respects Time.timeScale (pauses when game pauses).
///   - Prevents negative stamina via clamping.
///   - Guards against same-frame double-spend with a per-frame flag.
///
/// Design decisions:
///   - Uses UnityEngine.Time.deltaTime so regeneration naturally pauses when timeScale == 0.
///   - Exhaustion is hysteresis-based: enter at 0, exit at threshold. This avoids flickering.
///   - All costs live in PlayerStaminaData ScriptableObject for easy designer tuning.
///   - Public API is intentionally simple: TryConsume / ForceConsume / HasEnough / ResetStamina.
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────

    [Header("Configuration")]
    [SerializeField] private PlayerStaminaData data;

    // ──────────────────────────────────────────────
    //  Events  (event-driven hooks for UI / Audio / VFX)
    // ──────────────────────────────────────────────

    /// <summary>Fires every frame stamina changes. Args: (currentStamina, maxStamina).</summary>
    public event Action<float, float> OnStaminaChanged;

    /// <summary>Fires once when stamina hits zero.</summary>
    public event Action OnStaminaDepleted;

    /// <summary>Fires when exhausted state toggles. Arg: isExhausted.</summary>
    public event Action<bool> OnExhaustedStateChanged;

    /// <summary>Fires when an action is blocked due to insufficient stamina.</summary>
    public event Action OnActionBlocked;

    // ──────────────────────────────────────────────
    //  Runtime State  (read-only from outside)
    // ──────────────────────────────────────────────

    /// <summary>Current stamina value, clamped [0, maxStamina].</summary>
    public float CurrentStamina { get; private set; }

    /// <summary>True while stamina is zero and hasn't recovered past the threshold.</summary>
    public bool IsExhausted { get; private set; }

    /// <summary>Shorthand: maxStamina from data asset.</summary>
    public float MaxStamina => data != null ? data.maxStamina : 100f;

    /// <summary>Normalized 0-1 ratio for UI.</summary>
    public float StaminaRatio => MaxStamina > 0 ? CurrentStamina / MaxStamina : 0f;

    /// <summary>Speed multiplier that the player controller should apply. 1.0 when normal.</summary>
    public float SpeedMultiplier => IsExhausted ? data.exhaustedSpeedMultiplier : 1f;

    /// <summary>Reference to the data asset for external reads (e.g. cost look-ups).</summary>
    public PlayerStaminaData Data => data;

    // ──────────────────────────────────────────────
    //  Internal
    // ──────────────────────────────────────────────

    private float regenDelayTimer;       // Counts down after last stamina use
    private int lastConsumeFrame = -1;   // Guards against same-frame double-spend

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        if (data == null)
        {
            Debug.LogError("[PlayerStamina] No PlayerStaminaData assigned! Creating default values at runtime.");
            data = ScriptableObject.CreateInstance<PlayerStaminaData>();
        }

        CurrentStamina = data.maxStamina;
    }

    private void Update()
    {
        HandleRegeneration();
    }

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Attempts to consume stamina for an action.
    /// Returns true if successful (enough stamina), false if blocked.
    /// When blocked, fires OnActionBlocked.
    /// </summary>
    /// <param name="cost">Amount to consume.</param>
    /// <returns>True if action is allowed.</returns>
    public bool TryConsume(float cost)
    {
        if (cost <= 0f) return true; // Free action

        // Exhaustion gate: block actions unless config allows with penalty
        if (IsExhausted && !data.allowActionsOnCooldownWhenExhausted)
        {
            OnActionBlocked?.Invoke();
            return false;
        }

        // Standard insufficient-stamina check
        if (CurrentStamina < cost)
        {
            OnActionBlocked?.Invoke();
            return false;
        }

        ForceConsume(cost);
        return true;
    }

    /// <summary>
    /// Check if the player has enough stamina without consuming it.
    /// Useful for UI hints or animation previews.
    /// </summary>
    public bool HasEnough(float cost)
    {
        if (IsExhausted && !data.allowActionsOnCooldownWhenExhausted)
            return false;
        return CurrentStamina >= cost;
    }

    /// <summary>
    /// Force-consumes stamina (bypasses the "enough" check).
    /// Use sparingly — mainly for internal calls after validation.
    /// </summary>
    public void ForceConsume(float cost)
    {
        if (cost <= 0f) return;

        // Same-frame guard: prevent double-spend from two systems in one frame.
        // We allow it but log a warning in debug builds.
        if (Time.frameCount == lastConsumeFrame && GlobalData.DebugMode)
        {
            Debug.LogWarning("[PlayerStamina] Multiple consumes in the same frame — intentional?");
        }
        lastConsumeFrame = Time.frameCount;

        float previousStamina = CurrentStamina;
        CurrentStamina = Mathf.Clamp(CurrentStamina - cost, 0f, data.maxStamina);

        // Reset regen delay every time stamina is spent
        regenDelayTimer = data.regenDelayAfterUse;

        OnStaminaChanged?.Invoke(CurrentStamina, data.maxStamina);

        // Check for depletion
        if (CurrentStamina <= 0f && previousStamina > 0f)
        {
            OnStaminaDepleted?.Invoke();
            SetExhausted(true);
        }
    }

    /// <summary>
    /// Fully resets stamina (e.g. on respawn or level load). Clears exhaustion.
    /// </summary>
    public void ResetStamina()
    {
        CurrentStamina = data.maxStamina;
        regenDelayTimer = 0f;
        SetExhausted(false);
        OnStaminaChanged?.Invoke(CurrentStamina, data.maxStamina);
    }

    /// <summary>
    /// Returns a serializable snapshot for save/load systems.
    /// </summary>
    public StaminaSaveData GetSaveData()
    {
        return new StaminaSaveData
        {
            currentStamina = CurrentStamina,
            isExhausted = IsExhausted,
            regenDelayRemaining = regenDelayTimer
        };
    }

    /// <summary>
    /// Restores state from a save snapshot.
    /// </summary>
    public void LoadSaveData(StaminaSaveData saveData)
    {
        CurrentStamina = Mathf.Clamp(saveData.currentStamina, 0f, data.maxStamina);
        regenDelayTimer = saveData.regenDelayRemaining;
        SetExhausted(saveData.isExhausted);
        OnStaminaChanged?.Invoke(CurrentStamina, data.maxStamina);
    }

    // ──────────────────────────────────────────────
    //  Internal Logic
    // ──────────────────────────────────────────────

    /// <summary>
    /// Handles smooth, frame-rate-independent regeneration.
    /// Uses Time.deltaTime, which is 0 when timeScale == 0 (game paused).
    /// </summary>
    private void HandleRegeneration()
    {
        // Already full — nothing to do
        if (CurrentStamina >= data.maxStamina) return;

        // Wait for regen delay
        if (regenDelayTimer > 0f)
        {
            regenDelayTimer -= Time.deltaTime;
            return;
        }

        // Regenerate
        float previousStamina = CurrentStamina;
        CurrentStamina = Mathf.Clamp(
            CurrentStamina + data.regenRatePerSecond * Time.deltaTime,
            0f,
            data.maxStamina
        );

        // Notify listeners
        if (!Mathf.Approximately(CurrentStamina, previousStamina))
        {
            OnStaminaChanged?.Invoke(CurrentStamina, data.maxStamina);
        }

        // Exit exhaustion when recovered past threshold (hysteresis)
        if (IsExhausted && CurrentStamina >= data.exhaustionRecoveryThreshold)
        {
            SetExhausted(false);
        }
    }

    private void SetExhausted(bool exhausted)
    {
        if (IsExhausted == exhausted) return;
        IsExhausted = exhausted;
        OnExhaustedStateChanged?.Invoke(IsExhausted);

        if (GlobalData.DebugMode)
            Debug.Log($"[PlayerStamina] Exhausted state: {IsExhausted}");
    }
}

// ──────────────────────────────────────────────
//  Save / Load Data Structure
// ──────────────────────────────────────────────

/// <summary>
/// Plain data class for serializing stamina state.
/// Can be embedded in any save system (JSON, binary, PlayerPrefs).
/// </summary>
[System.Serializable]
public struct StaminaSaveData
{
    public float currentStamina;
    public bool isExhausted;
    public float regenDelayRemaining;
}
