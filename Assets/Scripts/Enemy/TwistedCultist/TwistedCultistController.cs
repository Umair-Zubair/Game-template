using System.Collections.Generic;
using UnityEngine;

public class TwistedCultistController : BossController
{
    [Header("Twisted Cultist - Combat")]
    [Min(0f)] public float pressSpeed = 2.6f;
    [Min(0f)] public float evadeSpeed = 2.8f;
    [Min(0f)] public float reactDelayMin = 0.07f;
    [Min(0f)] public float reactDelayMax = 0.18f;
    [Range(0f, 1f)] public float postAttackReengageChance = 0.55f;
    [Min(0f)] public float lowHealthRetreatThreshold = 0.35f;
    [Min(0f)] public float hurtMemoryDuration = 1.5f;
    [Tooltip("Hysteresis deadzone (world units) to prevent flip-flicker at collider center boundary")]
    [Min(0f)] public float directionDeadzone = 0.5f;

    [Header("Twisted Cultist - Ranged Arm Attack")]
    public Transform rangedAttackPoint;
    public Vector2 rangedAttackOffset = new Vector2(1.1f, 0.25f);
    [Min(0f)] public float rangedAttackRadius = 1.35f;
    [Min(0)] public int rangedAttackDamage = 1;
    [Min(0f)] public float rangedAttackCooldown = 2.2f;
    [Min(0f)] public float attackFallbackHitDelay = 0.30f;
    [Min(0f)] public float attackFallbackEndDelay = 0.95f;

    [Header("Twisted Cultist - Spawn")]
    [Min(0f)] public float spawnFallbackDuration = 0.9f;

    [Header("Twisted Cultist - Debug")]
    public bool logMissingAnimationStates = false;

    // FSM states
    public TwistedCultistSpawnState SpawnState { get; private set; }
    public TwistedCultistIdleState IdleState { get; private set; }
    public TwistedCultistPressState PressState { get; private set; }
    public TwistedCultistReactState ReactState { get; private set; }
    public TwistedCultistRangedAttackState RangedAttackState { get; private set; }
    public TwistedCultistHurtState HurtState { get; private set; }

    public float RangedAttackTimer { get; private set; }

    public event System.Action OnRangedAttackAttempted;
    public event System.Action<bool> OnRangedAttackResult;

    private SpriteRenderer SR;
    private float lastDamageTime = -999f;
    private bool collisionIgnored = false;

    // Tracks the "true" direction to player independently of FacingDirection.
    // This breaks the feedback loop where MoveInDirection → FaceDirection → changes
    // FacingDirection → next GetDirectionToPlayer returns opposite → oscillation.
    private int resolvedDirection = -1;

    public bool CanUseRangedAttack => RangedAttackTimer <= 0f && !IsAttacking;
    public bool RecentlyDamaged => Time.time - lastDamageTime <= hurtMemoryDuration;
    public float HealthRatio => Health != null && Health.MaxHealth > 0f
        ? Health.currentHealth / Health.MaxHealth : 1f;
    public bool IsLowHealth => HealthRatio <= lowHealthRetreatThreshold;

    protected override void Awake()
    {
        base.Awake();
        SR = GetComponent<SpriteRenderer>();
    }

    // Sprite faces LEFT natively; use flipX instead of negative scale to avoid
    // visual jump from off-center pivot at scale 4.
    public override void FaceDirection(int direction)
    {
        if (direction == 0) return;
        FacingDirection = direction;
        if (SR != null)
            SR.flipX = direction == 1;
    }

    protected override void InitializeStates()
    {
        SpawnState = new TwistedCultistSpawnState();
        IdleState = new TwistedCultistIdleState();
        PressState = new TwistedCultistPressState();
        ReactState = new TwistedCultistReactState();
        RangedAttackState = new TwistedCultistRangedAttackState();
        HurtState = new TwistedCultistHurtState();

        RangedAttackTimer = rangedAttackCooldown;
    }

    protected override void StartState()
    {
        StateMachine.Initialize(SpawnState, this);
    }

    // Collider is offset +1.14 world units right of transform (sprite pivot is at left edge).
    // Use collider center for direction/distance so the cultist doesn't flip when the
    // player touches the left edge of the collider before reaching the visual center.
    public override float GetDistanceToPlayer()
    {
        if (Player == null || Collider == null) return Mathf.Infinity;
        return Vector2.Distance(Collider.bounds.center, Player.position);
    }

    /// <summary>
    /// Direction to player with hysteresis, stored in <see cref="resolvedDirection"/>
    /// which is independent of <see cref="BossController.FacingDirection"/>.
    /// Only changes when the player is at least <see cref="directionDeadzone"/>
    /// world-units past the collider center. Inside the deadzone the last resolved
    /// value is held, which prevents the flip-flicker feedback loop caused by
    /// MoveInDirection ↔ FaceDirection ↔ FacingDirection ↔ GetDirectionToPlayer.
    /// </summary>
    public override int GetDirectionToPlayer()
    {
        if (Player == null || Collider == null) return resolvedDirection;

        float diff = Player.position.x - Collider.bounds.center.x;

        // Only update resolvedDirection when clearly on one side
        if (diff > directionDeadzone)
            resolvedDirection = 1;
        else if (diff < -directionDeadzone)
            resolvedDirection = -1;

        // Inside deadzone: resolvedDirection is unchanged (no flip)
        return resolvedDirection;
    }

    /// <summary>
    /// Moves the cultist in <paramref name="direction"/> at <paramref name="speed"/>
    /// WITHOUT changing FacingDirection or flipping the sprite.
    /// Used for retreat so the cultist backs away while still facing the player.
    /// </summary>
    public void MoveWithoutFacing(int direction, float speed)
    {
        if (RB != null)
            RB.linearVelocity = new Vector2(direction * speed, RB.linearVelocity.y);
    }

    // True while the player's X is inside the cultist's collider horizontal span.
    // Used to suppress FacePlayer() during pass-through so no mid-body flip occurs.
    public bool IsPlayerOverlapping()
    {
        if (Player == null || Collider == null) return false;
        float px = Player.position.x;
        return px >= Collider.bounds.min.x && px <= Collider.bounds.max.x;
    }

    protected override void Start()
    {
        base.Start();
        if (Health != null)
            Health.OnDamageTaken += OnTakeDamage;
        OnDied += OnDeathAnimation;
        TryIgnorePlayerCollision();
    }

    private void OnDeathAnimation()
    {
        SetWalking(false);
        Anim?.SetTrigger("death");
    }

    protected override void Update()
    {
        base.Update();
        TryIgnorePlayerCollision();
    }

    // Per-collider only — does not touch the global layer matrix so VoidbornGoddess
    // and other layer-11 enemies are unaffected.
    private void TryIgnorePlayerCollision()
    {
        if (collisionIgnored || Player == null || Collider == null) return;
        int playerLayer = LayerMask.NameToLayer("Player");
        foreach (Collider2D pc in Player.root.GetComponentsInChildren<Collider2D>(true))
        {
            if (pc.gameObject.layer == playerLayer)
                Physics2D.IgnoreCollision(Collider, pc, true);
        }
        collisionIgnored = true;
    }

    private void OnDestroy()
    {
        if (Health != null)
            Health.OnDamageTaken -= OnTakeDamage;
    }

    public void OnTakeDamage(float damage)
    {
        if (IsDead || Health == null || Health.currentHealth <= 0f) return;
        lastDamageTime = Time.time;
        if (IsAttacking) return;
        StateMachine.ChangeState(HurtState, this);
    }

    protected override void UpdateBossTimers()
    {
        if (RangedAttackTimer > 0f)
            RangedAttackTimer -= Time.deltaTime;
    }

    public void ResetRangedAttackTimer() => RangedAttackTimer = rangedAttackCooldown;

    // --- Animation helpers ---

    public void SetWalking(bool walking)
    {
        if (Anim != null) Anim.SetBool("walk", walking);
    }

    public void TriggerAttackAnimation()
    {
        if (Anim != null) Anim.SetTrigger("attack");
    }

    // --- Combat ---

    public void PerformExtendedArmAttack()
    {
        Vector2 origin = GetRangedAttackOrigin();
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, rangedAttackRadius, playerDamageLayer);
        OnRangedAttackAttempted?.Invoke();

        if (hits == null || hits.Length == 0)
        {
            OnRangedAttackResult?.Invoke(false);
            return;
        }

        bool landed = false;
        HashSet<Health> damaged = new HashSet<Health>();
        foreach (Collider2D hit in hits)
        {
            Health h = hit.GetComponent<Health>();
            if (h == null || damaged.Contains(h)) continue;
            h.TakeDamage(rangedAttackDamage);
            damaged.Add(h);
            landed = true;
        }
        OnRangedAttackResult?.Invoke(landed);
    }

    public Vector2 GetRangedAttackOrigin()
    {
        if (rangedAttackPoint != null) return rangedAttackPoint.position;
        float facing = FacingDirection == 0 ? 1f : FacingDirection;
        return (Vector2)transform.position + new Vector2(rangedAttackOffset.x * facing, rangedAttackOffset.y);
    }

    public override void Respawn(Vector3 spawnPosition)
    {
        base.Respawn(spawnPosition);
        RangedAttackTimer = rangedAttackCooldown;
        lastDamageTime = -999f;
    }

    public void Deactivate() => gameObject.SetActive(false);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(GetRangedAttackOrigin(), rangedAttackRadius);
    }
}
