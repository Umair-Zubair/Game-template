using UnityEngine;

// ============================================================================
// HUNTER AGGRESSIVE FSM
//
// Profile: Close-range fire pressure. Gets inside the boss's comfort zone,
// fires all ammo without pausing, uses aerial shots frequently, and keeps
// advancing even while reloading. Almost never retreats.
//
// Compared to other Hunter profiles:
//   AGGRESSIVE   Range 3f | advances during reload | 50% air shots | retreat at 0.8f | threshold 12f
//   BALANCED     Range 4.5f | partial reload wait   | 30% air shots | retreat at 2f  | threshold 35%
//   DEFENSIVE    Range 7f  | full reload wait       | 70% air shots | retreat at 4f  | threshold 55%
//
// States: Press → Shoot / JumpShoot → Press (or ReloadAdvance if reloading)
// ============================================================================
public class HunterAggressiveFSM : PlayerFSMController
{
    [Header("Hunter Aggressive — Tuning")]
    [Tooltip("Preferred shooting distance. Bot presses in until it reaches this.")]
    [SerializeField] private float preferredRange = 3.0f;

    [Tooltip("Only distance that forces a retreat — literally inside the boss.")]
    [SerializeField] private float pointBlankDistance = 0.8f;

    [Tooltip("Minimum stamina (absolute) before shooting.")]
    [SerializeField] private float staminaThreshold = 12f;

    [Tooltip("Probability (0–1) of choosing a jump-shot over a ground shot when in range.")]
    [SerializeField] private float jumpShootChance = 0.50f;

    // ── State Instances ────────────────────────────────────────────────────
    public HA_Press         PressState         { get; private set; }
    public HA_Shoot         ShootState         { get; private set; }
    public HA_JumpShoot     JumpShootState     { get; private set; }
    public HA_ReloadAdvance ReloadAdvanceState { get; private set; }

    protected override void InitializeStates()
    {
        PressState         = new HA_Press();
        ShootState         = new HA_Shoot();
        JumpShootState     = new HA_JumpShoot();
        ReloadAdvanceState = new HA_ReloadAdvance();
    }

    protected override void StartFSM() => FSM.Initialize(PressState, this);

    // ==========================================================================
    // PRESS STATE
    // Closes distance toward the boss. Detects reloading and routes accordingly.
    // Makes an attack decision as soon as it's in range.
    // ==========================================================================
    public class HA_Press : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            // Only retreat when literally touching the boss
            if (dist < ha.pointBlankDistance)
            {
                ctrl.MoveAway();
                return;
            }

            ctrl.MoveToward();
            ctrl.FaceTarget();

            // If reloading, keep advancing rather than stopping
            if (ha.RangedAttack != null && ha.RangedAttack.IsReloading)
            {
                ha.FSM.ChangeState(ha.ReloadAdvanceState, ctrl);
                return;
            }

            bool hasAmmo    = ha.RangedAttack != null && ha.RangedAttack.CurrentAmmo > 0;
            bool hasStamina = ctrl.Stamina == null || ctrl.Stamina.CurrentStamina > ha.staminaThreshold;

            if (dist <= ha.preferredRange && hasAmmo && hasStamina)
            {
                // 50% chance to jump-shoot; only jump if grounded
                bool doJumpShot = ctrl.IsGrounded() && Random.value < ha.jumpShootChance;
                ha.FSM.ChangeState(
                    doJumpShot ? (IPlayerFSMState)ha.JumpShootState : ha.ShootState,
                    ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // SHOOT STATE
    // Fires one shot from the ground, then immediately returns to Press.
    // The loop Press→Shoot→Press creates rapid sustained fire.
    // ==========================================================================
    public class HA_Shoot : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestAttack(); // one-shot signal; BlobRangedAttack handles cooldown
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ha.RangedAttack != null && ha.RangedAttack.IsReloading)
                ha.FSM.ChangeState(ha.ReloadAdvanceState, ctrl);
            else
                ha.FSM.ChangeState(ha.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // JUMP SHOOT STATE
    // Jumps then fires an aerial shot (BlobRangedAttack uses JumpRangeAttack
    // when IsGrounded() == false). Waits until airborne before requesting
    // the attack to ensure the air variant fires.
    // ==========================================================================
    public class HA_JumpShoot : IPlayerFSMState
    {
        private float airTimer;
        private bool  shotFired;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestJump(0.25f);
            airTimer  = 0.10f; // let the jump physics register before firing
            shotFired = false;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            airTimer -= Time.deltaTime;

            if (!shotFired && airTimer <= 0f)
            {
                ctrl.RequestAttack(); // fires air-shot if !IsGrounded, ground shot fallback
                shotFired = true;
            }

            // Return to Press after landing
            if (shotFired && ctrl.IsGrounded())
                ha.FSM.ChangeState(ha.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // RELOAD ADVANCE STATE
    // Keeps moving toward the boss while the magazine reloads.
    // Aggressive bot never stops for ammo — it closes the gap instead.
    // ==========================================================================
    public class HA_ReloadAdvance : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.MoveToward();
            ctrl.FaceTarget();

            if (ha.RangedAttack == null || !ha.RangedAttack.IsReloading)
                ha.FSM.ChangeState(ha.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }
}
