using UnityEngine;

// ============================================================================
// IDLE STATE - Standing still, observing
// ============================================================================
public class IdleEnemyState : IEnemyState
{
    public void OnEnter(EnemyController enemy)
    {
        enemy.Stop();
        enemy.SetMoving(false);
    }

    public void OnUpdate(EnemyController enemy)
    {
        // Transition to chase if player detected
        if (enemy.PlayerInDetectionRange())
        {
            enemy.FacePlayer();
            enemy.StateMachine.ChangeState(enemy.ChaseState, enemy);
            return;
        }

        // Otherwise, start patrolling
        enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
    }

    public void OnFixedUpdate(EnemyController enemy) { }
    public void OnExit(EnemyController enemy) { }
}

// ============================================================================
// PATROL STATE - Left-right patrol between edges
// ============================================================================
public class PatrolEnemyState : IEnemyState
{
    public void OnEnter(EnemyController enemy)
    {
        enemy.SetMoving(true);
    }

    public void OnUpdate(EnemyController enemy)
    {
        // Priority 1: Player detected -> Chase
        if (enemy.PlayerInDetectionRange())
        {
            enemy.StateMachine.ChangeState(enemy.ChaseState, enemy);
            return;
        }

        // Patrol logic
        if (enemy.PatrolMovingLeft)
        {
            if (!enemy.AtLeftEdge())
            {
                enemy.MoveInDirection(-1, enemy.Data.patrolSpeed);
                enemy.PatrolIdleTimer = 0;
            }
            else
            {
                // Wait at edge
                enemy.SetMoving(false);
                enemy.PatrolIdleTimer += Time.deltaTime;
                
                if (enemy.PatrolIdleTimer >= enemy.Data.patrolIdleDuration)
                {
                    enemy.PatrolMovingLeft = false;
                    enemy.SetMoving(true);
                }
            }
        }
        else
        {
            if (!enemy.AtRightEdge())
            {
                enemy.MoveInDirection(1, enemy.Data.patrolSpeed);
                enemy.PatrolIdleTimer = 0;
            }
            else
            {
                // Wait at edge
                enemy.SetMoving(false);
                enemy.PatrolIdleTimer += Time.deltaTime;
                
                if (enemy.PatrolIdleTimer >= enemy.Data.patrolIdleDuration)
                {
                    enemy.PatrolMovingLeft = true;
                    enemy.SetMoving(true);
                }
            }
        }
    }

    public void OnFixedUpdate(EnemyController enemy) { }
    
    public void OnExit(EnemyController enemy)
    {
        enemy.PatrolIdleTimer = 0;
    }
}

// ============================================================================
// CHASE STATE - Move toward player
// ============================================================================
public class ChaseEnemyState : IEnemyState
{
    public void OnEnter(EnemyController enemy)
    {
        enemy.SetMoving(true);
        enemy.FacePlayer();
    }

    public void OnUpdate(EnemyController enemy)
    {
        float distanceToPlayer = enemy.GetDistanceToPlayer();
        
        // Priority 1: Player too close -> Retreat
        if (enemy.PlayerTooClose())
        {
            enemy.StateMachine.ChangeState(enemy.RetreatState, enemy);
            return;
        }

        // Priority 2: Incoming projectile + can dodge -> Dodge
        if (enemy.IncomingProjectileDetected() && enemy.CanDodge())
        {
            enemy.StateMachine.ChangeState(enemy.DodgeState, enemy);
            return;
        }

        // Priority 3: In attack range + can attack -> Attack
        if (enemy.PlayerInAttackRange() && enemy.CanAttack())
        {
            enemy.StateMachine.ChangeState(enemy.AttackState, enemy);
            return;
        }

        // Priority 4: Player out of detection range -> Patrol
        if (!enemy.PlayerInDetectionRange())
        {
            enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
            return;
        }

        // Maintain optimal distance - stop early to stay at range
        enemy.FacePlayer();
        
        // Keep a buffer zone before attack range (80% of attack range)
        float stoppingDistance = enemy.Data.attackRange * 0.8f;
        
        if (distanceToPlayer > stoppingDistance)
        {
            enemy.MoveTowardPlayer(enemy.Data.chaseSpeed);
        }
        else
        {
            // In optimal position - stop and wait for attack cooldown
            enemy.Stop();
            enemy.SetMoving(false);
        }
    }

    public void OnFixedUpdate(EnemyController enemy) { }
    
    public void OnExit(EnemyController enemy)
    {
        enemy.SetMoving(false);
    }
}

// ============================================================================
// ATTACK STATE - Fire projectiles at player with pattern variety
// ============================================================================
public class AttackEnemyState : IEnemyState
{
    private float attackTimer;
    private bool hasFired;
    private const float FIRE_DELAY = 0.3f; // Wait for animation to reach attack frame

    public void OnEnter(EnemyController enemy)
    {
        attackTimer = 0;
        hasFired = false;
        enemy.Stop();
        enemy.SetMoving(false);
        enemy.FacePlayer();
        
        // Trigger attack animation
        enemy.TriggerAttack();
        // DON'T reset cooldown here - wait until after we fire
    }

    public void OnUpdate(EnemyController enemy)
    {
        attackTimer += UnityEngine.Time.deltaTime;

        // Fire after animation delay
        if (!hasFired && attackTimer >= FIRE_DELAY)
        {
            UnityEngine.Debug.Log($"[ATTACK STATE] Firing after {attackTimer:F2}s delay");
            enemy.ExecuteAttack();
            enemy.ResetAttackCooldown(); // Reset cooldown AFTER firing, not before
            hasFired = true;
        }

        // Wait for attack to complete
        if (hasFired && !enemy.IsAttacking)
        {
            UnityEngine.Debug.Log("[ATTACK STATE] Attack complete, transitioning...");
            // Attack complete, transition to appropriate state
            if (enemy.PlayerTooClose())
            {
                enemy.StateMachine.ChangeState(enemy.RetreatState, enemy);
            }
            else if (enemy.PlayerInDetectionRange())
            {
                enemy.StateMachine.ChangeState(enemy.ChaseState, enemy);
            }
            else
            {
                enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
            }
        }
    }

    public void OnFixedUpdate(EnemyController enemy) { }
    
    public void OnExit(EnemyController enemy)
    {
        hasFired = false;
    }
}

// ============================================================================
// RETREAT STATE - Back away from player
// ============================================================================
public class RetreatEnemyState : IEnemyState
{
    private float retreatTimer;
    private const float MAX_RETREAT_TIME = 1.5f;

    public void OnEnter(EnemyController enemy)
    {
        retreatTimer = 0;
        enemy.SetMoving(true);
        enemy.FacePlayer(); // Face player while backing up
    }

    public void OnUpdate(EnemyController enemy)
    {
        retreatTimer += Time.deltaTime;

        // Priority 1: Incoming projectile + can dodge -> Dodge
        if (enemy.IncomingProjectileDetected() && enemy.CanDodge())
        {
            enemy.StateMachine.ChangeState(enemy.DodgeState, enemy);
            return;
        }

        // Stop retreating if far enough or timed out
        if (!enemy.PlayerTooClose() || retreatTimer >= MAX_RETREAT_TIME)
        {
            // Check if we can attack
            if (enemy.PlayerInAttackRange() && enemy.CanAttack())
            {
                enemy.StateMachine.ChangeState(enemy.AttackState, enemy);
            }
            else if (enemy.PlayerInDetectionRange())
            {
                enemy.StateMachine.ChangeState(enemy.ChaseState, enemy);
            }
            else
            {
                enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
            }
            return;
        }

        // Keep retreating
        enemy.FacePlayer();
        enemy.MoveAwayFromPlayer(enemy.Data.retreatSpeed);
    }

    public void OnFixedUpdate(EnemyController enemy) { }
    
    public void OnExit(EnemyController enemy)
    {
        enemy.SetMoving(false);
    }
}

// ============================================================================
// DODGE STATE - Jump to avoid projectiles
// ============================================================================
public class DodgeEnemyState : IEnemyState
{
    private float dodgeTimer;
    private const float DODGE_DURATION = 0.5f; // Time to wait before transitioning

    public void OnEnter(EnemyController enemy)
    {
        dodgeTimer = 0;
        enemy.ResetDodgeCooldown();
        
        // Jump to dodge!
        enemy.Jump(enemy.Data.dodgeJumpForce);
        
        UnityEngine.Debug.Log("[DODGE] Jumping to avoid projectile!");
    }

    public void OnUpdate(EnemyController enemy)
    {
        dodgeTimer += UnityEngine.Time.deltaTime;

        // Wait for jump to complete or timeout
        if (dodgeTimer >= DODGE_DURATION || (dodgeTimer > 0.2f && enemy.IsGrounded()))
        {
            // Dodge complete, return to chase or attack
            if (enemy.PlayerInAttackRange() && enemy.CanAttack())
            {
                enemy.StateMachine.ChangeState(enemy.AttackState, enemy);
            }
            else if (enemy.PlayerInDetectionRange())
            {
                enemy.StateMachine.ChangeState(enemy.ChaseState, enemy);
            }
            else
            {
                enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
            }
        }
    }

    public void OnFixedUpdate(EnemyController enemy) { }
    
    public void OnExit(EnemyController enemy) { }
}

// ============================================================================
// STUNNED STATE - Hit reaction, briefly incapacitated
// ============================================================================
public class StunnedEnemyState : IEnemyState
{
    private float stunTimer;

    public void OnEnter(EnemyController enemy)
    {
        stunTimer = 0;
        enemy.Stop();
        enemy.SetMoving(false);
        enemy.TriggerHurt();
    }

    public void OnUpdate(EnemyController enemy)
    {
        stunTimer += Time.deltaTime;

        if (stunTimer >= enemy.Data.stunDuration)
        {
            // Recover and return to appropriate state
            if (enemy.PlayerTooClose())
            {
                enemy.StateMachine.ChangeState(enemy.RetreatState, enemy);
            }
            else if (enemy.PlayerInDetectionRange())
            {
                enemy.StateMachine.ChangeState(enemy.ChaseState, enemy);
            }
            else
            {
                enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
            }
        }
    }

    public void OnFixedUpdate(EnemyController enemy) { }
    public void OnExit(EnemyController enemy) { }
}
