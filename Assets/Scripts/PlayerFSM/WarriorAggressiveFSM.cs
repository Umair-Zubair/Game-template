using UnityEngine;

// ============================================================================
// WARRIOR AGGRESSIVE FSM
//
// Profile: All-in melee pressure. Never retreats. Always chains the combo.
// Rushes the boss relentlessly, attacks with almost no stamina, and jumps
// to close vertical gaps.
//
// Compared to other Warrior profiles:
//   AGGRESSIVE   No retreat | always combo | stamina threshold 5f  | constant jumping
//   BALANCED     Brief fallback | 60% combo | threshold 35%        | occasional jump
//   DEFENSIVE    Long retreat | no combo   | threshold 70%        | never jumps
//
// States: Chase → Attack (→ Chase)
// ============================================================================
public class WarriorAggressiveFSM : PlayerFSMController
{
    [Header("Warrior Aggressive — Tuning")]
    [Tooltip("Distance at which the bot stops chasing and starts attacking.")]
    [SerializeField] private float attackRange = 1.8f;

    [Tooltip("Minimum stamina (absolute) needed before the bot will attack. Very low.")]
    [SerializeField] private float staminaThreshold = 5f;

    [Tooltip("How high the boss must be above the bot before it jumps to close the gap.")]
    [SerializeField] private float jumpHeightThreshold = 1.5f;

    [Tooltip("Cooldown between jump decisions during the chase.")]
    [SerializeField] private float jumpCooldown = 1.2f;

    // ── State Instances ────────────────────────────────────────────────────
    public WA_Chase  ChaseState  { get; private set; }
    public WA_Attack AttackState { get; private set; }

    protected override void InitializeStates()
    {
        ChaseState  = new WA_Chase();
        AttackState = new WA_Attack();
    }

    protected override void StartFSM() => FSM.Initialize(ChaseState, this);

    // ==========================================================================
    // CHASE STATE
    // Relentlessly pursues the boss at full speed.
    // Jumps proactively to close vertical distance.
    // ==========================================================================
    public class WA_Chase : IPlayerFSMState
    {
        private float jumpTimer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            jumpTimer = 0f;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;

            ctrl.MoveToward();
            ctrl.FaceTarget();

            // Proactive vertical jump — close the gap when boss is elevated
            jumpTimer -= Time.deltaTime;
            if (jumpTimer <= 0f && ctrl.IsGrounded() && ctrl.Target != null)
            {
                if (ctrl.HeightDifferenceToTarget() > wa.jumpHeightThreshold)
                {
                    ctrl.RequestJump(0.35f); // full-height jump
                    jumpTimer = wa.jumpCooldown;
                }
                else
                {
                    jumpTimer = 0.25f; // check again soon
                }
            }

            // Close enough and has minimum stamina → attack
            if (ctrl.DistanceToTarget() <= wa.attackRange
                && ctrl.Stamina != null
                && ctrl.Stamina.CurrentStamina > wa.staminaThreshold)
            {
                wa.FSM.ChangeState(wa.AttackState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // ATTACK STATE
    // Stands still and hammers RequestAttack() every frame.
    //
    // Because AIAttackRequested is set EVERY frame, MeleeAttack's combo window
    // (opened via Animation Event) is always caught — the bot always chains
    // the combo hit. This is the key difference from the Defensive bot.
    //
    // Returns to Chase when:
    //   - Boss drifted out of range (multiplied by 1.5 to avoid micro-transitions)
    //   - Stamina fully exhausted
    // ==========================================================================
    public class WA_Attack : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;

            ctrl.RequestAttack(); // every frame → always catches the combo window
            ctrl.FaceTarget();

            bool tooFar     = ctrl.DistanceToTarget() > wa.attackRange * 1.5f;
            bool exhausted  = ctrl.Stamina != null && ctrl.Stamina.IsExhausted;

            if (tooFar || exhausted)
                wa.FSM.ChangeState(wa.ChaseState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }
}
