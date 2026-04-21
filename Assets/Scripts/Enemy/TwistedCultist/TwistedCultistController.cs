using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss-style FSM controller for the Twisted Cultist enemy.
/// Uses the same base architecture as VoidbornGoddessController (BossController + IBossState).
/// </summary>
public class TwistedCultistController : BossController
{
    private const string BaseLayerPrefix = "Base Layer.";

    [Header("Twisted Cultist - Heuristic Combat")]
    [Min(0f)] public float pressSpeed = 2.6f;
    [Min(0f)] public float evadeSpeed = 2.8f;
    [Min(0f)] public float minComfortRange = 1.6f;
    [Min(0f)] public float maxComfortRange = 3.4f;
    [Min(0f)] public float reengageRange = 4.8f;
    [Min(0f)] public float reactDelayMin = 0.07f;
    [Min(0f)] public float reactDelayMax = 0.18f;
    [Range(0f, 1f)] public float jumpEngageChance = 0.28f;
    [Range(0f, 1f)] public float jumpEvadeChance = 0.65f;
    [Range(0f, 1f)] public float postAttackReengageChance = 0.55f;
    [Min(0f)] public float lowHealthRetreatThreshold = 0.35f;
    [Min(0f)] public float hurtMemoryDuration = 1.5f;

    [Header("Twisted Cultist - Ranged Arm Attack")]
    public Transform rangedAttackPoint;
    public Vector2 rangedAttackOffset = new Vector2(1.1f, 0.25f);
    [Min(0f)] public float rangedAttackRadius = 1.35f;
    [Min(0)] public int rangedAttackDamage = 1;
    [Min(0f)] public float rangedAttackCooldown = 2.2f;
    [Range(0f, 1f)] public float attackHitNormalizedTime = 0.42f;
    [Min(0f)] public float attackFallbackHitDelay = 0.30f;
    [Min(0f)] public float attackFallbackEndDelay = 0.95f;

    [Header("Twisted Cultist - Jump")]
    [Min(0f)] public float jumpForce = 7f;
    [Min(0f)] public float jumpForwardSpeed = 2f;
    [Min(0f)] public float jumpBackwardSpeed = 2.6f;
    [Min(0f)] public float jumpCooldown = 2.4f;
    [Min(0f)] public float jumpHeightThreshold = 1.3f;
    [Min(0f)] public float jumpDistanceThreshold = 4f;
    [Min(0f)] public float jumpMaxDuration = 1.25f;

    [Header("Twisted Cultist - Evade")]
    [Min(0f)] public float evadeDurationMin = 0.35f;
    [Min(0f)] public float evadeDurationMax = 0.65f;

    [Header("Twisted Cultist - Spawn")]
    public string spawnStateName = "Twisted";
    [Min(0f)] public float spawnFallbackDuration = 0.9f;

    [Header("Twisted Cultist - Animation States")]
    public string idleStateName = "Idle";
    public string moveStateName = "Walk";
    public string attackStateName = "Attack";
    public string jumpStateName = "Jump";
    public string fallStateName = "Fall";
    public string hurtStateName = "Hurt";

    [Header("Twisted Cultist - Debug")]
    public bool logMissingAnimationStates = false;

    // FSM states
    public TwistedCultistSpawnState SpawnState { get; private set; }
    public TwistedCultistIdleState IdleState { get; private set; }
    public TwistedCultistPressState PressState { get; private set; }
    public TwistedCultistReactState ReactState { get; private set; }
    public TwistedCultistRangedAttackState RangedAttackState { get; private set; }
    public TwistedCultistJumpRepositionState JumpRepositionState { get; private set; }
    public TwistedCultistEvadeState EvadeState { get; private set; }
    public TwistedCultistHurtState HurtState { get; private set; }

    // Runtime timers
    public float RangedAttackTimer { get; private set; }
    public float JumpTimer { get; private set; }

    // Optional evaluation hooks
    public event System.Action OnRangedAttackAttempted;
    public event System.Action<bool> OnRangedAttackResult;

    private float lastDamageTime = -999f;
    private bool queuedJumpAway;
    private string lastMissingAnimationState = string.Empty;

    public bool CanUseRangedAttack => RangedAttackTimer <= 0f && !IsAttacking;
    public bool CanJump => JumpTimer <= 0f && !IsAttacking && IsGrounded();
    public bool RecentlyDamaged => Time.time - lastDamageTime <= hurtMemoryDuration;
    public float HealthRatio => Health != null && Health.MaxHealth > 0f
        ? Health.currentHealth / Health.MaxHealth
        : 1f;
    public bool IsLowHealth => HealthRatio <= lowHealthRetreatThreshold;

    protected override void InitializeStates()
    {
        SpawnState = new TwistedCultistSpawnState();
        IdleState = new TwistedCultistIdleState();
        PressState = new TwistedCultistPressState();
        ReactState = new TwistedCultistReactState();
        RangedAttackState = new TwistedCultistRangedAttackState();
        JumpRepositionState = new TwistedCultistJumpRepositionState();
        EvadeState = new TwistedCultistEvadeState();
        HurtState = new TwistedCultistHurtState();

        RangedAttackTimer = rangedAttackCooldown;
        JumpTimer = jumpCooldown;
    }

    protected override void StartState()
    {
        StateMachine.Initialize(SpawnState, this);
    }

    protected override void Start()
    {
        base.Start();
        if (Health != null)
        {
            Health.OnDamageTaken += OnTakeDamage;
        }
    }

    private void OnDestroy()
    {
        if (Health != null)
        {
            Health.OnDamageTaken -= OnTakeDamage;
        }
    }

    public void OnTakeDamage(float damage)
    {
        if (IsDead) return;
        if (Health == null) return;
        if (Health.currentHealth <= 0f) return;

        lastDamageTime = Time.time;
        if (IsAttacking) return;

        StateMachine.ChangeState(HurtState, this);
    }

    protected override void UpdateBossTimers()
    {
        if (RangedAttackTimer > 0f)
        {
            RangedAttackTimer -= Time.deltaTime;
        }

        if (JumpTimer > 0f)
        {
            JumpTimer -= Time.deltaTime;
        }
    }

    public void ResetRangedAttackTimer()
    {
        RangedAttackTimer = rangedAttackCooldown;
    }

    public void ResetJumpTimer()
    {
        JumpTimer = jumpCooldown;
    }

    public bool ShouldAttemptEngageJump()
    {
        if (Player == null) return false;

        float verticalGap = Player.position.y - transform.position.y;
        if (verticalGap >= jumpHeightThreshold)
        {
            return true;
        }

        if (GetDistanceToPlayer() >= jumpDistanceThreshold)
        {
            return Random.value < jumpEngageChance;
        }

        return false;
    }

    public bool ShouldAttemptEvadeJump()
    {
        return Random.value < jumpEvadeChance;
    }

    public void QueueJumpToward()
    {
        queuedJumpAway = false;
    }

    public void QueueJumpAway()
    {
        queuedJumpAway = true;
    }

    public bool ConsumeQueuedJumpAway()
    {
        bool jumpAway = queuedJumpAway;
        queuedJumpAway = false;
        return jumpAway;
    }

    public void JumpTowardPlayer()
    {
        JumpInDirection(GetDirectionToPlayer(), jumpForwardSpeed);
    }

    public void JumpAwayFromPlayer()
    {
        JumpInDirection(-GetDirectionToPlayer(), jumpBackwardSpeed);
    }

    public void JumpInDirection(int direction, float horizontalSpeed)
    {
        if (RB == null || !IsGrounded()) return;

        int dir = direction >= 0 ? 1 : -1;
        FaceDirection(dir);
        RB.linearVelocity = new Vector2(dir * horizontalSpeed, jumpForce);
    }

    public void PerformExtendedArmAttack()
    {
        Vector2 attackOrigin = GetRangedAttackOrigin();
        Collider2D[] hits = Physics2D.OverlapCircleAll(attackOrigin, rangedAttackRadius, playerDamageLayer);
        if (hits == null || hits.Length == 0)
        {
            OnRangedAttackAttempted?.Invoke();
            OnRangedAttackResult?.Invoke(false);
            return;
        }

        OnRangedAttackAttempted?.Invoke();
        bool landed = false;
        HashSet<Health> damagedTargets = new HashSet<Health>();

        foreach (Collider2D hit in hits)
        {
            Health playerHealth = hit.GetComponent<Health>();
            if (playerHealth == null || damagedTargets.Contains(playerHealth)) continue;

            playerHealth.TakeDamage(rangedAttackDamage);
            damagedTargets.Add(playerHealth);
            landed = true;
        }

        OnRangedAttackResult?.Invoke(landed);
    }

    public Vector2 GetRangedAttackOrigin()
    {
        if (rangedAttackPoint != null)
        {
            return rangedAttackPoint.position;
        }

        float facing = FacingDirection == 0 ? 1f : FacingDirection;
        return (Vector2)transform.position + new Vector2(rangedAttackOffset.x * facing, rangedAttackOffset.y);
    }

    public void PlayAnimationState(string stateName, bool forceRestart = false)
    {
        if (Anim == null) return;
        string resolvedStateName = ResolveAnimationStateName(stateName);
        if (string.IsNullOrEmpty(resolvedStateName)) return;

        bool alreadyPlaying = IsPlayingAnimationState(stateName);
        if (!forceRestart && alreadyPlaying)
        {
            return;
        }

        Anim.Play(resolvedStateName, 0, 0f);
    }

    public bool IsPlayingAnimationState(string stateName)
    {
        if (Anim == null || string.IsNullOrWhiteSpace(stateName)) return false;

        AnimatorStateInfo info = Anim.GetCurrentAnimatorStateInfo(0);
        return info.IsName(stateName) || info.IsName(BaseLayerPrefix + stateName);
    }

    public bool HasAnimationReached(string stateName, float normalizedThreshold)
    {
        if (!IsPlayingAnimationState(stateName) || Anim == null) return false;
        if (Anim.IsInTransition(0)) return false;

        AnimatorStateInfo info = Anim.GetCurrentAnimatorStateInfo(0);
        return info.normalizedTime >= normalizedThreshold;
    }

    public bool HasAnimationFinished(string stateName, float normalizedThreshold = 0.98f)
    {
        return HasAnimationReached(stateName, normalizedThreshold);
    }

    public override void Respawn(Vector3 spawnPosition)
    {
        base.Respawn(spawnPosition);
        RangedAttackTimer = rangedAttackCooldown;
        JumpTimer = jumpCooldown;
        lastDamageTime = -999f;
        queuedJumpAway = false;
        lastMissingAnimationState = string.Empty;
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
    }

    private string ResolveAnimationStateName(string stateName)
    {
        if (Anim == null || string.IsNullOrWhiteSpace(stateName))
        {
            return null;
        }

        int shortHash = Animator.StringToHash(stateName);
        if (Anim.HasState(0, shortHash))
        {
            return stateName;
        }

        string fullPath = BaseLayerPrefix + stateName;
        int fullPathHash = Animator.StringToHash(fullPath);
        if (Anim.HasState(0, fullPathHash))
        {
            return fullPath;
        }

        if (logMissingAnimationStates && DebugMode && lastMissingAnimationState != stateName)
        {
            Debug.LogWarning($"[TwistedCultist] Animation state '{stateName}' not found on animator '{Anim.runtimeAnimatorController?.name}'.");
            lastMissingAnimationState = stateName;
        }

        return null;
    }

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
