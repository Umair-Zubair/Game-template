using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the character selection panel.
/// Saves the selected character index to PlayerPrefs.
/// CharacterManager in the game scene handles spawning the correct prefab.
/// </summary>
public class CharacterSelectPanel : MonoBehaviour
{
    [Header("Character Buttons")]
    [SerializeField] private Button blobMeleeButton;
    [SerializeField] private Button blobRangedButton;
    [SerializeField] private Button backButton;

    [Header("Selection Arrow")]
    [SerializeField] private RectTransform selectionArrow;

    [SerializeField] private float blobMeleeArrowY = 157f;
    [SerializeField] private float blobRangedArrowY = -73f;
    [SerializeField] private float backArrowY = -267f;

    private int currentPosition = 0; // 0=Blob Melee, 1=Blob Ranged, 2=Back
    private const int TOTAL_OPTIONS = 3;

    [Header("Selection Indicators (Optional)")]
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

    private float lastInteractionTime = 0f;
    private const float INTERACTION_COOLDOWN = 0.15f;

    private Vector3 blobMeleeOriginalScale;
    private Vector3 blobRangedOriginalScale;

    private void Start()
    {
        if (blobMeleeButton != null)
            blobMeleeButton.onClick.AddListener(SelectBlobMelee);

        if (blobRangedButton != null)
            blobRangedButton.onClick.AddListener(SelectBlobRanged);

        if (backButton != null)
            backButton.onClick.AddListener(Close);

        if (blobMeleeButton != null) blobMeleeOriginalScale = blobMeleeButton.transform.localScale;
        if (blobRangedButton != null) blobRangedOriginalScale = blobRangedButton.transform.localScale;

        if (blobRangedPreviewImage != null)
            blobRangedPreviewImage.color = blobRangedColor;

        currentPosition = 0;
        UpdateArrowPosition();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            ChangePosition(-1);
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            ChangePosition(1);

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

    // ────────────────────────────────────────────────────────────────────────
    //  Character Selection — just write to PlayerPrefs.
    //  CharacterManager reads this on game scene load and spawns the right prefab.
    // ────────────────────────────────────────────────────────────────────────

    public void SelectBlobMelee()
    {
        PlayerPrefs.SetInt(SELECTED_CHARACTER_KEY, BLOB_MELEE_INDEX);
        PlayerPrefs.Save();

        PlayInteractSound();
        UpdateSelectionVisuals();

        Debug.Log("[CharacterSelect] Blob Melee selected.");
    }

    public void SelectBlobRanged()
    {
        PlayerPrefs.SetInt(SELECTED_CHARACTER_KEY, BLOB_RANGED_INDEX);
        PlayerPrefs.Save();

        PlayInteractSound();
        UpdateSelectionVisuals();

        Debug.Log("[CharacterSelect] Blob Ranged selected.");
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Visuals
    // ────────────────────────────────────────────────────────────────────────

    private void UpdateSelectionVisuals()
    {
        int selected = PlayerPrefs.GetInt(SELECTED_CHARACTER_KEY, -1);

        if (selectionArrow != null)
        {
            Vector2 arrowPos = selectionArrow.anchoredPosition;
            arrowPos.y = selected == BLOB_MELEE_INDEX ? blobMeleeArrowY : blobRangedArrowY;
            selectionArrow.anchoredPosition = arrowPos;
        }

        if (blobMeleeSelectedIndicator != null)
            blobMeleeSelectedIndicator.SetActive(selected == BLOB_MELEE_INDEX);

        if (blobRangedSelectedIndicator != null)
            blobRangedSelectedIndicator.SetActive(selected == BLOB_RANGED_INDEX);

        if (blobRangedPreviewImage != null)
            blobRangedPreviewImage.color = blobRangedColor;

        UpdateButtonVisual(blobMeleeButton, blobMeleeOriginalScale, selected == BLOB_MELEE_INDEX);
        UpdateButtonVisual(blobRangedButton, blobRangedOriginalScale, selected == BLOB_RANGED_INDEX);
    }

    private void UpdateButtonVisual(Button button, Vector3 originalScale, bool isSelected)
    {
        if (button == null) return;
        button.transform.localScale = originalScale * (isSelected ? 1.1f : 1.0f);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Panel Control
    // ────────────────────────────────────────────────────────────────────────

    public void Close()
    {
        if (mainMenuController != null)
            mainMenuController.HideCharacterSelect();
        else
            gameObject.SetActive(false);
    }

    public void ConfirmAndPlay()
    {
        if (!PlayerPrefs.HasKey(SELECTED_CHARACTER_KEY))
        {
            Debug.LogWarning("[CharacterSelect] No character selected!");
            return;
        }

        if (mainMenuController != null)
            mainMenuController.PlayGame();
        else
            Debug.LogError("MainMenuController not assigned in CharacterSelectPanel!");
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Audio
    // ────────────────────────────────────────────────────────────────────────

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
        if (blobMeleeButton != null)
            blobMeleeButton.onClick.RemoveListener(SelectBlobMelee);

        if (blobRangedButton != null)
            blobRangedButton.onClick.RemoveListener(SelectBlobRanged);
    }
}