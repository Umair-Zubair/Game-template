using UnityEngine;

/// <summary>
/// Abstract base class for all 6 player FSM bot profiles.
///
/// SETUP: Place a derived FSM script (e.g. WarriorAggressiveFSM) on the same
/// GameObject as PlayerController, MeleeAttack / BlobRangedAttack, Health,
/// and PlayerStamina. Enable the component for AI training; disable for human play.
///
/// On Enable  → sets PlayerController.IsAIControlled = true so AI inputs
///              are used instead of Unity's Input system.
/// On Disable → restores normal input and zeroes all AI overrides.
///
/// Mirrors the BossController / VoidbornGoddessController pattern:
///   - Component references are public fields (like BossController)
///   - Helper methods wrap common AI actions (MoveToward, RequestJump, etc.)
///   - Derived classes implement InitializeStates() + StartFSM()
/// </summary>
public abstract class PlayerFSMController : MonoBehaviour
{
    [Header("FSM Configuration")]
    [Tooltip("Tag of the target this bot fights against.")]
    [SerializeField] private string targetTag = "Enemy";

    // ── Component References (auto-found on same GameObject) ───────────────
    [HideInInspector] public PlayerController PC;
    [HideInInspector] public PlayerStamina Stamina;
    [HideInInspector] public Health Health;
    [HideInInspector] public MeleeAttack MeleeAttack;          // non-null on Warrior
    [HideInInspector] public BlobRangedAttack RangedAttack;    // non-null on Hunter

    // ── FSM ────────────────────────────────────────────────────────────────
    public PlayerFSMStateMachine FSM { get; private set; }

    // ── Runtime ────────────────────────────────────────────────────────────
    public Transform Target { get; private set; }

    // Jump hold timer: RequestJump() sets a duration; PC.AIJumpHeld stays true
    // until it elapses, producing a variable-height jump.
    private float jumpHoldTimer;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        PC           = GetComponent<PlayerController>();
        Stamina      = GetComponent<PlayerStamina>();
        Health       = GetComponent<Health>();
        MeleeAttack  = GetComponent<MeleeAttack>();
        RangedAttack = GetComponent<BlobRangedAttack>();
        FSM          = new PlayerFSMStateMachine();
    }

    protected virtual void OnEnable()
    {
        if (PC != null) PC.IsAIControlled = true;
        FindTarget();
        InitializeStates();
        StartFSM();
    }

    protected virtual void OnDisable()
    {
        if (PC == null) return;
        PC.IsAIControlled    = false;
        PC.AIHorizontalInput = 0f;
        PC.AIJumpPressed     = false;
        PC.AIJumpHeld        = false;
    }

    protected virtual void Update()
    {
        // Stop FSM when PlayerController is disabled (character is dead)
        if (PC == null || !PC.enabled) return;

        if (Target == null) FindTarget();

        // Tick jump-hold duration so the bot releases the jump button naturally
        if (jumpHoldTimer > 0f)
        {
            jumpHoldTimer -= Time.deltaTime;
            PC.AIJumpHeld = jumpHoldTimer > 0f;
        }

        FSM.Update(this);
    }

    private void FindTarget()
    {
        GameObject go = GameObject.FindGameObjectWithTag(targetTag);
        if (go != null) Target = go.transform;
    }

    // ── Abstract ───────────────────────────────────────────────────────────

    /// <summary>Create and assign all state instances here.</summary>
    protected abstract void InitializeStates();

    /// <summary>Call FSM.Initialize() with the starting state here.</summary>
    protected abstract void StartFSM();

    // ── Distance / Direction ───────────────────────────────────────────────

    public float DistanceToTarget()
    {
        if (Target == null) return Mathf.Infinity;
        return Vector2.Distance(transform.position, Target.position);
    }

    /// <summary>Returns 1 if target is to the right, -1 if to the left.</summary>
    public int DirectionToTarget()
    {
        if (Target == null) return PC != null ? PC.FacingDirection : 1;
        return Target.position.x > transform.position.x ? 1 : -1;
    }

    /// <summary>Positive = target is above; negative = target is below.</summary>
    public float HeightDifferenceToTarget()
    {
        if (Target == null) return 0f;
        return Target.position.y - transform.position.y;
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public bool IsGrounded() => PC != null && PC.IsGrounded();

    /// <summary>Stamina as a 0–1 ratio (1 = full).</summary>
    public float StaminaRatio() => Stamina != null ? Stamina.StaminaRatio : 1f;

    /// <summary>Health as a 0–1 ratio (1 = full).</summary>
    public float HealthRatio() => Health != null ? Health.currentHealth / Health.MaxHealth : 1f;

    // ── Movement ───────────────────────────────────────────────────────────

    /// <summary>Move at full speed toward the target.</summary>
    public void MoveToward()
    {
        if (PC != null) PC.AIHorizontalInput = DirectionToTarget();
    }

    /// <summary>Move toward the target at a fraction of full speed (0–1).</summary>
    public void MoveTowardAt(float speedFraction)
    {
        if (PC != null)
            PC.AIHorizontalInput = DirectionToTarget() * Mathf.Clamp01(speedFraction);
    }

    /// <summary>Move away from the target at full speed.</summary>
    public void MoveAway()
    {
        if (PC != null) PC.AIHorizontalInput = -DirectionToTarget();
    }

    public void StopMoving()
    {
        if (PC != null) PC.AIHorizontalInput = 0f;
    }

    /// <summary>Face the target without changing movement.</summary>
    public void FaceTarget()
    {
        if (PC != null && Target != null) PC.ForceFlip(DirectionToTarget());
    }

    // ── Jumping ────────────────────────────────────────────────────────────

    /// <summary>
    /// Request a jump. holdDuration controls height:
    ///   0.30f = full height jump
    ///   0.05f = short hop
    /// </summary>
    public void RequestJump(float holdDuration = 0.3f)
    {
        if (PC == null) return;
        PC.AIJumpPressed = true;
        jumpHoldTimer    = holdDuration;
        PC.AIJumpHeld    = true;
    }

    // ── Attacking ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the attack-request flag for ONE frame.
    /// Call every frame to hold down the attack button (aggressive/combo behavior).
    /// Call once for a single shot/hit.
    /// The underlying attack script (MeleeAttack / BlobRangedAttack) consumes
    /// the flag and handles cooldowns internally.
    /// </summary>
    public void RequestAttack()
    {
        if (MeleeAttack  != null) MeleeAttack.AIAttackRequested  = true;
        if (RangedAttack != null) RangedAttack.AIAttackRequested = true;
    }
}
