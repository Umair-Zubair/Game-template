using UnityEngine;

/// <summary>
/// Abstract base class for all bosses.
/// Handles physics, common components, player detection, and basic movement.
/// Specific bosses inherit from this to implement their unique attack patterns and states.
/// </summary>
public abstract class BossController : MonoBehaviour
{
    [Header("Base Settings")]
    public float detectionRange = 15f;
    public float attackRange = 5f;
    public float retreatRange = 2f;
    public float gravityScale = 2f;
    public float fallGravityMultiplier = 1.5f;
    public float maxFallSpeed = 20f;
    
    [Header("Layer Masks")]
    public LayerMask playerLayer;
    public LayerMask groundLayer;
    public LayerMask playerDamageLayer;

    // Core Components
    [HideInInspector] public Rigidbody2D RB;
    [HideInInspector] public Animator Anim;
    [HideInInspector] public BoxCollider2D Collider;
    [HideInInspector] public Health Health;

    // FSM
    public BossStateMachine StateMachine { get; private set; }

    // Runtime variables
    public Transform Player { get; private set; }
    public int FacingDirection { get; private set; } = -1; // -1 = left, 1 = right
    private Vector3 initScale;

    // Timers
    public float StunTimer;
    public bool IsAttacking { get; set; } = false;
    public bool IsDashing { get; set; } = false;

    // Death flag — prevents state machine from running after death
    public bool IsDead { get; private set; } = false;

    // Debugging
    public bool DebugMode = true;

    // ---- Heuristic Adaptation ----
    private AdaptationProfile activeProfile;
    public void ApplyAdaptationProfile(AdaptationProfile profile) { activeProfile = profile; }
    public AdaptationProfile ActiveProfile => activeProfile;

    // ---- AI Decision Engine (found on same GameObject, may be null) ----
    public AIDecisionEngine DecisionEngine { get; private set; }

    protected virtual void Awake()
    {
        RB = GetComponent<Rigidbody2D>();
        Anim = GetComponent<Animator>();
        Collider = GetComponent<BoxCollider2D>();
        Health = GetComponent<Health>();

        if (RB != null)
        {
            RB.gravityScale = gravityScale;
            RB.freezeRotation = true;
            RB.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        initScale = transform.localScale;
        StateMachine = new BossStateMachine();

        InitializeStates();
    }

    protected virtual void Start()
    {
        FindPlayer();
        StartState();
        DecisionEngine = GetComponent<AIDecisionEngine>();
    }

    protected virtual void Update()
    {
        if (IsDead) return;

        if (Player == null) FindPlayer();
        
        UpdateTimers();
        StateMachine.Update(this);
    }

    protected virtual void FixedUpdate()
    {
        if (IsDead) return;

        StateMachine.FixedUpdate(this);
        ApplyGravity();
    }

    /// <summary>Fired once when the boss dies. Used by AISessionLogger to end the fight record.</summary>
    public event System.Action OnDied;

    /// <summary>
    /// Resets the boss for a new ML-Agents episode: full health, zero velocity,
    /// teleport to <paramref name="spawnPosition"/> (with small random jitter),
    /// and restart the FSM. Override in subclasses to reset boss-specific state.
    /// </summary>
    public virtual void Respawn(Vector3 spawnPosition)
    {
        gameObject.SetActive(true);
        enabled = true;

        IsDead = false;
        IsAttacking = false;
        IsDashing = false;
        StunTimer = 0f;

        Health.Respawn();

        RB.linearVelocity = Vector2.zero;
        RB.angularVelocity = 0f;
        RB.gravityScale = gravityScale;

        Vector2 jitter = Random.insideUnitCircle * 0.5f;
        transform.position = spawnPosition + new Vector3(jitter.x, jitter.y, 0f);

        StartState();
    }

    public void OnDeath()
    {
        if (IsDead) return;
        IsDead = true;
        IsAttacking = false;
        Stop();
        OnDied?.Invoke();
    }

    /// <summary>
    /// Override this to initialize your specific boss states.
    /// </summary>
    protected abstract void InitializeStates();

    /// <summary>
    /// Override this to begin the FSM with a starting state.
    /// </summary>
    protected abstract void StartState();

    private void FindPlayer()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            Player = playerObj.transform;
    }

    private void ApplyGravity()
    {
        if (RB == null) return;

        if (IsDashing)
        {
            RB.gravityScale = 0f;
            return;
        }

        if (RB.linearVelocity.y < 0)
        {
            RB.gravityScale = gravityScale * fallGravityMultiplier;
        }
        else
        {
            RB.gravityScale = gravityScale;
        }

        if (RB.linearVelocity.y < -maxFallSpeed)
        {
            RB.linearVelocity = new Vector2(RB.linearVelocity.x, -maxFallSpeed);
        }
    }

    private void UpdateTimers()
    {
        if (StunTimer > 0) StunTimer -= Time.deltaTime;
        UpdateBossTimers();
    }

    /// <summary>
    /// Override this to update any boss-specific timers (e.g., attack cooldowns).
    /// </summary>
    protected virtual void UpdateBossTimers() { }

    #region Detection & Distance

    public float GetDistanceToPlayer()
    {
        if (Player == null) return Mathf.Infinity;
        return Vector2.Distance(transform.position, Player.position);
    }

    public bool PlayerInDetectionRange() => GetDistanceToPlayer() <= detectionRange;
    public bool PlayerInAttackRange() => GetDistanceToPlayer() <= attackRange;
    public bool PlayerTooClose() => GetDistanceToPlayer() <= retreatRange;

    public int GetDirectionToPlayer()
    {
        if (Player == null) return FacingDirection;
        return Player.position.x > transform.position.x ? 1 : -1;
    }

    #endregion

    #region Movement

    public void MoveInDirection(int direction, float speed)
    {
        FaceDirection(direction);
        if (RB != null)
        {
            RB.linearVelocity = new Vector2(direction * speed, RB.linearVelocity.y);
        }
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
            RB.linearVelocity = new Vector2(0, RB.linearVelocity.y);
        }
    }

    public bool IsGrounded()
    {
        if (Collider == null) return false;
        
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

    #region Animation Helpers
    
    public void SetMoving(bool isMoving)
    {
        if (Anim != null && Anim.GetBool("moving") != isMoving)
        {
            Anim.SetBool("moving", isMoving);
        }
    }

    public void TriggerHurt()
    {
        if (Anim != null) Anim.SetTrigger("hurt");
    }

    #endregion
}
