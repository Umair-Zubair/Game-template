using UnityEngine;

// ============================================================================
// WARRIOR DEFENSIVE FSM
//
// Cautious, methodical. Approaches with stops — like someone sneaking up.
// Stands at attack range for a moment before committing to the strike.
// After hitting, runs far away and STAYS away for a meaningful amount of time
// before considering another approach.
//
// The previous version had a 0.15s pause and came back in under a second —
// this version mandates a 2.5-4.5s minimum retreat, which actually reads as
// defensive.
//
// States: Wait → Approach → Commit → Retreat → Wait
// ============================================================================
public class WarriorDefensiveFSM : PlayerFSMController
{
    [Header("Warrior Defensive — Tuning")]
    [SerializeField] private float attackRange            = 3.0f;
    [SerializeField] private float panicDistance          = 1.5f;
    [SerializeField] private float staminaRequiredRatio   = 0.70f;
    [SerializeField] private float approachSpeedFraction  = 0.55f;
    [SerializeField] private float minRetreatDistance     = 6.0f;
    [SerializeField] private float maxRetreatDistance     = 7.5f;

    public WD_Wait     WaitState     { get; private set; }
    public WD_Approach ApproachState { get; private set; }
    public WD_Commit   CommitState   { get; private set; }
    public WD_Retreat  RetreatState  { get; private set; }

    protected override void InitializeStates()
    {
        WaitState     = new WD_Wait();
        ApproachState = new WD_Approach();
        CommitState   = new WD_Commit();
        RetreatState  = new WD_Retreat();
    }

    protected override void StartFSM() => FSM.Initialize(WaitState, this);

    // ==========================================================================
    // WAIT — Stands at safe distance. Has a mandatory minimum wait time that
    // must expire before the bot will approach, regardless of stamina.
    // This is the key fix: previously the bot came back in ~1s because stamina
    // recovers fast. Now it stays away for 2.5-4.5 real seconds minimum.
    // ==========================================================================
    public class WD_Wait : IPlayerFSMState
    {
        private float mandatoryWait;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            mandatoryWait = Random.Range(2.5f, 4.5f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;

            if (ctrl.DistanceToTarget() < wd.panicDistance)
            {
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
                return;
            }

            ctrl.FaceTarget();
            mandatoryWait -= Time.deltaTime;

            if (mandatoryWait <= 0f && ctrl.StaminaRatio() >= wd.staminaRequiredRatio)
                wd.FSM.ChangeState(wd.ApproachState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // APPROACH — Moves slowly toward the boss with natural intermittent pauses,
    // like someone being cautious. The stops make it feel like the bot is
    // checking the situation rather than walking on a timer.
    // ==========================================================================
    public class WD_Approach : IPlayerFSMState
    {
        private float stopTimer;   // how long to pause for
        private float moveTimer;   // how long to move before next pause
        private bool  isStopped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            isStopped  = false;
            moveTimer  = Random.Range(0.5f, 1.0f);
            stopTimer  = 0f;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd   = (WarriorDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            if (dist < wd.panicDistance)
            {
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
                return;
            }

            if (ctrl.StaminaRatio() < wd.staminaRequiredRatio)
            {
                wd.FSM.ChangeState(wd.WaitState, ctrl);
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
    // COMMIT — Arrives at attack range and waits a moment before striking.
    // Standing at attack distance without immediately attacking looks intentional
    // rather than robotic. One hit, no combo, then mandatory retreat.
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
            sizeUpTimer = Random.Range(0.20f, 0.45f); // "sizing up" pause
            attacked    = false;
            afterTimer  = 0f;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            ctrl.FaceTarget();

            if (!attacked)
            {
                sizeUpTimer -= Time.deltaTime;
                if (sizeUpTimer <= 0f)
                {
                    ctrl.RequestAttack(); // single hit, no combo
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
    // RETREAT — Runs away to a randomised safe distance (not the same spot
    // every time). Then transitions to Wait.
    // ==========================================================================
    public class WD_Retreat : IPlayerFSMState
    {
        private float targetDistance;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            targetDistance = Random.Range(wd.minRetreatDistance, wd.maxRetreatDistance);
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
}
