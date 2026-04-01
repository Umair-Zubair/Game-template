using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class RestartManager : MonoBehaviour
{
    [SerializeField] private float delay = 2f;

    [Header("Quick Reset (R key)")]
    [SerializeField] private Transform bossSpawnPoint;
    [SerializeField] private Transform playerSpawnPoint;

    private BossController boss;
    private PlayerController player;

    private void Start()
    {
        boss = FindFirstObjectByType<BossController>();
        player = FindFirstObjectByType<PlayerController>();

        if (boss != null)
            boss.OnDied += OnBossDied;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            ResetEncounter();
    }

    /// <summary>
    /// In-place respawn without reloading the scene.
    /// Falls back to each object's current position when no spawn point is assigned.
    /// </summary>
    public void ResetEncounter()
    {
        StopAllCoroutines();

        if (boss != null)
            boss.Respawn(bossSpawnPoint != null ? bossSpawnPoint.position : boss.transform.position);

        if (player != null)
            player.Respawn(playerSpawnPoint != null ? playerSpawnPoint.position : player.transform.position);

        Debug.Log("[RestartManager] Encounter reset via R key.");
    }

    private void OnBossDied()
    {
        StartCoroutine(RestartAfterDelay());
    }

    private IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
