using UnityEngine;

// ============================================================================
// WARRIOR AGGRESSIVE FSM
//
// Closes distance fast, commits to short melee bursts, then slips out before
// re-engaging. It stays aggressive without standing in the boss's face until
// stamina is gone.
// Unlike HunterAggressiveFSM this bot wants to live inside melee range, not hold
// a shooting lane. Stamina is checked only to avoid exhausting the warrior into
// helpless whiffs; MeleeAttack still owns exact attack costs/cooldowns.
//
// States: Press → Commit → Strike → Disengage → Press (or Breather)
//         Any state → Evade (artillery or boss attack at close range)
// ============================================================================
public class WarriorAggressiveFSM : PlayerFSMController
{
    [Header("Warrior Aggressive — Tuning")]
    [SerializeField] private float attackRange           = 3.0f;
    [SerializeField] private float stickRange            = 1.2f;
    [SerializeField] private float comboProbability      = 0.85f;
    [SerializeField] private float jumpHeightThreshold   = 1.8f;
    [SerializeField] private float jumpCheckInterval     = 1.4f;
    [SerializeField] private float counterEvadeChance    = 0.85f;
    [SerializeField] private float pressureAfterEvade    = 0.25f;
    [SerializeField] private float disengageMinTime      = 0.28f;
    [SerializeField] private float disengageMaxTime      = 0.55f;
    [SerializeField] private float reengageDistance      = 2.8f;

    [Header("Stamina")]
    [SerializeField] private float minAttackStaminaRatio = 0.35f;
    [SerializeField] private float reengageStaminaRatio  = 0.55f;

    public WA_Press    PressState    { get; private set; }
    public WA_Commit   CommitState   { get; private set; }
    public WA_Strike   StrikeState   { get; private set; }
    public WA_Disengage DisengageState { get; private set; }
    public WA_Evade    EvadeState    { get; private set; }
    public WA_Breather BreatherState { get; private set; }

    public bool IsExhaustedStamina => Stamina != null && Stamina.IsExhausted;
    private bool ReadyToAttack     => !IsExhaustedStamina && StaminaRatio() >= minAttackStaminaRatio;

    private Collider2D ownCollider;
    private Collider2D targetCollider;

    protected override void InitializeStates()
    {
        PressState    = new WA_Press();
        CommitState   = new WA_Commit();
        StrikeState   = new WA_Strike();
        DisengageState = new WA_Disengage();
        EvadeState    = new WA_Evade();
        BreatherState = new WA_Breather();
    }

    protected override void StartFSM() => FSM.Initialize(PressState, this);

    private float MeleeDistanceToTarget()
    {
        if (Target == null) return Mathf.Infinity;

        if (ownCollider == null)
            ownCollider = PC != null && PC.Collider != null ? PC.Collider : GetComponent<Collider2D>();

        if (targetCollider == null || !targetCollider.transform.IsChildOf(Target))
            targetCollider = FindTargetCollider();

        if (ownCollider != null && targetCollider != null)
            return Mathf.Max(0f, ownCollider.Distance(targetCollider).distance);

        // Fallback for targets without a usable collider: melee only cares about horizontal reach.
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

    // ==========================================================================
    // PRESS — Sprint into melee range. Aggressive still respects active boss
    // swings, but it dodges briefly and immediately returns to pressure.
    // ==========================================================================
    public class WA_Press : IPlayerFSMState
    {
        private float jumpTimer;

        public void OnEnter(PlayerFSMController ctrl) => jumpTimer = 0f;

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            float dist = wa.MeleeDistanceToTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && dist < wa.dangerRange)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (!wa.ReadyToAttack)
            {
                wa.FSM.ChangeState(wa.BreatherState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            jumpTimer -= Time.deltaTime;
            if (jumpTimer <= 0f && ctrl.IsGrounded())
            {
                jumpTimer = Random.Range(wa.jumpCheckInterval * 0.7f, wa.jumpCheckInterval * 1.3f);
                if (ctrl.HeightDifferenceToTarget() > wa.jumpHeightThreshold)
                    ctrl.RequestJump(Random.Range(0.22f, 0.32f));
            }

            if (dist <= wa.attackRange)
                wa.FSM.ChangeState(wa.CommitState, ctrl);
            else
                ctrl.MoveToward();
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // COMMIT — Tiny lining-up window, shorter than balanced warrior. If the boss
    // winds up, the aggressive bot usually evades, sometimes trades into it.
    // ==========================================================================
    public class WA_Commit : IPlayerFSMState
    {
        private float timer;
        private bool  willTrade;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            timer     = Random.Range(0.04f, 0.12f);
            willTrade = Random.value > wa.counterEvadeChance;
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

            if (!wa.ReadyToAttack)
            {
                wa.FSM.ChangeState(wa.BreatherState, ctrl);
                return;
            }

            float dist = wa.MeleeDistanceToTarget();
            if (dist > wa.attackRange * 1.25f)
            {
                wa.FSM.ChangeState(wa.PressState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && !willTrade && dist < wa.dangerRange)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wa.FSM.ChangeState(wa.StrikeState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) { }
    }

    // ==========================================================================
    // STRIKE — Fires a melee attack, may queue one combo, then exits into a
    // short disengage instead of looping attacks until stamina is empty.
    // ==========================================================================
    public class WA_Strike : IPlayerFSMState
    {
        private float timer;
        private bool  attemptCombo;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.StopMoving();
            ctrl.FaceTarget();
            ctrl.RequestAttack();
            attemptCombo = Random.value < wa.comboProbability;
            timer        = Random.Range(0.35f, 0.55f);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            float dist = wa.MeleeDistanceToTarget();
            ctrl.FaceTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (ctrl.BossIsAttacking && dist < wa.dangerRange && Random.value < wa.counterEvadeChance)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (attemptCombo && wa.MeleeAttack != null && wa.MeleeAttack.ComboWindowOpen)
                ctrl.RequestAttack();

            if (wa.MeleeAttack != null && wa.MeleeAttack.IsInAttackSequence)
            {
                ctrl.StopMoving();
                return;
            }

            if (!wa.ReadyToAttack)
            {
                wa.FSM.ChangeState(wa.DisengageState, ctrl);
                return;
            }

            if (dist > wa.attackRange * 1.35f)
            {
                wa.FSM.ChangeState(wa.PressState, ctrl);
                return;
            }

            if (dist > wa.stickRange)
                ctrl.MoveTowardAt(0.55f);
            else
                ctrl.StopMoving();

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wa.FSM.ChangeState(wa.DisengageState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // DISENGAGE — Takes a small step out after a hit/combo so the boss gets a
    // chance to whiff. This is the main difference from the all-in face-tank loop.
    // ==========================================================================
    public class WA_Disengage : IPlayerFSMState
    {
        private float timer;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            timer = Random.Range(wa.disengageMinTime, wa.disengageMaxTime);
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            float dist = wa.MeleeDistanceToTarget();

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            ctrl.FaceTarget();

            if (dist < wa.reengageDistance || ctrl.BossIsAttacking)
                ctrl.MoveAway();
            else
                ctrl.StopMoving();

            timer -= Time.deltaTime;
            if (timer <= 0f && dist >= wa.reengageDistance * 0.8f)
                wa.FSM.ChangeState(wa.ReadyToAttack ? (IPlayerFSMState)wa.PressState : wa.BreatherState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // EVADE — Brief dash/jump away from a close boss attack or artillery, then
    // immediately turns back in. This is a slip, not a defensive retreat.
    // ==========================================================================
    public class WA_Evade : IPlayerFSMState
    {
        private float timer;
        private bool  jumped;

        public void OnEnter(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.ConsumeArtilleryEvade();
            jumped = ctrl.IsGrounded() && Random.value < 0.60f;
            if (jumped)
                ctrl.RequestJump(Random.Range(0.18f, 0.28f));
            timer = Random.Range(0.25f, 0.45f);
            if (ctrl.RecentlyHurt)
                timer += wa.pressureAfterEvade;
        }

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;
            ctrl.FaceTarget();
            ctrl.MoveAway();

            timer -= Time.deltaTime;
            if (timer <= 0f)
                wa.FSM.ChangeState(wa.ReadyToAttack ? (IPlayerFSMState)wa.PressState : wa.BreatherState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }

    // ==========================================================================
    // BREATHER — Stamina is too low to keep swinging. Hold just outside danger,
    // face the boss, then re-enter as soon as the warrior can attack again.
    // ==========================================================================
    public class WA_Breather : IPlayerFSMState
    {
        public void OnEnter(PlayerFSMController ctrl) => ctrl.StopMoving();

        public void OnUpdate(PlayerFSMController ctrl)
        {
            var wa = (WarriorAggressiveFSM)ctrl;

            if (ctrl.ArtilleryEvadeRequested)
            {
                ctrl.ConsumeArtilleryEvade();
                ctrl.RequestJump(Random.Range(0.25f, 0.35f));
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            ctrl.FaceTarget();
            float dist = wa.MeleeDistanceToTarget();
            if (ctrl.BossIsAttacking && dist < wa.dangerRange)
            {
                wa.FSM.ChangeState(wa.EvadeState, ctrl);
                return;
            }

            if (dist < wa.dangerRange * 1.15f)
                ctrl.MoveAway();
            else if (dist > wa.attackRange * 1.6f)
                ctrl.MoveTowardAt(0.45f);
            else
                ctrl.StopMoving();

            if (!wa.IsExhaustedStamina && ctrl.StaminaRatio() >= wa.reengageStaminaRatio)
                wa.FSM.ChangeState(wa.PressState, ctrl);
        }

        public void OnExit(PlayerFSMController ctrl) => ctrl.StopMoving();
    }
}
