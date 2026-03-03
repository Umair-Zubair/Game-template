using UnityEngine;

/// <summary>
/// Specific controller for the Voidborn Goddess Boss.
/// Inherits from the base BossController and implements her unique states.
/// </summary>
public class VoidbornGoddessController : BossController
{
    [Header("Voidborn Settings")]
    public float chaseSpeed = 3f;
    public float attackCooldown = 2.5f;
    public float dashSpeed = 10f;
    
    [Header("Melee Setup")]
    public Transform meleeAttackPoint;
    public float meleeAttackRadius = 1.5f;
    public int meleeDamage = 2;
    
    // States
    public VoidbornIdleState IdleState { get; private set; }
    public VoidbornChaseState ChaseState { get; private set; }
    public VoidbornMeleeAttackState MeleeState { get; private set; }
    
    // Timers
    public float AttackTimer { get; private set; }
    public bool CanAttack => AttackTimer <= 0f && !IsAttacking;

    protected override void InitializeStates()
    {
        IdleState = new VoidbornIdleState();
        ChaseState = new VoidbornChaseState();
        MeleeState = new VoidbornMeleeAttackState();
        
        AttackTimer = attackCooldown;
    }

    protected override void StartState()
    {
        StateMachine.Initialize(IdleState, this);
    }

    protected override void UpdateBossTimers()
    {
        if (AttackTimer > 0)
            AttackTimer -= Time.deltaTime;
    }

    public void ResetAttackTimer()
    {
        AttackTimer = attackCooldown;
    }

    /// <summary>
    /// Performs the hitbox check for the melee attack.
    /// Can be called via Animation Event or Timer.
    /// </summary>
    public void PerformMeleeHit()
    {
        if (meleeAttackPoint == null) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(meleeAttackPoint.position, meleeAttackRadius, playerDamageLayer);
        foreach (var hit in hits)
        {
            Health playerHealth = hit.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(meleeDamage);
                Debug.Log($"[Voidborn] Melee hit for {meleeDamage} damage!");
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (meleeAttackPoint != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(meleeAttackPoint.position, meleeAttackRadius);
        }
    }
}
