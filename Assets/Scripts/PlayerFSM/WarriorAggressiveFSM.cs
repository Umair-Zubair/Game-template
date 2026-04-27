using UnityEngine;

// ============================================================================
// WARRIOR AGGRESSIVE FSM
//
// Rushes the boss, hesitates briefly to size up, then commits to a melee
// pattern: a single chop, a full two-hit combo, or a jump attack.  Choice is
// stamina-aware — if we can't afford the move we back off and recover.
// Reacts to boss attacks and artillery the whole way through.
//
// States: Chase → Hesitate → Attack → BriefPull → Chase
//         Any state → Evade (when boss attacks in range or artillery fires)
// ============================================================================
public class WarriorAggressiveFSM : PlayerFSMController
{
    [Header("Warrior Aggressive — Tuning")]
    [SerializeField] private float attackRange         = 2.0f;
    [SerializeField] private float jumpHeightThreshold = 1.5f;

    [Header("Pattern Weights")]
    [Tooltip("When stamina allows everything, relative odds of picking a full two-hit combo.")]
    [Range(0f, 1f)] [SerializeField] private float comboWeight       = 0.55f;
    [Tooltip("Relative odds of just chopping once and disengaging.")]
    [Range(0f, 1f)] [SerializeField] private float singleWeight      = 0.25f;
    [Tooltip("Relative odds of leaping in for an air attack.")]
    [Range(0f, 1f)] [SerializeField] private float jumpAttackWeight  = 0.20f;

    [Header("Stamina Gate")]
    [Tooltip("Extra stamina kept in reserve on top of the chosen pattern's cost (so we don't get caught empty).")]
    [SerializeField] private float staminaReserve = 5f;

    public WA_Chase     ChaseState     { get; private set; }
    public WA_Hesitate  HesitateState  { get; private set; }
    public WA_Attack    AttackState    { get; private set; }
    public WA_BriefPull BriefPullState { get; private set; }
    public WA_Evade     EvadeState     { get; private set; }

    /// <summary>Available melee patterns, ordered roughly by stamina demand.</summary>
    public enum AttackPattern { None, Single, Combo, JumpAttack }

    protected override void InitializeStates()
    {
        ChaseState     = new WA_Chase();
        HesitateState  = new WA_Hesitate();
        AttackState    = new WA_Attack();
        BriefPullState = new WA_BriefPull();
        EvadeState     = new WA_Evade();
    }

    protected override void StartFSM() => FSM.Initialize(ChaseState, this);

    // ── Stamina helpers ────────────────────────────────────────────────────

    private float AttackCost     => Stamina != null ? Stamina.Data.attackCost      : 0f;
    private float ComboCost      => Stamina != null ? Stamina.Data.comboAttackCost : 0f;
    private float JumpCost       => Stamina != null ? Stamina.Data.jumpCost        : 0f;
    private float AirAttackCost  => Stamina != null ? Stamina.Data.airAttackCost   : 0f;

    /// <summary>Total cost for a full two-hit combo (first swing + queued second).</summary>
    public float FullComboCost      => AttackCost + ComboCost      + staminaReserve;
    /// <summary>Cost of one chop with a small reserve.</summary>
    public float SingleCost         => AttackCost                  + staminaReserve;
    /// <summary>Cost of jumping then swinging in the air.</summary>
    public float JumpAttackTotalCost => JumpCost + AirAttackCost   + staminaReserve;

    public bool  CanAffordAnyAttack => Stamina == null
        || (!Stamina.IsExhausted && Stamina.CurrentStamina >= SingleCost);

    /// <summary>
    /// Picks the next melee pattern based on what stamina can support.  Returns
    /// AttackPattern.None when nothing is affordable — caller should retreat
    /// and recover instead of committing to a swing it can't pay for.
    /// </summary>
    public AttackPattern PickAttackPattern()
    {
        if (Stamina != null && Stamina.IsExhausted) return AttackPattern.None;

        float current = Stamina != null ? Stamina.CurrentStamina : Mathf.Infinity;

        bool canCombo  = current >= FullComboCost;
        bool canJump   = current >= JumpAttackTotalCost && IsGrounded();
        bool canSingle = current >= SingleCost;

        if (!canSingle) return AttackPattern.None;

        // Build a weighted roll over only the patterns we can actually afford
        float wCombo   = canCombo  ? comboWeight       : 0f;
        float wJump    = canJump   ? jumpAttackWeight  : 0f;
        float wSingle  =             singleWeight;

        // If we can't afford the heavier options, lean fully on what's left
        float total = wCombo + wJump + wSingle;
        if (total <= 0.0001f) return AttackPattern.Single;

        float roll = Random.value * total;
        if (roll < wCombo)              return AttackPattern.Combo;
        roll -= wCombo;
        if (roll < wJump)               return AttackPattern.JumpAttack;
        return AttackPattern.Single;
    }

    // ==========================================================================
    // CHASE — Rush toward boss. Reacts to artillery immediately.
    // Backs off if boss is already mid-swing on approach. If we don't have
    // stamina for any attack at all, we hold a respectful distance instead of
    // walking right onto the boss with nothing to throw.
    // ==========================================================================
    public class WA_Chase : IPlayerFSMState
    {
        private float jumpCheckTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            jumpCheckTimer = Random.Range(0.8f, 2.0f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wa.dangerRange)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            // No stamina for any swing — don't crowd the boss, let it recover
            if (!wa.CanAffordAnyAttack)
            {
                float dist = ctrl.DistanceToTarget();
                ctrl.FaceTarget();
                if (dist < wa.attackRange * 1.6f) ctrl.MoveAway();
                else                              ctrl.StopMoving();
                return;
            }

            ctrl.MoveToward();
            ctrl.FaceTarget();

            jumpCheckTimer -= Time.deltaTime;
            if (jumpCheckTimer <= 0f && ctrl.IsGrounded())
            {
                jumpCheckTimer = Random.Range(0.8f, 2.2f);
                bool bossElevated = ctrl.HeightDifferenceToTarget() > wa.jumpHeightThreshold;
                bool randomJump   = Random.value < 0.20f;
                // Don't burn the jump cost if it'd kill our attack budget
                if ((bossElevated || randomJump)
                    && ctrl.Stamina != null
                    && ctrl.Stamina.CurrentStamina >= wa.JumpCost + wa.SingleCost)
                {
                    ctrl.RequestJump(Random.Range(0.2f, 0.35f));
                }
            }

            if (ctrl.DistanceToTarget() <= wa.attackRange)
                wa.FSM.ChangeState(wa.HesitateState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // HESITATE — Stops before committing to the attack. If the boss starts
    // swinging during this window, abort — don't walk into it. Also bails
    // if we suddenly can't afford any swing (e.g. exhausted from a jump).
    // ==========================================================================
    public class WA_Hesitate : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            timer = Random.Range(0.10f, 0.28f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (!wa.CanAffordAnyAttack)
            {
                wa.FSM.ChangeState(wa.BriefPullState, ctrl);
                return;
            }

            if (ctrl.DistanceToTarget() > wa.attackRange * 1.3f)
            {
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wa.FSM.ChangeState(wa.AttackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // ATTACK — Picks one pattern (single chop, full two-hit combo, or a leap
    // attack) and sees it through.  Combo specifically queues the second hit
    // during the animation's combo window — that's what makes it actually
    // land both swings instead of looking like a spammy single chop.
    // ==========================================================================
    public class WA_Attack : IPlayerFSMState
    {
        private AttackPattern pattern;
        private bool  firstHitFired;
        private bool  comboQueued;
        private bool  jumpInitiated;
        private bool  airHitFired;
        private float patternTimeout;
        private float postSwingDelay;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();

            firstHitFired   = false;
            comboQueued     = false;
            jumpInitiated   = false;
            airHitFired     = false;
            postSwingDelay  = 0f;
            patternTimeout  = 1.8f;

            pattern = wa.PickAttackPattern();
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            // Boss counters mid-pattern — bail rather than eat the hit
            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wa.dangerRange)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.45f)
            {
                wa.FSM.ChangeState(wa.BriefPullState, ctrl);
                return;
            }

            // Stamina check failed at OnEnter — back off and recover
            if (pattern == AttackPattern.None)
            {
                wa.FSM.ChangeState(wa.BriefPullState, ctrl);
                return;
            }

            patternTimeout -= Time.deltaTime;
            if (patternTimeout <= 0f)
            {
                wa.FSM.ChangeState(wa.BriefPullState, ctrl);
                return;
            }

            // Drift slightly forward when just out of range so the swing connects
            float dist = ctrl.DistanceToTarget();
            if (dist > wa.attackRange * 1.05f && pattern != AttackPattern.JumpAttack)
                ctrl.MoveTowardAt(0.55f);
            else
                ctrl.StopMoving();

            switch (pattern)
            {
                case AttackPattern.Single:     UpdateSingle(wa, ctrl);    break;
                case AttackPattern.Combo:      UpdateCombo(wa, ctrl);     break;
                case AttackPattern.JumpAttack: UpdateJumpAttack(wa, ctrl); break;
            }
        }

        // One swing, then back off.
        private void UpdateSingle(WarriorAggressiveFSM wa, PlayerFSMController ctrl)
        {
            var melee = wa.MeleeAttack;
            if (melee == null) { wa.FSM.ChangeState(wa.BriefPullState, ctrl); return; }

            if (!firstHitFired)
            {
                if (melee.CanAttack && !melee.IsInAttackSequence)
                {
                    ctrl.RequestAttack();
                    firstHitFired = true;
                }
                return;
            }

            // Wait for the swing to play out, then pull back
            if (!melee.IsInAttackSequence)
            {
                postSwingDelay += Time.deltaTime;
                if (postSwingDelay >= 0.08f)
                    wa.FSM.ChangeState(wa.BriefPullState, ctrl);
            }
        }

        // Full two-hit combo — fire the first swing, then explicitly request
        // the second hit while the combo window is open.  MeleeAttack queues
        // it and ComboAttack() fires on the closing animation event.
        private void UpdateCombo(WarriorAggressiveFSM wa, PlayerFSMController ctrl)
        {
            var melee = wa.MeleeAttack;
            if (melee == null) { wa.FSM.ChangeState(wa.BriefPullState, ctrl); return; }

            if (!firstHitFired)
            {
                if (melee.CanAttack && !melee.IsInAttackSequence)
                {
                    ctrl.RequestAttack();
                    firstHitFired = true;
                }
                return;
            }

            // Queue the second swing while the window is open
            if (!comboQueued && melee.ComboWindowOpen)
            {
                ctrl.RequestAttack();
                comboQueued = true;
            }

            // After the second swing finishes (or window closed without us)
            if (!melee.IsInAttackSequence && !melee.ComboWindowOpen)
            {
                postSwingDelay += Time.deltaTime;
                if (postSwingDelay >= 0.10f)
                    wa.FSM.ChangeState(wa.BriefPullState, ctrl);
            }
        }

        // Leap up and chop down. Uses airAttackCost via MeleeAttack.
        private void UpdateJumpAttack(WarriorAggressiveFSM wa, PlayerFSMController ctrl)
        {
            var melee = wa.MeleeAttack;
            if (melee == null) { wa.FSM.ChangeState(wa.BriefPullState, ctrl); return; }

            if (!jumpInitiated)
            {
                ctrl.RequestJump(Random.Range(0.18f, 0.28f));
                jumpInitiated = true;
                return;
            }

            // Swing once we're airborne and near the apex — gives the boss less time to step out
            if (!airHitFired && !ctrl.IsGrounded())
            {
                bool nearApex = ctrl.PC != null && ctrl.PC.RB != null
                                && ctrl.PC.RB.linearVelocity.y < 1.5f;
                if (nearApex && melee.CanAttack && !melee.IsInAttackSequence)
                {
                    ctrl.RequestAttack();
                    airHitFired = true;
                }
                return;
            }

            // Land, let the swing settle, then pull back
            if (airHitFired && ctrl.IsGrounded() && !melee.IsInAttackSequence)
            {
                postSwingDelay += Time.deltaTime;
                if (postSwingDelay >= 0.08f)
                    wa.FSM.ChangeState(wa.BriefPullState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // BRIEF PULL — Steps back after a pattern. If hurt/low-HP we stay back; if
    // boss is mid-swing we evade; otherwise probabilistic re-engage. Stamina
    // is the gate: we don't dive back in until we can afford another swing.
    // ==========================================================================
    public class WA_BriefPull : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = Random.Range(0.20f, 0.95f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.MoveAway();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.35f)
            {
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wa.dangerRange * 1.5f)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            // Need stamina before pressing again — extend the pull a bit longer
            if (!wa.CanAffordAnyAttack)
            {
                timer = Random.Range(0.20f, 0.40f);
                ctrl.StopMoving();
                return;
            }

            bool stillClose = ctrl.DistanceToTarget() <= wa.attackRange * 1.4f;
            float roll = Random.value;

            if (stillClose && roll < 0.40f)
                wa.FSM.ChangeState(wa.HesitateState, ctrl);
            else if (stillClose && roll < 0.55f)
                wa.FSM.ChangeState(wa.BriefPullState, ctrl);
            else
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // EVADE — Triggered by boss attack or artillery. Jumps or dashes away.
    // After evading, reassesses based on health before going back in.
    // ==========================================================================
    public class WA_Evade : IPlayerFSMState
    {
        private float timer;
        private bool  jumped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.ConsumeArtilleryEvade();

            jumped = ctrl.IsGrounded() && Random.value < 0.55f;
            if (jumped)
                ctrl.RequestJump(Random.Range(0.20f, 0.32f));

            timer = Random.Range(0.35f, 0.65f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (!jumped)
                ctrl.MoveAway();

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.40f)
                    wa.FSM.ChangeState(wa.BriefPullState, ctrl);
                else
                    wa.FSM.ChangeState(wa.ChaseState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
