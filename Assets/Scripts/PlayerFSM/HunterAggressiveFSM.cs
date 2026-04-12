using UnityEngine;

// ============================================================================
// HUNTER AGGRESSIVE FSM
//
// Closes in fast, fires and immediately repositions in a loop. Reacts to boss
// attacks and artillery — jumps away and repositions to preferred shooting range
// rather than fully retreating. Keeps advancing during reload. Stamina is NOT
// checked — the attack component handles stamina; the FSM just keeps pressing.
//
// States: Press → React → Shoot / JumpShoot → Press (or ReloadAdvance)
//         Any state → Evade (artillery or boss attack at close range)
// ============================================================================
public class HunterAggressiveFSM : PlayerFSMController
{
    [Header("Hunter Aggressive — Tuning")]
    [SerializeField] private float preferredRange     = 4.5f;
    [SerializeField] private float pointBlankDistance = 4.0f;
    [SerializeField] private float jumpShootChance    = 0.30f;

    public HA_Press         PressState         { get; private set; }
    public HA_React         ReactState         { get; private set; }
    public HA_Shoot         ShootState         { get; private set; }
    public HA_JumpShoot     JumpShootState     { get; private set; }
    public HA_ReloadAdvance ReloadAdvanceState { get; private set; }
    public HA_Evade         EvadeState         { get; private set; }

    protected override void InitializeStates()
    {
        PressState         = new HA_Press();
        ReactState         = new HA_React();
        ShootState         = new HA_Shoot();
        JumpShootState     = new HA_JumpShoot();
        ReloadAdvanceState = new HA_ReloadAdvance();
        EvadeState         = new HA_Evade();
    }

    protected override void StartFSM() => FSM.Initialize(PressState, this);

    // ==========================================================================
    // PRESS — Advances toward the boss. Backs off if too close.
    // Evades if boss is attacking at danger range, or artillery fires.
    // ==========================================================================
    public class HA_Press : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && dist < ha.dangerRange)
            {
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

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

            bool hasAmmo = ha.RangedAttack == null || ha.RangedAttack.CurrentAmmo > 0;
            if (dist <= ha.preferredRange && hasAmmo)
                ha.FSM.ChangeState(ha.ReactState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // REACT — Brief pause to line up the shot. Very short — aggressive bot
    // doesn't wait long. Bails if boss swings during this window.
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
            timer    = Random.Range(0.05f, 0.14f);
            willJump = ctrl.IsGrounded() && Random.value < ha.jumpShootChance;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < ha.dangerRange)
            {
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

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
    // SHOOT — Fires one shot, returns to Press. Fast cycle.
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

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

            ctrl.FaceTarget();
            if (ha.RangedAttack != null && ha.RangedAttack.IsReloading)
                ha.FSM.ChangeState(ha.ReloadAdvanceState, ctrl);
            else
                ha.FSM.ChangeState(ha.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // JUMP SHOOT — Jumps then fires at apex. Variable hold so jump height differs.
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
            airTimer  = Random.Range(0.07f, 0.13f);
            shotFired = false;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

            airTimer -= Time.deltaTime;
            if (!shotFired && airTimer <= 0f) { ctrl.RequestAttack(); shotFired = true; }
            if (shotFired && ctrl.IsGrounded()) ha.FSM.ChangeState(ha.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // RELOAD ADVANCE — Closes distance during reload. Evades if boss swings.
    // ==========================================================================
    public class HA_ReloadAdvance : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < ha.dangerRange)
            {
                ha.FSM.ChangeState(ha.EvadeState, ctrl);
                return;
            }

            ctrl.MoveToward();
            ctrl.FaceTarget();
            if (ha.RangedAttack == null || !ha.RangedAttack.IsReloading)
                ha.FSM.ChangeState(ha.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // EVADE — Triggered by artillery or boss swinging at close range.
    // Jumps and repositions to preferred shooting range — keeps offensive
    // pressure alive from a safe angle, not a full retreat.
    // ==========================================================================
    public class HA_Evade : IPlayerFSMState
    {
        private float timer;
        private bool  jumped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.ConsumeArtilleryEvade();
            jumped = ctrl.IsGrounded() && Random.value < 0.70f;
            if (jumped)
                ctrl.RequestJump(Random.Range(0.22f, 0.32f));
            timer = Random.Range(0.30f, 0.50f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var ha = (HunterAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            float dist = ctrl.DistanceToTarget();
            if (dist < ha.preferredRange * 0.7f)
                ctrl.MoveAway();
            else if (dist > ha.preferredRange * 1.4f)
                ctrl.MoveTowardAt(0.65f);

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                bool inRange = dist >= ha.preferredRange * 0.7f && dist <= ha.preferredRange * 1.4f;
                bool hasAmmo = ha.RangedAttack == null || (ha.RangedAttack.CurrentAmmo > 0 && !ha.RangedAttack.IsReloading);
                ha.FSM.ChangeState(inRange && hasAmmo ? (IPlayerFSMState)ha.ReactState : ha.PressState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
