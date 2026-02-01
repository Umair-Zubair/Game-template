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

    // Runtime
    public int FacingDirection { get; private set; } = -1; // -1 = left, 1 = right
    public Transform Player { get; private set; }
    
    // Timers
    public float AttackCooldownTimer;
    public float DodgeCooldownTimer;
    public float StunTimer;

    // Patrol
    public bool PatrolMovingLeft = true;
    public float PatrolIdleTimer;

    // Attack Patterns
    public AttackPattern CurrentAttackPattern { get; private set; } = AttackPattern.Single;
    private int attackCounter = 0;
    public bool IsAttacking { get; private set; } = false;

    private Vector3 initScale;
    private Coroutine attackCoroutine;
    private int lastAttackFrame = -1;

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
        
        UnityEngine.Debug.Log($"[ENEMY] Ready! Attack CD: {AttackCooldownTimer}, Dodge CD: {DodgeCooldownTimer}");
        UnityEngine.Debug.Log($"[ENEMY] Layers -> Ground: {LayerMask.LayerToName((int)Mathf.Log(groundLayer.value, 2))}, Projectile: {LayerMask.LayerToName((int)Mathf.Log(projectileLayer.value, 2))}");

        // Start in idle or patrol
        StateMachine.Initialize(PatrolState, this);
    }

    private void Update()
    {
        UpdateTimers();
        StateMachine.Update(this);
        
        // Debug current state
        if (Time.frameCount % 60 == 0) // Log once per sec approx
            UnityEngine.Debug.Log($"[AI] Current State: {StateMachine.CurrentState.GetType().Name}");
    }

    private void FixedUpdate()
    {
        StateMachine.FixedUpdate(this);
        ApplyGravity();
    }

    private void ApplyGravity()
    {
        if (RB == null) return;

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
        // 1. PRIMARY CHECK: designate Projectile Layer (still useful for performance)
        Collider2D projectile = Physics2D.OverlapCircle(
            transform.position,
            Data.projectileDetectionRange,
            projectileLayer
        );
        
        // 2. ROBUST FALLBACK: Scan nearby for anything that looks like a threat
        // We use name and component checks to avoid crashing on missing tags/layers.
        if (projectile == null)
        {
            Collider2D[] allNearby = Physics2D.OverlapCircleAll(transform.position, Data.projectileDetectionRange);
            foreach(var c in allNearby)
            {
                if (c.gameObject == gameObject || c.isTrigger) continue;
                
                // If it has a Projectile script or a suspicious name, it's a threat!
                bool isProjectileComp = c.GetComponent<EnemyProjectile>() != null;
                string n = c.name.ToLower();
                
                if (isProjectileComp || n.Contains("fire") || n.Contains("proj") || n.Contains("ball") || n.Contains("arrow"))
                {
                    projectile = c;
                    break;
                }
            }
        }
        
        return projectile != null;
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
        transform.position += new Vector3(direction * speed * Time.deltaTime, 0, 0);
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
            RB.linearVelocity = Vector2.zero;
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
        // Use a slightly narrower box to prevent catching on walls
        float skinWidth = 0.1f;
        Vector2 boxSize = new Vector2(Collider.bounds.size.x - skinWidth, 0.1f);
        // Start center slightly INSIDE the collider to avoid Raycast/BoxCast issues starting on surface
        Vector2 boxCenter = new Vector2(Collider.bounds.center.x, Collider.bounds.min.y + 0.05f);

        RaycastHit2D hit = Physics2D.BoxCast(
            boxCenter,
            boxSize,
            0f,
            Vector2.down,
            0.15f,
            groundLayer
        );

        Debug.Log($"[GROUNDED] BoxCast from {boxCenter}, size {boxSize}, hit: {hit.collider != null}, groundLayer: {groundLayer.value}");
        
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
        
        SelectNextAttackPattern();
        attackCoroutine = StartCoroutine(ExecuteAttackPattern());
    }

    /// <summary>
    /// Cycle through attack patterns for variety
    /// </summary>
    private void SelectNextAttackPattern()
    {
        attackCounter++;
        
        // Pattern cycle: Single, Single, Burst, Single, Single, Spread, repeat
        if (attackCounter % 6 == 3)
            CurrentAttackPattern = AttackPattern.Burst;
        else if (attackCounter % 6 == 0)
            CurrentAttackPattern = AttackPattern.Spread;
        else
            CurrentAttackPattern = AttackPattern.Single;
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
        return AttackCooldownTimer >= Data.attackCooldown && !IsAttacking;
    }

    public bool CanDodge()
    {
        bool onCooldown = DodgeCooldownTimer < Data.dodgeCooldown;
        bool isBusy = IsAttacking;
        
        if (IncomingProjectileDetected() && (onCooldown || isBusy))
        {
            Debug.Log($"[CAN_DODGE] False. Cooldown: {DodgeCooldownTimer:F2}/{Data.dodgeCooldown} (OnCooldown: {onCooldown}), Busy: {isBusy}");
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
    /// Called when this enemy takes damage. Triggers StunnedState.
    /// Wire this to Health.TakeDamage or call from animation event.
    /// </summary>
    public void OnTakeDamage()
    {
        // Only stun if not already stunned
        if (StateMachine.CurrentState != StunnedState)
        {
            StateMachine.ChangeState(StunnedState, this);
        }
    }

    #endregion

    #region Animation Methods

    public void SetMoving(bool isMoving)
    {
        Anim.SetBool("moving", isMoving);
    }

    public void TriggerAttack()
    {
        Anim.SetTrigger("rangedAttack");
    }

    public void TriggerHurt()
    {
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
    }

    #endregion
}
