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
    [SerializeField] private Button newDragonButton;
    [SerializeField] private Button backButton;

    [Header("Selection Arrow")]
    [SerializeField] private RectTransform selectionArrow;
    
    // Arrow Y positions for each option: 0=Knight, 1=Dragon, 2=Back
    [SerializeField] private float knightArrowY = 157f;
    [SerializeField] private float dragonArrowY = -73f;
    [SerializeField] private float backArrowY = -267f;
    
    private int currentPosition = 0; // 0=Knight, 1=Dragon, 2=Back
    private const int TOTAL_OPTIONS = 3;

    [Header("Selection Indicators (Optional)")]
    [Tooltip("Visual indicator that shows which character is selected (e.g., a border or highlight)")]
    [SerializeField] private GameObject knightSelectedIndicator;
    [SerializeField] private GameObject dragonSelectedIndicator;

    [Header("Character Preview Images (Optional)")]
    [SerializeField] private Image knightPreviewImage;
    [SerializeField] private Image dragonPreviewImage;

    [Header("Audio")]
    [SerializeField] private AudioClip changeSound;
    [SerializeField] private AudioClip interactSound;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";
    private const int DRAGON_INDEX = 0;
    private const int KNIGHT_INDEX = 1;
    
    // Cooldown protection
    private float lastInteractionTime = 0f;
    private const float INTERACTION_COOLDOWN = 0.15f;
    
    // Original scales for proper scaling
    private Vector3 knightOriginalScale;
    private Vector3 dragonOriginalScale;
    private Vector3 newDragonOriginalScale;

    private void Start()
    {
        // Wire up button listeners
        if (knightButton != null)
            knightButton.onClick.AddListener(SelectKnight);

        if (dragonButton != null)
            dragonButton.onClick.AddListener(SelectDragon);
        
        if (backButton != null)
            backButton.onClick.AddListener(Close);

        // Store original scales
        if (knightButton != null) knightOriginalScale = knightButton.transform.localScale;
        if (dragonButton != null) dragonOriginalScale = dragonButton.transform.localScale;
        if (newDragonButton != null) newDragonOriginalScale = newDragonButton.transform.localScale;

        // Show current selection and update arrow
        currentPosition = 0; // Start on Knight
        UpdateArrowPosition();
    }

    private void Update()
    {
        // Handle Up/Down navigation
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            ChangePosition(-1);
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            ChangePosition(1);

        // Handle Enter/E to select current option
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.E))
            Interact();
    }
    
    private bool CanInteract()
    {
        if (Time.unscaledTime - lastInteractionTime < INTERACTION_COOLDOWN) return false;
        lastInteractionTime = Time.unscaledTime;
        return true;
    }

    private void ChangePosition(int change)
    {
        currentPosition += change;
        
        // Wrap around
        if (currentPosition < 0)
            currentPosition = TOTAL_OPTIONS - 1;
        else if (currentPosition >= TOTAL_OPTIONS)
            currentPosition = 0;
        
        PlayChangeSound();
        UpdateArrowPosition();
    }

    private void UpdateArrowPosition()
    {
        if (selectionArrow == null) return;
        
        Vector2 arrowPos = selectionArrow.anchoredPosition;
        switch (currentPosition)
        {
            case 0: arrowPos.y = knightArrowY; break;
            case 1: arrowPos.y = dragonArrowY; break;
            case 2: arrowPos.y = backArrowY; break;
        }
        selectionArrow.anchoredPosition = arrowPos;
    }

    private void Interact()
    {
        if (!CanInteract()) return;
        
        PlayInteractSound();
        
        switch (currentPosition)
        {
            case 0: SelectKnight(); break;
            case 1: SelectDragon(); break;
            case 2: Close(); break;
        }
    }


    /// <summary>
    /// Selects the Knight character.
    /// </summary>
    public void SelectKnight()
    {
        PlayerPrefs.SetInt(SELECTED_CHARACTER_KEY, KNIGHT_INDEX);
        PlayerPrefs.Save();

        PlayInteractSound();
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

        PlayInteractSound();
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
                arrowPos.y = knightArrowY;
            else if (selected == DRAGON_INDEX)
                arrowPos.y = dragonArrowY;
            selectionArrow.anchoredPosition = arrowPos;
        }

        // Update Knight indicator
        if (knightSelectedIndicator != null)
            knightSelectedIndicator.SetActive(selected == KNIGHT_INDEX);

        // Update Dragon indicator
        if (dragonSelectedIndicator != null)
            dragonSelectedIndicator.SetActive(selected == DRAGON_INDEX);

        // Scale buttons - pass their original scales
        UpdateButtonVisual(knightButton, knightOriginalScale, selected == KNIGHT_INDEX);
        UpdateButtonVisual(dragonButton, dragonOriginalScale, selected == DRAGON_INDEX);
        UpdateButtonVisual(newDragonButton, newDragonOriginalScale, selected == DRAGON_INDEX);
    }

    private void UpdateButtonVisual(Button button, Vector3 originalScale, bool isSelected)
    {
        if (button == null) return;

        // Scale up 10% when selected, restore to original when not
        float multiplier = isSelected ? 1.1f : 1.0f;
        button.transform.localScale = originalScale * multiplier;
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

    private void PlayChangeSound()
    {
        if (changeSound != null && SoundManager.instance != null)
            SoundManager.instance.PlaySound(changeSound);
    }

    private void PlayInteractSound()
    {
        if (interactSound != null && SoundManager.instance != null)
            SoundManager.instance.PlaySound(interactSound);
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
