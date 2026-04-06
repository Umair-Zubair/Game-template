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

    [Tooltip("How fast horizontal input ramps to the target value (higher = snappier, lower = smoother). 5-7 feels human.")]
    [SerializeField] private float inputSmoothSpeed = 6f;

    // ── Component References (auto-found on same GameObject) ───────────────
    [HideInInspector] public PlayerController PC;
    [HideInInspector] public PlayerStamina Stamina;
    [HideInInspector] public Health Health;
    [HideInInspector] public MeleeAttack MeleeAttack;
    [HideInInspector] public BlobRangedAttack RangedAttack;

    // ── FSM ────────────────────────────────────────────────────────────────
    public PlayerFSMStateMachine FSM { get; private set; }

    // ── Runtime ────────────────────────────────────────────────────────────
    public Transform Target { get; private set; }

    // Input smoothing — states set targetInput; Update() lerps smoothedInput
    // toward it each frame so direction changes accelerate/decelerate naturally.
    private float targetInput;
    private float smoothedInput;

    // Jump hold timer
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
        targetInput  = 0f;
        smoothedInput = 0f;
        if (PC != null) PC.IsAIControlled = true;
        FindTarget();
        InitializeStates();
        StartFSM();
    }

    protected virtual void OnDisable()
    {
        targetInput  = 0f;
        smoothedInput = 0f;
        if (PC == null) return;
        PC.IsAIControlled    = false;
        PC.AIHorizontalInput = 0f;
        PC.AIJumpPressed     = false;
        PC.AIJumpHeld        = false;
    }

    protected virtual void Update()
    {
        if (PC == null || !PC.enabled) return;

        if (Target == null) FindTarget();

        // Jump hold
        if (jumpHoldTimer > 0f)
        {
            jumpHoldTimer -= Time.deltaTime;
            PC.AIJumpHeld = jumpHoldTimer > 0f;
        }

        // Let the current state set targetInput via the movement helpers
        FSM.Update(this);

        // Smoothly ramp horizontal input toward the target — this is what makes
        // direction changes feel like a human rather than an instant state flip.
        smoothedInput        = Mathf.MoveTowards(smoothedInput, targetInput, inputSmoothSpeed * Time.deltaTime);
        PC.AIHorizontalInput = smoothedInput;
    }

    private void FindTarget()
    {
        GameObject go = GameObject.FindGameObjectWithTag(targetTag);
        if (go != null) Target = go.transform;
    }

    // ── Abstract ───────────────────────────────────────────────────────────

    protected abstract void InitializeStates();
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

    public float HeightDifferenceToTarget()
    {
        if (Target == null) return 0f;
        return Target.position.y - transform.position.y;
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public bool IsGrounded() => PC != null && PC.IsGrounded();

    public float StaminaRatio() => Stamina != null ? Stamina.StaminaRatio : 1f;

    public float HealthRatio() => Health != null ? Health.currentHealth / Health.MaxHealth : 1f;

    // ── Movement ───────────────────────────────────────────────────────────
    // These set targetInput only. The actual PC.AIHorizontalInput is applied
    // at the end of Update() with smoothing applied. This means all direction
    // changes — whether the bot turns around, decelerates, or stops —
    // are visually gradual rather than instant snaps.

    public void MoveToward()                    => targetInput = DirectionToTarget();
    public void MoveTowardAt(float fraction)    => targetInput = DirectionToTarget() * Mathf.Clamp01(fraction);
    public void MoveAway()                      => targetInput = -DirectionToTarget();
    public void StopMoving()                    => targetInput = 0f;

    /// <summary>Face the target without affecting movement input.</summary>
    public void FaceTarget()
    {
        if (PC != null && Target != null) PC.ForceFlip(DirectionToTarget());
    }

    // ── Jumping ────────────────────────────────────────────────────────────

    /// <summary>0.30f = full height, 0.05f = short hop.</summary>
    public void RequestJump(float holdDuration = 0.3f)
    {
        if (PC == null) return;
        PC.AIJumpPressed = true;
        jumpHoldTimer    = holdDuration;
        PC.AIJumpHeld    = true;
    }

    // ── Attacking ─────────────────────────────────────────────────────────

    /// <summary>
    /// Call every frame to simulate holding the attack button (chains combos).
    /// Call once for a single hit.
    /// </summary>
    public void RequestAttack()
    {
        if (MeleeAttack  != null) MeleeAttack.AIAttackRequested  = true;
        if (RangedAttack != null) RangedAttack.AIAttackRequested = true;
    }
}
