using UnityEngine;

// ============================================================================
// HUNTER DEFENSIVE FSM
//
// Deliberate and patient. Holds preferred distance, observes briefly before
// shooting, fires one shot, then repositions. Hard retreats when boss closes in.
// Stamina is NOT checked by the FSM — the attack component handles it.
//
// States: WaitReload → Position (observe) → ShootSingle → Position
//                                          ↓ (boss < retreatTrigger)
//                                     HardRetreat → WaitReload
//         Any state → Evade (artillery or boss threat)
// ============================================================================
public class HunterDefensiveFSM : PlayerFSMController
{
    [Header("Hunter Defensive — Tuning")]
    [SerializeField] private float preferredRange         = 8.0f;
    [SerializeField] private float rangeTolerance         = 1.0f;
    [SerializeField] private float retreatTriggerDistance = 4.0f;
    [SerializeField] private float airShotProbability     = 0.70f;

    public HD_Position    PositionState    { get; private set; }
    public HD_ShootSingle ShootSingleState { get; private set; }
    public HD_HardRetreat HardRetreatState { get; private set; }
    public HD_WaitReload  WaitReloadState  { get; private set; }
    public HD_Evade       EvadeState       { get; private set; }

    protected override void InitializeStates()
    {
        PositionState    = new HD_Position();
        ShootSingleState = new HD_ShootSingle();
        HardRetreatState = new HD_HardRetreat();
        WaitReloadState  = new HD_WaitReload();
        EvadeState       = new HD_Evade();
    }

    protected override void StartFSM() => FSM.Initialize(WaitReloadState, this);

    // ==========================================================================
    // POSITION — Holds preferred distance. Observes for 0.40-0.90s before firing.
    // Evades if boss attacks at close range during positioning.
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

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hd.FSM.ChangeState(hd.EvadeState, ctrl);
                return;
            }

            if (dist < hd.retreatTriggerDistance)
            {
                hd.FSM.ChangeState(hd.HardRetreatState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && dist < hd.retreatTriggerDistance * 1.5f)
            {
                hd.FSM.ChangeState(hd.EvadeState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            float minRange = hd.preferredRange - hd.rangeTolerance;
            float maxRange = hd.preferredRange + hd.rangeTolerance;
            bool  inSweet  = dist >= minRange && dist <= maxRange;

            if (!inSweet)
            {
                observing = false;
                if (dist < minRange) ctrl.MoveAway();
                else                 ctrl.MoveTowardAt(0.45f);
                return;
            }

            ctrl.StopMoving();

            bool hasAmmo = hd.RangedAttack == null || (hd.RangedAttack.CurrentAmmo > 0 && !hd.RangedAttack.IsReloading);

            if (!hasAmmo)
            {
                observing = false;
                if (hd.RangedAttack != null && hd.RangedAttack.IsReloading)
                    hd.FSM.ChangeState(hd.WaitReloadState, ctrl);
                return;
            }

            if (!observing)
            {
                observing    = true;
                observeTimer = Random.Range(0.40f, 0.90f);
            }

            observeTimer -= Time.deltaTime;
            if (observeTimer <= 0f)
                hd.FSM.ChangeState(hd.ShootSingleState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // SHOOT SINGLE — 70% chance to jump first. One shot then back to Position.
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
                airTimer = Random.Range(0.08f, 0.13f);
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

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hd.FSM.ChangeState(hd.EvadeState, ctrl);
                return;
            }

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
    // HARD RETREAT — Jumps and sprints away until at preferred range.
    // Fires a parting shot if ammo is available — flee-shooting feels human.
    // ==========================================================================
    public class HD_HardRetreat : IPlayerFSMState
    {
        private bool jumped;
        private bool shotFired;

        public void OnEnter(PlayerFSMController ctrl)
        {
            jumped    = false;
            shotFired = false;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd = (HunterDefensiveFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hd.FSM.ChangeState(hd.EvadeState, ctrl);
                return;
            }

            ctrl.MoveAway();
            ctrl.FaceTarget();

            if (!jumped && ctrl.IsGrounded()) { ctrl.RequestJump(0.28f); jumped = true; }

            // Fire a parting shot while fleeing
            if (!shotFired && hd.RangedAttack != null && hd.RangedAttack.CurrentAmmo > 0 && !hd.RangedAttack.IsReloading)
            {
                ctrl.RequestAttack();
                shotFired = true;
            }

            if (ctrl.DistanceToTarget() >= hd.preferredRange)
                hd.FSM.ChangeState(hd.WaitReloadState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // WAIT RELOAD — Waits for full ammo, then a short pause (0.40-1.00s) before
    // re-engaging. No stamina gate — just waiting on ammo. Maintains safe distance.
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

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hd.FSM.ChangeState(hd.EvadeState, ctrl);
                return;
            }

            if (dist < hd.retreatTriggerDistance) { hd.FSM.ChangeState(hd.HardRetreatState, ctrl); return; }
            if (dist < hd.preferredRange) ctrl.MoveAway();
            else ctrl.StopMoving();

            bool fullyLoaded = hd.RangedAttack == null
                            || (hd.RangedAttack.CurrentAmmo == hd.RangedAttack.MaxAmmo && !hd.RangedAttack.IsReloading);

            if (fullyLoaded && !conditionsMet)
            {
                conditionsMet = true;
                extraWait     = Random.Range(0.40f, 1.00f);
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

    // ==========================================================================
    // EVADE — Triggered by artillery or boss threat. Defensive bot jumps back
    // then goes to WaitReload — consistent with the cautious archetype.
    // ==========================================================================
    public class HD_Evade : IPlayerFSMState
    {
        private float timer;
        private bool  jumped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.ConsumeArtilleryEvade();
            jumped = ctrl.IsGrounded();
            if (jumped)
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
            timer = Random.Range(0.35f, 0.55f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hd = (HunterDefensiveFSM)ctrl;
            ctrl.MoveAway();
            ctrl.FaceTarget();
            timer -= Time.deltaTime;
            if (timer <= 0f)
                hd.FSM.ChangeState(hd.WaitReloadState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
