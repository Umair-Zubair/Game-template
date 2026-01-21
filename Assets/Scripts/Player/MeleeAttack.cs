using UnityEngine;

public class MeleeAttack : MonoBehaviour
{
    [Header("Attack Parameters")]
    [SerializeField] private float attackCooldown;
    [SerializeField] private float attackRange;
    [SerializeField] private int damage;

    [Header("References")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private AudioClip attackSound;

    private Animator anim;
    private PlayerController playerController;
    private float cooldownTimer = Mathf.Infinity;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        // Removed strict canAttack() check to allow attacking while moving
        if (Input.GetMouseButton(0) && cooldownTimer > attackCooldown && playerController != null
            && Time.timeScale > 0)
        {
            Attack();
        }

        cooldownTimer += Time.deltaTime;
    }

    private void Attack()
    {
        if(attackSound != null)
            SoundManager.instance.PlaySound(attackSound);
        
        anim.SetTrigger("attack");
        cooldownTimer = 0;

        // Detect enemies in range
        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(attackPoint.position, attackRange, enemyLayer);

        // Damage them
        foreach (Collider2D enemy in hitEnemies)
        {
            // Check for Health on the object or its parent
            if(enemy.GetComponent<Health>() != null)
                enemy.GetComponent<Health>().TakeDamage(damage);
            else if(enemy.GetComponentInParent<Health>() != null)
                enemy.GetComponentInParent<Health>().TakeDamage(damage);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}