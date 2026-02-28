using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Controls the Game Over screen.
/// Handles Restart (reload Level1) and Quit.
/// </summary>
public class GameOverController : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip buttonClickSound;

    private float lastInteractionTime = 0f;
    private const float INTERACTION_COOLDOWN = 0.1f;

    private bool CanInteract()
    {
        if (Time.unscaledTime - lastInteractionTime < INTERACTION_COOLDOWN) return false;
        lastInteractionTime = Time.unscaledTime;
        return true;
    }

    public void RestartGame()
    {
        if (!CanInteract()) return;

        PlayButtonSound();
        Time.timeScale = 1f;
        SceneManager.LoadScene("Level1");
    }

    public void QuitGame()
    {
        if (!CanInteract()) return;

        PlayButtonSound();
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
