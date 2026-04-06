using UnityEngine;

// ============================================================================
// WARRIOR BALANCED FSM
//
// Approaches, briefly sizes up the situation before attacking, rolls on the
// combo, then falls back for a randomised duration before re-engaging.
// Nothing is perfectly timed — all durations use Random.Range so the rhythm
// is different every cycle.
//
// States: Engage → SizeUp → Attack → Fallback → Recover → Engage
// ============================================================================
public class WarriorBalancedFSM : PlayerFSMController
{
    [Header("Warrior Balanced — Tuning")]
    [SerializeField] private float engageRange           = 2.5f;
    [SerializeField] private float staminaThresholdRatio = 0.35f;
    [SerializeField] private float comboProbability      = 0.60f;
    [SerializeField] private float jumpHeightThreshold   = 2.0f;
    [SerializeField] private float jumpCheckInterval     = 2.0f;

    public WB_Engage   EngageState   { get; private set; }
    public WB_SizeUp   SizeUpState   { get; private set; }
    public WB_Attack   AttackState   { get; private set; }
    public WB_Fallback FallbackState { get; private set; }
    public WB_Recover  RecoverState  { get; private set; }

    protected override void InitializeStates()
    {
        EngageState   = new WB_Engage();
        SizeUpState   = new WB_SizeUp();
        AttackState   = new WB_Attack();
        FallbackState = new WB_Fallback();
        RecoverState  = new WB_Recover();
    }

    protected override void StartFSM() => FSM.Initialize(EngageState, this);

    // ==========================================================================
    // ENGAGE — Approach at full speed. Infrequent jump checks for height gaps.
    // When in range and stamina is adequate, move to SizeUp rather than
    // immediately attacking.
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

            jumpTimer -= Time.deltaTime;
            if (jumpTimer <= 0f && ctrl.IsGrounded())
            {
                jumpTimer = Random.Range(wb.jumpCheckInterval * 0.7f, wb.jumpCheckInterval * 1.3f);
                if (ctrl.HeightDifferenceToTarget() > wb.jumpHeightThreshold)
                    ctrl.RequestJump(Random.Range(0.25f, 0.35f));
            }

            if (ctrl.DistanceToTarget() <= wb.engageRange
                && ctrl.StaminaRatio() >= wb.staminaThresholdRatio)
            {
                wb.FSM.ChangeState(wb.SizeUpState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // SIZE UP — Stops at attack range for a short random moment before
    // committing to the attack. Breaks the mechanical feel of instantly
    // attacking the frame you enter range.
    // ==========================================================================
    public class WB_SizeUp : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            ctrl.FaceTarget();
            timer = Random.Range(0.15f, 0.40f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.FaceTarget();

            // Boss moved away — go back to engaging
            if (ctrl.DistanceToTarget() > wb.engageRange * 1.3f)
            {
                wb.FSM.ChangeState(wb.EngageState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wb.FSM.ChangeState(wb.AttackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // ATTACK — Combo decision is made on entry. If attempting combo,
    // RequestAttack is called every frame for a random window to catch
    // the animation event. Duration is randomised each engagement.
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
            ctrl.RequestAttack();
            attemptCombo = Random.value < wb.comboProbability;
            timer        = Random.Range(0.50f, 0.80f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.FaceTarget();

            // For combo: only request during the actual combo window, not every frame.
            // Prevents re-triggering the attack animation before it finishes.
            if (attemptCombo && wb.MeleeAttack != null && wb.MeleeAttack.ComboWindowOpen)
                ctrl.RequestAttack();

            // Hold position and don't transition while a swing is still playing
            if (wb.MeleeAttack != null && wb.MeleeAttack.IsInAttackSequence)
            {
                ctrl.StopMoving();
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wb.FSM.ChangeState(wb.FallbackState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // FALLBACK — Retreats for a randomised duration. Never the same distance.
    // ==========================================================================
    public class WB_Fallback : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            timer = Random.Range(0.30f, 0.70f);
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
    // RECOVER — Stands still facing the boss for a random pause. Variable
    // duration means the re-engage timing is never perfectly predictable.
    // ==========================================================================
    public class WB_Recover : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            timer = Random.Range(0.40f, 1.00f);
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
