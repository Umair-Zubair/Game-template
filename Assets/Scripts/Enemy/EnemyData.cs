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
    public float projectileDetectionRange = 3f;
    
    [Tooltip("Jump force for dodge jump")]
    public float dodgeJumpForce = 8f;

    [Header("Stun")]
    [Tooltip("Duration of stun after being hit")]
    public float stunDuration = 0.5f;

    [Header("Patrol")]
    [Tooltip("Time to wait at patrol endpoints")]
    public float patrolIdleDuration = 1f;
}
