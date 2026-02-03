using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the character selection panel.
/// Handles Knight/Dragon selection and saves to PlayerPrefs.
/// </summary>
public class CharacterSelectPanel : MonoBehaviour
{
    [Header("Character Buttons")]
    [SerializeField] private Button blobMeleeButton;
    [SerializeField] private Button blobRangedButton;
    [SerializeField] private Button newDragonButton; // Keeping this for now as it seems to be an extra button mentioned in comments
    [SerializeField] private Button backButton;

    [Header("Selection Arrow")]
    [SerializeField] private RectTransform selectionArrow;
    
    // Arrow Y positions for each option: 0=Blob Melee, 1=Blob Ranged, 2=Back
    [SerializeField] private float blobMeleeArrowY = 157f;
    [SerializeField] private float blobRangedArrowY = -73f;
    [SerializeField] private float backArrowY = -267f;
    
    private int currentPosition = 0; // 0=Blob Melee, 1=Blob Ranged, 2=Back
    private const int TOTAL_OPTIONS = 3;

    [Header("Selection Indicators (Optional)")]
    [Tooltip("Visual indicator that shows which character is selected (e.g., a border or highlight)")]
    [SerializeField] private GameObject blobMeleeSelectedIndicator;
    [SerializeField] private GameObject blobRangedSelectedIndicator;

    [Header("Character Preview Images (Optional)")]
    [SerializeField] private Image blobMeleePreviewImage;
    [SerializeField] private Image blobRangedPreviewImage;
    [SerializeField] private Color blobRangedColor = Color.gray;

    [Header("Audio")]
    [SerializeField] private AudioClip changeSound;
    [SerializeField] private AudioClip interactSound;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";
    private const int BLOB_RANGED_INDEX = 0;
    private const int BLOB_MELEE_INDEX = 1;
    
    // Cooldown protection
    private float lastInteractionTime = 0f;
    private const float INTERACTION_COOLDOWN = 0.15f;
    
    // Original scales for proper scaling
    private Vector3 blobMeleeOriginalScale;
    private Vector3 blobRangedOriginalScale;
    private Vector3 newDragonOriginalScale;

    private void Start()
    {
        // Wire up button listeners
        if (blobMeleeButton != null)
            blobMeleeButton.onClick.AddListener(SelectBlobMelee);

        if (blobRangedButton != null)
            blobRangedButton.onClick.AddListener(SelectBlobRanged);
        
        if (backButton != null)
            backButton.onClick.AddListener(Close);

        // Store original scales
        if (blobMeleeButton != null) blobMeleeOriginalScale = blobMeleeButton.transform.localScale;
        if (blobRangedButton != null) blobRangedOriginalScale = blobRangedButton.transform.localScale;
        if (newDragonButton != null) newDragonOriginalScale = newDragonButton.transform.localScale;

        // Apply color to Ranged Blob preview
        if (blobRangedPreviewImage != null)
            blobRangedPreviewImage.color = blobRangedColor;

        // Show current selection and update arrow
        currentPosition = 0; // Start on Blob Melee
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
            case 0: arrowPos.y = blobMeleeArrowY; break;
            case 1: arrowPos.y = blobRangedArrowY; break;
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
            case 0: SelectBlobMelee(); break;
            case 1: SelectBlobRanged(); break;
            case 2: Close(); break;
        }
    }


    /// <summary>
    /// Selects the Blob Melee character.
    /// </summary>
    public void SelectBlobMelee()
    {
        PlayerPrefs.SetInt(SELECTED_CHARACTER_KEY, BLOB_MELEE_INDEX);
        PlayerPrefs.Save();

        PlayInteractSound();
        UpdateSelectionVisuals();

        Debug.Log("Blob Melee selected!");
    }

    /// <summary>
    /// Selects the Blob Ranged character.
    /// </summary>
    public void SelectBlobRanged()
    {
        PlayerPrefs.SetInt(SELECTED_CHARACTER_KEY, BLOB_RANGED_INDEX);
        PlayerPrefs.Save();

        PlayInteractSound();
        UpdateSelectionVisuals();

        Debug.Log("Blob Ranged selected!");
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
            if (selected == BLOB_MELEE_INDEX)
                arrowPos.y = blobMeleeArrowY;
            else if (selected == BLOB_RANGED_INDEX)
                arrowPos.y = blobRangedArrowY;
            selectionArrow.anchoredPosition = arrowPos;
        }

        // Update indicators
        if (blobMeleeSelectedIndicator != null)
            blobMeleeSelectedIndicator.SetActive(selected == BLOB_MELEE_INDEX);

        if (blobRangedSelectedIndicator != null)
            blobRangedSelectedIndicator.SetActive(selected == BLOB_RANGED_INDEX);

        // Update colors
        if (blobRangedPreviewImage != null)
            blobRangedPreviewImage.color = blobRangedColor;

        // Scale buttons - pass their original scales
        UpdateButtonVisual(blobMeleeButton, blobMeleeOriginalScale, selected == BLOB_MELEE_INDEX);
        UpdateButtonVisual(blobRangedButton, blobRangedOriginalScale, selected == BLOB_RANGED_INDEX);
        UpdateButtonVisual(newDragonButton, newDragonOriginalScale, selected == BLOB_RANGED_INDEX);
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
        if (blobMeleeButton != null)
            blobMeleeButton.onClick.RemoveListener(SelectBlobMelee);

        if (blobRangedButton != null)
            blobRangedButton.onClick.RemoveListener(SelectBlobRanged);
    }
}
