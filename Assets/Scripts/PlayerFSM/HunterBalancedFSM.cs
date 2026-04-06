using UnityEngine;

// ============================================================================
// HUNTER BALANCED FSM
//
// Profile: Controlled mid-range shooter. Finds a sweet spot (4–5f), fires
// short 2-shot bursts, slides back after each burst, occasionally jumps for
// aerial shots, and re-engages once 50% ammo has reloaded.
//
// Compared to other Hunter profiles:
//   AGGRESSIVE   Range 3f | advances during reload | 50% air shots | retreat at 0.8f | threshold 12f
//   BALANCED     Range 4.5f | partial reload wait   | 30% air shots | retreat at 2f  | threshold 35%
//   DEFENSIVE    Range 7f  | full reload wait       | 70% air shots | retreat at 4f  | threshold 55%
//
// States: FindRange → Shoot / JumpShoot → SlideBack → FindRange (or WaitAmmo)
// ============================================================================
public class HunterBalancedFSM : PlayerFSMController
{
    [Header("Hunter Balanced — Tuning")]
    [Tooltip("Ideal shooting distance.")]
    [SerializeField] private float preferredRange = 4.5f;

    [Tooltip("Acceptable band around preferredRange before the bot repositions.")]
    [SerializeField] private float rangeTolerance = 0.8f;

    [Tooltip("Distance that triggers a moderate retreat.")]
    [SerializeField] private float retreatTriggerDistance = 2.0f;

    [Tooltip("Number of shots per burst before the bot slides back.")]
    [SerializeField] private int shotsPerBurst = 2;

    [Tooltip("Delay between shots within a burst (seconds).")]
    [SerializeField] private float burstShotInterval = 0.15f;

    [Tooltip("Stamina ratio (0–1) required before engaging.")]
    [SerializeField] private float staminaThresholdRatio = 0.35f;

    [Tooltip("Ammo ratio (0–1) required to re-engage after sliding back.")]
    [SerializeField] private float reloadThresholdRatio = 0.50f;

    [Tooltip("Probability (0–1) of a jump-shot instead of a ground shot.")]
    [SerializeField] private float jumpShootChance = 0.30f;

    [Tooltip("How long the bot slides away after a burst.")]
    [SerializeField] private float slideBackDuration = 0.40f;

    // ── State Instances ────────────────────────────────────────────────────
    public HB_FindRange  FindRangeState  { get; private set; }
    public HB_Shoot      ShootState      { get; private set; }
    public HB_JumpShoot  JumpShootState  { get; private set; }
    public HB_SlideBack  SlideBackState  { get; private set; }
    public HB_WaitAmmo   WaitAmmoState   { get; private set; }

    protected override void InitializeStates()
    {
        FindRangeState = new HB_FindRange();
        ShootState     = new HB_Shoot();
        JumpShootState = new HB_JumpShoot();
        SlideBackState = new HB_SlideBack();
        WaitAmmoState  = new HB_WaitAmmo();
    }

    protected override void StartFSM() => FSM.Initialize(FindRangeState, this);

    // ==========================================================================
    // FIND RANGE STATE
    // Adjusts position to maintain the preferred distance band.
    // Picks an attack type when in range and ready.
    // ==========================================================================
    public class HB_FindRange : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb   = (HunterBalancedFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            ctrl.FaceTarget();

            // Moderate retreat if boss too close
            if (dist < hb.retreatTriggerDistance)
            {
                ctrl.MoveAway();
                return;
            }

            // No ammo → wait for partial reload
            bool hasAmmo = hb.RangedAttack != null
                        && hb.RangedAttack.CurrentAmmo > 0
                        && !hb.RangedAttack.IsReloading;
            if (!hasAmmo)
            {
                hb.FSM.ChangeState(hb.WaitAmmoState, ctrl);
                return;
            }

            float minRange = hb.preferredRange - hb.rangeTolerance;
            float maxRange = hb.preferredRange + hb.rangeTolerance;

            if (dist < minRange)
            {
                ctrl.MoveAway();
            }
            else if (dist > maxRange)
            {
                ctrl.MoveTowardAt(0.70f);
            }
            else
            {
                ctrl.StopMoving();

                if (ctrl.StaminaRatio() < hb.staminaThresholdRatio) return;

                // 30% chance of jump-shot; only when grounded
                bool doJumpShot = ctrl.IsGrounded() && Random.value < hb.jumpShootChance;
                hb.FSM.ChangeState(
                    doJumpShot ? (IPlayerFSMState)hb.JumpShootState : hb.ShootState,
                    ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // SHOOT STATE
    // Fires a burst of shotsPerBurst shots with burstShotInterval spacing,
    // then slides back. This burst-and-retreat rhythm is the defining visual
    // behaviour of the Balanced profile.
    // ==========================================================================
    public class HB_Shoot : IPlayerFSMState
    {
        private int   shotsFired;
        private float shotTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            shotsFired = 0;
            shotTimer  = 0f;
            ctrl.RequestAttack(); // first shot immediately
            shotsFired++;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            // Moderate retreat override
            if (ctrl.DistanceToTarget() < hb.retreatTriggerDistance)
            {
                hb.FSM.ChangeState(hb.SlideBackState, ctrl);
                return;
            }

            // Burst complete → slide back
            if (shotsFired >= hb.shotsPerBurst)
            {
                hb.FSM.ChangeState(hb.SlideBackState, ctrl);
                return;
            }

            // Continue burst with spacing
            shotTimer -= Time.deltaTime;
            if (shotTimer <= 0f)
            {
                bool hasAmmo = hb.RangedAttack != null && hb.RangedAttack.CurrentAmmo > 0;
                if (hasAmmo)
                {
                    ctrl.RequestAttack();
                    shotsFired++;
                    shotTimer = hb.burstShotInterval;
                }
                else
                {
                    hb.FSM.ChangeState(hb.WaitAmmoState, ctrl);
                }
            }
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // JUMP SHOOT STATE
    // Jumps then fires one aerial shot (uses BlobRangedAttack's air variant).
    // After landing, always slides back regardless of ammo.
    // ==========================================================================
    public class HB_JumpShoot : IPlayerFSMState
    {
        private float airTimer;
        private bool  shotFired;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestJump(0.25f);
            airTimer  = 0.10f;
            shotFired = false;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            airTimer -= Time.deltaTime;
            if (!shotFired && airTimer <= 0f)
            {
                ctrl.RequestAttack();
                shotFired = true;
            }

            if (shotFired && ctrl.IsGrounded())
                hb.FSM.ChangeState(hb.SlideBackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // SLIDE BACK STATE
    // Retreats for a fixed duration after every burst or aerial shot.
    // After the slide, checks ammo: if below threshold → WaitAmmo, else → FindRange.
    // ==========================================================================
    public class HB_SlideBack : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = ((HunterBalancedFSM)ctrl).slideBackDuration;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.MoveAway();
            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                float ammoRatio = hb.RangedAttack == null
                    ? 1f
                    : (float)hb.RangedAttack.CurrentAmmo / Mathf.Max(1, hb.RangedAttack.MaxAmmo);

                hb.FSM.ChangeState(
                    ammoRatio >= hb.reloadThresholdRatio
                        ? (IPlayerFSMState)hb.FindRangeState
                        : hb.WaitAmmoState,
                    ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // WAIT AMMO STATE
    // Holds position (or backs up) until ammo ratio reaches 50%.
    // Unlike the Defensive bot (100%), this bot re-engages at half a magazine.
    // ==========================================================================
    public class HB_WaitAmmo : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) => ctrl.StopMoving();

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb   = (HunterBalancedFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            ctrl.FaceTarget();

            // Maintain comfortable distance while waiting
            float minComfort = hb.preferredRange - hb.rangeTolerance;
            if (dist < minComfort)
                ctrl.MoveAway();
            else
                ctrl.StopMoving();

            // Check partial reload threshold
            float ammoRatio = hb.RangedAttack == null
                ? 1f
                : (float)hb.RangedAttack.CurrentAmmo / Mathf.Max(1, hb.RangedAttack.MaxAmmo);

            bool ready = ammoRatio >= hb.reloadThresholdRatio
                      && ctrl.StaminaRatio() >= hb.staminaThresholdRatio;

            if (ready)
                hb.FSM.ChangeState(hb.FindRangeState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
