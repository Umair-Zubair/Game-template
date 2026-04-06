using UnityEngine;

// ============================================================================
// WARRIOR BALANCED FSM
//
// Profile: Disciplined engagement loop. Approaches, attacks with a probabilistic
// combo, briefly falls back, recovers for a short pause, then re-engages.
// Moderate in every dimension.
//
// Compared to other Warrior profiles:
//   AGGRESSIVE   No retreat | always combo | threshold 5f  | constant jumping
//   BALANCED     Brief fallback | 60% combo | threshold 35% | occasional jump
//   DEFENSIVE    Long retreat | no combo   | threshold 70% | never jumps
//
// States: Engage → Attack → Fallback → Recover → Engage
// ============================================================================
public class WarriorBalancedFSM : PlayerFSMController
{
    [Header("Warrior Balanced — Tuning")]
    [Tooltip("Preferred engagement distance.")]
    [SerializeField] private float engageRange = 2.0f;

    [Tooltip("Stamina ratio (0–1) required before engaging.")]
    [SerializeField] private float staminaThresholdRatio = 0.35f;

    [Tooltip("Probability (0–1) of attempting to chain the combo after the initial hit.")]
    [SerializeField] private float comboProbability = 0.60f;

    [Tooltip("How long the bot holds the attack request open (covers the combo window).")]
    [SerializeField] private float attackWindowDuration = 0.65f;

    [Tooltip("How long the bot moves away from the boss after an attack sequence.")]
    [SerializeField] private float fallbackDuration = 0.40f;

    [Tooltip("How long the bot stands still between engagements.")]
    [SerializeField] private float recoverDuration = 0.50f;

    [Tooltip("Height difference required before the bot bothers jumping.")]
    [SerializeField] private float jumpHeightThreshold = 2.0f;

    [Tooltip("How often the bot checks whether to jump (seconds).")]
    [SerializeField] private float jumpCheckInterval = 2.0f;

    // ── State Instances ────────────────────────────────────────────────────
    public WB_Engage   EngageState   { get; private set; }
    public WB_Attack   AttackState   { get; private set; }
    public WB_Fallback FallbackState { get; private set; }
    public WB_Recover  RecoverState  { get; private set; }

    protected override void InitializeStates()
    {
        EngageState   = new WB_Engage();
        AttackState   = new WB_Attack();
        FallbackState = new WB_Fallback();
        RecoverState  = new WB_Recover();
    }

    protected override void StartFSM() => FSM.Initialize(EngageState, this);

    // ==========================================================================
    // ENGAGE STATE
    // Moves toward the boss at full speed. Jumps only for significant height
    // gaps (≥ 2f) and only checks periodically (every 2s), unlike Aggressive
    // which checks every 0.25s.
    // ==========================================================================
    public class WB_Engage : IPlayerFSMState
    {
        private float jumpTimer;

        public void OnEnter(PlayerFSMController ctrl) { jumpTimer = 0f; }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;

            ctrl.MoveToward();
            ctrl.FaceTarget();

            // Conditional jump: only for significant height gap, infrequent checks
            jumpTimer -= Time.deltaTime;
            if (jumpTimer <= 0f && ctrl.IsGrounded())
            {
                jumpTimer = wb.jumpCheckInterval;
                if (ctrl.HeightDifferenceToTarget() > wb.jumpHeightThreshold)
                    ctrl.RequestJump(0.30f);
            }

            if (ctrl.DistanceToTarget() <= wb.engageRange
                && ctrl.StaminaRatio() >= wb.staminaThresholdRatio)
            {
                wb.FSM.ChangeState(wb.AttackState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // ATTACK STATE
    // Sends the initial attack, then holds the request open for attackWindowDuration.
    //
    // Combo decision is made once on entry (60% chance). If attempting combo,
    // RequestAttack() is called every frame so MeleeAttack's combo window is
    // caught. If not, only the initial hit request fires and the window closes
    // without a second hit — giving the Balanced bot an intermediate combo rate.
    // ==========================================================================
    public class WB_Attack : IPlayerFSMState
    {
        private float timer;
        private bool  attemptCombo;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestAttack(); // initial hit
            attemptCombo = Random.value < wb.comboProbability;
            timer        = wb.attackWindowDuration;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.FaceTarget();

            // Keep requesting only if the combo roll succeeded
            if (attemptCombo)
                ctrl.RequestAttack();

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wb.FSM.ChangeState(wb.FallbackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // FALLBACK STATE
    // Moves away from the boss for a fixed duration after the attack sequence.
    // Shorter and less committed than the Defensive retreat — just a step back.
    // ==========================================================================
    public class WB_Fallback : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = ((WarriorBalancedFSM)ctrl).fallbackDuration;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.MoveAway();
            timer -= Time.deltaTime;
            if (timer <= 0f)
                wb.FSM.ChangeState(wb.RecoverState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // RECOVER STATE
    // Brief stand-still before re-engaging. Faces the boss but doesn't move.
    // ==========================================================================
    public class WB_Recover : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            timer = ((WarriorBalancedFSM)ctrl).recoverDuration;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.FaceTarget();
            timer -= Time.deltaTime;
            if (timer <= 0f)
                wb.FSM.ChangeState(wb.EngageState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }
}
