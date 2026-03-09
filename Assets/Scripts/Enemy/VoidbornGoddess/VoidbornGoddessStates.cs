using UnityEngine;

// ============================================================================
// IDLE STATE — Waits until the player enters detection range
// ============================================================================
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

// ============================================================================
// CHASE STATE — Pursues the player; transitions to melee or artillery attacks
// ============================================================================
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

        // --- Artillery attack takes priority when its cooldown is ready ---
        if (goddess.CanUseArtillery)
        {
            goddess.StateMachine.ChangeState(goddess.ArtilleryState, goddess);
            return;
        }

        // --- Melee attack when in close range ---
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

// ============================================================================
// MELEE ATTACK STATE — Close-range hit driven by timers (unchanged)
// ============================================================================
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

// ============================================================================
// ARTILLERY ATTACK STATE — Animation-driven teleport + falling projectile
// ============================================================================
//
// This state drives a 6-phase animation sequence:
//   1. despawn       → Boss disappears from its current arena position.
//   2. spawn         → Boss reappears at the artillery teleport anchor.
//   3. cast2idle     → Transition animation preparing the spell.
//   4. cast2spell    → The spell cast; the artillery projectile is summoned here.
//   5. despawn2      → Post-cast transition (boss disappears from anchor).
//   6. spawn2        → Teleport back to arena + reappear. Animator transitions to idle.
//
// IMPORTANT — the code does NOT use timers or hardcoded durations.
// It listens to the Animator's current state and waits for each animation
// to finish (normalizedTime >= 0.95) before advancing to the next phase.
// The animation system fully controls the timing of the attack.
//
// Animation tracking uses two wait stages per phase:
//   WaitingForNewState  — waits until the Animator has fully transitioned
//                         into the new state (not in transition, hash differs).
//   WaitingForCompletion — waits until THAT state's normalizedTime >= 0.95
//                          or the Animator has already moved past it.
// This prevents the code from misreading a leftover/default state and
// skipping through phases.
// ============================================================================
public class VoidbornArtilleryState : IBossState
{
    /// <summary>
    /// The six phases of the artillery attack, matching the animation transitions.
    /// </summary>
    private enum Phase
    {
        DespawnFromArena,   // 1. Trigger "despawn" — boss disappears
        SpawnAtAnchor,      // 2. Teleport to anchor, trigger "spawn"
        CastIdle,           // 3. Trigger "cast2idle" — preparing the cast
        CastSpell,          // 4. Trigger "cast2spell" — summon the projectile
        Despawn2,           // 5. Trigger "despawn2" — post-cast transition
        Spawn2,             // 6. Teleport back, trigger "spawn2" — Animator transitions to idle
        Complete
    }

    /// <summary>
    /// Two-stage wait: first confirm we entered the new animation state,
    /// then watch for that specific state to finish playing.
    /// </summary>
    private enum AnimWait
    {
        WaitingForNewState,   // Animator hasn't settled into the triggered state yet
        WaitingForCompletion  // We're in the new state; waiting for it to finish
    }

    private Phase currentPhase;
    private AnimWait animWait;
    private Vector3 originalPosition;
    private bool phaseInitialized;

    // Hash of the state the Animator was in when we fired the trigger
    private int triggerStateHash;
    // Hash of the state we actually entered (recorded once settled)
    private int targetStateHash;

    // ---------------------------------------------------------------
    // IBossState — OnEnter
    // ---------------------------------------------------------------
    public void OnEnter(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        goddess.IsAttacking = true;
        goddess.Stop();

        // Save the boss's current position so we can return after the attack
        originalPosition = boss.transform.position;

        // Disable gravity while the boss is airborne at the artillery anchor
        boss.IsDashing = true;
        boss.RB.linearVelocity = Vector2.zero;

        currentPhase = Phase.DespawnFromArena;
        phaseInitialized = false;

        Debug.Log("[Voidborn] Artillery attack started.");
    }

    // ---------------------------------------------------------------
    // IBossState — OnUpdate (animation-driven phase progression)
    // ---------------------------------------------------------------
    public void OnUpdate(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        Animator anim = boss.Anim;
        if (anim == null) return;

        switch (currentPhase)
        {
            // ---- Phase 1: despawn from current arena position ----
            case Phase.DespawnFromArena:
                if (!phaseInitialized)
                {
                    BeginPhase(anim, "despawn");
                    return;
                }
                if (IsPhaseComplete(anim)) AdvanceTo(Phase.SpawnAtAnchor);
                break;

            // ---- Phase 2: teleport to anchor, play spawn ----
            case Phase.SpawnAtAnchor:
                if (!phaseInitialized)
                {
                    // Teleport the boss to the configurable artillery anchor
                    boss.transform.position = new Vector3(
                        goddess.artilleryAnchorPosition.x,
                        goddess.artilleryAnchorPosition.y,
                        boss.transform.position.z
                    );
                    boss.RB.linearVelocity = Vector2.zero;
                    BeginPhase(anim, "spawn");
                    return;
                }
                if (IsPhaseComplete(anim)) AdvanceTo(Phase.CastIdle);
                break;

            // ---- Phase 3: cast2idle — transition preparing the cast ----
            case Phase.CastIdle:
                if (!phaseInitialized)
                {
                    BeginPhase(anim, "cast2idle");
                    return;
                }
                if (IsPhaseComplete(anim)) AdvanceTo(Phase.CastSpell);
                break;

            // ---- Phase 4: cast2spell — summon the artillery projectile ----
            case Phase.CastSpell:
                if (!phaseInitialized)
                {
                    BeginPhase(anim, "cast2spell");
                    return;
                }
                // Projectile spawning is handled by Animation Events on the cast2spell clip.
                // The Animator calls SpawnArtilleryProjectile() at the exact frames set in Unity.
                if (IsPhaseComplete(anim)) AdvanceTo(Phase.Despawn2);
                break;

            // ---- Phase 5: despawn2 — post-cast transition ----
            case Phase.Despawn2:
                if (!phaseInitialized)
                {
                    BeginPhase(anim, "despawn2");
                    return;
                }
                if (IsPhaseComplete(anim)) AdvanceTo(Phase.Spawn2);
                break;

            // ---- Phase 6: teleport back, play spawn2 — Animator goes to idle on its own ----
            case Phase.Spawn2:
                if (!phaseInitialized)
                {
                    // Return the boss to the saved arena position before spawning back
                    boss.transform.position = originalPosition;
                    boss.RB.linearVelocity = Vector2.zero;
                    BeginPhase(anim, "spawn2");
                    return;
                }
                if (IsPhaseComplete(anim)) AdvanceTo(Phase.Complete);
                break;

            // ---- All phases finished — return to chase ----
            case Phase.Complete:
                Debug.Log("[Voidborn] Artillery attack complete.");
                goddess.StateMachine.ChangeState(goddess.ChaseState, goddess);
                break;
        }
    }

    // ---------------------------------------------------------------
    // Triggers an animation and resets the two-stage wait.
    // Clears ALL artillery triggers first to prevent stale triggers
    // from causing the Animator to skip to the wrong state.
    // ---------------------------------------------------------------
    private void BeginPhase(Animator anim, string triggerName)
    {
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        triggerStateHash = info.fullPathHash;
        targetStateHash = 0;
        animWait = AnimWait.WaitingForNewState;
        phaseInitialized = true;

        // Clear any stale triggers so the Animator only processes the one we want
        anim.ResetTrigger("despawn");
        anim.ResetTrigger("spawn");
        anim.ResetTrigger("cast2idle");
        anim.ResetTrigger("cast2spell");
        anim.ResetTrigger("despawn2");
        anim.ResetTrigger("spawn2");

        anim.SetTrigger(triggerName);
    }

    // ---------------------------------------------------------------
    // Two-stage animation completion check:
    //   Stage 1 (WaitingForNewState):
    //     Wait until the Animator is NOT in transition AND has settled
    //     into a state whose hash differs from the one we were in when
    //     the trigger was fired. This confirms the new clip is playing.
    //   Stage 2 (WaitingForCompletion):
    //     Wait until the entered state's normalizedTime >= 0.95
    //     OR the Animator has already moved past it to another state.
    // ---------------------------------------------------------------
    private bool IsPhaseComplete(Animator anim)
    {
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        bool inTransition = anim.IsInTransition(0);

        switch (animWait)
        {
            case AnimWait.WaitingForNewState:
                // Don't check during a blend — the source state info is unreliable
                if (inTransition) return false;
                // Must be in a different state from when we triggered
                if (info.fullPathHash == triggerStateHash) return false;
                // We've settled into the new animation — record its hash
                targetStateHash = info.fullPathHash;
                animWait = AnimWait.WaitingForCompletion;
                return false; // let it play at least one frame

            case AnimWait.WaitingForCompletion:
                // Still in the target state — wait for it to finish
                if (!inTransition && info.fullPathHash == targetStateHash)
                    return info.normalizedTime >= 0.95f;
                // Animator already moved on (the clip finished and auto-transitioned)
                if (!inTransition && info.fullPathHash != targetStateHash)
                    return true;
                // Mid-transition FROM the target state — it's wrapping up
                if (inTransition && info.fullPathHash == targetStateHash)
                    return true;
                return false;
        }
        return false;
    }

    /// <summary>
    /// Advances to the next phase in the artillery sequence.
    /// </summary>
    private void AdvanceTo(Phase nextPhase)
    {
        currentPhase = nextPhase;
        phaseInitialized = false;
    }

    public void OnFixedUpdate(BossController boss) { }

    // ---------------------------------------------------------------
    // IBossState — OnExit
    // ---------------------------------------------------------------
    public void OnExit(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        goddess.IsAttacking = false;
        goddess.IsDashing = false; // Re-enable gravity
        goddess.ResetArtilleryTimer();

        Debug.Log("[Voidborn] Artillery state exited, cooldown reset.");
    }
}
