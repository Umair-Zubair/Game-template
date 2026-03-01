using UnityEngine;

public class BlobRangedAttack : MonoBehaviour
{
    [SerializeField] private float attackCooldown;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject projectilePrefab;        // Ground range attack projectile
    [SerializeField] private GameObject jumpProjectilePrefab;    // Air range attack projectile (jumpProjectile)
    [SerializeField] private int poolSize = 10;
    [SerializeField] private AudioClip projectileSound;
    [SerializeField] private AudioClip jumpProjectileSound;

    private Animator anim;
    private PlayerController playerController;
    private PlayerStamina stamina;
    private float cooldownTimer = Mathf.Infinity;

    private GameObject[] projectiles;
    private GameObject[] jumpProjectiles;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
        stamina = GetComponent<PlayerStamina>();

        CreateProjectilePool();
    }

    private void OnEnable()
    {
        // Re-check pool in case prefabs changed
        if (projectiles == null) CreateProjectilePool();
    }

    private void CreateProjectilePool()
    {
        // Ground projectile pool
        if (projectilePrefab != null)
        {
            projectiles = new GameObject[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                projectiles[i] = Instantiate(projectilePrefab, transform);
                projectiles[i].name = "Projectile_" + i;
                projectiles[i].SetActive(false);
            }
        }

        // Jump projectile pool
        if (jumpProjectilePrefab != null)
        {
            jumpProjectiles = new GameObject[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                jumpProjectiles[i] = Instantiate(jumpProjectilePrefab, transform);
                jumpProjectiles[i].name = "JumpProjectile_" + i;
                jumpProjectiles[i].SetActive(false);
            }
        }
    }

    private void Update()
    {
        // LMB fires range attack — ground fires normal, air fires jump range
        if (Input.GetMouseButtonDown(0) && cooldownTimer > attackCooldown && playerController != null && Time.timeScale > 0)
        {
            bool isGrounded = playerController.IsGrounded();

            if (isGrounded)
            {
                // Ground ranged attack
                float cost = stamina != null ? stamina.Data.rangedAttackCost : 0f;
                if (stamina == null || stamina.TryConsume(cost))
                    Attack();
            }
            else
            {
                // Air jump range attack
                float cost = stamina != null ? stamina.Data.rangedAttackCost : 0f;
                if (stamina == null || stamina.TryConsume(cost))
                    JumpRangeAttack();
            }
        }

        cooldownTimer += Time.deltaTime;
    }

    // ── Ground Range Attack ──────────────────────────────────────────────────
    private void Attack()
    {
        if (projectiles == null)
        {
            Debug.LogWarning("BlobRangedAttack: projectilePrefab not assigned!");
            return;
        }

        if (projectileSound != null)
            SoundManager.instance.PlaySound(projectileSound);

        anim.SetTrigger("rangeAttack");
        cooldownTimer = 0;

        int index = FindProjectile(projectiles);
        float dir = Mathf.Sign(transform.localScale.x);

        Debug.Log($"[RangedAttack] Firing ground projectile - Direction: {dir}");

        projectiles[index].transform.position = firePoint.position;
        projectiles[index].GetComponent<Projectile>().SetDirection(dir);
    }

    // ── Air Jump Range Attack ────────────────────────────────────────────────
    private void JumpRangeAttack()
    {
        if (jumpProjectiles == null)
        {
            Debug.LogWarning("BlobRangedAttack: jumpProjectilePrefab not assigned! Falling back to normal projectile.");
            Attack(); // Fallback to normal if jump prefab missing
            return;
        }

        if (jumpProjectileSound != null)
            SoundManager.instance.PlaySound(jumpProjectileSound);
        else if (projectileSound != null)
            SoundManager.instance.PlaySound(projectileSound);

        anim.SetTrigger("jumpRange"); // Uses the jumpRang animator parameter (visible in your animator)
        cooldownTimer = 0;

        int index = FindProjectile(jumpProjectiles);
        float dir = Mathf.Sign(transform.localScale.x);

        Debug.Log($"[RangedAttack] Firing jump projectile - Direction: {dir}");

        jumpProjectiles[index].transform.position = firePoint.position;
        jumpProjectiles[index].GetComponent<Projectile>().SetDirection(dir);
    }

    // ── Pool Helper ──────────────────────────────────────────────────────────
    private int FindProjectile(GameObject[] pool)
    {
        for (int i = 0; i < pool.Length; i++)
        {
            if (!pool[i].activeInHierarchy)
                return i;
        }
        return 0; // Reuse first if all active
    }
}