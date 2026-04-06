using UnityEngine;

// ============================================================================
// HUNTER AGGRESSIVE FSM
//
// Closes in fast, reacts quickly but not instantly (small reaction delay),
// fires and immediately repositions in a loop. Keeps advancing during reload.
// Jump-shoots opportunistically. The loop feels fast but not perfectly mechanical
// because every timer has variance.
//
// States: Press → React → Shoot / JumpShoot → Press (or ReloadAdvance)
// ============================================================================
public class HunterAggressiveFSM : PlayerFSMController
{
    [Header("Hunter Aggressive — Tuning")]
    [SerializeField] private float preferredRange     = 3.0f;
    [SerializeField] private float pointBlankDistance = 0.8f;
    [SerializeField] private float staminaThreshold   = 12f;
    [SerializeField] private float jumpShootChance    = 0.50f;

    public HA_Press         PressState         { get; private set; }
    public HA_React         ReactState         { get; private set; }
    public HA_Shoot         ShootState         { get; private set; }
    public HA_JumpShoot     JumpShootState     { get; private set; }
    public HA_ReloadAdvance ReloadAdvanceState { get; private set; }

    protected override void InitializeStates()
    {
        PressState         = new HA_Press();
        ReactState         = new HA_React();
        ShootState         = new HA_Shoot();
        JumpShootState     = new HA_JumpShoot();
        ReloadAdvanceState = new HA_ReloadAdvance();
    }

    protected override void StartFSM() => FSM.Initialize(PressState, this);

    // ==========================================================================
    // PRESS — Advances toward the boss. Backs off if too close.
    // When in range and ready, moves to React rather than shooting instantly.
    // ==========================================================================
    public class HA_Press : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            if (dist < ha.pointBlankDistance)
            {
                ctrl.MoveAway();
                return;
            }

            ctrl.MoveToward();
            ctrl.FaceTarget();

            if (ha.RangedAttack != null && ha.RangedAttack.IsReloading)
            {
                ha.FSM.ChangeState(ha.ReloadAdvanceState, ctrl);
                return;
            }

            bool hasAmmo    = ha.RangedAttack != null && ha.RangedAttack.CurrentAmmo > 0;
            bool hasStamina = ctrl.Stamina == null || ctrl.Stamina.CurrentStamina > ha.staminaThreshold;

            if (dist <= ha.preferredRange && hasAmmo && hasStamina)
                ha.FSM.ChangeState(ha.ReactState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // REACT — Brief pause before shooting. Simulates the human moment of
    // "lining up the shot" rather than firing the instant you're in range.
    // Decides here whether to jump-shoot or stay grounded.
    // ==========================================================================
    public class HA_React : IPlayerFSMState
    {
        private float timer;
        private bool  willJump;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            timer    = Random.Range(0.08f, 0.22f);
            willJump = ctrl.IsGrounded() && Random.value < ha.jumpShootChance;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            // Boss moved away — back to pressing
            if (ctrl.DistanceToTarget() > ha.preferredRange * 1.4f)
            {
                ha.FSM.ChangeState(ha.PressState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                ha.FSM.ChangeState(willJump ? (IPlayerFSMState)ha.JumpShootState : ha.ShootState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // SHOOT — Fires one shot, returns to Press. The React→Shoot→Press loop
    // creates rapid fire with just enough timing variation to not look scripted.
    // ==========================================================================
    public class HA_Shoot : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestAttack();
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
    // JUMP SHOOT — Jumps then fires. Random hold duration so jump height varies.
    // ==========================================================================
    public class HA_JumpShoot : IPlayerFSMState
    {
        private float airTimer;
        private bool  shotFired;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestJump(Random.Range(0.18f, 0.30f));
            airTimer  = Random.Range(0.08f, 0.14f);
            shotFired = false;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.FaceTarget();
            airTimer -= Time.deltaTime;
            if (!shotFired && airTimer <= 0f) { ctrl.RequestAttack(); shotFired = true; }
            if (shotFired && ctrl.IsGrounded()) ha.FSM.ChangeState(ha.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // RELOAD ADVANCE — Keeps closing distance during reload. Aggressive bot
    // never stops moving just because it's out of ammo.
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
