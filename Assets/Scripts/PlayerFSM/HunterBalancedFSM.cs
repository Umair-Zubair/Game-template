using UnityEngine;

// ============================================================================
// HUNTER BALANCED FSM
//
// Positions at mid range, briefly aims before firing a random burst (1-3 shots),
// slides back, then re-engages. Stamina is NOT checked by the FSM — the attack
// component handles it. The bot never stands idle waiting for stamina.
//
// States: FindRange → Aim → Shoot / JumpShoot → SlideBack → FindRange / WaitAmmo
//         Any state → Evade (artillery or boss attack at close range)
// ============================================================================
public class HunterBalancedFSM : PlayerFSMController
{
    [Header("Hunter Balanced — Tuning")]
    [SerializeField] private float preferredRange         = 6.0f;
    [SerializeField] private float rangeTolerance         = 0.8f;
    [SerializeField] private float retreatTriggerDistance = 2.0f;
    [SerializeField] private float reloadThresholdRatio   = 0.20f;  // re-engage with just 1/3 ammo
    [SerializeField] private float jumpShootChance        = 0.20f;

    public HB_FindRange FindRangeState { get; private set; }
    public HB_Aim       AimState       { get; private set; }
    public HB_Shoot     ShootState     { get; private set; }
    public HB_JumpShoot JumpShootState { get; private set; }
    public HB_SlideBack SlideBackState { get; private set; }
    public HB_WaitAmmo  WaitAmmoState  { get; private set; }
    public HB_Evade     EvadeState     { get; private set; }

    protected override void InitializeStates()
    {
        FindRangeState = new HB_FindRange();
        AimState       = new HB_Aim();
        ShootState     = new HB_Shoot();
        JumpShootState = new HB_JumpShoot();
        SlideBackState = new HB_SlideBack();
        WaitAmmoState  = new HB_WaitAmmo();
        EvadeState     = new HB_Evade();
    }

    protected override void StartFSM() => FSM.Initialize(FindRangeState, this);

    // ==========================================================================
    // FIND RANGE — Adjusts position to the preferred band. No stamina gate —
    // as soon as in range with any ammo, transitions straight to Aim.
    // ==========================================================================
    public class HB_FindRange : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb   = (HunterBalancedFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            if (dist < hb.retreatTriggerDistance)
            {
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && dist < hb.dangerRange)
            {
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            // Reloading — hold range while waiting, don't go to WaitAmmo yet
            if (hb.RangedAttack != null && hb.RangedAttack.IsReloading)
            {
                float min = hb.preferredRange - hb.rangeTolerance;
                float max = hb.preferredRange + hb.rangeTolerance;
                if      (dist < min) ctrl.MoveAway();
                else if (dist > max) ctrl.MoveTowardAt(0.65f);
                else                 ctrl.StopMoving();
                return;
            }

            bool hasAmmo = hb.RangedAttack == null || hb.RangedAttack.CurrentAmmo > 0;
            if (!hasAmmo) { hb.FSM.ChangeState(hb.WaitAmmoState, ctrl); return; }

            float minR = hb.preferredRange - hb.rangeTolerance;
            float maxR = hb.preferredRange + hb.rangeTolerance;

            if      (dist < minR) ctrl.MoveAway();
            else if (dist > maxR) ctrl.MoveTowardAt(0.70f);
            else
            {
                ctrl.StopMoving();
                hb.FSM.ChangeState(hb.AimState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // AIM — Brief stop before the burst. Bails on boss attack mid-aim.
    // ==========================================================================
    public class HB_Aim : IPlayerFSMState
    {
        private float timer;
        private bool  willJump;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            timer    = Random.Range(0.08f, 0.18f);
            willJump = ctrl.IsGrounded() && Random.value < hb.jumpShootChance;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            if (ctrl.DistanceToTarget() < hb.retreatTriggerDistance)
            {
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < hb.dangerRange)
            {
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                hb.FSM.ChangeState(willJump ? (IPlayerFSMState)hb.JumpShootState : hb.ShootState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // SHOOT — Random burst (1-3 shots), variable inter-shot interval.
    // Interrupts immediately if artillery fires mid-burst.
    // ==========================================================================
    public class HB_Shoot : IPlayerFSMState
    {
        private int   targetBurst;
        private int   shotsFired;
        private float shotTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            targetBurst = Random.Range(1, 4);
            shotsFired  = 0;
            shotTimer   = 0f;
            ctrl.RequestAttack();
            shotsFired++;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            if (ctrl.DistanceToTarget() < hb.retreatTriggerDistance || shotsFired >= targetBurst)
            {
                hb.FSM.ChangeState(hb.SlideBackState, ctrl);
                return;
            }

            shotTimer -= Time.deltaTime;
            if (shotTimer <= 0f)
            {
                bool hasAmmo = hb.RangedAttack == null || hb.RangedAttack.CurrentAmmo > 0;
                if (hasAmmo)
                {
                    ctrl.RequestAttack();
                    shotsFired++;
                    shotTimer = Random.Range(0.12f, 0.28f);
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
    // JUMP SHOOT — Jumps, fires one aerial shot, slides back on landing.
    // ==========================================================================
    public class HB_JumpShoot : IPlayerFSMState
    {
        private float airTimer;
        private bool  shotFired;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestJump(Random.Range(0.20f, 0.28f));
            airTimer  = Random.Range(0.08f, 0.13f);
            shotFired = false;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            airTimer -= Time.deltaTime;
            if (!shotFired && airTimer <= 0f) { ctrl.RequestAttack(); shotFired = true; }
            if (shotFired && ctrl.IsGrounded()) hb.FSM.ChangeState(hb.SlideBackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // SLIDE BACK — Short retreat after a burst. Checks ammo on exit — re-engages
    // with even 1 bullet left (20% threshold). Almost always goes to FindRange.
    // ==========================================================================
    public class HB_SlideBack : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = Random.Range(0.15f, 0.35f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            ctrl.MoveAway();
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                float ammoRatio = hb.RangedAttack == null
                    ? 1f
                    : (float)hb.RangedAttack.CurrentAmmo / Mathf.Max(1, hb.RangedAttack.MaxAmmo);
                hb.FSM.ChangeState(
                    ammoRatio >= hb.reloadThresholdRatio ? (IPlayerFSMState)hb.FindRangeState : hb.WaitAmmoState,
                    ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // WAIT AMMO — Holds comfortable distance during reload. No stamina gate.
    // Tiny extra wait (0.05-0.15s) once ammo is back — feels like reacting,
    // not like instantly springing back.
    // ==========================================================================
    public class HB_WaitAmmo : IPlayerFSMState
    {
        private float extraWait;
        private bool  conditionsMet;

        public void OnEnter(PlayerFSMController ctrl) { ctrl.StopMoving(); conditionsMet = false; extraWait = 0f; }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb   = (HunterBalancedFSM)ctrl;
            float dist = ctrl.DistanceToTarget();
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                hb.FSM.ChangeState(hb.EvadeState, ctrl);
                return;
            }

            // Keep preferred range even while waiting
            float minComfort = hb.preferredRange - hb.rangeTolerance;
            float maxComfort = hb.preferredRange + hb.rangeTolerance;
            if      (dist < minComfort) ctrl.MoveAway();
            else if (dist > maxComfort) ctrl.MoveTowardAt(0.50f);
            else                        ctrl.StopMoving();

            float ammoRatio = hb.RangedAttack == null
                ? 1f
                : (float)hb.RangedAttack.CurrentAmmo / Mathf.Max(1, hb.RangedAttack.MaxAmmo);

            if (ammoRatio >= hb.reloadThresholdRatio && !conditionsMet)
            {
                conditionsMet = true;
                extraWait     = Random.Range(0.05f, 0.15f);
            }

            if (conditionsMet)
            {
                extraWait -= Time.deltaTime;
                if (extraWait <= 0f)
                    hb.FSM.ChangeState(hb.FindRangeState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // EVADE — Triggered by artillery or boss closing in. Jumps and repositions
    // to preferred range — not a full retreat, keeps the engagement alive.
    // ==========================================================================
    public class HB_Evade : IPlayerFSMState
    {
        private float timer;
        private bool  jumped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.ConsumeArtilleryEvade();
            jumped = ctrl.IsGrounded() && Random.value < 0.65f;
            if (jumped)
                ctrl.RequestJump(Random.Range(0.22f, 0.30f));
            timer = Random.Range(0.30f, 0.50f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            float dist = ctrl.DistanceToTarget();
            if (dist < hb.preferredRange * 0.8f)
                ctrl.MoveAway();
            else if (dist > hb.preferredRange * 1.3f)
                ctrl.MoveTowardAt(0.60f);

            timer -= Time.deltaTime;
            if (timer <= 0f)
                hb.FSM.ChangeState(hb.FindRangeState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
