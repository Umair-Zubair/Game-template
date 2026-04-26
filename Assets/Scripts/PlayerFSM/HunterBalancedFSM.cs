using UnityEngine;

// ============================================================================
// HUNTER BALANCED FSM
//
// Positions at mid range, briefly aims before firing a random burst (1-3 shots),
// slides back, then re-engages. Stamina is factored into every decision:
//   - Low stamina  → shorter bursts, longer aim hesitation, wider comfort range
//   - Exhausted    → won't engage, holds back until recovered
//   - Jump-shoots are skipped when stamina can't afford the jump
//   - WaitAmmo also waits for stamina before re-engaging
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
    [SerializeField] private float reloadThresholdRatio   = 0.20f;
    [SerializeField] private float jumpShootChance        = 0.20f;

    [Header("Stamina Awareness")]
    [Tooltip("Below this ratio the bot plays cautiously — shorter bursts, longer aim, wider range.")]
    [SerializeField] private float lowStaminaThreshold    = 0.30f;
    [Tooltip("Extra distance added to preferred range when stamina is low.")]
    [SerializeField] private float lowStaminaRangeBuffer  = 1.5f;
    [Tooltip("Stamina ratio required before re-engaging after waiting.")]
    [SerializeField] private float reengageStaminaRatio   = 0.35f;

    public HB_FindRange FindRangeState { get; private set; }
    public HB_Aim       AimState       { get; private set; }
    public HB_Shoot     ShootState     { get; private set; }
    public HB_JumpShoot JumpShootState { get; private set; }
    public HB_SlideBack SlideBackState { get; private set; }
    public HB_WaitAmmo  WaitAmmoState  { get; private set; }
    public HB_Evade     EvadeState     { get; private set; }

    public bool  IsLowStamina      => StaminaRatio() < lowStaminaThreshold;
    public bool  IsExhaustedStamina => Stamina != null && Stamina.IsExhausted;

    /// <summary>
    /// Preferred range widens when stamina is low so the bot plays safer.
    /// </summary>
    public float EffectiveRange => IsExhaustedStamina
        ? preferredRange + lowStaminaRangeBuffer
        : IsLowStamina
            ? preferredRange + lowStaminaRangeBuffer * 0.5f
            : preferredRange;

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
    // FIND RANGE — Adjusts position to the preferred band.  Uses EffectiveRange
    // so the bot stands farther back when stamina is low.  Won't engage while
    // exhausted — holds distance and waits for recovery first.
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

            float effRange = hb.EffectiveRange;
            float minR     = effRange - hb.rangeTolerance;
            float maxR     = effRange + hb.rangeTolerance;

            if (hb.RangedAttack != null && hb.RangedAttack.IsReloading)
            {
                if      (dist < minR) ctrl.MoveAway();
                else if (dist > maxR) ctrl.MoveTowardAt(0.65f);
                else                  ctrl.StopMoving();
                return;
            }

            bool hasAmmo = hb.RangedAttack == null || hb.RangedAttack.CurrentAmmo > 0;
            if (!hasAmmo) { hb.FSM.ChangeState(hb.WaitAmmoState, ctrl); return; }

            if      (dist < minR) ctrl.MoveAway();
            else if (dist > maxR) ctrl.MoveTowardAt(0.70f);
            else
            {
                ctrl.StopMoving();

                // Exhausted — hold position until stamina recovers enough to fight
                if (hb.IsExhaustedStamina) return;

                hb.FSM.ChangeState(hb.AimState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // AIM — Brief stop before the burst. Low stamina means longer hesitation
    // and no jump-shoots (conserves the jump cost). Bails on boss attack.
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

            // Low stamina → longer hesitation (cautious), high → snappy aim
            timer = hb.IsLowStamina
                ? Random.Range(0.18f, 0.35f)
                : Random.Range(0.08f, 0.18f);

            // Skip jump-shoot when stamina is low — jumping costs stamina
            willJump = !hb.IsLowStamina && ctrl.IsGrounded() && Random.value < hb.jumpShootChance;
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
    // SHOOT — Burst size scales with stamina: 1-3 when flush, capped to 1
    // when low. Aborts immediately if exhausted mid-burst.
    // ==========================================================================
    public class HB_Shoot : IPlayerFSMState
    {
        private int   targetBurst;
        private int   shotsFired;
        private float shotTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();

            targetBurst = hb.IsLowStamina ? 1 : Random.Range(1, 4);
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

            // Exhausted mid-burst — stop firing and slide back
            if (hb.IsExhaustedStamina || ctrl.DistanceToTarget() < hb.retreatTriggerDistance
                                      || shotsFired >= targetBurst)
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
    // Falls back to ground Shoot if stamina is too low for the jump.
    // ==========================================================================
    public class HB_JumpShoot : IPlayerFSMState
    {
        private float airTimer;
        private bool  shotFired;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;

            // Can't afford the jump — do a ground shot instead
            if (hb.IsLowStamina)
            {
                hb.FSM.ChangeState(hb.ShootState, ctrl);
                return;
            }

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
    // SLIDE BACK — Short retreat after a burst. Slides farther when stamina is
    // low. If exhausted, always goes to WaitAmmo for a breather regardless of
    // ammo state.
    // ==========================================================================
    public class HB_SlideBack : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var hb = (HunterBalancedFSM)ctrl;
            timer = hb.IsLowStamina
                ? Random.Range(0.30f, 0.55f)
                : Random.Range(0.15f, 0.35f);
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
                // Exhausted — always pause to recover, even if ammo is fine
                if (hb.IsExhaustedStamina)
                {
                    hb.FSM.ChangeState(hb.WaitAmmoState, ctrl);
                    return;
                }

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
    // WAIT AMMO — Holds comfortable distance during reload. Also waits for
    // stamina to recover past reengageStaminaRatio before re-engaging — the
    // bot won't spring back into the fight while gassed.
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

            float effRange   = hb.EffectiveRange;
            float minComfort = effRange - hb.rangeTolerance;
            float maxComfort = effRange + hb.rangeTolerance;
            if      (dist < minComfort) ctrl.MoveAway();
            else if (dist > maxComfort) ctrl.MoveTowardAt(0.50f);
            else                        ctrl.StopMoving();

            float ammoRatio = hb.RangedAttack == null
                ? 1f
                : (float)hb.RangedAttack.CurrentAmmo / Mathf.Max(1, hb.RangedAttack.MaxAmmo);

            bool ammoReady    = ammoRatio >= hb.reloadThresholdRatio;
            bool staminaReady = !hb.IsExhaustedStamina && ctrl.StaminaRatio() >= hb.reengageStaminaRatio;

            if (ammoReady && staminaReady && !conditionsMet)
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

            float dist     = ctrl.DistanceToTarget();
            float effRange = hb.EffectiveRange;
            if (dist < effRange * 0.8f)
                ctrl.MoveAway();
            else if (dist > effRange * 1.3f)
                ctrl.MoveTowardAt(0.60f);

            timer -= Time.deltaTime;
            if (timer <= 0f)
                hb.FSM.ChangeState(hb.FindRangeState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
