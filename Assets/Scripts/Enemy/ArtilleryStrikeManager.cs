using UnityEngine;

/// <summary>
/// Manages artillery strike spawning for boss enemies.
/// Attach this to the boss GameObject alongside EnemyController.
/// </summary>
public class ArtilleryStrikeManager : MonoBehaviour
{
    [Header("Artillery Settings")]
    [Tooltip("Number of artillery projectiles in the pool")]
    [SerializeField] private int poolSize = 10;

    [Tooltip("Height above the player where artillery spawns")]
    [SerializeField] private float spawnHeight = 12f;

    [Tooltip("Random horizontal spread from player position")]
    [SerializeField] private float horizontalSpread = 3f;

    [Tooltip("Whether to target player position or random area")]
    [SerializeField] private bool targetPlayer = true;

    [Tooltip("Area bounds for random strikes (if not targeting player)")]
    [SerializeField] private float strikeAreaWidth = 10f;

    [Header("Audio")]
    [SerializeField] private AudioClip strikeWarningSound;
    [SerializeField] private AudioClip strikeImpactSound;

    // Object pool for artillery projectiles
    private ArtilleryProjectile[] projectilePool;
    private Transform player;
    private int currentPoolIndex = 0;

    private void Awake()
    {
        InitializePool();
    }

    private void Start()
    {
        // Find the player
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
    }

    private void Update()
    {
        // Keep looking for player if not found yet
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }
    }

    /// <summary>
    /// Initialize the object pool for artillery projectiles
    /// </summary>
    private void InitializePool()
    {
        projectilePool = new ArtilleryProjectile[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            GameObject projectileObj = new GameObject($"ArtilleryProjectile_{i}");
            projectileObj.transform.SetParent(transform);
            
            ArtilleryProjectile projectile = projectileObj.AddComponent<ArtilleryProjectile>();
            projectilePool[i] = projectile;
            projectileObj.SetActive(false);
        }
    }

    /// <summary>
    /// Spawn an artillery strike. Called by ArtilleryStrikeEnemyState.
    /// </summary>
    public void SpawnStrike()
    {
        if (projectilePool == null || projectilePool.Length == 0)
        {
            Debug.LogError("[ARTILLERY MANAGER] Pool not initialized!");
            return;
        }

        // Get next available projectile from pool
        ArtilleryProjectile projectile = GetNextProjectile();
        if (projectile == null)
        {
            Debug.LogWarning("[ARTILLERY MANAGER] No available projectiles in pool!");
            return;
        }

        // Calculate target position
        Vector3 targetPos = CalculateTargetPosition();
        
        // Spawn position is directly above target
        Vector3 spawnPos = new Vector3(targetPos.x, targetPos.y + spawnHeight, targetPos.z);

        // Initialize and activate the projectile
        projectile.Initialize(spawnPos, targetPos);

        // Play warning sound
        if (strikeWarningSound != null && SoundManager.instance != null)
        {
            SoundManager.instance.PlaySound(strikeWarningSound);
        }

        Debug.Log($"[ARTILLERY MANAGER] Spawned strike at {spawnPos}, targeting {targetPos}");
    }

    /// <summary>
    /// Get the next available projectile from the pool
    /// </summary>
    private ArtilleryProjectile GetNextProjectile()
    {
        // Try to find an inactive projectile
        for (int i = 0; i < poolSize; i++)
        {
            int index = (currentPoolIndex + i) % poolSize;
            if (!projectilePool[index].gameObject.activeInHierarchy)
            {
                currentPoolIndex = (index + 1) % poolSize;
                return projectilePool[index];
            }
        }

        // All active, reuse oldest
        ArtilleryProjectile oldest = projectilePool[currentPoolIndex];
        currentPoolIndex = (currentPoolIndex + 1) % poolSize;
        return oldest;
    }

    /// <summary>
    /// Calculate where the artillery should land
    /// </summary>
    private Vector3 CalculateTargetPosition()
    {
        Vector3 targetPos;

        if (targetPlayer && player != null)
        {
            // Target near player with some randomness
            float randomOffset = Random.Range(-horizontalSpread, horizontalSpread);
            targetPos = new Vector3(
                player.position.x + randomOffset,
                player.position.y,
                0f
            );
        }
        else
        {
            // Random position within strike area centered on boss
            float randomX = Random.Range(-strikeAreaWidth / 2f, strikeAreaWidth / 2f);
            targetPos = new Vector3(
                transform.position.x + randomX,
                transform.position.y,
                0f
            );
        }

        return targetPos;
    }

    /// <summary>
    /// Spawn multiple strikes at once (for more intense attack patterns)
    /// </summary>
    public void SpawnMultipleStrikes(int count)
    {
        for (int i = 0; i < count; i++)
        {
            SpawnStrike();
        }
    }

    /// <summary>
    /// Check if the manager can spawn strikes
    /// </summary>
    public bool CanSpawnStrikes()
    {
        return projectilePool != null && projectilePool.Length > 0;
    }

    private void OnDrawGizmosSelected()
    {
        // Draw spawn height
        Gizmos.color = Color.yellow;
        Vector3 spawnIndicator = transform.position + Vector3.up * spawnHeight;
        Gizmos.DrawWireSphere(spawnIndicator, 0.5f);
        Gizmos.DrawLine(transform.position, spawnIndicator);

        // Draw strike area
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Vector3 areaCenter = transform.position;
        Vector3 areaSize = new Vector3(strikeAreaWidth, 0.5f, 0f);
        Gizmos.DrawWireCube(areaCenter, areaSize);

        // Draw horizontal spread if targeting player
        if (targetPlayer)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(areaCenter, new Vector3(horizontalSpread * 2f, 0.3f, 0f));
        }
    }
}
