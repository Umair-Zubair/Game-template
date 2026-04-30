using UnityEngine;

// ============================================================================
// WARRIOR BALANCED FSM
//
// Reads both fighter health bars and shifts posture instead of using one fixed
// loop. Healthy player + low boss health means more pressure. Low player health
// means longer retreats, fewer combos, and earlier evasions. Otherwise it plays
// the original measured hit-and-fallback pattern.
//
// States: Engage → SizeUp → Attack → Fallback → Recover → Engage
//         Any state → Evade (boss attack in range or artillery)
// ============================================================================
public class WarriorBalancedFSM : PlayerFSMController
{
    [Header("Warrior Balanced — Tuning")]
    [SerializeField] private float engageRange           = 0.75f;
    [SerializeField] private float staminaThresholdRatio = 0.35f;
    [SerializeField] private float comboProbability      = 0.60f;
    [SerializeField] private float jumpHeightThreshold   = 2.8f;
    [SerializeField] private float jumpCheckInterval     = 3.5f;
    [SerializeField] private bool  allowApproachJumps    = false;
    [SerializeField] private float artilleryJumpChance   = 0.12f;
    [SerializeField] private float evadeJumpChance       = 0.08f;

    [Header("Health-Aware Posture")]
    [SerializeField] private float lowHealthThreshold     = 0.35f;
    [SerializeField] private float highHealthThreshold    = 0.65f;
    [SerializeField] private float bossLowHealthThreshold = 0.35f;

    public WB_Engage   EngageState   { get; private set; }
    public WB_SizeUp   SizeUpState   { get; private set; }
    public WB_Attack   AttackState   { get; private set; }
    public WB_Fallback FallbackState { get; private set; }
    public WB_Recover  RecoverState  { get; private set; }
    public WB_Evade    EvadeState    { get; private set; }

    private Health targetHealth;
    private Collider2D ownCollider;
    private Collider2D targetCollider;
    private bool nextEvadeIsArtillery;

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

    private float BossHealthRatio()
    {
        if (Target == null) return 1f;

        if (targetHealth == null
            || (!targetHealth.transform.IsChildOf(Target) && !Target.IsChildOf(targetHealth.transform)))
        {
            targetHealth = Target.GetComponent<Health>() ?? Target.GetComponentInParent<Health>();
        }

        if (targetHealth == null || targetHealth.MaxHealth <= 0f) return 1f;
        return Mathf.Clamp01(targetHealth.currentHealth / targetHealth.MaxHealth);
    }

    private float MeleeDistanceToTarget()
    {
        if (Target == null) return Mathf.Infinity;

        if (ownCollider == null)
            ownCollider = PC != null && PC.Collider != null ? PC.Collider : GetComponent<Collider2D>();

        if (targetCollider == null || !targetCollider.transform.IsChildOf(Target))
            targetCollider = FindTargetCollider();

        if (ownCollider != null && targetCollider != null)
            return Mathf.Max(0f, ownCollider.Distance(targetCollider).distance);

        return Mathf.Abs(Target.position.x - transform.position.x);
    }

    private Collider2D FindTargetCollider()
    {
        if (Target == null) return null;

        Collider2D[] colliders = Target.GetComponentsInChildren<Collider2D>();
        foreach (Collider2D col in colliders)
        {
            if (col != null && col.enabled && !col.isTrigger)
                return col;
        }

        return colliders.Length > 0 ? colliders[0] : null;
    }

    private bool PlayerLowHealth => HealthRatio() <= lowHealthThreshold;
    private bool PlayerHealthy   => HealthRatio() >= highHealthThreshold;
    private bool BossLowHealth   => BossHealthRatio() <= bossLowHealthThreshold;
    private bool HealthyPressure => PlayerHealthy && !BossLowHealth;
    private bool PressAdvantage  => PlayerHealthy && BossLowHealth;

    private float EffectiveEngageRange => PressAdvantage ? engageRange * 1.15f : (HealthyPressure ? engageRange * 1.05f : engageRange);

    private float SizeUpDuration()
    {
        if (PlayerLowHealth) return Random.Range(0.18f, 0.38f);
        if (PressAdvantage) return Random.Range(0.05f, 0.18f);
        if (HealthyPressure) return Random.Range(0.10f, 0.28f);
        return Random.Range(0.15f, 0.40f);
    }

    private float AttackDuration()
    {
        if (PlayerLowHealth) return Random.Range(0.40f, 0.65f);
        if (PressAdvantage) return Random.Range(0.65f, 0.95f);
        if (HealthyPressure) return Random.Range(0.55f, 0.85f);
        return Random.Range(0.50f, 0.80f);
    }

    private float ComboChance()
    {
        if (PlayerLowHealth) return comboProbability * 0.50f;
        if (PressAdvantage) return Mathf.Clamp01(comboProbability + 0.25f);
        if (HealthyPressure) return Mathf.Clamp01(comboProbability + 0.10f);
        return comboProbability;
    }

    private float FallbackDuration()
    {
        if (PlayerLowHealth) return Random.Range(0.55f, 0.95f);
        if (PressAdvantage) return Random.Range(0.20f, 0.45f);
        if (HealthyPressure) return Random.Range(0.25f, 0.55f);
        return Random.Range(0.30f, 0.70f);
    }

    private float RecoverDuration()
    {
        if (PlayerLowHealth) return Random.Range(0.65f, 1.15f);
        if (PressAdvantage) return Random.Range(0.20f, 0.50f);
        if (HealthyPressure) return Random.Range(0.30f, 0.70f);
        return Random.Range(0.40f, 1.00f);
    }

    private bool ShouldEvadeBossAttack(float dist)
    {
        if (!BossIsAttacking || dist >= dangerRange) return false;
        if (PlayerLowHealth) return RecentlyHurt || dist < engageRange * 0.65f;
        if (PressAdvantage) return Random.value < 0.55f;
        return true;
    }

    private void MaybeJumpForArtillery()
    {
        if (IsGrounded() && Random.value < artilleryJumpChance)
            RequestJump(Random.Range(0.16f, 0.24f));
    }

    private void EnterArtilleryEvade()
    {
        ConsumeArtilleryEvade();
        MaybeJumpForArtillery();
        nextEvadeIsArtillery = true;
        FSM.ChangeState(EvadeState, this);
    }

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
            float dist = wb.MeleeDistanceToTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                wb.EnterArtilleryEvade();
                return;
            }

            if (wb.ShouldEvadeBossAttack(dist))
            {
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            ctrl.MoveToward();
            ctrl.FaceTarget();

            jumpTimer -= Time.deltaTime;
            if (wb.allowApproachJumps && jumpTimer <= 0f && ctrl.IsGrounded())
            {
                jumpTimer = Random.Range(wb.jumpCheckInterval * 0.7f, wb.jumpCheckInterval * 1.3f);
                if (ctrl.HeightDifferenceToTarget() > wb.jumpHeightThreshold)
                    ctrl.RequestJump(Random.Range(0.16f, 0.24f));
            }

            if (dist <= wb.EffectiveEngageRange
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
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            timer = wb.SizeUpDuration();
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                wb.EnterArtilleryEvade();
                return;
            }

            float dist = wb.MeleeDistanceToTarget();
            if (wb.ShouldEvadeBossAttack(dist))
            {
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            if (dist > wb.EffectiveEngageRange * 1.3f)
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
            attemptCombo = Random.value < wb.ComboChance();
            timer        = wb.AttackDuration();
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            float dist = wb.MeleeDistanceToTarget();
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                wb.EnterArtilleryEvade();
                return;
            }

            if (wb.ShouldEvadeBossAttack(dist))
            {
                wb.FSM.ChangeState(wb.EvadeState, ctrl);
                return;
            }

            if (ctrl.RecentlyHurt && wb.PlayerLowHealth)
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
            var wb = (WarriorBalancedFSM)ctrl;
            timer = wb.FallbackDuration();
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                wb.EnterArtilleryEvade();
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
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.StopMoving();
            timer = wb.RecoverDuration();
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                wb.EnterArtilleryEvade();
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
        private bool  artilleryEvade;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.ConsumeArtilleryEvade();
            artilleryEvade = wb.nextEvadeIsArtillery;
            wb.nextEvadeIsArtillery = false;

            float jumpChance = artilleryEvade ? 0f : (wb.PlayerLowHealth ? wb.evadeJumpChance * 1.5f : wb.evadeJumpChance);
            jumped = ctrl.IsGrounded() && Random.value < jumpChance;
            if (jumped)
                ctrl.RequestJump(Random.Range(0.16f, 0.24f));
            timer = artilleryEvade ? Random.Range(0.18f, 0.32f)
                                   : (wb.PlayerLowHealth ? Random.Range(0.55f, 0.90f) : Random.Range(0.35f, 0.60f));
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wb = (WarriorBalancedFSM)ctrl;
            ctrl.FaceTarget();

            if (artilleryEvade)
                ctrl.StopMoving();
            else if (!jumped)
                ctrl.MoveAway();

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                if (artilleryEvade)
                    wb.FSM.ChangeState(wb.PlayerLowHealth ? (IPlayerFSMState)wb.RecoverState : wb.EngageState, ctrl);
                else if (wb.PlayerLowHealth)
                    wb.FSM.ChangeState(wb.RecoverState, ctrl);
                else if (wb.PressAdvantage)
                    wb.FSM.ChangeState(wb.EngageState, ctrl);
                else
                    wb.FSM.ChangeState(wb.FallbackState, ctrl);
            }
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
