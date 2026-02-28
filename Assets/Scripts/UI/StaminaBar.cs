using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI component that displays the player's stamina as a fill bar.
///
/// Design:
///   - Listens to PlayerStamina events (no per-frame polling for the target value).
///   - Smoothly lerps the visual fill toward the actual value for a polished feel.
///   - Supports three color states: Normal, Low, Exhausted.
///   - Resolution-independent — works with any Canvas Scaler setting.
///   - Auto-finds PlayerStamina if not assigned (convenience for single-player).
///
/// Setup:
///   1. Create a UI Image (type = Filled, Fill Method = Horizontal) for the bar foreground.
///   2. Optionally create a second Image behind it for the "total" bar background.
///   3. Assign references in the Inspector.
///   4. Drag the PlayerStamina component (on the player) into the playerStamina field,
///      or let it auto-find at Start().
/// </summary>
public class StaminaBar : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  References
    // ──────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The PlayerStamina component on the player. Auto-found if left empty.")]
    [SerializeField] private PlayerStamina playerStamina;

    [Tooltip("Foreground fill image (Image Type = Filled).")]
    [SerializeField] private Image currentStaminaBar;

    [Tooltip("Optional background / total bar image.")]
    [SerializeField] private Image totalStaminaBar;

    [Tooltip("Optional border / frame image. Pure decoration — position and size it yourself in the scene.")]
    [SerializeField] private Image borderImage;

    // ──────────────────────────────────────────────
    //  Visual Tuning
    // ──────────────────────────────────────────────

    [Header("Smooth Fill")]
    [Tooltip("Speed at which the bar catches up to the real value. Higher = snappier.")]
    [SerializeField] private float fillLerpSpeed = 8f;

    [Header("Color States")]
    [SerializeField] private Color normalColor   = new Color(0.2f, 0.8f, 0.2f, 1f);  // Green
    [SerializeField] private Color lowColor      = new Color(1.0f, 0.6f, 0.0f, 1f);  // Orange
    [SerializeField] private Color exhaustedColor = new Color(0.8f, 0.1f, 0.1f, 1f); // Red

    // ──────────────────────────────────────────────
    //  Runtime
    // ──────────────────────────────────────────────

    private float targetFill;         // The fill amount we're lerping toward
    private bool isExhausted;
    private float lowThreshold = 0.3f; // Cached from data asset

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Start()
    {
        // Force the fill image to type Filled + Horizontal so fillAmount
        // actually shrinks the bar visually (without this it stays a full rectangle).
        ConfigureFillImage(currentStaminaBar);

        // Show a full green bar by default (before the player spawns)
        if (currentStaminaBar != null)
        {
            currentStaminaBar.fillAmount = 1f;
            currentStaminaBar.color = normalColor;
        }
        if (totalStaminaBar != null)
            totalStaminaBar.fillAmount = 1f;

        // Auto-find if not assigned
        if (playerStamina == null)
            playerStamina = FindFirstObjectByType<PlayerStamina>();

        // Player may not exist yet — CharacterManager calls SetPlayer() later.
        if (playerStamina == null)
            return;

        // Cache threshold
        if (playerStamina.Data != null)
            lowThreshold = playerStamina.Data.lowStaminaThreshold;

        // Subscribe to events
        playerStamina.OnStaminaChanged += HandleStaminaChanged;
        playerStamina.OnExhaustedStateChanged += HandleExhaustedChanged;

        // Snap to current stamina ratio
        targetFill = playerStamina.StaminaRatio;
        if (currentStaminaBar != null)
            currentStaminaBar.fillAmount = targetFill;
    }

    private void OnDestroy()
    {
        // Unsubscribe to prevent leaks if UI is destroyed before player
        if (playerStamina != null)
        {
            playerStamina.OnStaminaChanged -= HandleStaminaChanged;
            playerStamina.OnExhaustedStateChanged -= HandleExhaustedChanged;
        }
    }

    private void Update()
    {
        if (currentStaminaBar == null || playerStamina == null) return;

        // Smooth lerp toward target (uses unscaledDeltaTime so bar updates even in slow-mo)
        currentStaminaBar.fillAmount = Mathf.Lerp(
            currentStaminaBar.fillAmount,
            targetFill,
            fillLerpSpeed * Time.unscaledDeltaTime
        );

        // Update color
        currentStaminaBar.color = GetBarColor();
    }

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Call this when the player reference changes (e.g. character select, respawn with new object).
    /// </summary>
    public void SetPlayer(PlayerStamina newStamina)
    {
        // Unsub old
        if (playerStamina != null)
        {
            playerStamina.OnStaminaChanged -= HandleStaminaChanged;
            playerStamina.OnExhaustedStateChanged -= HandleExhaustedChanged;
        }

        playerStamina = newStamina;

        if (playerStamina != null)
        {
            playerStamina.OnStaminaChanged += HandleStaminaChanged;
            playerStamina.OnExhaustedStateChanged += HandleExhaustedChanged;

            if (playerStamina.Data != null)
                lowThreshold = playerStamina.Data.lowStaminaThreshold;

            targetFill = playerStamina.StaminaRatio;

            // Ensure fill image is configured when player is assigned after Start()
            ConfigureFillImage(currentStaminaBar);
            if (currentStaminaBar != null)
                currentStaminaBar.fillAmount = targetFill;
        }
    }

    // ──────────────────────────────────────────────
    //  Event Handlers
    // ──────────────────────────────────────────────

    private void HandleStaminaChanged(float current, float max)
    {
        targetFill = max > 0 ? current / max : 0f;
    }

    private void HandleExhaustedChanged(bool exhausted)
    {
        isExhausted = exhausted;
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private Color GetBarColor()
    {
        if (isExhausted)
            return exhaustedColor;
        if (targetFill <= lowThreshold)
            return lowColor;
        return normalColor;
    }

    /// <summary>
    /// Forces an Image to type Filled + Horizontal (origin Left).
    /// Without this, fillAmount has no visual effect and the bar stays full-size.
    /// </summary>
    private static void ConfigureFillImage(Image img)
    {
        if (img == null) return;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.preserveAspect = false;
    }

}
