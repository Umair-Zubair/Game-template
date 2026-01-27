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
    [SerializeField] private SelectionArrow mainMenuArrow;

    [Header("Audio")]
    [SerializeField] private AudioClip buttonClickSound;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";
    
    // Prevents crashes from rapid input
    private bool isLoading = false;
    private float lastInteractionTime = 0f;
    private const float INTERACTION_COOLDOWN = 0.1f;

    private void Start()
    {
        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);

        Time.timeScale = 1f;
        isLoading = false;
    }

    /// <summary>
    /// Returns true if interaction is allowed (cooldown passed, not loading)
    /// </summary>
    private bool CanInteract()
    {
        if (isLoading) return false;
        if (Time.unscaledTime - lastInteractionTime < INTERACTION_COOLDOWN) return false;
        lastInteractionTime = Time.unscaledTime;
        return true;
    }

    public void PlayGame()
    {
        if (!CanInteract()) return;
        
        Debug.Log("PlayGame called. Attempting to load Level1...");
        PlayButtonSound();

        if (!PlayerPrefs.HasKey(SELECTED_CHARACTER_KEY))
        {
            Debug.Log("No character selected! Please select a character first.");
            ShowCharacterSelect();
            return;
        }

        // Prevent double-loading
        isLoading = true;
        SceneManager.LoadScene("Level1");
    }

    public void ShowCharacterSelect()
    {
        if (!CanInteract()) return;
        
        PlayButtonSound();

        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(true);
        
        if (mainMenuArrow != null)
            mainMenuArrow.enabled = false;
    }

    public void HideCharacterSelect()
    {
        if (!CanInteract()) return;
        
        PlayButtonSound();

        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);
        
        if (mainMenuArrow != null)
            mainMenuArrow.enabled = true;
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
        isLoading = true; // Prevent further interactions

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
