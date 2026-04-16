using UnityEngine;
using UnityEngine.UI;

public class Healthbar : MonoBehaviour
{
    [SerializeField] private Health playerHealth;

    [Tooltip("Background image — always shows full bar. Image Type = Simple.")]
    [SerializeField] private Image totalhealthBar;

    [Tooltip("Foreground fill image — shrinks with health. Image Type = Filled.")]
    [SerializeField] private Image currenthealthBar;

    [Tooltip("Speed at which the bar lerps to the real value. Higher = snappier.")]
    [SerializeField] private float fillLerpSpeed = 8f;

    private float targetFill = 1f;

    public void SetPlayer(Health newPlayer)
    {
        playerHealth = newPlayer;
        if (playerHealth != null)
        {
            targetFill = playerHealth.currentHealth / playerHealth.MaxHealth;
            ConfigureFillImage(currenthealthBar);
            if (currenthealthBar != null)
                currenthealthBar.fillAmount = targetFill;
        }
    }

    private void Start()
    {
        // Ensure the foreground image is always Filled + Horizontal so fillAmount works.
        ConfigureFillImage(currenthealthBar);

        // Total/background bar stays full at all times.
        if (totalhealthBar != null)
            totalhealthBar.fillAmount = 1f;

        if (playerHealth == null)
            playerHealth = FindFirstObjectByType<Health>();

        if (playerHealth != null)
        {
            targetFill = playerHealth.currentHealth / playerHealth.MaxHealth;
            if (currenthealthBar != null)
                currenthealthBar.fillAmount = targetFill;
        }
    }

    private void Update()
    {
        if (playerHealth == null || currenthealthBar == null) return;

        targetFill = playerHealth.currentHealth / playerHealth.MaxHealth;

        // Smooth lerp toward target (same pattern as StaminaBar)
        currenthealthBar.fillAmount = Mathf.Lerp(
            currenthealthBar.fillAmount,
            targetFill,
            fillLerpSpeed * Time.unscaledDeltaTime
        );
    }

    private static void ConfigureFillImage(Image img)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.preserveAspect = false;
    }
}
