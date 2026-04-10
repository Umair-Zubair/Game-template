using UnityEngine;

// ============================================================================
// WARRIOR AGGRESSIVE FSM
//
// Rushes the boss, sizes up briefly before striking, attacks hard, then pulls
// back. Critically: reacts to boss attacks and artillery — doesn't blindly
// press into a swinging boss. Post-attack decisions are context-driven, not
// always the same loop.
//
// States: Chase → Hesitate → Attack → BriefPull → Chase
//         Any state → Evade (when boss attacks in range or artillery fires)
// ============================================================================
public class WarriorAggressiveFSM : PlayerFSMController
{
    [Header("Warrior Aggressive — Tuning")]
    [SerializeField] private float attackRange         = 2.0f;
    [SerializeField] private float jumpHeightThreshold = 1.5f;

    public WA_Chase     ChaseState     { get; private set; }
    public WA_Hesitate  HesitateState  { get; private set; }
    public WA_Attack    AttackState    { get; private set; }
    public WA_BriefPull BriefPullState { get; private set; }
    public WA_Evade     EvadeState     { get; private set; }

    protected override void InitializeStates()
    {
        ChaseState     = new WA_Chase();
        HesitateState  = new WA_Hesitate();
        AttackState    = new WA_Attack();
        BriefPullState = new WA_BriefPull();
        EvadeState     = new WA_Evade();
    }

    protected override void StartFSM() => FSM.Initialize(ChaseState, this);

    // ==========================================================================
    // CHASE — Rush toward boss. Reacts to artillery immediately.
    // Backs off if boss is already mid-swing on approach.
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

            // Artillery fired — jump away immediately
            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            // Boss is swinging and we're about to run into it — pull back
            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wa.dangerRange)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            ctrl.MoveToward();
            ctrl.FaceTarget();

            jumpCheckTimer -= Time.deltaTime;
            if (jumpCheckTimer <= 0f && ctrl.IsGrounded())
            {
                jumpCheckTimer = Random.Range(0.8f, 2.2f);
                bool bossElevated = ctrl.HeightDifferenceToTarget() > wa.jumpHeightThreshold;
                bool randomJump   = Random.value < 0.25f;
                if (bossElevated || randomJump)
                    ctrl.RequestJump(Random.Range(0.2f, 0.35f));
            }

            if (ctrl.DistanceToTarget() <= wa.attackRange)
                wa.FSM.ChangeState(wa.HesitateState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // HESITATE — Stops before committing to the attack. If the boss starts
    // swinging during this window, abort — don't walk into it.
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

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            // Boss swings during our hesitation — back off, don't commit
            if (ctrl.BossIsAttacking)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

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
    // ATTACK — Sustains attacks for a random duration. Evades if the boss
    // counters. If we got hurt and health is low, retreats sooner.
    // ==========================================================================
    public class WA_Attack : IPlayerFSMState
    {
        private float sustainTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            sustainTimer = Random.Range(0.6f, 2.2f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            // Boss counters mid-combo — evade rather than eat the hit
            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wa.dangerRange)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            // Took damage at low health — retreat now
            if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.45f)
            {
                wa.FSM.ChangeState(wa.BriefPullState, ctrl);
                return;
            }

            if (wa.MeleeAttack != null
                && (wa.MeleeAttack.ComboWindowOpen
                    || (wa.MeleeAttack.CanAttack && !wa.MeleeAttack.IsInAttackSequence)))
            {
                ctrl.RequestAttack();
            }

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
    // BRIEF PULL — Steps back after an attack sequence. Exit decision is
    // context-driven: if hurt and low health, stay back longer; if boss is
    // mid-swing, evade instead of pressing; otherwise probabilistic re-engage.
    // ==========================================================================
    public class WA_BriefPull : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = Random.Range(0.15f, 0.90f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.MoveAway();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            // Hurt and low health — don't rush back in, keep distance
            if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.35f)
            {
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
                return;
            }

            // Boss is still attacking — don't walk back in yet, evade
            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wa.dangerRange * 1.5f)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            bool stillClose = ctrl.DistanceToTarget() <= wa.attackRange * 1.4f;
            float roll = Random.value;

            if (stillClose && roll < 0.40f)
                wa.FSM.ChangeState(wa.HesitateState, ctrl); // press straight back in
            else if (stillClose && roll < 0.55f)
                wa.FSM.ChangeState(wa.BriefPullState, ctrl); // extend the retreat a bit more
            else
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // EVADE — Triggered by boss attack or artillery. Jumps or dashes away.
    // After evading, reassesses based on health before going back in.
    // ==========================================================================
    public class WA_Evade : IPlayerFSMState
    {
        private float timer;
        private bool  jumped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.ConsumeArtilleryEvade();

            jumped = ctrl.IsGrounded() && Random.value < 0.55f;
            if (jumped)
                ctrl.RequestJump(Random.Range(0.20f, 0.32f));

            timer = Random.Range(0.35f, 0.65f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();

            if (!jumped)
                ctrl.MoveAway();

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                // After evading: hurt at low health → stay back; otherwise re-engage
                if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.40f)
                    wa.FSM.ChangeState(wa.BriefPullState, ctrl);
                else
                    wa.FSM.ChangeState(wa.ChaseState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
