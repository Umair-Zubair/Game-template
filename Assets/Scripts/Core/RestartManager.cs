using UnityEngine;
using System.Collections;

/// <summary>
/// Single coordinator for all encounter resets.
/// ML-aware: when MLBrain is active, deaths route through EndEpisode → OnEpisodeBegin
/// so the RL loop stays consistent. Without ML, resets happen after a delay.
/// </summary>
public class RestartManager : MonoBehaviour
{
    [SerializeField] private float deathResetDelay = 2f;

    [Header("Spawn Points")]
    [SerializeField] private Transform bossSpawnPoint;
    [SerializeField] private Transform playerSpawnPoint;

    private BossController boss;
    private PlayerController player;
    private Health playerHealth;
    private MLBrain mlBrain;

    private bool IsMLActive => mlBrain != null && mlBrain.isActiveAndEnabled;

    private void Start()
    {
        boss   = FindFirstObjectByType<BossController>();
        player = FindFirstObjectByType<PlayerController>();
        mlBrain = boss != null ? boss.GetComponent<MLBrain>() : null;

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerHealth = playerObj.GetComponent<Health>();

        if (boss != null) boss.OnDied += OnBossDied;
        if (playerHealth != null) playerHealth.OnDamageTaken += OnPlayerDamaged;
    }

    private void OnDestroy()
    {
        if (boss != null) boss.OnDied -= OnBossDied;
        if (playerHealth != null) playerHealth.OnDamageTaken -= OnPlayerDamaged;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.R)) return;
        StopAllCoroutines();
        if (IsMLActive) mlBrain.ForceEndEpisode();
        else ResetEncounter();
    }

    public void ResetEncounter()
    {
        StopAllCoroutines();
        CleanupProjectiles();

        if (boss != null)
            boss.Respawn(bossSpawnPoint != null ? bossSpawnPoint.position : boss.transform.position);
        if (player != null)
            player.Respawn(playerSpawnPoint != null ? playerSpawnPoint.position : player.transform.position);

        Debug.Log("[RestartManager] Encounter reset.");
    }

    private void OnBossDied()
    {
        if (IsMLActive) mlBrain.OnFightLost();
        else StartCoroutine(DelayedReset());
    }

    private void OnPlayerDamaged(float damage)
    {
        if (playerHealth == null || playerHealth.currentHealth > 0f) return;
        if (IsMLActive) mlBrain.OnFightWon();
        else StartCoroutine(DelayedReset());
    }

    private IEnumerator DelayedReset()
    {
        yield return new WaitForSeconds(deathResetDelay);
        ResetEncounter();
    }

    private static void CleanupProjectiles()
    {
        foreach (var p in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            p.gameObject.SetActive(false);
    }
}
