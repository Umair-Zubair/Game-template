using UnityEngine;
using System.Collections;

/// <summary>
/// Attack pattern types for variety in combat
/// </summary>
public enum AttackPattern
{
    Single,     // One projectile
    Burst,      // 3 rapid projectiles
    Spread      // 3 projectiles in a fan
}

/// <summary>
/// Main controller for FSM-based enemy AI.
/// Handles state machine, player detection, movement, and attacks.
/// </summary>
public class EnemyController : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private EnemyData data;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask projectileLayer;
    [SerializeField] private LayerMask groundLayer;

    [Header("Patrol Points")]
    [SerializeField] private Transform leftEdge;
    [SerializeField] private Transform rightEdge;

    [Header("Attack")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject[] projectiles;
    [SerializeField] private AudioClip attackSound;

    [Header("Melee Attack")]
    [Tooltip("Attack point for melee hitbox detection (leave empty for ranged enemies)")]
    [SerializeField] private Transform meleeAttackPoint;
    [Tooltip("Radius of melee attack detection")]
    [SerializeField] private float meleeAttackRadius = 1.5f;
    [Tooltip("Layer for player damage detection")]
    [SerializeField] private LayerMask playerDamageLayer;

    [Header("Debug")]
    public bool DebugMode = true;

    // Components
    [HideInInspector] public Rigidbody2D RB;
    [HideInInspector] public Animator Anim;
    [HideInInspector] public BoxCollider2D Collider;
    [HideInInspector] public Health Health;

    // State Machine
    public EnemyStateMachine StateMachine { get; private set; }

    // States
    public IdleEnemyState IdleState { get; private set; }
    public PatrolEnemyState PatrolState { get; private set; }
    public ChaseEnemyState ChaseState { get; private set; }
    public AttackEnemyState AttackState { get; private set; }
    public RetreatEnemyState RetreatState { get; private set; }
    public DodgeEnemyState DodgeState { get; private set; }
    public StunnedEnemyState StunnedState { get; private set; }
    public ArtilleryStrikeEnemyState ArtilleryStrikeState { get; private set; }
    public DashEnemyState DashState { get; private set; }

    // Runtime
    public int FacingDirection { get; private set; } = -1; // -1 = left, 1 = right
    public Transform Player { get; private set; }
    
    // Timers
    public float AttackCooldownTimer;
    public float DodgeCooldownTimer;
    public float StunTimer;
    public float ArtilleryCooldownTimer;
    public float DashCooldownTimer;
    
    // Artillery
    private ArtilleryStrikeManager artilleryManager;

    // Patrol
    public bool PatrolMovingLeft = true;
    public float PatrolIdleTimer;

    // Attack Patterns
    public AttackPattern CurrentAttackPattern { get; private set; } = AttackPattern.Single;
    private int attackCounter = 0;
    public bool IsAttacking { get; private set; } = false;
    public bool IsDashing { get; set; } = false;

    private Vector3 initScale;
    private Coroutine attackCoroutine;
    private int lastAttackFrame = -1;

    // ---- Layer 2: Heuristic Adaptation ----
    private AdaptationProfile activeProfile;

    /// <summary>Apply a new adaptation profile from HeuristicAdaptationManager.</summary>
    public void ApplyAdaptationProfile(AdaptationProfile profile) { activeProfile = profile; }

    // Effective values — states use these instead of Data.X directly
    public float EffectiveChaseSpeed    => Mathf.Clamp(Data.chaseSpeed    * (activeProfile?.chaseSpeedMultiplier    ?? 1f), Data.chaseSpeed    * AdaptationProfile.MinSpeedMultiplier,    Data.chaseSpeed    * AdaptationProfile.MaxSpeedMultiplier);
    public float EffectiveRetreatRange  => Mathf.Clamp(Data.retreatRange  * (activeProfile?.retreatRangeMultiplier  ?? 1f), Data.retreatRange  * 0.3f, Data.retreatRange  * 3f);
    public float EffectiveRetreatSpeed  => Mathf.Clamp(Data.retreatSpeed  * (activeProfile?.retreatSpeedMultiplier  ?? 1f), Data.retreatSpeed  * AdaptationProfile.MinSpeedMultiplier,    Data.retreatSpeed  * AdaptationProfile.MaxSpeedMultiplier);
    public float EffectiveAttackCooldown=> Mathf.Clamp(Data.attackCooldown * (activeProfile?.attackCooldownMultiplier ?? 1f), Data.attackCooldown * AdaptationProfile.MinCooldownMultiplier, Data.attackCooldown * AdaptationProfile.MaxCooldownMultiplier);
    public float EffectiveDodgeCooldown => Mathf.Clamp(Data.dodgeCooldown  * (activeProfile?.dodgeCooldownMultiplier  ?? 1f), Data.dodgeCooldown  * AdaptationProfile.MinCooldownMultiplier, Data.dodgeCooldown  * AdaptationProfile.MaxCooldownMultiplier);

    // Priority bonuses for ChaseState scoring
    public float DashPriorityBonus      => activeProfile?.dashPriorityBonus      ?? 0f;
    public float ArtilleryPriorityBonus => activeProfile?.artilleryPriorityBonus ?? 0f;

    #region Unity Lifecycle

    private void Awake()
    {
        // Get components
        RB = GetComponent<Rigidbody2D>();
        Anim = GetComponent<Animator>();
        Collider = GetComponent<BoxCollider2D>();
        Health = GetComponent<Health>();

        if (RB == null) UnityEngine.Debug.LogError("[ENEMY] Rigidbody2D MISSING! Add one to " + gameObject.name);
        if (Collider == null) UnityEngine.Debug.LogError("[ENEMY] BoxCollider2D MISSING! Add one to " + gameObject.name);
        else if (Collider.isTrigger) UnityEngine.Debug.LogWarning("[ENEMY] Collider is set to TRIGGER! Uncheck 'Is Trigger' so he doesn't fall through floor.");
        
        // Match player physics initialization
        if (RB != null)
        {
            RB.gravityScale = data.gravityScale;
            RB.freezeRotation = true;
            RB.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        initScale = transform.localScale;

        // Initialize state machine
        StateMachine = new EnemyStateMachine();
        IdleState = new IdleEnemyState();
        PatrolState = new PatrolEnemyState();
        ChaseState = new ChaseEnemyState();
        AttackState = new AttackEnemyState();
        RetreatState = new RetreatEnemyState();
        DodgeState = new DodgeEnemyState();
        StunnedState = new StunnedEnemyState();
        ArtilleryStrikeState = new ArtilleryStrikeEnemyState();
        DashState = new DashEnemyState();
    }

    private void Start()
    {
        // Find player
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            Player = playerObj.transform;

        // Initialize timers so AI can act immediately
        AttackCooldownTimer = data.attackCooldown;
        DodgeCooldownTimer = data.dodgeCooldown;
        ArtilleryCooldownTimer = 0f; // Start ready to use
        DashCooldownTimer = data.dashCooldown; // Ready to dash immediately

        // Get artillery manager if present (for bosses)
        artilleryManager = GetComponent<ArtilleryStrikeManager>();

        // Start in idle or patrol
        StateMachine.Initialize(PatrolState, this);
    }

    private void Update()
    {
        // Keep looking for player if not found yet (handles spawn timing issues)
        if (Player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                Player = playerObj.transform;
            }
        }

        UpdateTimers();
        StateMachine.Update(this);
    }

    private void FixedUpdate()
    {
        StateMachine.FixedUpdate(this);
        ApplyGravity();
    }

    private void ApplyGravity()
    {
        if (RB == null) return;

        // During a dash, disable gravity so the boss stays at the same height
        if (IsDashing)
        {
            RB.gravityScale = 0f;
            return;
        }

        // Apply snappier falling physics like the player
        if (RB.linearVelocity.y < 0)
        {
            RB.gravityScale = Data.gravityScale * Data.fallGravityMultiplier;
        }
        else
        {
            RB.gravityScale = Data.gravityScale;
        }

        // Clamp fall speed
        if (RB.linearVelocity.y < -Data.maxFallSpeed)
        {
            RB.linearVelocity = new Vector2(RB.linearVelocity.x, -Data.maxFallSpeed);
        }
    }

    private void UpdateTimers()
    {
        AttackCooldownTimer += Time.deltaTime;
        DodgeCooldownTimer += Time.deltaTime;
        ArtilleryCooldownTimer += Time.deltaTime;
        DashCooldownTimer += Time.deltaTime;
        
        if (StunTimer > 0)
            StunTimer -= Time.deltaTime;
    }

    #endregion

    #region Detection Methods

    public float GetDistanceToPlayer()
    {
        if (Player == null) return Mathf.Infinity;
        return Vector2.Distance(transform.position, Player.position);
    }

    public bool PlayerInDetectionRange()
    {
        return GetDistanceToPlayer() <= Data.detectionRange;
    }

    public bool PlayerInAttackRange()
    {
        return GetDistanceToPlayer() <= Data.attackRange;
    }

    public bool PlayerTooClose()
    {
        return GetDistanceToPlayer() <= Data.retreatRange;
    }

    public bool PlayerInSight()
    {
        if (Player == null) return false;
        
        // Check if player is in the direction we're facing
        float directionToPlayer = Mathf.Sign(Player.position.x - transform.position.x);
        if (directionToPlayer != FacingDirection) return false;

        // Raycast to check line of sight
        RaycastHit2D hit = Physics2D.Raycast(
            transform.position,
            new Vector2(FacingDirection, 0),
            Data.detectionRange,
            playerLayer
        );

        return hit.collider != null;
    }

    public bool IncomingProjectileDetected()
    {
        // SCAN NEARBY AREA for any player projectiles
        Collider2D[] allNearby = Physics2D.OverlapCircleAll(transform.position, Data.projectileDetectionRange);
        
        foreach(var c in allNearby)
        {
            // Ignore ourselves only
            if (c.gameObject == gameObject) continue;
            
            // --- IGNORE ENEMY DAMAGE/PROJECTILES ---
            if (c.GetComponent<EnemyProjectile>() != null || c.GetComponent<EnemyDamage>() != null)
            {
                continue;
            }

            // --- DETECT PLAYER PROJECTILES ---
            // Check for Projectile script (Player's fireball uses this)
            Projectile playerProj = c.GetComponent<Projectile>();
            if (playerProj != null)
            {
                Debug.Log($"[DETECTION] Found PLAYER threat: {c.name} at {c.transform.position}");
                return true; 
            }
            
            // Fallback: name-based detection for any "fireball" objects
            string n = c.name.ToLower();
            if (n.Contains("fireball") || n.Contains("player") && n.Contains("proj"))
            {
                Debug.Log($"[DETECTION] Found threat by NAME: {c.name}");
                return true;
            }
        }
        
        return false;
    }

    public int GetDirectionToPlayer()
    {
        if (Player == null) return FacingDirection;
        return Player.position.x > transform.position.x ? 1 : -1;
    }

    #endregion

    #region Movement Methods

    public void MoveInDirection(int direction, float speed)
    {
        FaceDirection(direction);
        // --- FIX: Velocity-based movement ---
        // We apply speed to X but preserve Y so gravity works correctly.
        if (RB != null)
        {
            RB.linearVelocity = new Vector2(direction * speed, RB.linearVelocity.y);
        }
        else
        {
            // Fallback if RB is missing (though it shouldn't be)
            transform.position += new Vector3(direction * speed * Time.deltaTime, 0, 0);
        }
    }

    public void MoveTowardPlayer(float speed)
    {
        int direction = GetDirectionToPlayer();
        MoveInDirection(direction, speed);
    }

    public void MoveAwayFromPlayer(float speed)
    {
        int direction = -GetDirectionToPlayer();
        MoveInDirection(direction, speed);
    }

    public void FaceDirection(int direction)
    {
        if (direction == 0) return;
        FacingDirection = direction;
        transform.localScale = new Vector3(
            Mathf.Abs(initScale.x) * direction,
            initScale.y,
            initScale.z
        );
    }

    public void FacePlayer()
    {
        FaceDirection(GetDirectionToPlayer());
    }

    public void Stop()
    {
        if (RB != null)
        {
            // --- FIX: ONLY STOP HORIZONTAL MOVEMENT ---
            // If we set Y to 0, the enemy floats in the air.
            RB.linearVelocity = new Vector2(0, RB.linearVelocity.y);
        }
    }

    public void Jump(float jumpForce)
    {
        bool grounded = IsGrounded();
        Debug.Log($"[JUMP] Jump called! Grounded: {grounded}, JumpForce: {jumpForce}, RB: {RB != null}");
        
        if (RB != null && grounded)
        {
            // Reset vertical velocity for consistent jump height
            RB.linearVelocity = new Vector2(RB.linearVelocity.x, jumpForce);
            Debug.Log($"[JUMP] Jump applied! New velocity: {RB.linearVelocity}");
        }
        else if (!grounded)
        {
            Debug.LogWarning("[JUMP] Cannot jump - NOT GROUNDED!");
        }
    }

    public bool IsGrounded()
    {
        if (Collider == null)
        {
            Debug.LogError("[ENEMY] BoxCollider2D is NULL! Cannot check ground.");
            return false;
        }

        // Better grounding check using BoxCast
        float extraHeight = 0.15f;
        RaycastHit2D hit = Physics2D.BoxCast(
            Collider.bounds.center, 
            Collider.bounds.size, 
            0f, 
            Vector2.down, 
            extraHeight, 
            groundLayer
        );

        return hit.collider != null;
    }

    #endregion

    #region Patrol Methods

    public bool AtLeftEdge()
    {
        if (leftEdge == null) return false;
        return transform.position.x <= leftEdge.position.x;
    }

    public bool AtRightEdge()
    {
        if (rightEdge == null) return false;
        return transform.position.x >= rightEdge.position.x;
    }

    #endregion

    #region Combat Methods

    /// <summary>
    /// Execute attack based on current pattern
    /// </summary>
    public void ExecuteAttack()
    {
        // SYNC GUARD: Prevent multiple calls in the same frame or if already attacking
        if (IsAttacking || Time.frameCount == lastAttackFrame) return;
        lastAttackFrame = Time.frameCount;

        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
        }
        
        // Check if this is a melee or ranged enemy
        if (meleeAttackPoint != null)
        {
            // Melee enemy - perform direct attack
            attackCoroutine = StartCoroutine(ExecuteMeleeAttackPattern());
        }
        else
        {
            // Ranged enemy - use projectile patterns
            SelectNextAttackPattern();
            attackCoroutine = StartCoroutine(ExecuteAttackPattern());
        }
    }

    /// <summary>
    /// Cycle through attack patterns for variety
    /// </summary>
    private void SelectNextAttackPattern()
    {
        // Force single fire for consistency as requested by user
        CurrentAttackPattern = AttackPattern.Single;
        attackCounter++;
    }

    private IEnumerator ExecuteAttackPattern()
    {
        IsAttacking = true;

        switch (CurrentAttackPattern)
        {
            case AttackPattern.Single:
                FireSingleProjectile();
                break;

            case AttackPattern.Burst:
                // 3 rapid shots with small delay
                for (int i = 0; i < 3; i++)
                {
                    FireSingleProjectile();
                    yield return new WaitForSeconds(0.15f);
                }
                break;

            case AttackPattern.Spread:
                FireSpreadProjectiles();
                break;
        }
        
        // Wait a small buffer frame to ensure state transitions complete properly
        yield return new WaitForSeconds(0.1f);
        
        IsAttacking = false;
        ResetAttackCooldown(); // Reset cooldown ONLY when the pattern is fully done
    }

    /// <summary>
    /// Execute melee attack pattern - performs hitbox detection
    /// </summary>
    private IEnumerator ExecuteMeleeAttackPattern()
    {
        IsAttacking = true;

        // Wait for animation to reach attack frame (similar to ranged)
        yield return new WaitForSeconds(0.3f);

        // Perform melee attack hitbox detection
        PerformMeleeAttack();

        // Wait for attack animation to complete
        yield return new WaitForSeconds(0.1f);

        IsAttacking = false;
        ResetAttackCooldown();
    }

    /// <summary>
    /// Perform melee attack with hitbox detection
    /// </summary>
    private void PerformMeleeAttack()
    {
        if (attackSound != null)
            SoundManager.instance.PlaySound(attackSound);

        if (meleeAttackPoint == null)
        {
            Debug.LogWarning("[MELEE] meleeAttackPoint is null! Cannot perform melee attack.");
            return;
        }

        // Detect all colliders in melee range
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            meleeAttackPoint.position,
            meleeAttackRadius,
            playerDamageLayer
        );

        foreach (var hit in hits)
        {
            Health playerHealth = hit.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(Data.damage);
                Debug.Log($"[MELEE] Hit {hit.name} for {Data.damage} damage!");
            }
        }
    }

    /// <summary>
    /// Fire a single projectile straight ahead
    /// </summary>
    public void FireSingleProjectile()
    {
        if (attackSound != null)
            SoundManager.instance.PlaySound(attackSound);

        int index = FindAvailableProjectile();
        if (index >= 0 && projectiles[index] != null)
        {
            projectiles[index].transform.position = firePoint.position;
            projectiles[index].transform.rotation = Quaternion.identity;
            
            EnemyProjectile projScript = projectiles[index].GetComponent<EnemyProjectile>();
            if (projScript != null)
            {
                projScript.ActivateProjectile();
            }
        }
    }

    /// <summary>
    /// Fire 3 projectiles in a fan pattern (spread shot)
    /// </summary>
    public void FireSpreadProjectiles()
    {
        if (attackSound != null)
            SoundManager.instance.PlaySound(attackSound);

        float[] angles = { -15f, 0f, 15f }; // Fan angles

        foreach (float angle in angles)
        {
            int index = FindAvailableProjectile();
            if (index >= 0 && projectiles[index] != null)
            {
                projectiles[index].transform.position = firePoint.position;
                projectiles[index].transform.rotation = Quaternion.Euler(0, 0, angle * FacingDirection);
                projectiles[index].GetComponent<EnemyProjectile>()?.ActivateProjectile();
            }
        }
    }

    /// <summary>
    /// Legacy single fire for animation events
    /// </summary>
    public void FireProjectile()
    {
        FireSingleProjectile();
    }

    /// <summary>
    /// Called by RangedAttack.anim animation event.
    /// DISABLED - We handle firing in AttackState directly to avoid timing issues.
    /// </summary>
    public void RangedAttack()
    {
        // Do nothing - AttackState handles firing with a timer
        // Keeping this method so animation event doesn't throw errors
    }

    /// <summary>
    /// Called by death animation event to disable enemy
    /// </summary>
    public void Deactivate()
    {
        gameObject.SetActive(false);
    }

    private int FindAvailableProjectile()
    {
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (!projectiles[i].activeInHierarchy)
                return i;
        }
        return 0; // Reuse first if all active
    }

    public bool CanAttack()
    {
        // Layer 2: compare against EffectiveAttackCooldown (adapted by HeuristicAdaptationManager)
        return AttackCooldownTimer >= EffectiveAttackCooldown && !IsAttacking;
    }

    public bool CanDodge()
    {
        // Layer 2: compare against EffectiveDodgeCooldown (adapted by HeuristicAdaptationManager)
        bool onCooldown = DodgeCooldownTimer < EffectiveDodgeCooldown;
        bool isBusy = IsAttacking;
        
        if (IncomingProjectileDetected() && (onCooldown || isBusy))
        {
            Debug.Log($"[CAN_DODGE] False. Cooldown: {DodgeCooldownTimer:F2}/{EffectiveDodgeCooldown:F2} (OnCooldown: {onCooldown}), Busy: {isBusy}");
        }
        
        return !onCooldown && !isBusy;
    }

    public void ResetAttackCooldown()
    {
        AttackCooldownTimer = 0;
    }

    public void ResetDodgeCooldown()
    {
        DodgeCooldownTimer = 0;
    }

    /// <summary>
    /// Check if this enemy can use artillery strikes (has manager + off cooldown)
    /// Artillery cooldown is 8 seconds by default
    /// </summary>
    public bool CanUseArtillery()
    {
        return artilleryManager != null && ArtilleryCooldownTimer >= 8f && !IsAttacking;
    }

    public void ResetArtilleryCooldown()
    {
        ArtilleryCooldownTimer = 0;
    }

    /// <summary>
    /// Check if this enemy can perform a dash (off cooldown, not busy, player in range band)
    /// </summary>
    public bool CanDash()
    {
        if (IsDashing || IsAttacking) return false;
        if (DashCooldownTimer < Data.dashCooldown) return false;

        float dist = GetDistanceToPlayer();
        return dist >= Data.dashMinRange && dist <= Data.dashMaxRange;
    }

    public void ResetDashCooldown()
    {
        DashCooldownTimer = 0;
    }

    /// <summary>
    /// Called when this enemy takes damage. Triggers StunnedState.
    /// Wire this to Health.TakeDamage or call from animation event.
    /// </summary>
    public void OnTakeDamage()
    {
        // Only stun if not already stunned
        if (StateMachine.CurrentState == StunnedState)
            return;

        // If dashing, check whether dash is interruptible
        if (IsDashing && !Data.dashInterruptibleByStun)
            return;

        StateMachine.ChangeState(StunnedState, this);
    }

    #endregion

    #region Animation Methods

    public void SetMoving(bool isMoving)
    {
        if (Anim != null)
            Anim.SetBool("moving", isMoving);
    }

    public void TriggerAttack()
    {
        if (Anim != null)
        {
            // For boss melee, trigger the rangedAttack parameter (reused for melee animation)
            Anim.SetTrigger("rangedAttack");
        }
    }

    public void TriggerHurt()
    {
        if (Anim != null)
            Anim.SetTrigger("hurt");
    }

    #endregion

    #region Public Accessors

    public EnemyData Data => data;
    public Transform LeftEdge => leftEdge;
    public Transform RightEdge => rightEdge;

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        if (data == null) return;

        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, data.detectionRange);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, data.attackRange);

        // Retreat range
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, data.retreatRange);

        // Projectile detection
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, data.projectileDetectionRange);

        // Melee attack hitbox (if applicable)
        if (meleeAttackPoint != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(meleeAttackPoint.position, meleeAttackRadius);
        }
    }

    #endregion
}
