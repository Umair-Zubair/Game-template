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

    [Header("Ammo & Reload")]
    [Tooltip("Shots before a reload is required. Set to 0 for unlimited ammo.")]
    [SerializeField] private int maxAmmo = 3;
    [Tooltip("Seconds to reload after the magazine is empty.")]
    [SerializeField] private float reloadTime = 2f;
    [SerializeField] private AudioClip reloadSound;

    /// <summary>Fired on every attack. Arg = attack type ("ranged" or "jumpAttack"). Consumed by PlayerBehaviorTracker.</summary>
    public System.Action<string> OnAttackPerformed;

    private Animator anim;
    private PlayerController playerController;
    private PlayerStamina stamina;
    private float cooldownTimer = Mathf.Infinity;

    private int currentAmmo;
    private float reloadTimer;
    private bool isReloading;

    [Header("Hit Stun")]
    [Tooltip("How long the player is locked out of attacking after being hit.")]
    [SerializeField] private float hitStunDuration = 0.4f;
    private float hitStunTimer;

    private GameObject[] projectiles;
    private GameObject[] jumpProjectiles;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
        stamina = GetComponent<PlayerStamina>();

        currentAmmo = maxAmmo;
        CreateProjectilePool();
    }

    private void OnEnable()
    {
        if (projectiles == null) CreateProjectilePool();
        currentAmmo = maxAmmo;
        isReloading = false;
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
        if (hitStunTimer > 0)
        {
            hitStunTimer -= Time.deltaTime;
            cooldownTimer += Time.deltaTime;
            return;
        }

        // Reload tick
        if (isReloading)
        {
            reloadTimer -= Time.deltaTime;
            if (reloadTimer <= 0f)
            {
                currentAmmo = maxAmmo;
                isReloading = false;
            }
            return;
        }

        bool hasAmmo = maxAmmo <= 0 || currentAmmo > 0;

        if (Input.GetMouseButtonDown(0) && hasAmmo && cooldownTimer > attackCooldown
            && playerController != null && Time.timeScale > 0)
        {
            bool isGrounded = playerController.IsGrounded();

            if (isGrounded)
            {
                float cost = stamina != null ? stamina.Data.rangedAttackCost : 0f;
                if (stamina == null || stamina.TryConsume(cost))
                    Attack();
            }
            else
            {
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
        OnAttackPerformed?.Invoke("ranged");

        int index = FindProjectile(projectiles);
        float dir = Mathf.Sign(transform.localScale.x);

        projectiles[index].transform.position = firePoint.position;
        projectiles[index].GetComponent<Projectile>().SetDirection(dir);

        ConsumeAmmo();
    }

    // ── Air Jump Range Attack ────────────────────────────────────────────────
    private void JumpRangeAttack()
    {
        if (jumpProjectiles == null)
        {
            Debug.LogWarning("BlobRangedAttack: jumpProjectilePrefab not assigned! Falling back to normal projectile.");
            Attack();
            return;
        }

        if (jumpProjectileSound != null)
            SoundManager.instance.PlaySound(jumpProjectileSound);
        else if (projectileSound != null)
            SoundManager.instance.PlaySound(projectileSound);

        anim.SetTrigger("jumpRange");
        cooldownTimer = 0;
        OnAttackPerformed?.Invoke("jumpAttack");

        int index = FindProjectile(jumpProjectiles);
        float dir = Mathf.Sign(transform.localScale.x);

        jumpProjectiles[index].transform.position = firePoint.position;
        jumpProjectiles[index].GetComponent<Projectile>().SetDirection(dir);

        ConsumeAmmo();
    }

    private void ConsumeAmmo()
    {
        if (maxAmmo <= 0) return;

        currentAmmo--;
        if (currentAmmo <= 0)
        {
            isReloading = true;
            reloadTimer = reloadTime;
            if (reloadSound != null)
                SoundManager.instance.PlaySound(reloadSound);
        }
    }

    /// <summary>
    /// Called by Health when the player takes damage.
    /// Interrupts the current attack and locks out attacks briefly.
    /// </summary>
    public void OnHitStun()
    {
        hitStunTimer = hitStunDuration;
        cooldownTimer = 0; // Reset cooldown so they can't instantly attack after stun ends

        // Reset attack animation triggers so they don't fire after stun
        if (anim != null)
        {
            anim.ResetTrigger("rangeAttack");
            anim.ResetTrigger("jumpRange");
        }
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