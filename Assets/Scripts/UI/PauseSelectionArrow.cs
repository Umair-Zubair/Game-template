using UnityEngine;

/// <summary>
/// Pause menu selection arrow controller.
/// Handles keyboard navigation for 4 pause menu options:
///   0 = Resume
///   1 = Volume
///   2 = Music
///   3 = Quit
/// </summary>
public class PauseSelectionArrow : MonoBehaviour
{
    [Header("Arrow Positions (X, Y)")]
    [SerializeField] private Vector2 resumeArrowPos = new Vector2(-293f,   99f);
    [SerializeField] private Vector2 volumeArrowPos = new Vector2(-359f,  -31f);
    [SerializeField] private Vector2 musicArrowPos  = new Vector2(-312f, -151f);
    [SerializeField] private Vector2 quitArrowPos   = new Vector2(-163f, -287f);

    [Header("Audio")]
    [SerializeField] private AudioClip changeSound;
    [SerializeField] private AudioClip interactSound;

    [Header("References")]
    [SerializeField] private PauseMenuController pauseMenuController;

    private RectTransform arrow;
    private int currentPosition = 0;
    private const int TOTAL_OPTIONS = 4;

    // Option indices
    private const int RESUME = 0;
    private const int VOLUME = 1;
    private const int MUSIC  = 2;
    private const int QUIT   = 3;

    private void Awake()
    {
        arrow = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        // Always start at RESUME when the pause menu opens
        currentPosition = RESUME;
        UpdateArrowPosition();
    }

    private void Update()
    {
        // Navigate Up / Down
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
            RESUME => resumeArrowPos,
            VOLUME => volumeArrowPos,
            MUSIC  => musicArrowPos,
            QUIT   => quitArrowPos,
            _      => resumeArrowPos
        };

        arrow.anchoredPosition = targetPos;
    }

    private void Interact()
    {
        if (pauseMenuController == null)
        {
            Debug.LogError("PauseMenuController is NOT assigned in PauseSelectionArrow!");
            return;
        }

        PlayInteractSound();

        switch (currentPosition)
        {
            case RESUME:
                pauseMenuController.ResumeGame();
                break;
            case VOLUME:
                pauseMenuController.SoundVolume();
                break;
            case MUSIC:
                pauseMenuController.MusicVolume();
                break;
            case QUIT:
                pauseMenuController.QuitGame();
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
