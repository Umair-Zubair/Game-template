using UnityEngine;

// ============================================================================
// HUNTER BALANCED FSM
//
// Positions at mid range, briefly aims before firing a random-sized burst
// (1-3 shots), slides back, waits for partial reload. Nothing is perfectly
// timed — burst size, shot intervals, and slide duration all vary each cycle.
//
// States: FindRange → Aim → Shoot / JumpShoot → SlideBack → FindRange / WaitAmmo
// ============================================================================
public class HunterBalancedFSM : PlayerFSMController
{
    [Header("Hunter Balanced — Tuning")]
    [SerializeField] private float preferredRange        = 4.5f;
    [SerializeField] private float rangeTolerance        = 0.8f;
    [SerializeField] private float retreatTriggerDistance = 2.0f;
    [SerializeField] private float staminaThresholdRatio = 0.35f;
    [SerializeField] private float reloadThresholdRatio  = 0.50f;
    [SerializeField] private float jumpShootChance       = 0.30f;

    public HB_FindRange FindRangeState { get; private set; }
    public HB_Aim       AimState       { get; private set; }
    public HB_Shoot     ShootState     { get; private set; }
    public HB_JumpShoot JumpShootState { get; private set; }
    public HB_SlideBack SlideBackState { get; private set; }
    public HB_WaitAmmo  WaitAmmoState  { get; private set; }

    protected override void InitializeStates()
    {
        FindRangeState = new HB_FindRange();
        AimState       = new HB_Aim();
        ShootState     = new HB_Shoot();
        JumpShootState = new HB_JumpShoot();
        SlideBackState = new HB_SlideBack();
        WaitAmmoState  = new HB_WaitAmmo();
    }

    protected override void StartFSM() => FSM.Initialize(FindRangeState, this);

    // ==========================================================================
    // FIND RANGE — Adjusts position to the preferred band. Once in range and
    // ready, transitions to Aim rather than shooting instantly.
    // ==========================================================================
    public class HB_FindRange : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb   = (HunterBalancedFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            ctrl.FaceTarget();

            if (dist < hb.retreatTriggerDistance) { ctrl.MoveAway(); return; }

            bool hasAmmo = hb.RangedAttack != null && hb.RangedAttack.CurrentAmmo > 0 && !hb.RangedAttack.IsReloading;
            if (!hasAmmo) { hb.FSM.ChangeState(hb.WaitAmmoState, ctrl); return; }

            float min = hb.preferredRange - hb.rangeTolerance;
            float max = hb.preferredRange + hb.rangeTolerance;

            if      (dist < min) ctrl.MoveAway();
            else if (dist > max) ctrl.MoveTowardAt(0.70f);
            else
            {
                ctrl.StopMoving();
                if (ctrl.StaminaRatio() >= hb.staminaThresholdRatio)
                    hb.FSM.ChangeState(hb.AimState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // AIM — Brief stop before the burst. Decides whether to jump-shoot.
    // The random duration (0.15-0.45s) means the "aim time" varies each burst.
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
            timer    = Random.Range(0.15f, 0.45f);
            willJump = ctrl.IsGrounded() && Random.value < hb.jumpShootChance;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.DistanceToTarget() < hb.retreatTriggerDistance)
            {
                hb.FSM.ChangeState(hb.SlideBackState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                hb.FSM.ChangeState(willJump ? (IPlayerFSMState)hb.JumpShootState : hb.ShootState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // SHOOT — Random burst size (1-3 shots), random interval between shots.
    // Each engagement has a different rhythm because of this.
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
            targetBurst = Random.Range(1, 4); // 1, 2, or 3 shots
            shotsFired  = 0;
            shotTimer   = 0f;
            ctrl.RequestAttack();
            shotsFired++;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.DistanceToTarget() < hb.retreatTriggerDistance || shotsFired >= targetBurst)
            {
                hb.FSM.ChangeState(hb.SlideBackState, ctrl);
                return;
            }

            shotTimer -= Time.deltaTime;
            if (shotTimer <= 0f)
            {
                bool hasAmmo = hb.RangedAttack != null && hb.RangedAttack.CurrentAmmo > 0;
                if (hasAmmo)
                {
                    ctrl.RequestAttack();
                    shotsFired++;
                    shotTimer = Random.Range(0.10f, 0.25f);
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
            airTimer -= Time.deltaTime;
            if (!shotFired && airTimer <= 0f) { ctrl.RequestAttack(); shotFired = true; }
            if (shotFired && ctrl.IsGrounded()) hb.FSM.ChangeState(hb.SlideBackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // SLIDE BACK — Random duration. After the slide, checks ammo level to
    // decide between re-engaging or waiting.
    // ==========================================================================
    public class HB_SlideBack : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = Random.Range(0.30f, 0.60f);
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
                    ammoRatio >= hb.reloadThresholdRatio ? (IPlayerFSMState)hb.FindRangeState : hb.WaitAmmoState,
                    ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // WAIT AMMO — Holds distance until 50% ammo and stamina are back.
    // Adds a short extra pause even once conditions are met.
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

            float minComfort = hb.preferredRange - hb.rangeTolerance;
            if (dist < minComfort) ctrl.MoveAway();
            else ctrl.StopMoving();

            float ammoRatio = hb.RangedAttack == null
                ? 1f
                : (float)hb.RangedAttack.CurrentAmmo / Mathf.Max(1, hb.RangedAttack.MaxAmmo);

            bool ready = ammoRatio >= hb.reloadThresholdRatio && ctrl.StaminaRatio() >= hb.staminaThresholdRatio;

            if (ready && !conditionsMet)
            {
                conditionsMet = true;
                extraWait     = Random.Range(0.40f, 1.20f);
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
}
