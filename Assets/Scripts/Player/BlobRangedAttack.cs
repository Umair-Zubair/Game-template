using UnityEngine;

public class BlobRangedAttack : MonoBehaviour
{
    [SerializeField] private float attackCooldown;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private int poolSize = 10;
    [SerializeField] private AudioClip projectileSound;

    private Animator anim;
    private PlayerController playerController;
    private PlayerStamina stamina;
    private float cooldownTimer = Mathf.Infinity;
    
    private GameObject[] projectiles;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
        stamina = GetComponent<PlayerStamina>();
        
        // Create projectile pool from prefab
        CreateProjectilePool();
    }

    private void CreateProjectilePool()
    {
        projectiles = new GameObject[poolSize];
        
        for (int i = 0; i < poolSize; i++)
        {
            projectiles[i] = Instantiate(projectilePrefab, transform);
            projectiles[i].name = "Projectile_" + i;
            projectiles[i].SetActive(false);
        }
    }

    private void Update()
    {
        // Ranged attack bound to 'E' key
        if (Input.GetKeyDown(KeyCode.E) && cooldownTimer > attackCooldown && playerController != null)
        {
            // Check stamina before firing
            float cost = stamina != null ? stamina.Data.rangedAttackCost : 0f;
            if (stamina == null || stamina.TryConsume(cost))
                Attack();
        }

        cooldownTimer += Time.deltaTime;
    }

    private void Attack()
    {
        SoundManager.instance.PlaySound(projectileSound);
        anim.SetTrigger("rangeAttack");
        cooldownTimer = 0;

        // Find inactive projectile
        int index = FindProjectile();
        float dir = Mathf.Sign(transform.localScale.x);
        
        Debug.Log($"Firing projectile - Player localScale.x: {transform.localScale.x}, Direction: {dir}");
        
        projectiles[index].transform.position = firePoint.position;
        projectiles[index].GetComponent<Projectile>().SetDirection(dir);
    }

    private int FindProjectile()
    {
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (!projectiles[i].activeInHierarchy)
                return i;
        }
        return 0;
    }
}