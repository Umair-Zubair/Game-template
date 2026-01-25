using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the character selection panel.
/// Handles Knight/Dragon selection and saves to PlayerPrefs.
/// </summary>
public class CharacterSelectPanel : MonoBehaviour
{
    [Header("Character Buttons")]
    [SerializeField] private Button knightButton;
    [SerializeField] private Button dragonButton;

    [Header("Selection Arrow")]
    [SerializeField] private RectTransform selectionArrow;
    private const float KNIGHT_ARROW_Y = 157f;
    private const float DRAGON_ARROW_Y = 58f;

    [Header("Selection Indicators (Optional)")]
    [Tooltip("Visual indicator that shows which character is selected (e.g., a border or highlight)")]
    [SerializeField] private GameObject knightSelectedIndicator;
    [SerializeField] private GameObject dragonSelectedIndicator;

    [Header("Character Preview Images (Optional)")]
    [SerializeField] private Image knightPreviewImage;
    [SerializeField] private Image dragonPreviewImage;

    [Header("Audio")]
    [SerializeField] private AudioClip selectSound;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";
    private const int DRAGON_INDEX = 0;
    private const int KNIGHT_INDEX = 1;

    private void Start()
    {
        // Wire up button listeners
        if (knightButton != null)
            knightButton.onClick.AddListener(SelectKnight);

        if (dragonButton != null)
            dragonButton.onClick.AddListener(SelectDragon);

        // Show current selection if any
        UpdateSelectionVisuals();
    }

    /// <summary>
    /// Selects the Knight character.
    /// </summary>
    public void SelectKnight()
    {
        PlayerPrefs.SetInt(SELECTED_CHARACTER_KEY, KNIGHT_INDEX);
        PlayerPrefs.Save();

        PlaySelectSound();
        UpdateSelectionVisuals();

        Debug.Log("Knight selected!");
    }

    /// <summary>
    /// Selects the Dragon character.
    /// </summary>
    public void SelectDragon()
    {
        PlayerPrefs.SetInt(SELECTED_CHARACTER_KEY, DRAGON_INDEX);
        PlayerPrefs.Save();

        PlaySelectSound();
        UpdateSelectionVisuals();

        Debug.Log("Dragon selected!");
    }

    /// <summary>
    /// Updates the visual indicators to show which character is selected.
    /// </summary>
    private void UpdateSelectionVisuals()
    {
        int selected = PlayerPrefs.GetInt(SELECTED_CHARACTER_KEY, -1);

        // Move the selection arrow
        if (selectionArrow != null)
        {
            Vector3 arrowPos = selectionArrow.anchoredPosition;
            if (selected == KNIGHT_INDEX)
                arrowPos.y = KNIGHT_ARROW_Y;
            else if (selected == DRAGON_INDEX)
                arrowPos.y = DRAGON_ARROW_Y;
            selectionArrow.anchoredPosition = arrowPos;
        }

        // Update Knight indicator
        if (knightSelectedIndicator != null)
            knightSelectedIndicator.SetActive(selected == KNIGHT_INDEX);

        // Update Dragon indicator
        if (dragonSelectedIndicator != null)
            dragonSelectedIndicator.SetActive(selected == DRAGON_INDEX);

        // Optional: Adjust button colors or scale for better feedback
        UpdateButtonVisual(knightButton, selected == KNIGHT_INDEX);
        UpdateButtonVisual(dragonButton, selected == DRAGON_INDEX);
    }

    private void UpdateButtonVisual(Button button, bool isSelected)
    {
        if (button == null) return;

        // Scale selected button slightly larger
        float scale = isSelected ? 1.1f : 1.0f;
        button.transform.localScale = Vector3.one * scale;

        // You can also change colors here if desired
        // ColorBlock colors = button.colors;
        // colors.normalColor = isSelected ? Color.yellow : Color.white;
        // button.colors = colors;
    }

    /// <summary>
    /// Closes the character selection panel.
    /// </summary>
    public void Close()
    {
        if (mainMenuController != null)
            mainMenuController.HideCharacterSelect();
        else
            gameObject.SetActive(false);
    }

    /// <summary>
    /// Confirms selection and starts the game.
    /// </summary>
    public void ConfirmAndPlay()
    {
        if (!PlayerPrefs.HasKey(SELECTED_CHARACTER_KEY))
        {
            Debug.LogWarning("Please select a character first!");
            return;
        }

        if (mainMenuController != null)
        {
            Debug.Log("Calling MainMenuController.PlayGame()...");
            mainMenuController.PlayGame();
        }
        else
        {
            Debug.LogError("MainMenuController is NOT assigned in CharacterSelectPanel! Please assign it in the Inspector.");
        }
    }

    private void PlaySelectSound()
    {
        if (selectSound != null && SoundManager.instance != null)
            SoundManager.instance.PlaySound(selectSound);
    }

    private void OnDestroy()
    {
        // Clean up listeners
        if (knightButton != null)
            knightButton.onClick.RemoveListener(SelectKnight);

        if (dragonButton != null)
            dragonButton.onClick.RemoveListener(SelectDragon);
    }
}
