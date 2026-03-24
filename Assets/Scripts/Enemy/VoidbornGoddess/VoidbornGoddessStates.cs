using UnityEngine;

// ============================================================================
// HURT STATE — Brief stagger when the boss takes non-lethal damage
//
// NOTE: Health.TakeDamage() already fires the "hurt" animator trigger directly,
// so this state does NOT re-trigger it. It just pauses AI behaviour for
// STUN_DURATION seconds, then returns to Chase (or Idle if player left).
//
// Attacks are NOT interrupted (IsAttacking check in OnTakeDamage) — boss
// finishes its combo/artillery before reacting to hits.
// ============================================================================
public class VoidbornHurtState : IBossState
{
    private const float STUN_DURATION = 0.7f;
    private float timer;

    public void OnEnter(BossController boss)
    {
        timer = 0f;
        boss.Stop();
        boss.SetMoving(false);
        Debug.Log("[Voidborn] Hurt state entered.");
    }

    public void OnUpdate(BossController boss)
    {
        timer += Time.deltaTime;
        if (timer < STUN_DURATION) return;

        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        goddess.StateMachine.ChangeState(
            goddess.PlayerInDetectionRange() ? (IBossState)goddess.ChaseState : goddess.IdleState,
            goddess
        );
    }

    public void OnFixedUpdate(BossController boss) { }
    public void OnExit(BossController boss) { }
}

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

        // --- Ask AIDecisionEngine for action (three-layer cascade) ---
        AIDecisionEngine engine = goddess.DecisionEngine;
        if (engine != null)
        {
            BossDecision decision = engine.CurrentDecision;

            switch (decision.action)
            {
                case BossActionType.ArtilleryAttack:
                    if (goddess.CanUseArtillery)
                    {
                        engine.OnBossActionStarted(BossActionType.ArtilleryAttack);
                        goddess.StateMachine.ChangeState(goddess.ArtilleryState, goddess);
                        return;
                    }
                    break;

                case BossActionType.MeleeAttack:
                    if (goddess.PlayerInAttackRange() && goddess.CanAttack)
                    {
                        engine.OnBossActionStarted(BossActionType.MeleeAttack);
                        goddess.StateMachine.ChangeState(goddess.MeleeState, goddess);
                        return;
                    }
                    break;
            }
        }
        else
        {
            // Fallback: original behavior when no AIDecisionEngine is attached
            if (goddess.CanUseArtillery)
            {
                goddess.StateMachine.ChangeState(goddess.ArtilleryState, goddess);
                return;
            }
            if (goddess.PlayerInAttackRange() && goddess.CanAttack)
            {
                goddess.StateMachine.ChangeState(goddess.MeleeState, goddess);
                return;
            }
        }

        // --- Chase behavior ---
        goddess.FacePlayer();

        if (goddess.GetDistanceToPlayer() > goddess.attackRange)
        {
            goddess.SetMoving(true);
            goddess.MoveInDirection(goddess.GetDirectionToPlayer(), goddess.EffectiveChaseSpeed);
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
// MELEE ATTACK STATE — Animation-driven combo system (1–4 random swipes)
//
// HOW IT WORKS:
//   On OnEnter, the boss randomly picks how many swipes to do (MIN_SWIPES to
//   MAX_SWIPES). It then fires each swipe trigger one at a time, waits for that
//   animation clip to finish using the same two-stage wait pattern as the
//   artillery state, then fires the next trigger — until the chosen count is done.
//
// DAMAGE:
//   Each swipe deals damage via PerformMeleeHit(). This is called either:
//     (a) By an Animation Event on each clip at the exact hit frame — recommended.
//     (b) By the fallback timer FALLBACK_HIT_DELAY seconds into the swipe — safety net.
//   If you have Animation Events set up, set FALLBACK_HIT_DELAY = 0f to disable (b).
//
// ANIMATOR REQUIREMENTS:
//   Trigger parameters : meleeSwipe1, meleeSwipe2, meleeSwipe3, meleeSwipe4
//   Any State → MeleeSwipe1  [Condition: meleeSwipe1 | Has Exit Time: OFF | Duration: 0 | Can Transition To Self: OFF]
//   Any State → MeleeSwipe2  [Condition: meleeSwipe2 | Has Exit Time: OFF | Duration: 0 | Can Transition To Self: OFF]
//   Any State → MeleeSwipe3  [Condition: meleeSwipe3 | Has Exit Time: OFF | Duration: 0 | Can Transition To Self: OFF]
//   Any State → MeleeSwipe4  [Condition: meleeSwipe4 | Has Exit Time: OFF | Duration: 0 | Can Transition To Self: OFF]
//   MeleeSwipe1 → Idle       [No condition           | Has Exit Time: ON  | Exit: 0.95  | Duration: 0.1]
//   MeleeSwipe2 → Idle       [same as above]
//   MeleeSwipe3 → Idle       [same as above]
//   MeleeSwipe4 → Idle       [same as above]
// ============================================================================
public class VoidbornMeleeAttackState : IBossState
{
    // ── Combo settings ───────────────────────────────────────────────────────
    private const int MIN_SWIPES = 1;
    private const int MAX_SWIPES = 4; // inclusive

    // Trigger names — must exactly match the Trigger parameters in your Animator
    private static readonly string[] SwipeTriggers =
        { "meleeSwipe1", "meleeSwipe2", "meleeSwipe3", "meleeSwipe4" };

    // Fallback hit delay per swipe (seconds after the trigger fires).
    // Set to 0f if you are using Animation Events on all clips instead.
    private const float FALLBACK_HIT_DELAY = 0.25f;

    // ── Two-stage animation wait (same pattern as ArtilleryState) ───────────
    private enum AnimWait { WaitingForNewState, WaitingForCompletion }
    private AnimWait animWait;
    private int triggerStateHash;
    private int targetStateHash;

    // ── Runtime state ────────────────────────────────────────────────────────
    private int totalSwipes;        // Chosen randomly each time we enter this state
    private int currentSwipeIndex;  // Which swipe we are currently on (0-based)
    private bool swipeInitialized;  // Has the trigger for the current swipe been fired?
    private bool hitLanded;         // Has damage been dealt for the current swipe?
    private float swipeTimer;       // Time elapsed since the current swipe trigger fired

    // ── OnEnter ──────────────────────────────────────────────────────────────
    public void OnEnter(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;

        goddess.Stop();
        goddess.SetMoving(false);
        goddess.FacePlayer();
        goddess.IsAttacking = true;

        // Randomly decide combo length for this attack
        totalSwipes       = Random.Range(MIN_SWIPES, MAX_SWIPES + 1);
        currentSwipeIndex = 0;
        swipeInitialized  = false;
        hitLanded         = false;
        swipeTimer        = 0f;

        Debug.Log($"[Voidborn] ===== Melee combo started — chosen swipe count: {totalSwipes} =====");
    }

    // ── OnUpdate ─────────────────────────────────────────────────────────────
    public void OnUpdate(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        Animator anim = boss.Anim;
        if (anim == null) return;

        // All chosen swipes finished — return to chase
        if (currentSwipeIndex >= totalSwipes)
        {
            Debug.Log($"[Voidborn] Melee combo complete ({totalSwipes} swipe(s) done). Returning to Chase.");
            goddess.StateMachine.ChangeState(goddess.ChaseState, goddess);
            return;
        }

        // ── Fire the trigger for the current swipe ───────────────────────────
        if (!swipeInitialized)
        {
            BeginSwipe(anim, currentSwipeIndex);
            return;
        }

        // ── Fallback hit timer ───────────────────────────────────────────────
        if (FALLBACK_HIT_DELAY > 0f && !hitLanded)
        {
            swipeTimer += Time.deltaTime;
            if (swipeTimer >= FALLBACK_HIT_DELAY)
            {
                Debug.Log($"[Voidborn] Swipe {currentSwipeIndex + 1} — fallback hit fired.");
                goddess.PerformMeleeHit();
                hitLanded = true;
            }
        }

        // ── Wait for the current swipe animation to finish ───────────────────
        if (IsSwipeComplete(anim))
        {
            // Safety: ensure damage landed even if both Animation Event and fallback missed
            if (!hitLanded)
            {
                Debug.Log($"[Voidborn] Swipe {currentSwipeIndex + 1} — safety hit fired on clip end.");
                goddess.PerformMeleeHit();
                hitLanded = true;
            }

            Debug.Log($"[Voidborn] Swipe {currentSwipeIndex + 1}/{totalSwipes} finished.");

            // Advance to the next swipe
            currentSwipeIndex++;
            swipeInitialized = false;
            hitLanded        = false;
            swipeTimer       = 0f;
        }
    }

    /// <summary>
    /// Called by an Animation Event on each MeleeSwipe clip at the hit frame.
    /// Delivers damage at the exact frame the swipe visually connects.
    /// The fallback timer is a safety net — hitLanded prevents double-firing.
    /// </summary>
    public void OnSwipeAnimationHit(BossController boss)
    {
        if (hitLanded) return;
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        Debug.Log($"[Voidborn] Swipe {currentSwipeIndex + 1} — Animation Event hit fired.");
        goddess.PerformMeleeHit();
        hitLanded = true;
    }

    // ── BeginSwipe: fire the trigger and reset the two-stage wait ────────────
    private void BeginSwipe(Animator anim, int swipeIndex)
    {
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        triggerStateHash = info.fullPathHash;
        targetStateHash  = 0;
        animWait         = AnimWait.WaitingForNewState;
        swipeInitialized = true;

        // Clear all swipe triggers first to prevent any stale trigger from firing
        foreach (string t in SwipeTriggers)
            anim.ResetTrigger(t);

        string triggerName = SwipeTriggers[swipeIndex];
        anim.SetTrigger(triggerName);

        Debug.Log($"[Voidborn] Swipe {swipeIndex + 1}/{totalSwipes} — firing trigger '{triggerName}'");
    }

    // ── IsSwipeComplete: two-stage animation completion check ────────────────
    private bool IsSwipeComplete(Animator anim)
    {
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        bool inTransition = anim.IsInTransition(0);

        switch (animWait)
        {
            case AnimWait.WaitingForNewState:
                if (inTransition) return false;
                if (info.fullPathHash == triggerStateHash) return false;
                // Settled into the new clip — record its hash
                targetStateHash = info.fullPathHash;
                animWait = AnimWait.WaitingForCompletion;
                return false; // let it play at least one frame before checking

            case AnimWait.WaitingForCompletion:
                // Still inside the swipe clip — wait for it to reach the end
                if (!inTransition && info.fullPathHash == targetStateHash)
                    return info.normalizedTime >= 0.95f;
                // Animator already moved on (clip finished and auto-transitioned to Idle)
                if (!inTransition && info.fullPathHash != targetStateHash)
                    return true;
                // Mid-transition away from the swipe clip — it's wrapping up
                if (inTransition && info.fullPathHash == targetStateHash)
                    return true;
                return false;
        }
        return false;
    }

    public void OnFixedUpdate(BossController boss) { }

    // ── OnExit ───────────────────────────────────────────────────────────────
    public void OnExit(BossController boss)
    {
        VoidbornGoddessController goddess = boss as VoidbornGoddessController;
        goddess.IsAttacking = false;
        goddess.ResetAttackTimer();

        // Notify AIDecisionEngine of action completion (feeds into Layer 2 weight update)
        goddess.DecisionEngine?.OnBossActionCompleted(BossActionType.MeleeAttack);

        Debug.Log("[Voidborn] Melee state exited — attack timer reset.");
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

        // Notify AIDecisionEngine of action completion (feeds into Layer 2 weight update)
        goddess.DecisionEngine?.OnBossActionCompleted(BossActionType.ArtilleryAttack);

        Debug.Log("[Voidborn] Artillery state exited, cooldown reset.");
    }
}