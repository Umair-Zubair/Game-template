using UnityEngine;

/// <summary>
/// Specific controller for the Voidborn Goddess Boss.
/// Inherits from the base BossController and implements her unique states.
/// Supports melee attacks and an animation-driven artillery attack.
/// </summary>
public class VoidbornGoddessController : BossController
{
    [Header("Voidborn Settings")]
    public float chaseSpeed = 3f;
    public float meleeAttackCooldown = 2.5f;
    public float dashSpeed = 10f;
    
    [Header("Melee Setup")]
    public Transform meleeAttackPoint;
    public float meleeAttackRadius = 1.5f;
    public int meleeDamage = 2;

    // -----------------------------------------------------------------
    // ARTILLERY ATTACK — configurable teleport anchor + projectile setup
    // -----------------------------------------------------------------
    [Header("Artillery Attack")]
    [Tooltip("World position where the boss teleports during the artillery attack (top-right area). Adjustable in the Inspector or via script.")]
    public Vector2 artilleryAnchorPosition = new Vector2(8f, 6f);

    [Tooltip("Prefab for the artillery projectile. Leave empty to auto-create at runtime.")]
    public GameObject artilleryProjectilePrefab;

    [Tooltip("Height above the player where the artillery projectile spawns")]
    public float artillerySpawnHeight = 10f;

    [Tooltip("Damage dealt by the artillery projectile")]
    public int artilleryDamage = 2;

    [Tooltip("Cooldown (seconds) between artillery attacks")]
    public float artilleryCooldown = 8f;

    // -----------------------------------------------------------------
    // CULTIST SPAWN — summons a TwistedCultist in front of the boss
    // -----------------------------------------------------------------
    [Header("Cultist Spawning")]
    [Tooltip("Master toggle — uncheck to completely disable cultist spawning at runtime.")]
    public bool enableCultistSpawning = true;

    [Tooltip("TwistedCultist prefab to instantiate")]
    public GameObject cultistPrefab;

    [Tooltip("World units in front of the boss where the cultist spawns")]
    [Min(0f)] public float cultistSpawnOffset = 3f;

    [Tooltip("Cooldown (seconds) between cultist spawns")]
    [Min(0f)] public float cultistSpawnCooldown = 30f;

    [Tooltip("Total time (seconds) spent in the SpawnCultist state before returning to Chase")]
    [Min(0f)] public float cultistSpawnDuration = 1.5f;

    [Tooltip("Maximum number of cultists that may be alive at once")]
    [Min(0)] public int maxLivingCultists = 2;

    // ---- Evaluation ----
    private bool fightStarted = false;
    public event System.Action OnMeleeAttempted;
    public event System.Action<bool> OnMeleeResult;
    public event System.Action OnArtilleryLaunched;

    // States
    public VoidbornIdleState IdleState { get; private set; }
    public VoidbornChaseState ChaseState { get; private set; }
    public VoidbornMeleeAttackState MeleeState { get; private set; }
    public VoidbornArtilleryState ArtilleryState { get; private set; }
    public VoidbornHurtState HurtState { get; private set; }
    public VoidbornSpawnCultistState SpawnCultistState { get; private set; }
    
    // Melee cooldown
    public float AttackTimer { get; private set; }
    public bool CanAttack => AttackTimer <= 0f && !IsAttacking;

    // Artillery cooldown
    public float ArtilleryTimer { get; private set; }
    public bool CanUseArtillery => ArtilleryTimer <= 0f && !IsAttacking;

    // Cultist spawn cooldown
    public float CultistSpawnTimer { get; private set; }
    public bool CanSpawnCultist => enableCultistSpawning
        && CultistSpawnTimer <= 0f && !IsAttacking
        && cultistPrefab != null && CountLivingCultists() < maxLivingCultists;

    // ---- Effective values (scaled by active AdaptationProfile) ----
    public float EffectiveChaseSpeed => Mathf.Clamp(
        chaseSpeed * (ActiveProfile?.chaseSpeedMultiplier ?? 1f),
        chaseSpeed * AdaptationProfile.MinSpeedMultiplier,
        chaseSpeed * AdaptationProfile.MaxSpeedMultiplier);

    public float EffectiveMeleeAttackCooldown => Mathf.Clamp(
        meleeAttackCooldown
            * (ActiveProfile?.attackCooldownMultiplier ?? 1f)
            * (DecisionEngine?.FairnessCooldownMultiplier ?? 1f),
        meleeAttackCooldown * AdaptationProfile.MinCooldownMultiplier,
        meleeAttackCooldown * AdaptationProfile.MaxCooldownMultiplier);

    // artilleryPriorityBonus > 0 = fires more often (shorter cooldown), < 0 = less often
    public float EffectiveArtilleryCooldown
    {
        get
        {
            float bonus = ActiveProfile?.artilleryPriorityBonus ?? 0f;
            float baseCooldown = artilleryCooldown / Mathf.Max(0.5f, 1f + bonus);
            float fairness = DecisionEngine?.FairnessCooldownMultiplier ?? 1f;
            return Mathf.Max(3f, baseCooldown * fairness);
        }
    }

    protected override void InitializeStates()
    {
        IdleState = new VoidbornIdleState();
        ChaseState = new VoidbornChaseState();
        MeleeState = new VoidbornMeleeAttackState();
        ArtilleryState = new VoidbornArtilleryState();
        HurtState = new VoidbornHurtState();
        SpawnCultistState = new VoidbornSpawnCultistState();

        AttackTimer = meleeAttackCooldown;
        ArtilleryTimer = artilleryCooldown;
        CultistSpawnTimer = 0f;
    }

    protected override void StartState()
    {
        StateMachine.Initialize(IdleState, this);
    }

    protected override void Start()
    {
        base.Start();
        if (Health != null)
            Health.OnDamageTaken += OnTakeDamage;
    }

    private void OnDestroy()
    {
        if (Health != null)
            Health.OnDamageTaken -= OnTakeDamage;
    }

    /// <summary>
    /// Subscribed to Health.OnDamageTaken. Transitions the FSM to HurtState
    /// unless the boss is already dead or mid-attack.
    /// </summary>
    /// <summary>
    /// Called the first time the boss enters ChaseState.
    /// Notifies AISessionLogger that a new fight has begun.
    /// </summary>
    public void NotifyFightStart()
    {
        if (fightStarted) return;
        fightStarted = true;
        AISessionLogger.Instance?.StartFight();
        AIDecisionLogger.Instance?.StartFight();
    }

    public void OnTakeDamage(float damage)
    {
        if (IsDead) return;

        // Detect fatal hit — health is already updated before OnDamageTaken fires
        if (Health.currentHealth <= 0)
        {
            AISessionLogger.Instance?.EndFight(false);
            AIDecisionLogger.Instance?.EndFight(false);
            return;
        }

        if (IsAttacking) return; // don't interrupt melee combo or artillery sequence
        StateMachine.ChangeState(HurtState, this);
    }

    protected override void UpdateBossTimers()
    {
        if (AttackTimer > 0)
            AttackTimer -= Time.deltaTime;

        if (ArtilleryTimer > 0)
            ArtilleryTimer -= Time.deltaTime;

        if (CultistSpawnTimer > 0)
            CultistSpawnTimer -= Time.deltaTime;
    }

    public void ResetAttackTimer()
    {
        AttackTimer = EffectiveMeleeAttackCooldown;
    }

    /// <summary>
    /// Resets the artillery cooldown so the next artillery attack
    /// cannot fire until the full cooldown elapses again.
    /// Called by VoidbornArtilleryState.OnExit().
    /// </summary>
    public void ResetArtilleryTimer()
    {
        ArtilleryTimer = EffectiveArtilleryCooldown;
    }

    public void ResetCultistSpawnTimer() => CultistSpawnTimer = cultistSpawnCooldown;

    /// <summary>
    /// Performs the hitbox check for the melee attack.
    /// Can be called via Animation Event or Timer.
    /// </summary>
    public void PerformMeleeHit()
    {
        if (meleeAttackPoint == null) return;
        OnMeleeAttempted?.Invoke();
        Collider2D[] hits = Physics2D.OverlapCircleAll(meleeAttackPoint.position, meleeAttackRadius, playerDamageLayer);
        bool landed = false;
        foreach (var hit in hits)
        {
            Health playerHealth = hit.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(meleeDamage);
                landed = true;
                Debug.Log($"[Voidborn] Melee hit for {meleeDamage} damage!");
            }
        }
        OnMeleeResult?.Invoke(landed);
    }

    // -----------------------------------------------------------------
    // Artillery Projectile Spawning
    // -----------------------------------------------------------------

    /// <summary>
    /// Spawns the artillery projectile above the player's current X position.
    /// Called by VoidbornArtilleryState during the cast2spell animation phase.
    /// Can also be invoked directly as an Animation Event on the cast2spell clip
    /// if you want even tighter synchronisation with the animation.
    /// </summary>
    public void SpawnArtilleryProjectile()
    {
        if (Player == null) return;
        OnArtilleryLaunched?.Invoke();

        // Capture the player's current X position — creates the "artillery" targeting
        float playerX = Player.position.x;
        Vector3 spawnPos = new Vector3(playerX, Player.position.y + artillerySpawnHeight, 0f);

        // Instantiate from prefab or create at runtime
        GameObject projObj;
        if (artilleryProjectilePrefab != null)
        {
            projObj = Instantiate(artilleryProjectilePrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            projObj = new GameObject("VoidbornArtilleryProjectile");
            projObj.transform.position = spawnPos;
            projObj.AddComponent<VoidbornArtilleryProjectile>();
        }

        // Initialize the projectile with position and damage
        VoidbornArtilleryProjectile proj = projObj.GetComponent<VoidbornArtilleryProjectile>();
        if (proj != null)
        {
            proj.Initialize(spawnPos, artilleryDamage);
        }

        Debug.Log($"[Voidborn] Artillery projectile spawned at {spawnPos}");
    }

    public void SpawnCultist()
    {
        if (cultistPrefab == null) return;
        float side = FacingDirection == 0 ? 1f : FacingDirection;
        Vector3 spawnPos = transform.position + new Vector3(side * cultistSpawnOffset, 0f, 0f);
        Instantiate(cultistPrefab, spawnPos, Quaternion.identity);
    }

    public int CountLivingCultists()
    {
        int count = 0;
        foreach (TwistedCultistController c in Object.FindObjectsOfType<TwistedCultistController>())
            if (!c.IsDead) count++;
        return count;
    }

    public override void Respawn(Vector3 spawnPosition)
    {
        base.Respawn(spawnPosition);
        AttackTimer = meleeAttackCooldown;
        ArtilleryTimer = artilleryCooldown;
        CultistSpawnTimer = 0f;
        fightStarted = false;
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Melee attack point
        if (meleeAttackPoint != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(meleeAttackPoint.position, meleeAttackRadius);
        }

        // Artillery teleport anchor — visible in the Scene view for easy positioning
        Gizmos.color = Color.cyan;
        Vector3 anchorPos = new Vector3(artilleryAnchorPosition.x, artilleryAnchorPosition.y, 0f);
        Gizmos.DrawWireSphere(anchorPos, 0.5f);
        Gizmos.DrawLine(transform.position, anchorPos);
    }
}
