using UnityEngine;

public class VoidbornIdleState : IBossState
{
    public void OnEnter(BossController boss)
    {
        boss.Stop();
        boss.SetMoving(false);
    }

    public void OnUpdate(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        
        if (goddess.PlayerInDetectionRange())
        {
            goddess.StateMachine.ChangeState(goddess.ChaseState, goddess);
        }
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss) { }
}

public class VoidbornChaseState : IBossState
{
    public void OnEnter(BossController boss)
    {
        boss.SetMoving(true);
        boss.FacePlayer();
    }

    public void OnUpdate(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;

        if (!goddess.PlayerInDetectionRange())
        {
            goddess.StateMachine.ChangeState(goddess.IdleState, goddess);
            return;
        }

        if (goddess.PlayerInAttackRange() && goddess.CanAttack)
        {
            goddess.StateMachine.ChangeState(goddess.MeleeState, goddess);
            return;
        }

        goddess.FacePlayer();
        
        // Move towards player if outside attack range
        if (goddess.GetDistanceToPlayer() > goddess.attackRange)
        {
            goddess.SetMoving(true);
            goddess.MoveInDirection(goddess.GetDirectionToPlayer(), goddess.chaseSpeed);
        }
        else
        {
            goddess.Stop();
            goddess.SetMoving(false);
        }
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss)
    {
        boss.SetMoving(false);
    }
}

public class VoidbornMeleeAttackState : IBossState
{
    private float stateTimer;
    private bool hasHit;
    // We assume the animation plays for roughly this long. Later this can be driven by Animation Events.
    private const float ATTACK_WINDUP = 0.4f; 
    private const float ATTACK_DURATION = 1.0f;

    public void OnEnter(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        stateTimer = 0f;
        hasHit = false;
        
        goddess.Stop();
        goddess.SetMoving(false);
        goddess.FacePlayer();
        goddess.IsAttacking = true;
        
        if (goddess.Anim != null)
            goddess.Anim.SetTrigger("meleeAttack");
    }

    public void OnUpdate(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        stateTimer += Time.deltaTime;

        if (stateTimer >= ATTACK_WINDUP && !hasHit)
        {
            goddess.PerformMeleeHit();
            hasHit = true;
        }

        if (stateTimer >= ATTACK_DURATION)
        {
            goddess.StateMachine.ChangeState(goddess.ChaseState, goddess);
        }
    }

    public void OnFixedUpdate(BossController boss) { }
    
    public void OnExit(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        goddess.IsAttacking = false;
        goddess.ResetAttackTimer();
    }
}
