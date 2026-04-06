using UnityEngine;

// ============================================================================
// WARRIOR AGGRESSIVE FSM
//
// Rushes the boss, hesitates briefly before striking (like a human sizing up),
// attacks hard, then briefly pulls back to break the rhythm before charging in
// again. Jumps unpredictably — not just when the boss is elevated.
//
// States: Chase → Hesitate → Attack → BriefPull → Chase
// ============================================================================
public class WarriorAggressiveFSM : PlayerFSMController
{
    [Header("Warrior Aggressive — Tuning")]
    [SerializeField] private float attackRange        = 2.0f;
    [SerializeField] private float staminaThreshold  = 5f;
    [SerializeField] private float jumpHeightThreshold = 1.5f;

    public WA_Chase     ChaseState     { get; private set; }
    public WA_Hesitate  HesitateState  { get; private set; }
    public WA_Attack    AttackState    { get; private set; }
    public WA_BriefPull BriefPullState { get; private set; }

    protected override void InitializeStates()
    {
        ChaseState     = new WA_Chase();
        HesitateState  = new WA_Hesitate();
        AttackState    = new WA_Attack();
        BriefPullState = new WA_BriefPull();
    }

    protected override void StartFSM() => FSM.Initialize(ChaseState, this);

    // ==========================================================================
    // CHASE — Rush toward boss. Jumps on height gap AND randomly to be
    // unpredictable. Jump timing is randomised so it never looks like a script.
    // ==========================================================================
    public class WA_Chase : IPlayerFSMState
    {
        private float jumpCheckTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            jumpCheckTimer = Random.Range(0.8f, 2.0f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;

            ctrl.MoveToward();
            ctrl.FaceTarget();

            jumpCheckTimer -= Time.deltaTime;
            if (jumpCheckTimer <= 0f && ctrl.IsGrounded())
            {
                jumpCheckTimer = Random.Range(0.8f, 2.2f);
                bool bossElevated = ctrl.HeightDifferenceToTarget() > wa.jumpHeightThreshold;
                bool randomJump   = Random.value < 0.30f; // unpredictable jump 30% of checks
                if (bossElevated || randomJump)
                    ctrl.RequestJump(Random.Range(0.2f, 0.35f));
            }

            if (ctrl.DistanceToTarget() <= wa.attackRange
                && ctrl.Stamina != null
                && ctrl.Stamina.CurrentStamina > wa.staminaThreshold)
            {
                wa.FSM.ChangeState(wa.HesitateState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // HESITATE — Stops for a brief random moment before committing to the
    // attack. This single state makes the bot look intentional rather than
    // robotic. If the boss moves out of range during the hesitation, chase resumes.
    // ==========================================================================
    public class WA_Hesitate : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            timer = Random.Range(0.10f, 0.28f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            // Boss backed off during hesitation — resume chase
            if (ctrl.DistanceToTarget() > wa.attackRange * 1.3f)
            {
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wa.FSM.ChangeState(wa.AttackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // ATTACK — Only calls RequestAttack when the script is actually ready:
    // either CanAttack (cooldown expired) or ComboWindowOpen (animation event
    // opened the window). Calling every frame was firing anim.SetTrigger("attack")
    // 0.25s into a 1s animation, restarting it from frame 0 — the half-animation bug.
    // Now the full swing plays, combo chains naturally via the window, then
    // the bot sustains for a randomised duration before pulling back.
    // ==========================================================================
    public class WA_Attack : IPlayerFSMState
    {
        private float sustainTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            sustainTimer = Random.Range(0.6f, 2.2f); // wide variance — short flurries or longer pressure
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (wa.MeleeAttack != null
                && (wa.MeleeAttack.ComboWindowOpen
                    || (wa.MeleeAttack.CanAttack && !wa.MeleeAttack.IsInAttackSequence)))
            {
                ctrl.RequestAttack();
            }

            // Hold position and don't transition while a swing is still playing
            if (wa.MeleeAttack != null && wa.MeleeAttack.IsInAttackSequence)
            {
                ctrl.StopMoving();
                return;
            }

            sustainTimer -= Time.deltaTime;

            bool tooFar    = ctrl.DistanceToTarget() > wa.attackRange * 1.6f;
            bool exhausted = ctrl.Stamina != null && ctrl.Stamina.IsExhausted;

            if (tooFar || exhausted)
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
            else if (sustainTimer <= 0f)
                wa.FSM.ChangeState(wa.BriefPullState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // BRIEF PULL — Wide timer range so sometimes it barely steps back and
    // immediately presses in again, other times it takes a longer breath.
    // On exit, 40% chance to skip the full chase and go straight back to
    // Hesitate if still close — this is what breaks the constant repetition.
    // ==========================================================================
    public class WA_BriefPull : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = Random.Range(0.10f, 0.90f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.MoveAway();
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                bool stillClose = ctrl.DistanceToTarget() <= wa.attackRange * 1.4f;
                if (stillClose && Random.value < 0.40f)
                    wa.FSM.ChangeState(wa.HesitateState, ctrl); // skip chase, press straight back in
                else
                    wa.FSM.ChangeState(wa.ChaseState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
