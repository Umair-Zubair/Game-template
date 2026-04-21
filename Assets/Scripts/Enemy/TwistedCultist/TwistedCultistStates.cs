using UnityEngine;

/// <summary>
/// Spawn phase for the Twisted Cultist.
/// Plays intro animation, then switches into combat loop.
/// </summary>
public class TwistedCultistSpawnState : IBossState
{
    private float timer;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer = 0f;

        cultist.Stop();
        cultist.IsAttacking = true;
        cultist.PlayAnimationState(cultist.spawnStateName, true);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer += Time.deltaTime;

        bool doneByAnimation = cultist.HasAnimationFinished(cultist.spawnStateName);
        bool doneByFallback = timer >= cultist.spawnFallbackDuration;
        if (!doneByAnimation && !doneByFallback) return;

        cultist.StateMachine.ChangeState(
            cultist.PlayerInDetectionRange() ? (IBossState)cultist.PressState : cultist.IdleState,
            cultist);
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.IsAttacking = false;
        cultist.PlayAnimationState(cultist.idleStateName, true);
    }
}

/// <summary>
/// Passive idle until player is detected.
/// </summary>
public class TwistedCultistIdleState : IBossState
{
    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.Stop();
        cultist.PlayAnimationState(cultist.idleStateName);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        if (cultist.PlayerInDetectionRange())
        {
            cultist.StateMachine.ChangeState(cultist.PressState, cultist);
        }
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss) { }
}

/// <summary>
/// PRESS:
/// Close to effective range, avoid face-tanking at point blank,
/// and opportunistically enter react window before attacking.
/// </summary>
public class TwistedCultistPressState : IBossState
{
    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.FacePlayer();
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;

        if (!cultist.PlayerInDetectionRange())
        {
            cultist.StateMachine.ChangeState(cultist.IdleState, cultist);
            return;
        }

        float distance = cultist.GetDistanceToPlayer();
        cultist.FacePlayer();

        // Too close: immediate defensive reposition
        if (distance < cultist.minComfortRange)
        {
            if (cultist.CanJump && cultist.ShouldAttemptEvadeJump())
            {
                cultist.QueueJumpAway();
                cultist.StateMachine.ChangeState(cultist.JumpRepositionState, cultist);
            }
            else
            {
                cultist.StateMachine.ChangeState(cultist.EvadeState, cultist);
            }
            return;
        }

        // Far away: press forward hard, sometimes jump-engage
        if (distance > cultist.maxComfortRange)
        {
            if (cultist.CanJump && cultist.ShouldAttemptEngageJump())
            {
                cultist.QueueJumpToward();
                cultist.StateMachine.ChangeState(cultist.JumpRepositionState, cultist);
                return;
            }

            cultist.MoveInDirection(cultist.GetDirectionToPlayer(), cultist.pressSpeed);
            cultist.PlayAnimationState(cultist.moveStateName);
            return;
        }

        // In comfort band: line up attack if ready
        cultist.Stop();
        cultist.PlayAnimationState(cultist.idleStateName);

        if (cultist.PlayerInAttackRange() && cultist.CanUseRangedAttack)
        {
            cultist.StateMachine.ChangeState(cultist.ReactState, cultist);
        }
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.Stop();
    }
}

/// <summary>
/// REACT:
/// Very short commitment window before firing, similar to human wind-up.
/// Can still bail out if spacing changes.
/// </summary>
public class TwistedCultistReactState : IBossState
{
    private float timer;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.Stop();
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

        float distance = cultist.GetDistanceToPlayer();
        cultist.FacePlayer();

        // Abort if spacing became unsafe
        if (distance < cultist.minComfortRange * 0.9f)
        {
            cultist.StateMachine.ChangeState(cultist.EvadeState, cultist);
            return;
        }

        // Lost spacing: return to pressure loop
        if (distance > cultist.reengageRange)
        {
            cultist.StateMachine.ChangeState(cultist.PressState, cultist);
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        if (cultist.PlayerInAttackRange() && cultist.CanUseRangedAttack)
        {
            cultist.StateMachine.ChangeState(cultist.RangedAttackState, cultist);
        }
        else
        {
            cultist.StateMachine.ChangeState(cultist.PressState, cultist);
        }
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss) { }
}

/// <summary>
/// RANGED ATTACK:
/// Single arm-extension strike, animation-timed with fallback hit timers.
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
        cultist.PlayAnimationState(cultist.attackStateName, true);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer += Time.deltaTime;

        if (!hitApplied)
        {
            bool animationHit = cultist.HasAnimationReached(cultist.attackStateName, cultist.attackHitNormalizedTime);
            bool fallbackHit = timer >= cultist.attackFallbackHitDelay;
            if (animationHit || fallbackHit)
            {
                cultist.PerformExtendedArmAttack();
                hitApplied = true;
            }
        }

        bool animationEnd = cultist.HasAnimationFinished(cultist.attackStateName);
        bool fallbackEnd = timer >= cultist.attackFallbackEndDelay;
        if (!animationEnd && !fallbackEnd) return;

        float distance = cultist.GetDistanceToPlayer();
        bool stayAggressive = Random.value < cultist.postAttackReengageChance;

        if (distance < cultist.minComfortRange || (cultist.IsLowHealth && cultist.RecentlyDamaged))
        {
            cultist.StateMachine.ChangeState(cultist.EvadeState, cultist);
            return;
        }

        if (stayAggressive && cultist.CanJump && distance > cultist.maxComfortRange)
        {
            cultist.QueueJumpToward();
            cultist.StateMachine.ChangeState(cultist.JumpRepositionState, cultist);
            return;
        }

        cultist.StateMachine.ChangeState(cultist.PressState, cultist);
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.IsAttacking = false;
        cultist.ResetRangedAttackTimer();
        cultist.PlayAnimationState(cultist.idleStateName);
    }
}

/// <summary>
/// JUMP REPOSITION:
/// One committed jump, either toward or away from player, then reassess.
/// </summary>
public class TwistedCultistJumpRepositionState : IBossState
{
    private float timer;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer = 0f;
        cultist.IsAttacking = true;

        bool jumpAway = cultist.ConsumeQueuedJumpAway();
        if (jumpAway) cultist.JumpAwayFromPlayer();
        else cultist.JumpTowardPlayer();

        cultist.PlayAnimationState(cultist.jumpStateName, true);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer += Time.deltaTime;

        if (cultist.RB != null && cultist.RB.linearVelocity.y < 0f)
        {
            cultist.PlayAnimationState(cultist.fallStateName);
        }

        bool landed = timer >= 0.18f && cultist.IsGrounded();
        bool timedOut = timer >= cultist.jumpMaxDuration;
        if (!landed && !timedOut) return;

        if (!cultist.PlayerInDetectionRange())
        {
            cultist.StateMachine.ChangeState(cultist.IdleState, cultist);
            return;
        }

        float distance = cultist.GetDistanceToPlayer();
        if (distance < cultist.minComfortRange)
        {
            cultist.StateMachine.ChangeState(cultist.EvadeState, cultist);
            return;
        }

        if (distance <= cultist.maxComfortRange && cultist.CanUseRangedAttack && Random.value < 0.60f)
        {
            cultist.StateMachine.ChangeState(cultist.ReactState, cultist);
            return;
        }

        cultist.StateMachine.ChangeState(cultist.PressState, cultist);
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.IsAttacking = false;
        cultist.ResetJumpTimer();
    }
}

/// <summary>
/// EVADE:
/// Short disengage window to re-establish spacing, then return to pressure.
/// </summary>
public class TwistedCultistEvadeState : IBossState
{
    private float timer;

    public void OnEnter(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.IsAttacking = true;
        timer = Random.Range(cultist.evadeDurationMin, cultist.evadeDurationMax);

        if (cultist.CanJump && cultist.ShouldAttemptEvadeJump())
        {
            cultist.JumpAwayFromPlayer();
            cultist.PlayAnimationState(cultist.jumpStateName, true);
        }
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;

        if (!cultist.PlayerInDetectionRange())
        {
            cultist.StateMachine.ChangeState(cultist.IdleState, cultist);
            return;
        }

        float distance = cultist.GetDistanceToPlayer();
        cultist.FacePlayer();

        if (cultist.IsGrounded())
        {
            if (distance < cultist.maxComfortRange)
            {
                cultist.MoveInDirection(-cultist.GetDirectionToPlayer(), cultist.evadeSpeed);
                cultist.PlayAnimationState(cultist.moveStateName);
            }
            else
            {
                cultist.Stop();
                cultist.PlayAnimationState(cultist.idleStateName);
            }
        }
        else if (cultist.RB != null && cultist.RB.linearVelocity.y < 0f)
        {
            cultist.PlayAnimationState(cultist.fallStateName);
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        bool canCounter = distance >= cultist.minComfortRange
                          && distance <= cultist.maxComfortRange
                          && cultist.PlayerInAttackRange()
                          && cultist.CanUseRangedAttack;

        cultist.StateMachine.ChangeState(canCounter ? (IBossState)cultist.ReactState : cultist.PressState, cultist);
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.IsAttacking = false;
        cultist.Stop();
    }
}

/// <summary>
/// HURT:
/// Brief stagger. On recovery, low-health or point-blank situations prioritize evade.
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
        cultist.IsAttacking = true;
        cultist.PlayAnimationState(cultist.hurtStateName, true);
    }

    public void OnUpdate(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        timer += Time.deltaTime;
        if (timer < StunDuration) return;

        if (!cultist.PlayerInDetectionRange())
        {
            cultist.StateMachine.ChangeState(cultist.IdleState, cultist);
            return;
        }

        if (cultist.GetDistanceToPlayer() < cultist.minComfortRange * 1.1f || cultist.IsLowHealth)
        {
            cultist.StateMachine.ChangeState(cultist.EvadeState, cultist);
        }
        else
        {
            cultist.StateMachine.ChangeState(cultist.PressState, cultist);
        }
    }

    public void OnFixedUpdate(BossController boss) { }

    public void OnExit(BossController boss)
    {
        TwistedCultistController cultist = boss as TwistedCultistController;
        cultist.IsAttacking = false;
    }
}
