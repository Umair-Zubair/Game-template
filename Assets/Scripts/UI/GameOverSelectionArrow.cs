using UnityEngine;

/// <summary>
/// Game Over screen selection arrow controller.
/// Handles keyboard navigation for 2 options:
///   0 = Restart
///   1 = Quit
/// </summary>
public class GameOverSelectionArrow : MonoBehaviour
{
    [Header("Arrow Positions (X, Y)")]
    [SerializeField] private Vector2 restartArrowPos = new Vector2(-244f,   20f);
    [SerializeField] private Vector2 quitArrowPos    = new Vector2(-159f, -111f);

    [Header("Audio")]
    [SerializeField] private AudioClip changeSound;
    [SerializeField] private AudioClip interactSound;

    [Header("References")]
    [SerializeField] private GameOverController gameOverController;

    private RectTransform arrow;
    private int currentPosition = 0;
    private const int TOTAL_OPTIONS = 2;

    private const int RESTART = 0;
    private const int QUIT    = 1;

    private void Awake()
    {
        arrow = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        currentPosition = RESTART;
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
        if (arrow == null) return;

        arrow.anchoredPosition = currentPosition switch
        {
            RESTART => restartArrowPos,
            QUIT    => quitArrowPos,
            _       => restartArrowPos
        };
    }

    private void Interact()
    {
        if (gameOverController == null)
        {
            Debug.LogError("GameOverController is NOT assigned in GameOverSelectionArrow!");
            return;
        }

        PlayInteractSound();

        switch (currentPosition)
        {
            case RESTART:
                gameOverController.RestartGame();
                break;
            case QUIT:
                gameOverController.QuitGame();
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
