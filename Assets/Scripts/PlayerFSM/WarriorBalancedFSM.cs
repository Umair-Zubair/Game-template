using UnityEngine;

// ============================================================================
// WARRIOR BALANCED FSM
//
// Approaches, briefly sizes up the situation before attacking, rolls on the
// combo, then falls back. Reacts to boss attacks and artillery — won't just
// walk into a swing. Post-fallback recovery duration is health-influenced.
//
// States: Engage → SizeUp → Attack → Fallback → Recover → Engage
//         Any state → Evade (boss attack in range or artillery)
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
    public WB_Evade    EvadeState    { get; private set; }

    protected override void InitializeStates()
    {
        EngageState   = new WB_Engage();
        SizeUpState   = new WB_SizeUp();
        AttackState   = new WB_Attack();
        FallbackState = new WB_Fallback();
        RecoverState  = new WB_Recover();
        EvadeState    = new WB_Evade();
    }

    protected override void StartFSM() => FSM.Initialize(EngageState, this);

    // ==========================================================================
    // ENGAGE — Approach at full speed. Occasional jump for height gaps.
    // Bails out if boss is mid-swing on close approach.
    // ==========================================================================
    public class WB_Engage : IPlayerFSMState
    {
        private float jumpTimer;

        public void OnEnter(PlayerFSMController ctrl) { jumpTimer = 0f; }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wb.dangerRange)
            {
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

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
    // SIZE UP — Stops at attack range for a random moment before committing.
    // If the boss swings during this window, evade — don't eat the hit for free.
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

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            // Boss winds up while we're sizing up — abort and evade
            if (ctrl.BossIsAttacking)
            {
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

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
    // ATTACK — Combo decision on entry. Evades if the boss counters mid-combo.
    // Low health + recent damage causes an early retreat.
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

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            // Boss counters — evade
            if (ctrl.BossIsAttacking && ctrl.DistanceToTarget() < wb.dangerRange)
            {
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            // Hurt at low health — retreat early
            if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.45f)
            {
                wb.FSM.ChangeState(wb.FallbackState, ctrl);
                return;
            }

            if (attemptCombo && wb.MeleeAttack != null && wb.MeleeAttack.ComboWindowOpen)
                ctrl.RequestAttack();

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
    // FALLBACK — Retreats for a random duration. If hurt at low health, the
    // timer runs longer so the bot doesn't immediately rush back in.
    // ==========================================================================
    public class WB_Fallback : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            float healthMod = ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.45f ? 1.6f : 1.0f;
            timer = Random.Range(0.30f, 0.70f) * healthMod;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            ctrl.MoveAway();
            timer -= Time.deltaTime;
            if (timer <= 0f)
                wb.FSM.ChangeState(wb.RecoverState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // RECOVER — Stands still facing the boss. If hurt and low health, stays
    // back noticeably longer before re-engaging.
    // ==========================================================================
    public class WB_Recover : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.StopMoving();
            float healthMod = ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.40f ? 2.0f : 1.0f;
            timer = Random.Range(0.40f, 1.00f) * healthMod;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            ctrl.FaceTarget();
            timer -= Time.deltaTime;
            if (timer <= 0f)
                wb.FSM.ChangeState(wb.EngageState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // EVADE — Triggered by boss attack or artillery. Mix of jump and dash-back.
    // After evading, health check: low health → extended recover, else re-engage.
    // ==========================================================================
    public class WB_Evade : IPlayerFSMState
    {
        private float timer;
        private bool  jumped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            ctrl.ConsumeArtilleryEvade();
            jumped = ctrl.IsGrounded() && Random.value < 0.50f;
            if (jumped)
                ctrl.RequestJump(Random.Range(0.20f, 0.30f));
            timer = Random.Range(0.35f, 0.60f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (!jumped)
                ctrl.MoveAway();

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                if (ctrl.RecentlyHurt && ctrl.HealthRatio() < 0.40f)
                    wb.FSM.ChangeState(wb.RecoverState, ctrl);
                else
                    wb.FSM.ChangeState(wb.FallbackState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
