using UnityEngine;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    [Header ("Game Over")]
    [SerializeField] private GameObject gameOverScreen;
    [SerializeField] private AudioClip gameOverSound;

    [Tooltip("When true the Game Over and Victory screens are suppressed (useful for AI training loops).")]
    public bool disableGameOver;

    [Header("Victory")]
    [SerializeField] private GameObject winScreen;
    [SerializeField] private AudioClip winSound;

    [Header("Pause")]
    [SerializeField] private GameObject pauseScreen;

    private void Awake()
    {
        if (gameOverScreen != null)
            gameOverScreen.SetActive(false);
        if (winScreen != null)
            winScreen.SetActive(false);
        if (pauseScreen != null)
            pauseScreen.SetActive(false);

        EnsureBossHealthBar();
    }
    private bool IsEndScreenActive =>
        (gameOverScreen != null && gameOverScreen.activeInHierarchy) ||
        (winScreen != null && winScreen.activeInHierarchy);

    private void Update()
    {
        if (IsEndScreenActive) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            PauseGame(!pauseScreen.activeInHierarchy);
        }
    }

    #region Game Over
    public void GameOver()
    {
        if (disableGameOver) return;

        if (gameOverScreen != null)
            gameOverScreen.SetActive(true);

        Time.timeScale = 0f;

        if (gameOverSound != null && SoundManager.instance != null)
            SoundManager.instance.PlaySound(gameOverSound);
    }
    #endregion

    #region Victory
    public void Victory()
    {
        if (disableGameOver) return;

        if (winScreen != null)
            winScreen.SetActive(true);

        Time.timeScale = 0f;

        if (winSound != null && SoundManager.instance != null)
            SoundManager.instance.PlaySound(winSound);
    }

    //Restart level
    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // //Main Menu
    // public void MainMenu()
    // {
    //     SceneManager.LoadScene(0);
    // }

    //Quit game/exit play mode if in Editor
    public void Quit()
    {
        Application.Quit(); //Quits the game (only works in build)

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; //Exits play mode (will only be executed in the editor)
#endif
    }
    #endregion

    #region Boss Health Bar
    private void EnsureBossHealthBar()
    {
        if (FindFirstObjectByType<BossHealthBar>() != null) return;

        var go = new GameObject("BossHealthBarHost");
        go.transform.SetParent(transform);
        go.AddComponent<BossHealthBar>();
    }
    #endregion

    #region Pause
    public void PauseGame(bool status)
    {
        //If status == true pause | if status == false unpause
        pauseScreen.SetActive(status);

        //When pause status is true change timescale to 0 (time stops)
        //when it's false change it back to 1 (time goes by normally)
        if (status)
            Time.timeScale = 0;
        else
            Time.timeScale = 1;
    }
    public void SoundVolume()
    {
        SoundManager.instance.ChangeSoundVolume(0.2f);
    }
    public void MusicVolume()
    {
        SoundManager.instance.ChangeMusicVolume(0.2f);
    }
    #endregion
}