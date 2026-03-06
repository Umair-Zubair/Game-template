using UnityEngine;

public class MeleeEnemy : MonoBehaviour
{
    [Header("Attack Parameters")]
    [SerializeField] private float attackCooldown;
    [SerializeField] private float range;
    [SerializeField] private int damage;

    [Header("Collider Parameters")]
    [SerializeField] private float colliderDistance;
    [SerializeField] private BoxCollider2D boxCollider;

    [Header("Player Layer")]
    [SerializeField] private LayerMask playerLayer;
    
    [Header("Damage Timing")]
    [Tooltip("Delay after attack animation starts before dealing damage (seconds)")]
    [SerializeField] private float damageDelay = 0.3f;
    
    [Header("Visual Debug")]
    [Tooltip("Show attack range gizmo in Scene view (always visible)")]
    [SerializeField] private bool showAttackRange = true;
    [Tooltip("Color of the attack range box")]
    [SerializeField] private Color gizmoColor = Color.red;
    
    private float cooldownTimer = Mathf.Infinity;
    private float damageTimer = Mathf.Infinity;
    private bool hasDealtDamageThisAttack = false;

    //References
    private Animator anim;
    private Health playerHealth;
    private EnemyPatrol enemyPatrol;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        enemyPatrol = GetComponentInParent<EnemyPatrol>();
    }

    private void Update()
    {
        cooldownTimer += Time.deltaTime;

        //Attack only when player in sight?
        if (PlayerInSight())
        {
            if (cooldownTimer >= attackCooldown)
            {
                // Start attack
                cooldownTimer = 0;
                damageTimer = 0;
                hasDealtDamageThisAttack = false;
                anim.SetTrigger("meleeAttack");
            }
        }

        // Handle damage timing after attack starts
        if (!hasDealtDamageThisAttack && damageTimer < damageDelay)
        {
            damageTimer += Time.deltaTime;
            
            // Deal damage at the right moment in the animation
            if (damageTimer >= damageDelay)
            {
                DamagePlayer();
                hasDealtDamageThisAttack = true;
            }
        }

        if (enemyPatrol != null)
            enemyPatrol.enabled = !PlayerInSight();
    }

    private bool PlayerInSight()
    {
        RaycastHit2D hit =
            Physics2D.BoxCast(boxCollider.bounds.center + transform.right * range * transform.localScale.x * colliderDistance,
            new Vector3(boxCollider.bounds.size.x * range, boxCollider.bounds.size.y, boxCollider.bounds.size.z),
            0, Vector2.left, 0, playerLayer);

        if (hit.collider != null)
            playerHealth = hit.transform.GetComponent<Health>();

        return hit.collider != null;
    }
    
    // This draws the gizmo ALWAYS (even when not selected)
    private void OnDrawGizmos()
    {
        if (!showAttackRange || boxCollider == null) return;
        
        // Draw the attack detection box
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.3f); // Semi-transparent fill
        Vector3 boxCenter = boxCollider.bounds.center + transform.right * range * transform.localScale.x * colliderDistance;
        Vector3 boxSize = new Vector3(boxCollider.bounds.size.x * range, boxCollider.bounds.size.y, boxCollider.bounds.size.z);
        Gizmos.DrawCube(boxCenter, boxSize);
        
        // Draw wire outline for better visibility
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(boxCenter, boxSize);
    }
    
    // This draws when the GameObject is SELECTED (brighter/more visible)
    private void OnDrawGizmosSelected()
    {
        if (boxCollider == null) return;
        
        // Draw a brighter, more visible version when selected
        Gizmos.color = Color.yellow;
        Vector3 boxCenter = boxCollider.bounds.center + transform.right * range * transform.localScale.x * colliderDistance;
        Vector3 boxSize = new Vector3(boxCollider.bounds.size.x * range, boxCollider.bounds.size.y, boxCollider.bounds.size.z);
        Gizmos.DrawWireCube(boxCenter, boxSize);
        
        // Draw a line from enemy center to attack box center
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, boxCenter);
    }

    private void DamagePlayer()
    {
        // Re-check if player is still in range when damage is dealt
        if (PlayerInSight())
        {
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
                Debug.Log($"[MELEE ENEMY] Dealt {damage} damage to player!");
            }
            else
            {
                Debug.LogWarning("[MELEE ENEMY] Player in sight but Health component not found!");
            }
        }
        else
        {
            Debug.Log("[MELEE ENEMY] Attack missed - player moved out of range during attack!");
        }
    }
}