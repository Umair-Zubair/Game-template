using UnityEngine;

/// <summary>
/// Main menu selection arrow controller.
/// Handles keyboard navigation for 5 menu options: Play, Character, Volume, Music, Quit
/// </summary>
public class SelectionArrow : MonoBehaviour
{
    [Header("Arrow Positions (X, Y)")]
    [SerializeField] private Vector2 playArrowPos = new Vector2(-379f, 264f);
    [SerializeField] private Vector2 characterArrowPos = new Vector2(-436f, 93f);
    [SerializeField] private Vector2 volumeArrowPos = new Vector2(-328f, -31f);
    [SerializeField] private Vector2 musicArrowPos = new Vector2(-328f, -151f);
    [SerializeField] private Vector2 quitArrowPos = new Vector2(-188f, -287f);

    [Header("Audio")]
    [SerializeField] private AudioClip changeSound;
    [SerializeField] private AudioClip interactSound;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private RectTransform arrow;
    private int currentPosition = 0;
    private const int TOTAL_OPTIONS = 5; // Play, Character, Volume, Music, Quit

    // Menu option indices
    private const int PLAY = 0;
    private const int CHARACTER = 1;
    private const int VOLUME = 2;
    private const int MUSIC = 3;
    private const int QUIT = 4;

    private void Awake()
    {
        arrow = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        currentPosition = 0;
        UpdateArrowPosition();
    }

    private void Update()
    {
        // Navigate Up/Down
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            ChangePosition(-1);
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            ChangePosition(1);

        // Interact with current option
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.E))
            Interact();
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
        if (arrow == null) return;

        Vector2 targetPos = currentPosition switch
        {
            PLAY => playArrowPos,
            CHARACTER => characterArrowPos,
            VOLUME => volumeArrowPos,
            MUSIC => musicArrowPos,
            QUIT => quitArrowPos,
            _ => playArrowPos
        };

        arrow.anchoredPosition = targetPos;
    }

    private void Interact()
    {
        PlayInteractSound();

        if (mainMenuController == null)
        {
            Debug.LogError("MainMenuController is NOT assigned in SelectionArrow!");
            return;
        }

        switch (currentPosition)
        {
            case PLAY:
                mainMenuController.PlayGame();
                break;
            case CHARACTER:
                mainMenuController.ShowCharacterSelect();
                break;
            case VOLUME:
                mainMenuController.SoundVolume();
                break;
            case MUSIC:
                mainMenuController.MusicVolume();
                break;
            case QUIT:
                mainMenuController.QuitGame();
                break;
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
}