using UnityEngine;

public class TwistedCultistSpawnState : IBossState
{
    private float timer;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer = 0f;
        cultist.Stop();
        cultist.SetWalking(false);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer += Time.deltaTime;
        if (timer < cultist.spawnFallbackDuration) return;

        cultist.StateMachine.ChangeState(
            cultist.PlayerInDetectionRange() ? (IBossState)cultist.PressState : cultist.IdleState,
            cultist);
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss) { }
}

public class TwistedCultistIdleState : IBossState
{
    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.Stop();
        cultist.SetWalking(false);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        if (cultist.PlayerInDetectionRange())
            cultist.StateMachine.ChangeState(cultist.PressState, cultist);
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss) { }
}

/// <summary>
/// PRESS: Walk toward player. Back off if too close. Stop and attack when in range.
/// </summary>
public class TwistedCultistPressState : IBossState
{
    public void OnEnter(BossController boss)
    {
        (boss as TwistedCultistController).FacePlayer();
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;

        if (!cultist.PlayerInDetectionRange())
        {
            cultist.StateMachine.ChangeState(cultist.IdleState, cultist);
            return;
        }

        if (!cultist.IsPlayerOverlapping())
            cultist.FacePlayer();
        float distance = cultist.GetDistanceToPlayer();

        // Too close — back up while still facing the player
        if (distance < cultist.retreatRange)
        {
            cultist.SetWalking(true);
            cultist.MoveWithoutFacing(-cultist.GetDirectionToPlayer(), cultist.evadeSpeed);
            return;
        }

        // In attack range — stand still, transition to react when ready
        if (cultist.PlayerInAttackRange())
        {
            cultist.Stop();
            cultist.SetWalking(false);
            if (cultist.CanUseRangedAttack)
                cultist.StateMachine.ChangeState(cultist.ReactState, cultist);
            return;
        }

        // Out of range — walk toward player
        cultist.SetWalking(true);
        cultist.MoveInDirection(cultist.GetDirectionToPlayer(), cultist.pressSpeed);
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.Stop();
        cultist.SetWalking(false);
    }
}

/// <summary>
/// REACT: Brief wind-up before firing. Aborts if spacing breaks.
/// </summary>
public class TwistedCultistReactState : IBossState
{
    private float timer;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.Stop();
        cultist.SetWalking(false);
        cultist.FacePlayer();
        timer = Random.Range(cultist.reactDelayMin, cultist.reactDelayMax);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;

        if (!cultist.PlayerInDetectionRange())
        {
            cultist.StateMachine.ChangeState(cultist.IdleState, cultist);
            return;
        }

        if (!cultist.IsPlayerOverlapping())
            cultist.FacePlayer();

        if (cultist.PlayerTooClose() || !cultist.PlayerInAttackRange())
        {
            cultist.StateMachine.ChangeState(cultist.PressState, cultist);
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        cultist.StateMachine.ChangeState(
            cultist.CanUseRangedAttack ? (IBossState)cultist.RangedAttackState : cultist.PressState,
            cultist);
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss) { }
}

/// <summary>
/// RANGED ATTACK: Fires and waits for animation via fallback timers.
/// </summary>
public class TwistedCultistRangedAttackState : IBossState
{
    private float timer;
    private bool hitApplied;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer = 0f;
        hitApplied = false;
        cultist.IsAttacking = true;
        cultist.Stop();
        cultist.FacePlayer();
        cultist.TriggerAttackAnimation();
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer += Time.deltaTime;

        if (!hitApplied && timer >= cultist.attackFallbackHitDelay)
        {
            cultist.PerformExtendedArmAttack();
            hitApplied = true;
        }

        if (timer >= cultist.attackFallbackEndDelay)
            cultist.StateMachine.ChangeState(cultist.PressState, cultist);
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.IsAttacking = false;
        cultist.ResetRangedAttackTimer();
        cultist.SetWalking(false);
    }
}

/// <summary>
/// HURT: Brief stagger, then return to press.
/// </summary>
public class TwistedCultistHurtState : IBossState
{
    private const float StunDuration = 0.55f;
    private float timer;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer = 0f;
        cultist.Stop();
        cultist.SetWalking(false);
        cultist.IsAttacking = true;
        cultist.TriggerHurt();
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer += Time.deltaTime;
        if (timer < StunDuration) return;

        cultist.StateMachine.ChangeState(
            cultist.PlayerInDetectionRange() ? (IBossState)cultist.PressState : cultist.IdleState,
            cultist);
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        (boss as TwistedCultistController).IsAttacking = false;
    }
}
