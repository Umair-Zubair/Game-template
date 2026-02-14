using UnityEngine;

/// <summary>
/// ScriptableObject containing all tunable parameters for enemy behavior.
/// Create instances via: Assets > Create > Scriptable Objects > EnemyData
/// </summary>
[CreateAssetMenu(fileName = "EnemyData", menuName = "Scriptable Objects/EnemyData")]
public class EnemyData : ScriptableObject
{
    [Header("Movement")]
    [Tooltip("Speed when patrolling")]
    public float patrolSpeed = 2f;
    
    [Tooltip("Speed when chasing player")]
    public float chaseSpeed = 3.5f;
    
    [Tooltip("Speed when retreating from player")]
    public float retreatSpeed = 3f;

    [Header("Detection Ranges")]
    [Tooltip("Distance at which enemy detects player")]
    public float detectionRange = 8f;
    
    [Tooltip("Distance at which enemy can attack")]
    public float attackRange = 5f;
    
    [Tooltip("If player gets closer than this, enemy retreats")]
    public float retreatRange = 2f;

    [Header("Combat")]
    [Tooltip("Time between attacks")]
    public float attackCooldown = 1.5f;
    
    [Tooltip("Damage per attack")]
    public int damage = 1;

    [Header("Dodge")]
    [Tooltip("Distance covered when dodging")]
    public float dodgeDistance = 2f;
    
    [Tooltip("Time between dodges")]
    public float dodgeCooldown = 3f;
    
    [Tooltip("Range to detect incoming projectiles")]
    public float projectileDetectionRange = 5f;
    
    [Tooltip("Jump force for dodge jump")]
    public float dodgeJumpForce = 12f;

    [Header("Physics")]
    [Tooltip("Default gravity scale")]
    public float gravityScale = 2.0f;
    
    [Tooltip("Multiplier applied when falling for a snappier jump")]
    public float fallGravityMultiplier = 1.2f;
    
    [Tooltip("Terminal velocity")]
    public float maxFallSpeed = 20f;

    [Header("Stun")]
    [Tooltip("Duration of stun after being hit")]
    public float stunDuration = 0.5f;

    [Header("Patrol")]
    [Tooltip("Time to wait at patrol endpoints")]
    public float patrolIdleDuration = 1f;

    [Header("Dash")]
    [Tooltip("Horizontal speed during dash (units/sec)")]
    public float dashSpeed = 14f;

    [Tooltip("Total dash duration in seconds (should roughly match Dash.anim length)")]
    public float dashDuration = 0.35f;

    [Tooltip("Cooldown between dashes in seconds")]
    public float dashCooldown = 5f;

    [Tooltip("Minimum distance to player before AI considers dashing")]
    public float dashMinRange = 3f;

    [Tooltip("Maximum distance to player for dash to be considered")]
    public float dashMaxRange = 8f;

    [Tooltip("Damage dealt to player if dash connects")]
    public int dashDamage = 1;

    [Tooltip("Horizontal knockback force applied to player on dash hit")]
    public float dashKnockbackForce = 8f;

    [Tooltip("Whether the dash stops on collision with the player")]
    public bool dashStopsOnPlayerHit = false;

    [Tooltip("Whether the boss can be stunned out of a dash")]
    public bool dashInterruptibleByStun = true;
}
