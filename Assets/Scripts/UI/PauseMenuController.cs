using UnityEngine;

/// <summary>
/// Controls the Pause Menu in Level1.
/// Handles Resume, Volume, Music, and Quit.
/// Attach this to a GameObject in your pause menu canvas.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject pauseMenuPanel;

    [Header("Audio")]
    [SerializeField] private AudioClip buttonClickSound;

    // Prevents crashes from rapid input
    private float lastInteractionTime = 0f;
    private const float INTERACTION_COOLDOWN = 0.1f;

    /// <summary>
    /// Returns true if interaction is allowed (cooldown passed)
    /// Uses unscaled time so it works even while paused (timeScale = 0)
    /// </summary>
    private bool CanInteract()
    {
        if (Time.unscaledTime - lastInteractionTime < INTERACTION_COOLDOWN) return false;
        lastInteractionTime = Time.unscaledTime;
        return true;
    }

    /// <summary>
    /// Resumes the game — equivalent to pressing ESC while paused.
    /// Restores timeScale and hides the pause menu panel.
    /// </summary>
    public void ResumeGame()
    {
        if (!CanInteract()) return;

        PlayButtonSound();

        Time.timeScale = 1f;

        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);
    }

    public void SoundVolume()
    {
        if (!CanInteract()) return;

        PlayButtonSound();
        if (SoundManager.instance != null)
            SoundManager.instance.ChangeSoundVolume(0.2f);
    }

    public void MusicVolume()
    {
        if (!CanInteract()) return;

        PlayButtonSound();
        if (SoundManager.instance != null)
            SoundManager.instance.ChangeMusicVolume(0.2f);
    }

    public void QuitGame()
    {
        if (!CanInteract()) return;

        PlayButtonSound();

        // Restore timeScale before quitting
        Time.timeScale = 1f;

        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void PlayButtonSound()
    {
        if (buttonClickSound != null && SoundManager.instance != null)
            SoundManager.instance.PlaySound(buttonClickSound);
    }
}
