using UnityEngine;

public class MeleeAttack : MonoBehaviour
{
    [Header("Attack Parameters")]
    [SerializeField] private float attackCooldown;
    [SerializeField] private float attackRange;
    [SerializeField] private int damage;

    [Header("Jump Attack Parameters")]
    [SerializeField] private float jumpAttackHeight;

    [Header("References")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private AudioClip attackSound;
    [SerializeField] private AudioClip jumpAttackSound;
    [SerializeField] private AudioClip uppercutSound;

    // ---- Layer 2 AI: Behavior tracking events ----
    /// <summary>Fired whenever the player performs any attack. Arg = attack type string.</summary>
    public System.Action<string> OnAttackPerformed;
    /// <summary>Fired when an attack successfully hits an enemy. Arg = damage dealt.</summary>
    public System.Action<float> OnDamageDealt;

    private Animator anim;
    private PlayerController playerController;
    private PlayerStamina stamina;
    private Rigidbody2D body;
    private BoxCollider2D boxCollider;
    private float cooldownTimer = Mathf.Infinity;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
        stamina = GetComponent<PlayerStamina>();
        body = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
    }

    private void Update()
    {
        // LMB — Regular melee attack
        if (Input.GetMouseButton(0) && cooldownTimer > attackCooldown && playerController != null
            && Time.timeScale > 0)
        {
            float cost = IsGrounded()
                ? (stamina != null ? stamina.Data.attackCost : 0f)
                : (stamina != null ? stamina.Data.airAttackCost : 0f);

            if (stamina == null || stamina.TryConsume(cost))
                Attack();
        }

        // F key — Jump Attack (grounded only)
        if (Input.GetKeyDown(KeyCode.F) && cooldownTimer > attackCooldown && playerController != null
            && Time.timeScale > 0 && IsGrounded())
        {
            float cost = stamina != null ? stamina.Data.jumpAttackCost : 0f;
            if (stamina == null || stamina.TryConsume(cost))
                JumpAttack();
        }

        // RMB — Uppercut
        if (Input.GetMouseButtonDown(1) && cooldownTimer > attackCooldown && playerController != null
            && Time.timeScale > 0)
        {
            float cost = stamina != null ? stamina.Data.uppercutCost : 0f;
            if (stamina == null || stamina.TryConsume(cost))
                Uppercut();
        }

        cooldownTimer += Time.deltaTime;
    }

    private void Attack()
    {
        if (attackSound != null)
            SoundManager.instance.PlaySound(attackSound);

        anim.SetTrigger("attack");
        cooldownTimer = 0;
        OnAttackPerformed?.Invoke("melee");

        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(attackPoint.position, attackRange, enemyLayer);
        foreach (Collider2D enemy in hitEnemies)
        {
            if (enemy.GetComponent<Health>() != null)
            {
                enemy.GetComponent<Health>().TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
            }
            else if (enemy.GetComponentInParent<Health>() != null)
            {
                enemy.GetComponentInParent<Health>().TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
            }
        }
    }

    private void JumpAttack()
    {
        if (jumpAttackSound != null)
            SoundManager.instance.PlaySound(jumpAttackSound);

        anim.SetTrigger("jumpAttack");
        cooldownTimer = 0;
        OnAttackPerformed?.Invoke("jumpAttack");

        if (body != null)
            body.linearVelocity = new Vector2(body.linearVelocity.x, jumpAttackHeight);

        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(attackPoint.position, attackRange, enemyLayer);
        foreach (Collider2D enemy in hitEnemies)
        {
            if (enemy.GetComponent<Health>() != null)
            {
                enemy.GetComponent<Health>().TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
            }
            else if (enemy.GetComponentInParent<Health>() != null)
            {
                enemy.GetComponentInParent<Health>().TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
            }
        }
    }

    private void Uppercut()
    {
        if (uppercutSound != null)
            SoundManager.instance.PlaySound(uppercutSound);

        anim.SetTrigger("uppercut");
        cooldownTimer = 0;
        OnAttackPerformed?.Invoke("uppercut");

        Collider2D[] hitEnemies = Physics2D.OverlapCircleAll(attackPoint.position, attackRange, enemyLayer);
        foreach (Collider2D enemy in hitEnemies)
        {
            if (enemy.GetComponent<Health>() != null)
            {
                enemy.GetComponent<Health>().TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
            }
            else if (enemy.GetComponentInParent<Health>() != null)
            {
                enemy.GetComponentInParent<Health>().TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
            }
        }
    }

    private bool IsGrounded()
    {
        if (boxCollider == null) return false;
        RaycastHit2D raycastHit = Physics2D.BoxCast(boxCollider.bounds.center, boxCollider.bounds.size, 0, Vector2.down, 0.1f, groundLayer);
        return raycastHit.collider != null;
    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}