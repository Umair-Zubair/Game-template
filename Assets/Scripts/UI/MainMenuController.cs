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
    [SerializeField] private GameObject optionsButton;  // The actual Options button itself
    [SerializeField] private GameObject menuButtonsContainer; // The parent of Play, Options, Quit
    [SerializeField] private GameObject playButton;
    [SerializeField] private GameObject quitButton;
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
        if (characterSelectPanel != null) characterSelectPanel.SetActive(false);
        
        // Hide all children of the options button initially
        SetOptionsChildrenActive(false);
        
        // Force the main buttons to be ON
        if (optionsButton != null)
            optionsButton.SetActive(true);

        if (menuButtonsContainer != null)
            menuButtonsContainer.SetActive(true);
        
        if (playButton != null)
            playButton.SetActive(true);
        
        if (quitButton != null)
            quitButton.SetActive(true);

        if (mainMenuArrow != null)
            mainMenuArrow.enabled = true;

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
        
        if (menuButtonsContainer != null)
            menuButtonsContainer.SetActive(false);
        
        if (mainMenuArrow != null)
            mainMenuArrow.enabled = false;
    }

    public void HideCharacterSelect()
    {
        if (!CanInteract()) return;
        
        PlayButtonSound();

        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);
        
        if (menuButtonsContainer != null)
            menuButtonsContainer.SetActive(true);
        
        if (mainMenuArrow != null)
            mainMenuArrow.enabled = true;
    }

    public void ShowOptions()
    {
        if (!CanInteract()) return;
        
        PlayButtonSound();

        // 1. Show the sub-buttons (Volume, Music, Back)
        // 2. Hide the "Options" word/background
        ToggleOptionsUI(showSubOptions: true);
        
        if (playButton != null) playButton.SetActive(false);
        if (quitButton != null) quitButton.SetActive(false);
        if (mainMenuArrow != null) mainMenuArrow.enabled = false;
    }

    public void HideOptions()
    {
        if (!CanInteract()) return;
        
        PlayButtonSound();

        // 1. Hide the sub-buttons
        // 2. Show the "Options" word/background again
        ToggleOptionsUI(showSubOptions: false);
        
        if (playButton != null) playButton.SetActive(true);
        if (quitButton != null) quitButton.SetActive(true);
        if (mainMenuArrow != null) mainMenuArrow.enabled = true;
    }

    /// <summary>
    /// Toggles between showing the "Options" word/background 
    /// OR showing the inner settings (Volume, Music, Back).
    /// </summary>
    private void ToggleOptionsUI(bool showSubOptions)
    {
        if (optionsButton == null) return;

        // 1. Target the "Options" word specifically (on the button or child)
        // Disable the Text/TMP components on the OptionsButton itself
        var mainText = optionsButton.GetComponent<UnityEngine.UI.Text>();
        if (mainText != null) mainText.enabled = !showSubOptions;
        
        var mainTMP = optionsButton.GetComponent<TMPro.TMP_Text>();
        if (mainTMP != null) mainTMP.enabled = !showSubOptions;

        // 2. Parent Button Background Image
        if (optionsButton.TryGetComponent<UnityEngine.UI.Image>(out var img))
            img.enabled = !showSubOptions;

        // 3. Iterate Children
        foreach (Transform child in optionsButton.transform)
        {
            // If it's a Sub-Button (Volume, Music, Back), toggle the whole object
            bool isSubButton = child.GetComponent<UnityEngine.UI.Selectable>() != null;

            if (isSubButton)
            {
                child.gameObject.SetActive(showSubOptions);
            }
            else
            {
                // If it's a Label child (not a button), hide it when options are open
                child.gameObject.SetActive(!showSubOptions);
            }
        }
    }

    private void SetOptionsChildrenActive(bool active)
    {
        ToggleOptionsUI(showSubOptions: active);
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
