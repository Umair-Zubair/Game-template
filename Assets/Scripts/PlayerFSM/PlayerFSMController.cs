using UnityEngine;

/// <summary>
/// Abstract base class for all player FSM bot profiles.
///
/// SETUP: Place a derived FSM script (e.g. WarriorAggressiveFSM) on the same
/// GameObject as PlayerController, MeleeAttack / BlobRangedAttack, Health,
/// and PlayerStamina. Enable the component for AI training; disable for human play.
///
/// On Enable  → sets PlayerController.IsAIControlled = true so AI inputs
///              are used instead of Unity's Input system.
/// On Disable → restores normal input and zeroes all AI overrides.
/// </summary>
public abstract class PlayerFSMController : MonoBehaviour
{
    [Header("FSM Configuration")]
    [SerializeField] private string targetTag = "Enemy";

    [Tooltip("How fast horizontal input ramps to the target value. 5-7 feels human.")]
    [SerializeField] private float inputSmoothSpeed = 6f;

    [Header("Boss Awareness")]
    [Tooltip("Distance within which a boss melee attack triggers evasion.")]
    [SerializeField] protected float dangerRange = 3.0f;

    [Tooltip("Reaction delay range (seconds) before acting on artillery — simulates human perception.")]
    [SerializeField] private float reactionDelayMin = 0.10f;
    [SerializeField] private float reactionDelayMax = 0.26f;

    // ── Component References ───────────────────────────────────────────────
    [HideInInspector] public PlayerController PC;
    [HideInInspector] public PlayerStamina Stamina;
    [HideInInspector] public Health Health;
    [HideInInspector] public MeleeAttack MeleeAttack;
    [HideInInspector] public BlobRangedAttack RangedAttack;

    // ── FSM ────────────────────────────────────────────────────────────────
    public PlayerFSMStateMachine FSM { get; private set; }

    // ── Runtime ────────────────────────────────────────────────────────────
    public Transform Target { get; private set; }

    private float targetInput;
    private float smoothedInput;
    private float jumpHoldTimer;

    // ── Boss awareness ─────────────────────────────────────────────────────
    private BossController            _bossRef;
    private VoidbornGoddessController _goddessRef;

    // Artillery evade — delayed so the bot doesn't react before seeing the projectile
    private bool  _artilleryPending;
    private float _artilleryTimer;

    /// <summary>True for one Update cycle after the reaction delay expires. Consume with ConsumeArtilleryEvade().</summary>
    public bool ArtilleryEvadeRequested { get; private set; }
    public void ConsumeArtilleryEvade() => ArtilleryEvadeRequested = false;

    // Damage tracking
    private float _lastDamageTime = -999f;
    public  bool  RecentlyHurt => (Time.time - _lastDamageTime) < 2.0f;
    public  float TimeSinceDamage => Time.time - _lastDamageTime;

    // Boss state queries
    public bool  BossIsAttacking => _bossRef != null && _bossRef.IsAttacking && !_bossRef.IsDead;
    public bool  BossIsDead      => _bossRef == null || _bossRef.IsDead;

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

    protected virtual void Start()
    {
        _bossRef    = FindFirstObjectByType<BossController>();
        _goddessRef = FindFirstObjectByType<VoidbornGoddessController>();

        if (Health      != null) Health.OnDamageTaken              += _ => _lastDamageTime = Time.time;
        if (_goddessRef != null) _goddessRef.OnArtilleryLaunched   += OnArtilleryDetected;
    }

    protected virtual void OnDestroy()
    {
        if (_goddessRef != null) _goddessRef.OnArtilleryLaunched -= OnArtilleryDetected;
    }

    protected virtual void OnEnable()
    {
        targetInput   = 0f;
        smoothedInput = 0f;
        if (PC != null) PC.IsAIControlled = true;
        FindTarget();
        InitializeStates();
        StartFSM();
    }

    protected virtual void OnDisable()
    {
        targetInput   = 0f;
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
            PC.AIJumpHeld  = jumpHoldTimer > 0f;
        }

        // Artillery reaction delay
        if (_artilleryPending)
        {
            _artilleryTimer -= Time.deltaTime;
            if (_artilleryTimer <= 0f)
            {
                _artilleryPending        = false;
                ArtilleryEvadeRequested  = true;
            }
        }

        FSM.Update(this);

        smoothedInput        = Mathf.MoveTowards(smoothedInput, targetInput, inputSmoothSpeed * Time.deltaTime);
        PC.AIHorizontalInput = smoothedInput;
    }

    private void OnArtilleryDetected()
    {
        if (_artilleryPending || ArtilleryEvadeRequested) return;
        _artilleryPending = true;
        _artilleryTimer   = Random.Range(reactionDelayMin, reactionDelayMax);
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

    public bool  IsGrounded()   => PC != null && PC.IsGrounded();
    public float StaminaRatio() => Stamina != null ? Stamina.StaminaRatio : 1f;
    public float HealthRatio()  => Health  != null ? Health.currentHealth / Health.MaxHealth : 1f;

    // ── Movement ───────────────────────────────────────────────────────────

    public void MoveToward()                 => targetInput = DirectionToTarget();
    public void MoveTowardAt(float fraction) => targetInput = DirectionToTarget() * Mathf.Clamp01(fraction);
    public void MoveAway()                   => targetInput = -DirectionToTarget();
    public void StopMoving()                 => targetInput = 0f;

    public void FaceTarget()
    {
        if (PC != null && Target != null) PC.ForceFlip(DirectionToTarget());
    }

    // ── Jumping ────────────────────────────────────────────────────────────

    public void RequestJump(float holdDuration = 0.3f)
    {
        if (PC == null) return;
        PC.AIJumpPressed = true;
        jumpHoldTimer    = holdDuration;
        PC.AIJumpHeld    = true;
    }

    // ── Attacking ─────────────────────────────────────────────────────────

    public void RequestAttack()
    {
        if (MeleeAttack  != null) MeleeAttack.AIAttackRequested  = true;
        if (RangedAttack != null) RangedAttack.AIAttackRequested = true;
    }
}
