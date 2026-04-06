using UnityEngine;

// ============================================================================
// HUNTER DEFENSIVE FSM
//
// Deliberate and patient. Holds its preferred distance, "observes" for a
// moment before shooting, fires one shot at a time, then repositions. Hard
// retreats when the boss closes in. Waits for full ammo AND adds extra time
// after reload before re-engaging.
//
// States: WaitReload → Position (with observation) → ShootSingle → Position
//                                                   ↓ (boss < 4f)
//                                              HardRetreat → WaitReload
// ============================================================================
public class HunterDefensiveFSM : PlayerFSMController
{
    [Header("Hunter Defensive — Tuning")]
    [SerializeField] private float preferredRange         = 7.0f;
    [SerializeField] private float rangeTolerance         = 1.0f;
    [SerializeField] private float retreatTriggerDistance = 4.0f;
    [SerializeField] private float staminaRequiredRatio   = 0.55f;
    [SerializeField] private float airShotProbability     = 0.70f;

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

    protected override void StartFSM() => FSM.Initialize(WaitReloadState, this);

    // ==========================================================================
    // POSITION — Holds preferred distance. Before deciding to shoot, the bot
    // must stay in the sweet spot for an "observation window" — a random 0.8-1.8s.
    // This makes the bot look like it's lining up the shot rather than firing
    // the instant conditions are met.
    // ==========================================================================
    public class HD_Position : IPlayerFSMState
    {
        private float observeTimer;
        private bool  observing;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.FaceTarget();
            observing    = false;
            observeTimer = 0f;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd   = (HunterDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            if (dist < hd.retreatTriggerDistance)
            {
                hd.FSM.ChangeState(hd.HardRetreatState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            float minRange = hd.preferredRange - hd.rangeTolerance;
            float maxRange = hd.preferredRange + hd.rangeTolerance;

            bool inSweet = dist >= minRange && dist <= maxRange;

            if (!inSweet)
            {
                observing = false; // reset observation if we drift out
                if (dist < minRange) ctrl.MoveAway();
                else                 ctrl.MoveTowardAt(0.45f);
                return;
            }

            ctrl.StopMoving();

            bool hasAmmo    = hd.RangedAttack != null && hd.RangedAttack.CurrentAmmo > 0 && !hd.RangedAttack.IsReloading;
            bool hasStamina = ctrl.StaminaRatio() >= hd.staminaRequiredRatio;

            if (!hasAmmo || !hasStamina)
            {
                observing = false;
                if (hd.RangedAttack != null && hd.RangedAttack.IsReloading)
                    hd.FSM.ChangeState(hd.WaitReloadState, ctrl);
                return;
            }

            // Start the observation window
            if (!observing)
            {
                observing    = true;
                observeTimer = Random.Range(0.80f, 1.80f);
            }

            observeTimer -= Time.deltaTime;
            if (observeTimer <= 0f)
                hd.FSM.ChangeState(hd.ShootSingleState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // SHOOT SINGLE — 70% chance to jump first for aerial shot. One shot then
    // back to Position. The observation in Position means this state fires
    // infrequently and deliberately.
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

            if (Random.value < hd.airShotProbability && ctrl.IsGrounded())
            {
                ctrl.RequestJump(Random.Range(0.18f, 0.28f));
                jumped   = true;
                airTimer = Random.Range(0.10f, 0.15f);
            }
            else
            {
                jumped = false;
                ctrl.RequestAttack();
                shotFired = true;
            }
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd = (HunterDefensiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.DistanceToTarget() < hd.retreatTriggerDistance)
            {
                hd.FSM.ChangeState(hd.HardRetreatState, ctrl);
                return;
            }

            if (jumped && !shotFired)
            {
                airTimer -= Time.deltaTime;
                if (airTimer <= 0f) { ctrl.RequestAttack(); shotFired = true; }
            }

            if (shotFired)
                hd.FSM.ChangeState(hd.PositionState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // HARD RETREAT — Jumps and sprints away. Continues until at preferred range.
    // ==========================================================================
    public class HD_HardRetreat : IPlayerFSMState
    {
        private bool jumped;

        public void OnEnter(PlayerFSMController ctrl) { jumped = false; }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd = (HunterDefensiveFSM)ctrl;
            ctrl.MoveAway();
            if (!jumped && ctrl.IsGrounded()) { ctrl.RequestJump(0.30f); jumped = true; }
            if (ctrl.DistanceToTarget() >= hd.preferredRange)
                hd.FSM.ChangeState(hd.WaitReloadState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // WAIT RELOAD — Waits for full ammo + stamina, PLUS a mandatory extra pause
    // (1.5-3s) even after everything is ready. The defensive bot takes its time
    // before every engagement — it doesn't rush back the instant it can.
    // ==========================================================================
    public class HD_WaitReload : IPlayerFSMState
    {
        private float extraWait;
        private bool  conditionsMet;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            conditionsMet = false;
            extraWait     = 0f;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd   = (HunterDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            ctrl.FaceTarget();

            if (dist < hd.retreatTriggerDistance) { hd.FSM.ChangeState(hd.HardRetreatState, ctrl); return; }
            if (dist < hd.preferredRange) ctrl.MoveAway();
            else ctrl.StopMoving();

            bool fullyLoaded = hd.RangedAttack == null
                            || (hd.RangedAttack.CurrentAmmo == hd.RangedAttack.MaxAmmo && !hd.RangedAttack.IsReloading);
            bool hasStamina  = ctrl.StaminaRatio() >= hd.staminaRequiredRatio;

            if (fullyLoaded && hasStamina && !conditionsMet)
            {
                conditionsMet = true;
                extraWait     = Random.Range(1.5f, 3.0f); // extra patience before re-engaging
            }

            if (conditionsMet)
            {
                extraWait -= Time.deltaTime;
                if (extraWait <= 0f)
                    hd.FSM.ChangeState(hd.PositionState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }
}
