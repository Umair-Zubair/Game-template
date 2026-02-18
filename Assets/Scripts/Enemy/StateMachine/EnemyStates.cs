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
        // Transition to dodge if projectile detected
        if (enemy.IncomingProjectileDetected() && enemy.CanDodge())
        {
            enemy.StateMachine.ChangeState(enemy.DodgeState, enemy);
            return;
        }

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
        // Priority 1: Incoming projectile -> Dodge
        if (enemy.IncomingProjectileDetected() && enemy.CanDodge())
        {
            enemy.StateMachine.ChangeState(enemy.DodgeState, enemy);
            return;
        }

        // Priority 2: Player detected -> Chase
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
        
        // Priority 1: Player too close → Retreat (always highest priority)
        if (enemy.PlayerTooClose())
        {
            enemy.StateMachine.ChangeState(enemy.RetreatState, enemy);
            return;
        }

        // Priority 2: Incoming projectile + can dodge → Dodge
        if (enemy.IncomingProjectileDetected() && enemy.CanDodge())
        {
            enemy.StateMachine.ChangeState(enemy.DodgeState, enemy);
            return;
        }

        // ---- Layer 2: Scored priority for special actions ----
        // Base scores are 1.0; adaptation profiles add bonuses to promote preferred actions.
        float dashScore      = enemy.CanDash()         ? 1f + enemy.DashPriorityBonus      : 0f;
        float artilleryScore = enemy.CanUseArtillery() ? 1f + enemy.ArtilleryPriorityBonus : 0f;
        float attackScore    = (enemy.PlayerInAttackRange() && enemy.CanAttack()) ? 1f : 0f;

        // Pick the highest-scoring available action
        if (dashScore > 0f && dashScore >= artilleryScore && dashScore >= attackScore)
        {
            enemy.StateMachine.ChangeState(enemy.DashState, enemy);
            return;
        }

        if (artilleryScore > 0f && artilleryScore >= dashScore && artilleryScore >= attackScore)
        {
            enemy.ResetArtilleryCooldown();
            enemy.StateMachine.ChangeState(enemy.ArtilleryStrikeState, enemy);
            return;
        }

        if (attackScore > 0f)
        {
            enemy.StateMachine.ChangeState(enemy.AttackState, enemy);
            return;
        }

        // Priority: Player out of detection range → Patrol
        if (!enemy.PlayerInDetectionRange())
        {
            enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
            return;
        }

        // Maintain optimal distance — stop early to stay at range
        enemy.FacePlayer();
        
        // Keep a buffer zone before attack range (80% of attack range)
        float stoppingDistance = enemy.Data.attackRange * 0.8f;
        
        if (distanceToPlayer > stoppingDistance)
        {
            enemy.MoveTowardPlayer(enemy.EffectiveChaseSpeed); // Layer 2: uses adapted speed
        }
        else
        {
            // In optimal position — stop and wait for attack cooldown
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

        // EMERGENCY DODGE during windup
        if (!hasFired && enemy.IncomingProjectileDetected() && enemy.CanDodge())
        {
            enemy.StateMachine.ChangeState(enemy.DodgeState, enemy);
            return;
        }

        // Fire after animation delay
        if (!hasFired && attackTimer >= FIRE_DELAY)
        {
            enemy.ExecuteAttack();
            enemy.ResetAttackCooldown(); // Reset cooldown AFTER firing, not before
            hasFired = true;
        }

        // Wait for attack to complete
        if (hasFired && !enemy.IsAttacking)
        {
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

        // Priority 1: Incoming projectile + can dodge → Dodge
        if (enemy.IncomingProjectileDetected() && enemy.CanDodge())
        {
            enemy.StateMachine.ChangeState(enemy.DodgeState, enemy);
            return;
        }

        // Stop retreating if far enough or timed out
        // Layer 2: uses EffectiveRetreatRange so adaptation can widen/narrow the safe zone
        if (enemy.GetDistanceToPlayer() > enemy.EffectiveRetreatRange || retreatTimer >= MAX_RETREAT_TIME)
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
        enemy.MoveAwayFromPlayer(enemy.EffectiveRetreatSpeed); // Layer 2: uses adapted speed
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
    private const float DODGE_DURATION = 0.4f; // Slightly shorter wait

    public void OnEnter(EnemyController enemy)
    {
        UnityEngine.Debug.Log("[DODGE STATE] Entered DodgeState! Attempting jump...");
        dodgeTimer = 0;
        enemy.ResetDodgeCooldown();
        
        // Jump to dodge!
        enemy.Jump(enemy.Data.dodgeJumpForce);
    }

    public void OnUpdate(EnemyController enemy)
    {
        dodgeTimer += UnityEngine.Time.deltaTime;

        // Transition back as soon as we land or timer expires
        if (dodgeTimer >= DODGE_DURATION || (dodgeTimer > 0.15f && enemy.IsGrounded()))
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

// ============================================================================
// DASH STATE - Rapid directional charge toward the player
// ============================================================================
public class DashEnemyState : IEnemyState
{
    private float dashTimer;
    private int dashDirection;
    private Vector2 startPosition;
    private bool hasHitPlayer;

    public void OnEnter(EnemyController enemy)
    {
        dashTimer = 0f;
        hasHitPlayer = false;

        // Lock direction at start — commit to it for the full dash
        dashDirection = enemy.GetDirectionToPlayer();
        enemy.FaceDirection(dashDirection);
        startPosition = enemy.transform.position;

        // Stop vertical velocity so gravity doesn't pull us mid-dash
        if (enemy.RB != null)
        {
            enemy.RB.linearVelocity = new Vector2(0f, 0f);
        }

        enemy.SetMoving(false);
        enemy.IsDashing = true;

        // Trigger dash animation
        if (enemy.Anim != null)
        {
            enemy.Anim.SetTrigger("dash");
        }

        if (enemy.DebugMode)
            Debug.Log($"[DASH] Started! Direction: {dashDirection}, Speed: {enemy.Data.dashSpeed}, Duration: {enemy.Data.dashDuration}");
    }

    public void OnUpdate(EnemyController enemy)
    {
        dashTimer += Time.deltaTime;

        // --- Dash movement (time-based, not frame-based) ---
        if (enemy.RB != null)
        {
            // Drive horizontal velocity; zero out vertical to keep dash flat
            enemy.RB.linearVelocity = new Vector2(dashDirection * enemy.Data.dashSpeed, 0f);
        }

        // --- Collision: check for player hit ---
        if (!hasHitPlayer && enemy.Player != null)
        {
            float distToPlayer = Vector2.Distance(enemy.transform.position, enemy.Player.position);
            if (distToPlayer <= 1.2f) // Contact threshold
            {
                OnDashHitPlayer(enemy);
            }
        }

        // --- Wall check: stop if we hit geometry ---
        if (IsBlockedByWall(enemy))
        {
            if (enemy.DebugMode)
                Debug.Log("[DASH] Blocked by wall — ending early.");
            TransitionOut(enemy);
            return;
        }

        // --- Duration complete ---
        if (dashTimer >= enemy.Data.dashDuration)
        {
            TransitionOut(enemy);
        }
    }

    public void OnFixedUpdate(EnemyController enemy) { }

    public void OnExit(EnemyController enemy)
    {
        enemy.IsDashing = false;
        enemy.Stop();
        enemy.ResetDashCooldown();

        if (enemy.DebugMode)
        {
            float distanceTravelled = Vector2.Distance(startPosition, enemy.transform.position);
            Debug.Log($"[DASH] Ended. Distance travelled: {distanceTravelled:F2}");
        }
    }

    // ---- Private helpers ----

    private void OnDashHitPlayer(EnemyController enemy)
    {
        hasHitPlayer = true;

        // Apply damage
        if (enemy.Player != null)
        {
            Health playerHealth = enemy.Player.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(enemy.Data.dashDamage);
                if (enemy.DebugMode)
                    Debug.Log($"[DASH] Hit player for {enemy.Data.dashDamage} damage!");
            }

            // Apply knockback
            Rigidbody2D playerRB = enemy.Player.GetComponent<Rigidbody2D>();
            if (playerRB != null)
            {
                Vector2 knockback = new Vector2(dashDirection * enemy.Data.dashKnockbackForce, enemy.Data.dashKnockbackForce * 0.3f);
                playerRB.linearVelocity = knockback;
            }
        }

        // Optionally stop on hit
        if (enemy.Data.dashStopsOnPlayerHit)
        {
            TransitionOut(enemy);
        }
    }

    private bool IsBlockedByWall(EnemyController enemy)
    {
        if (enemy.Collider == null) return false;

        // Horizontal raycast in dash direction
        float castDistance = 0.2f;
        RaycastHit2D hit = Physics2D.BoxCast(
            enemy.Collider.bounds.center,
            new Vector2(enemy.Collider.bounds.size.x * 0.9f, enemy.Collider.bounds.size.y * 0.8f),
            0f,
            new Vector2(dashDirection, 0),
            castDistance,
            LayerMask.GetMask("Default") // Ground/wall layers — adjust if your walls use a custom layer
        );

        return hit.collider != null && hit.collider.gameObject != enemy.gameObject;
    }

    private void TransitionOut(EnemyController enemy)
    {
        // Player may have died mid-dash
        if (enemy.Player == null)
        {
            enemy.StateMachine.ChangeState(enemy.PatrolState, enemy);
            return;
        }

        if (enemy.PlayerTooClose())
        {
            enemy.StateMachine.ChangeState(enemy.RetreatState, enemy);
        }
        else if (enemy.PlayerInAttackRange() && enemy.CanAttack())
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

// ============================================================================
// ARTILLERY STRIKE STATE - Calls down artillery strikes from above
// ============================================================================
public class ArtilleryStrikeEnemyState : IEnemyState
{
    private float stateTimer;
    private float strikeTimer;
    private int strikesLaunched;
    private bool animationTriggered;

    // Configuration - can be adjusted
    private const float WINDUP_DURATION = 0.5f;      // Time before strikes begin
    private const float STRIKE_INTERVAL = 0.4f;      // Time between each strike
    private const int TOTAL_STRIKES = 5;             // Number of artillery pieces
    private const float STATE_DURATION = 3.5f;       // Total state duration

    public void OnEnter(EnemyController enemy)
    {
        stateTimer = 0f;
        strikeTimer = 0f;
        strikesLaunched = 0;
        animationTriggered = false;

        enemy.Stop();
        enemy.SetMoving(false);
        enemy.FacePlayer();

        // Trigger artillery animation (reuses attack trigger for now)
        if (enemy.Anim != null)
        {
            enemy.Anim.SetTrigger("artilleryStrike");
        }
    }

    public void OnUpdate(EnemyController enemy)
    {
        stateTimer += Time.deltaTime;

        // Windup phase - boss raises arm/charges
        if (stateTimer < WINDUP_DURATION)
        {
            return;
        }

        // Strike phase - spawn artillery
        if (strikesLaunched < TOTAL_STRIKES)
        {
            strikeTimer += Time.deltaTime;

            if (strikeTimer >= STRIKE_INTERVAL)
            {
                strikeTimer = 0f;
                SpawnArtilleryStrike(enemy);
                strikesLaunched++;
            }
        }

        // State complete
        if (stateTimer >= STATE_DURATION)
        {
            TransitionToNextState(enemy);
        }
    }

    private void SpawnArtilleryStrike(EnemyController enemy)
    {
        // Get the ArtilleryStrikeManager from the enemy
        ArtilleryStrikeManager manager = enemy.GetComponent<ArtilleryStrikeManager>();
        if (manager != null)
        {
            manager.SpawnStrike();
        }
        else
        {
            Debug.LogWarning("[ARTILLERY] No ArtilleryStrikeManager found on enemy!");
        }
    }

    private void TransitionToNextState(EnemyController enemy)
    {
        // Return to appropriate state based on player position
        if (enemy.PlayerTooClose())
        {
            enemy.StateMachine.ChangeState(enemy.RetreatState, enemy);
        }
        else if (enemy.PlayerInAttackRange() && enemy.CanAttack())
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

    public void OnFixedUpdate(EnemyController enemy) { }

    public void OnExit(EnemyController enemy)
    {
        strikesLaunched = 0;
        enemy.ResetAttackCooldown();
    }
}