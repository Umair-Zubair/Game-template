using UnityEngine;

// ============================================================================
// WARRIOR DEFENSIVE FSM
//
// Profile: Patient and conservative. Approaches slowly, strikes ONCE from the
// edge of melee range, retreats immediately to a safe distance, then waits
// for stamina to regenerate before engaging again.
//
// Compared to other Warrior profiles:
//   AGGRESSIVE   No retreat | always combo | threshold 5f  | constant jumping
//   BALANCED     Brief fallback | 60% combo | threshold 35% | occasional jump
//   DEFENSIVE    Long retreat | no combo   | threshold 70% | never jumps
//
// States: Wait → Approach → Attack → Retreat → Wait
// ============================================================================
public class WarriorDefensiveFSM : PlayerFSMController
{
    [Header("Warrior Defensive — Tuning")]
    [Tooltip("Distance at which the bot stops approaching and strikes.")]
    [SerializeField] private float attackRange = 3.0f;

    [Tooltip("Distance the bot retreats to after every attack.")]
    [SerializeField] private float safeDistance = 5.5f;

    [Tooltip("If boss closes to within this distance unexpectedly, trigger emergency retreat.")]
    [SerializeField] private float panicDistance = 1.5f;

    [Tooltip("Stamina ratio (0–1) required before the bot will engage. Very high.")]
    [SerializeField] private float staminaRequiredRatio = 0.70f;

    [Tooltip("How long the bot pauses after the single attack before retreating.")]
    [SerializeField] private float attackPauseSeconds = 0.15f;

    [Tooltip("Speed fraction used during approach (0–1). Slow and measured.")]
    [SerializeField] private float approachSpeedFraction = 0.60f;

    // ── State Instances ────────────────────────────────────────────────────
    public WD_Approach ApproachState { get; private set; }
    public WD_Attack   AttackState   { get; private set; }
    public WD_Retreat  RetreatState  { get; private set; }
    public WD_Wait     WaitState     { get; private set; }

    protected override void InitializeStates()
    {
        ApproachState = new WD_Approach();
        AttackState   = new WD_Attack();
        RetreatState  = new WD_Retreat();
        WaitState     = new WD_Wait();
    }

    // Start in Wait — bot must earn the right to attack via stamina first
    protected override void StartFSM() => FSM.Initialize(WaitState, this);

    // ==========================================================================
    // APPROACH STATE
    // Moves SLOWLY toward the boss. Panics if boss closes in unexpectedly.
    // Re-checks stamina; aborts to Wait if it dips below threshold.
    // ==========================================================================
    public class WD_Approach : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            // Emergency: boss got too close → retreat immediately
            if (dist < wd.panicDistance)
            {
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
                return;
            }

            // Stamina dipped while approaching → abort and recover
            if (ctrl.StaminaRatio() < wd.staminaRequiredRatio)
            {
                wd.FSM.ChangeState(wd.WaitState, ctrl);
                return;
            }

            // Close enough → attack
            if (dist <= wd.attackRange)
            {
                wd.FSM.ChangeState(wd.AttackState, ctrl);
                return;
            }

            ctrl.MoveTowardAt(wd.approachSpeedFraction);
            ctrl.FaceTarget();
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // ATTACK STATE
    // Fires EXACTLY ONE attack — no combo, no sustained pressure.
    // RequestAttack() is called only in OnEnter (single frame signal).
    // After a brief pause the bot always retreats regardless of result.
    //
    // Key distinction: the Aggressive bot calls RequestAttack() every frame;
    // this bot calls it once and stops, so the combo window closes unfired.
    // ==========================================================================
    public class WD_Attack : IPlayerFSMState
    {
        private float pauseTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestAttack(); // single one-shot signal — no combo
            pauseTimer = wd.attackPauseSeconds;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            ctrl.FaceTarget();
            pauseTimer -= Time.deltaTime;
            if (pauseTimer <= 0f)
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // RETREAT STATE
    // Runs away at full speed until reaching a safe distance.
    // Never jumps — stays grounded for predictable movement.
    // ==========================================================================
    public class WD_Retreat : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) { }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            ctrl.MoveAway();

            if (ctrl.DistanceToTarget() >= wd.safeDistance)
                wd.FSM.ChangeState(wd.WaitState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // WAIT STATE
    // Stands still at safe distance regenerating stamina. Does nothing until
    // stamina recovers past the high threshold.
    // Emergency retreat fires if the boss closes in while waiting.
    // ==========================================================================
    public class WD_Wait : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) => ctrl.StopMoving();

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wd = (WarriorDefensiveFSM)ctrl;
            float dist = ctrl.DistanceToTarget();

            // Emergency: boss hunted us down during recovery
            if (dist < wd.panicDistance)
            {
                wd.FSM.ChangeState(wd.RetreatState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            if (ctrl.StaminaRatio() >= wd.staminaRequiredRatio)
                wd.FSM.ChangeState(wd.ApproachState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }
}
