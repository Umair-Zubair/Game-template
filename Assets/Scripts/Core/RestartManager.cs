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

    private bool IsMLActive => mlBrain != null && mlBrain.isActiveAndEnabled && mlBrain.IsModelLoaded;

    private void Awake()
    {
        // Find the boss in Awake — it is always a scene object so it exists at Awake time.
        boss = FindFirstObjectByType<BossController>();
        if (boss == null)
            Debug.LogWarning("[RestartManager] Could not find a BossController in the scene! Reset will not work.");

        mlBrain = boss != null ? boss.GetComponent<MLBrain>() : null;
    }

    private void Start()
    {
        // Subscribe to boss death now that both Awake() passes are done.
        if (boss != null)
            boss.OnDied += OnBossDied;
        else
            Debug.LogWarning("[RestartManager] boss is null in Start() — OnBossDied will never fire.");

        // Player is spawned dynamically by CharacterManager — try to find it now and
        // fall back to a retry coroutine if CharacterManager.Start() hasn't run yet.
        TrySubscribePlayer();
        if (playerHealth == null)
            StartCoroutine(RetryFindPlayer());
    }

    /// <summary>
    /// Called by CharacterManager immediately after it instantiates the player prefab
    /// so we always get the reference regardless of Start() ordering.
    /// </summary>
    public void InitPlayer(PlayerController spawnedPlayer)
    {
        // Unsubscribe from any old reference first.
        if (playerHealth != null)
            playerHealth.OnDamageTaken -= OnPlayerDamaged;

        player = spawnedPlayer;
        playerHealth = spawnedPlayer != null ? spawnedPlayer.GetComponentInChildren<Health>() : null;

        if (playerHealth != null)
            playerHealth.OnDamageTaken += OnPlayerDamaged;
        else
            Debug.LogWarning("[RestartManager] InitPlayer: Health component not found on spawned player!");
    }

    private void TrySubscribePlayer()
    {
        player = FindFirstObjectByType<PlayerController>();
        var playerObj = player != null ? player.gameObject : GameObject.FindGameObjectWithTag("Player");

        if (playerObj != null)
        {
            playerHealth = playerObj.GetComponent<Health>();
            if (playerHealth != null)
                playerHealth.OnDamageTaken += OnPlayerDamaged;
        }
    }

    private IEnumerator RetryFindPlayer()
    {
        // Wait one frame for CharacterManager.Start() to instantiate the player.
        yield return null;
        TrySubscribePlayer();

        if (playerHealth == null)
            Debug.LogWarning("[RestartManager] Still could not find player Health after 1-frame delay. Player-death reset may not work.");
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
        else
            Debug.LogWarning("[RestartManager] ResetEncounter: boss is null — cannot respawn boss.");

        if (player != null)
            player.Respawn(playerSpawnPoint != null ? playerSpawnPoint.position : player.transform.position);
        else
            Debug.LogWarning("[RestartManager] ResetEncounter: player is null — cannot respawn player.");

        Debug.Log("[RestartManager] Encounter reset.");
    }

    private void OnBossDied()
    {
        Debug.Log("[RestartManager] Boss died — scheduling reset.");
        if (IsMLActive)
        {
            try   { mlBrain.OnFightLost(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RestartManager] MLBrain.OnFightLost() threw: {e.Message}. Falling back to DelayedReset.");
                StartCoroutine(DelayedReset());
            }
        }
        else
        {
            StartCoroutine(DelayedReset());
        }
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
