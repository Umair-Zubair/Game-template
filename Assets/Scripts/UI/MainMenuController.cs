using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Controls the Main Menu scene.
/// Handles Play, Quit, and shows character selection panel.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject characterSelectPanel;
    [SerializeField] private MenuManager menuManager; // Reference to disable when popup opens

    [Header("Audio")]
    [SerializeField] private AudioClip buttonClickSound;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";

    private void Start()
    {
        // Hide character selection panel on start
        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);

        // Ensure time is running (in case we came from a paused game)
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Called when Play button is clicked.
    /// Checks if character is selected, then loads Level1.
    /// </summary>
    public void PlayGame()
    {
        Debug.Log("PlayGame called. Attempting to load Level1...");
        PlayButtonSound();

        // Check if a character has been selected
        if (!PlayerPrefs.HasKey(SELECTED_CHARACTER_KEY))
        {
            // No character selected - show selection panel
            Debug.Log("No character selected! Please select a character first.");
            ShowCharacterSelect();
            return;
        }

        // Load the first level
        SceneManager.LoadScene("Level1");
    }

    /// <summary>
    /// Shows the character selection panel.
    /// </summary>
    public void ShowCharacterSelect()
    {
        PlayButtonSound();

        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(true);
        
        // Disable main menu navigation
        if (menuManager != null)
            menuManager.enabled = false;
    }

    /// <summary>
    /// Hides the character selection panel.
    /// </summary>
    public void HideCharacterSelect()
    {
        PlayButtonSound();

        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);
        
        // Re-enable main menu navigation
        if (menuManager != null)
            menuManager.enabled = true;
    }

    /// <summary>
    /// Adjusts sound volume (delegates to SoundManager).
    /// </summary>
    public void SoundVolume()
    {
        PlayButtonSound();
        SoundManager.instance.ChangeSoundVolume(0.2f);
    }

    /// <summary>
    /// Adjusts music volume (delegates to SoundManager).
    /// </summary>
    public void MusicVolume()
    {
        PlayButtonSound();
        SoundManager.instance.ChangeMusicVolume(0.2f);
    }

    /// <summary>
    /// Quits the application.
    /// </summary>
    public void QuitGame()
    {
        PlayButtonSound();

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
