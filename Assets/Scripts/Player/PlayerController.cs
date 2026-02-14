using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private PlayerData data;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private LayerMask wallLayer;

    [Header("References")]
    [HideInInspector] public Rigidbody2D RB;
    [HideInInspector] public Animator Anim;
    [HideInInspector] public BoxCollider2D Collider;

    // Stamina — optional: auto-found on the same GameObject
    public PlayerStamina Stamina { get; private set; }

    // State Machine
    public PlayerStateMachine StateMachine { get; private set; }

    // States
    public IdleState IdleState { get; private set; }
    public RunState RunState { get; private set; }
    public JumpState JumpState { get; private set; }
    public FallState FallState { get; private set; }
    public WallSlideState WallSlideState { get; private set; }
    public WallJumpState WallJumpState { get; private set; }

    // Input Parameters
    public float HorizontalInput { get; private set; }
    public bool JumpPressed { get; private set; } // Pressed this frame
    public bool JumpHeld { get; private set; }    // Held down

    // Timers
    public float CoyoteTimeCounter;
    public float JumpBufferCounter;
    public float WallJumpLockCounter; // Prevents horizontal input after wall jump

    // Runtime
    public int FacingDirection { get; private set; } = 1;

    private void Awake()
    {
        StateMachine = new PlayerStateMachine();
        IdleState = new IdleState();
        RunState = new RunState();
        JumpState = new JumpState();
        FallState = new FallState();
        WallSlideState = new WallSlideState();
        WallJumpState = new WallJumpState();

        RB = GetComponent<Rigidbody2D>();
        Anim = GetComponent<Animator>();
        Collider = GetComponent<BoxCollider2D>();
        Stamina = GetComponent<PlayerStamina>();  // Optional: null-safe throughout
        
        // Ensure proper setup
        RB.gravityScale = data.gravityScale;
        RB.freezeRotation = true;
        RB.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        
        // Auto-Assign Zero Friction to prevent wall sticking
        RB.sharedMaterial = Resources.Load<PhysicsMaterial2D>("ZeroFriction");
        if (RB.sharedMaterial == null)
        {
             // Fallback if not created yet (Editor script should handle it, but runtime needs it)
             // We can't create assets at runtime easily like this without editor, 
             // so we assume the user/tool ran. 
             // Or we simply create a memory-only material
             PhysicsMaterial2D mat = new PhysicsMaterial2D("ZeroFriction");
             mat.friction = 0;
             mat.bounciness = 0;
             RB.sharedMaterial = mat;
        }
    }

    private void Start()
    {
        StateMachine.Initialize(IdleState, this);
    }

    private void Update()
    {
        ProcessInput();
        UpdateTimers();
        StateMachine.Update(this);
    }

    private void FixedUpdate()
    {
        StateMachine.FixedUpdate(this);
        ApplyGravity();
    }

    private void ProcessInput()
    {
        HorizontalInput = Input.GetAxisRaw("Horizontal");
        JumpPressed = Input.GetButtonDown("Jump"); // Usually Space
        JumpHeld = Input.GetButton("Jump");

        if (JumpPressed)
        {
            JumpBufferCounter = data.jumpBufferTime;
        }
    }

    private void UpdateTimers()
    {
        JumpBufferCounter -= Time.deltaTime;
        
        if (IsGrounded())
        {
            CoyoteTimeCounter = data.coyoteTime;
        }
        else
        {
            CoyoteTimeCounter -= Time.deltaTime;
        }

        if (WallJumpLockCounter > 0)
        {
            WallJumpLockCounter -= Time.deltaTime;
        }
    }

    private void ApplyGravity()
    {
        // 1. Wall Logic (States handle their own gravity usually, but we can override here if needed)
        if (StateMachine.CurrentState == WallSlideState || StateMachine.CurrentState == WallJumpState)
        {
             // Let states handle it
             return;
        }

        // 2. Variable Jump Height (Short Hop)
        // If we represent jumping, are moving up, and NOT holding button -> High Gravity
        if (StateMachine.CurrentState == JumpState && RB.linearVelocity.y > 0 && !JumpHeld)
        {
            RB.gravityScale = data.gravityScale * data.fallGravityMultiplier; // Use multiplier for "Short Hop" feel
            // Or typically even higher for sharper cutoff:
            // RB.gravityScale = data.gravityScale * 2f; 
        }
        // 3. Falling (Faster Fall)
        else if (RB.linearVelocity.y < 0)
        {
             RB.gravityScale = data.gravityScale * data.fallGravityMultiplier;
        }
        // 4. Default
        else
        {
            RB.gravityScale = data.gravityScale;
        }
    }


    #region Public Helper Methods for States

    public void SetVelocity(float x, float y)
    {
        // Prevent horizontal movement if locked (after wall jump)
        if (WallJumpLockCounter > 0)
        {
            RB.linearVelocity = new Vector2(RB.linearVelocity.x, y);
        }
        else
        {
            RB.linearVelocity = new Vector2(x, y);
        }
    }

    public void SetVelocityX(float x)
    {
        if (WallJumpLockCounter > 0) return;
        RB.linearVelocity = new Vector2(x, RB.linearVelocity.y);
    }
    
    public void SetVelocityY(float y)
    {
        RB.linearVelocity = new Vector2(RB.linearVelocity.x, y);
    }

    public void CheckFlip()
    {
        if (WallJumpLockCounter > 0) return;

        if (HorizontalInput > 0 && FacingDirection == -1)
            Flip();
        else if (HorizontalInput < 0 && FacingDirection == 1)
            Flip();
    }

    private void Flip()
    {
        FacingDirection *= -1;
        // Fix for "Tiny Player": Preserve the original scale magnitude
        transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x) * FacingDirection, transform.localScale.y, transform.localScale.z);
    }

    public void ForceFlip(int direction)
    {
        FacingDirection = direction;
        transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x) * FacingDirection, transform.localScale.y, transform.localScale.z);
    }
    
    public bool IsGrounded()
    {
        // Simple BoxCast
        return Physics2D.BoxCast(Collider.bounds.center, Collider.bounds.size, 0f, Vector2.down, 0.1f, groundLayer);
    }

    public bool IsTouchingWall()
    {
        // Simple BoxCast in facing direction
        return Physics2D.BoxCast(Collider.bounds.center, Collider.bounds.size, 0f, new Vector2(FacingDirection, 0), 0.1f, wallLayer);
    }
    
    public bool IsPushingAgainstWall()
    {
         // Check if input matches wall direction
         if (!IsTouchingWall()) return false;
         return (FacingDirection == 1 && HorizontalInput > 0.1f) || (FacingDirection == -1 && HorizontalInput < -0.1f);
    }
    
    // Explicitly check direction relative to player
    public bool IsTouchingWallDirection(int dir)
    {
        return Physics2D.BoxCast(Collider.bounds.center, Collider.bounds.size, 0f, new Vector2(dir, 0), 0.1f, wallLayer);
    }

    public PlayerData Data => data;

    /// <summary>
    /// Effective run speed, accounting for stamina exhaustion debuff.
    /// States should use this instead of Data.runSpeed directly.
    /// </summary>
    public float EffectiveRunSpeed
    {
        get
        {
            float baseSpeed = data.runSpeed;
            if (Stamina != null)
                baseSpeed *= Stamina.SpeedMultiplier;
            return baseSpeed;
        }
    }

    #endregion
}
