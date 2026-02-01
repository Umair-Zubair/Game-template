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

    #region Unity Lifecycle

    private void Awake()
    {
        // Get components
        RB = GetComponent<Rigidbody2D>();
        Anim = GetComponent<Animator>();
        Collider = GetComponent<BoxCollider2D>();
        Health = GetComponent<Health>();
        
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

        // Start in idle or patrol
        StateMachine.Initialize(PatrolState, this);
    }

    private void Update()
    {
        UpdateTimers();
        StateMachine.Update(this);
    }

    private void FixedUpdate()
    {
        StateMachine.FixedUpdate(this);
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
        // Check for player projectiles in detection range
        Collider2D projectile = Physics2D.OverlapCircle(
            transform.position,
            Data.projectileDetectionRange,
            projectileLayer
        );

        if (projectile != null)
        {
            Debug.Log("[DODGE CHECK] Player projectile detected! Can dodge: " + CanDodge());
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
        if (RB != null && IsGrounded())
        {
            RB.linearVelocity = new Vector2(RB.linearVelocity.x, jumpForce);
        }
    }

    public bool IsGrounded()
    {
        // Check if enemy is touching ground
        return Physics2D.BoxCast(
            Collider.bounds.center,
            Collider.bounds.size,
            0f,
            Vector2.down,
            0.1f,
            groundLayer
        );
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
        SelectNextAttackPattern();
        StartCoroutine(ExecuteAttackPattern());
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
            
        Debug.Log($"[ENEMY] Attack #{attackCounter}, Pattern: {CurrentAttackPattern}");
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

        IsAttacking = false;
    }

    /// <summary>
    /// Fire a single projectile straight ahead
    /// </summary>
    public void FireSingleProjectile()
    {
        Debug.Log($"[ENEMY] FireSingleProjectile() called! Stack: {System.Environment.StackTrace}");
        
        if (attackSound != null)
            SoundManager.instance.PlaySound(attackSound);

        int index = FindAvailableProjectile();
        if (index >= 0 && projectiles[index] != null)
        {
            projectiles[index].transform.position = firePoint.position;
            projectiles[index].transform.rotation = Quaternion.identity;
            projectiles[index].GetComponent<EnemyProjectile>()?.ActivateProjectile();
            Debug.Log($"[ENEMY] Fireball {index} activated at {firePoint.position}");
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
        bool canAttack = AttackCooldownTimer >= Data.attackCooldown && !IsAttacking;
        Debug.Log($"[COOLDOWN] CanAttack? {canAttack} (timer: {AttackCooldownTimer:F2} / {Data.attackCooldown}, IsAttacking: {IsAttacking})");
        return canAttack;
    }

    public bool CanDodge()
    {
        return DodgeCooldownTimer >= Data.dodgeCooldown;
    }

    public void ResetAttackCooldown()
    {
        Debug.Log("[COOLDOWN] Attack cooldown RESET to 0");
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
