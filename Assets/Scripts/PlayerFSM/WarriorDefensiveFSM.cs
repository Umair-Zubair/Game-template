using UnityEngine;

// ============================================================================
// WARRIOR DEFENSIVE FSM
//
// Cautious and methodical. Approaches with pauses, commits to one hit, then
// retreats. Now reacts to boss attacks — if the boss swings during approach
// or commit, the bot evades rather than eating the hit. Health influences
// how long it stays back between engagements.
//
// States: Wait → Approach → Commit → Retreat → Wait
//         Any state → Evade (boss attack or artillery)
// ============================================================================
public class WarriorDefensiveFSM : PlayerFSMController
{
    [Header("Warrior Defensive — Tuning")]
    [SerializeField] private float attackRange           = 3.0f;
    [SerializeField] private float panicDistance         = 1.5f;
    [SerializeField] private float approachSpeedFraction = 0.55f;
    [SerializeField] private float minRetreatDistance    = 5.0f;
    [SerializeField] private float maxRetreatDistance    = 6.5f;

    public WD_Wait     WaitState     { get; private set; }
    public WD_Approach ApproachState { get; private set; }
    public WD_Commit   CommitState   { get; private set; }
    public WD_Retreat  RetreatState  { get; private set; }
    public WD_Evade    EvadeState    { get; private set; }

    protected override void InitializeStates()
    {
        WaitState     = new WD_Wait();
        ApproachState = new WD_Approach();
        CommitState   = new WD_Commit();
        RetreatState  = new WD_Retreat();
        EvadeState    = new WD_Evade();
    }

    protected override void StartFSM() => FSM.Initialize(WaitState, this);

    // ==========================================================================
    // WAIT — Stands at safe distance. Mandatory wait time scales with damage
    // taken — if hurt, stays back longer before re-engaging.
    // ==========================================================================
    public class WD_Wait : IPlayerFSMState
    {
        private float mandatoryWait;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            // Low health → wait slightly longer before committing again
            float healthMod = 1f + (1f - ctrl.HealthRatio()) * 0.5f;
            mandatoryWait   = Random.Range(0.80f, 1.50f) * healthMod;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wd.FSM.ChangeState(wd.EvadeState, ctrl);
                return;
            }

            if (ctrl.DistanceToTarget() < wd.panicDistance)
            {
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
                return;
            }

            ctrl.FaceTarget();
            mandatoryWait -= Time.deltaTime;

            if (mandatoryWait <= 0f)
                wd.FSM.ChangeState(wd.ApproachState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // APPROACH — Moves slowly with natural pauses. If the boss swings during
    // approach, evade rather than walking into it.
    // ==========================================================================
    public class WD_Approach : IPlayerFSMState
    {
        private float stopTimer;
        private float moveTimer;
        private bool  isStopped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            isStopped = false;
            moveTimer = Random.Range(0.5f, 1.0f);
            stopTimer = 0f;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd   = (WarriorDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wd.FSM.ChangeState(wd.EvadeState, ctrl);
                return;
            }

            // Boss attacks while we're approaching — evade, don't commit
            if (ctrl.BossIsAttacking && dist < wd.dangerRange)
            {
                wd.FSM.ChangeState(wd.EvadeState, ctrl);
                return;
            }

            if (dist < wd.panicDistance)
            {
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
                return;
            }

            if (dist <= wd.attackRange)
            {
                ctrl.StopMoving();
                wd.FSM.ChangeState(wd.CommitState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            if (isStopped)
            {
                ctrl.StopMoving();
                stopTimer -= Time.deltaTime;
                if (stopTimer <= 0f)
                {
                    isStopped = false;
                    moveTimer = Random.Range(0.5f, 1.1f);
                }
            }
            else
            {
                ctrl.MoveTowardAt(wd.approachSpeedFraction);
                moveTimer -= Time.deltaTime;
                if (moveTimer <= 0f)
                {
                    isStopped = true;
                    stopTimer = Random.Range(0.25f, 0.55f);
                }
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // COMMIT — Sizes up the boss, attacks once, then retreats. If the boss
    // starts swinging during the size-up, cancel — don't get hit for free.
    // ==========================================================================
    public class WD_Commit : IPlayerFSMState
    {
        private float sizeUpTimer;
        private bool  attacked;
        private float afterTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            sizeUpTimer = Random.Range(0.20f, 0.45f);
            attacked    = false;
            afterTimer  = 0f;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wd.FSM.ChangeState(wd.EvadeState, ctrl);
                return;
            }

            if (!attacked)
            {
                // Boss winds up while we're sizing up — don't commit, evade
                if (ctrl.BossIsAttacking)
                {
                    wd.FSM.ChangeState(wd.EvadeState, ctrl);
                    return;
                }

                sizeUpTimer -= Time.deltaTime;
                if (sizeUpTimer <= 0f)
                {
                    ctrl.RequestAttack();
                    attacked   = true;
                    afterTimer = Random.Range(0.30f, 0.50f);
                }
            }
            else
            {
                afterTimer -= Time.deltaTime;
                if (afterTimer <= 0f)
                    wd.FSM.ChangeState(wd.RetreatState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // RETREAT — Runs to a randomised safe distance, then waits.
    // ==========================================================================
    public class WD_Retreat : IPlayerFSMState
    {
        private float targetDistance;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            // Hurt → retreat farther
            float healthMod    = 1f + (1f - ctrl.HealthRatio()) * 0.5f;
            targetDistance     = Random.Range(wd.minRetreatDistance, wd.maxRetreatDistance) * healthMod;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            ctrl.MoveAway();
            if (ctrl.DistanceToTarget() >= targetDistance)
                wd.FSM.ChangeState(wd.WaitState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // EVADE — Triggered by boss attack or artillery during approach/commit.
    // Defensive bot always dashes away rather than jumping — keeps ground.
    // After evading, goes straight to Retreat → Wait (doesn't immediately
    // re-approach, that would defeat the defensive style).
    // ==========================================================================
    public class WD_Evade : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.ConsumeArtilleryEvade();
            // Defensive bot prefers dash-back over jump, but occasionally jumps
            if (ctrl.IsGrounded() && Random.value < 0.30f)
                ctrl.RequestJump(Random.Range(0.15f, 0.25f));
            timer = Random.Range(0.30f, 0.55f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            ctrl.MoveAway();
            ctrl.FaceTarget();
            timer -= Time.deltaTime;
            if (timer <= 0f)
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
