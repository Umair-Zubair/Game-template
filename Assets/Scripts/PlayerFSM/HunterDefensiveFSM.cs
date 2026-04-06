using UnityEngine;

// ============================================================================
// HUNTER DEFENSIVE FSM
//
// Profile: Maximum-range sniper. Maintains 7f distance, fires single shots,
// strongly prefers aerial shots (70%), hard-retreats the moment the boss
// closes in, and waits for a FULL reload before re-engaging.
//
// Compared to other Hunter profiles:
//   AGGRESSIVE   Range 3f | advances during reload | 50% air shots | retreat at 0.8f | threshold 12f
//   BALANCED     Range 4.5f | partial reload wait   | 30% air shots | retreat at 2f  | threshold 35%
//   DEFENSIVE    Range 7f  | full reload wait       | 70% air shots | retreat at 4f  | threshold 55%
//
// States: WaitReload → Position → ShootSingle → Position
//                                              ↓ (boss < 4f)
//                                         HardRetreat → WaitReload
// ============================================================================
public class HunterDefensiveFSM : PlayerFSMController
{
    [Header("Hunter Defensive — Tuning")]
    [Tooltip("Ideal distance from the boss. Bot tries to hold this at all times.")]
    [SerializeField] private float preferredRange = 7.0f;

    [Tooltip("Acceptable drift around preferredRange before the bot repositions.")]
    [SerializeField] private float rangeTolerance = 1.0f;

    [Tooltip("Distance that triggers a hard retreat — much wider than other profiles.")]
    [SerializeField] private float retreatTriggerDistance = 4.0f;

    [Tooltip("Stamina ratio (0–1) required before shooting. High — very conservative.")]
    [SerializeField] private float staminaRequiredRatio = 0.55f;

    [Tooltip("Probability (0–1) of jumping before shooting. Defensive bot prefers air shots.")]
    [SerializeField] private float airShotProbability = 0.70f;

    // ── State Instances ────────────────────────────────────────────────────
    public HD_Position    PositionState    { get; private set; }
    public HD_ShootSingle ShootSingleState { get; private set; }
    public HD_HardRetreat HardRetreatState { get; private set; }
    public HD_WaitReload  WaitReloadState  { get; private set; }

    protected override void InitializeStates()
    {
        PositionState    = new HD_Position();
        ShootSingleState = new HD_ShootSingle();
        HardRetreatState = new HD_HardRetreat();
        WaitReloadState  = new HD_WaitReload();
    }

    // Start in WaitReload — defensive bot checks ammo and stamina before anything
    protected override void StartFSM() => FSM.Initialize(WaitReloadState, this);

    // ==========================================================================
    // POSITION STATE
    // Maintains preferredRange ± tolerance. Backs up if too close, inches in
    // if too far. Shoots only when in the sweet spot and fully ready.
    // ==========================================================================
    public class HD_Position : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { ctrl.FaceTarget(); }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd  = (HunterDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            // Hard retreat override — highest priority
            if (dist < hd.retreatTriggerDistance)
            {
                hd.FSM.ChangeState(hd.HardRetreatState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            float minRange = hd.preferredRange - hd.rangeTolerance;
            float maxRange = hd.preferredRange + hd.rangeTolerance;

            if (dist < minRange)
            {
                ctrl.MoveAway(); // back up to sweet spot
            }
            else if (dist > maxRange)
            {
                ctrl.MoveTowardAt(0.5f); // inch in slowly
            }
            else
            {
                // In sweet spot — decide whether to shoot
                ctrl.StopMoving();

                bool hasAmmo    = hd.RangedAttack != null
                               && hd.RangedAttack.CurrentAmmo > 0
                               && !hd.RangedAttack.IsReloading;
                bool hasStamina = ctrl.StaminaRatio() >= hd.staminaRequiredRatio;

                if (hasAmmo && hasStamina)
                    hd.FSM.ChangeState(hd.ShootSingleState, ctrl);
                else if (hd.RangedAttack != null && hd.RangedAttack.IsReloading)
                    hd.FSM.ChangeState(hd.WaitReloadState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // SHOOT SINGLE STATE
    // Fires ONE shot — with a 70% chance of jumping first for an aerial shot.
    // Immediately returns to Position after firing (single-shot discipline).
    //
    // Key difference from Aggressive: one shot then full reposition, not
    // continuous fire. Combined with the air-first preference this creates
    // a very different visual rhythm.
    // ==========================================================================
    public class HD_ShootSingle : IPlayerFSMState
    {
        private bool  jumped;
        private bool  shotFired;
        private float airTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var hd = (HunterDefensiveFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            shotFired = false;

            bool doAirShot = Random.value < hd.airShotProbability;
            if (doAirShot && ctrl.IsGrounded())
            {
                ctrl.RequestJump(0.20f);
                jumped   = true;
                airTimer = 0.12f;
            }
            else
            {
                jumped = false;
                ctrl.RequestAttack(); // ground shot immediately
                shotFired = true;
            }
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd = (HunterDefensiveFSM)ctrl;
            ctrl.FaceTarget();

            // Hard retreat always overrides
            if (ctrl.DistanceToTarget() < hd.retreatTriggerDistance)
            {
                hd.FSM.ChangeState(hd.HardRetreatState, ctrl);
                return;
            }

            // Aerial shot: wait until airborne, then fire
            if (jumped && !shotFired)
            {
                airTimer -= Time.deltaTime;
                if (airTimer <= 0f)
                {
                    ctrl.RequestAttack(); // fires air variant when !IsGrounded
                    shotFired = true;
                }
            }

            // Shot committed — reposition
            if (shotFired)
                hd.FSM.ChangeState(hd.PositionState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // HARD RETREAT STATE
    // Jumps AND runs away simultaneously for maximum escape distance.
    // Keeps moving until at preferredRange, then transitions to WaitReload.
    // ==========================================================================
    public class HD_HardRetreat : IPlayerFSMState
    {
        private bool jumped;

        public void OnEnter(PlayerFSMController ctrl) { jumped = false; }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd = (HunterDefensiveFSM)ctrl;

            ctrl.MoveAway();

            // Jump on first grounded frame for extra clearance
            if (!jumped && ctrl.IsGrounded())
            {
                ctrl.RequestJump(0.30f);
                jumped = true;
            }

            if (ctrl.DistanceToTarget() >= hd.preferredRange)
                hd.FSM.ChangeState(hd.WaitReloadState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // WAIT RELOAD STATE
    // Stays at or beyond preferred range, doing nothing until BOTH ammo is full
    // AND stamina is above the threshold. Unlike Balanced (50% ammo) this bot
    // waits for a complete reload — it never rushes back under-equipped.
    // ==========================================================================
    public class HD_WaitReload : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) => ctrl.StopMoving();

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd   = (HunterDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            ctrl.FaceTarget();

            // Hard retreat override even here
            if (dist < hd.retreatTriggerDistance)
            {
                hd.FSM.ChangeState(hd.HardRetreatState, ctrl);
                return;
            }

            // Maintain safe distance while waiting
            if (dist < hd.preferredRange)
                ctrl.MoveAway();
            else
                ctrl.StopMoving();

            // Only re-engage when FULLY loaded and stamina is high
            bool fullyLoaded = hd.RangedAttack == null
                            || (hd.RangedAttack.CurrentAmmo == hd.RangedAttack.MaxAmmo
                                && !hd.RangedAttack.IsReloading);
            bool hasStamina  = ctrl.StaminaRatio() >= hd.staminaRequiredRatio;

            if (fullyLoaded && hasStamina)
                hd.FSM.ChangeState(hd.PositionState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }
}
