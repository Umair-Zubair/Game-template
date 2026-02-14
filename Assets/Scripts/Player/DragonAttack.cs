using UnityEngine;

public class DragonAttack : MonoBehaviour
{
    [SerializeField] private float attackCooldown;
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject[] fireballs;
    [SerializeField] private AudioClip fireballSound;

    private Animator anim;

    private PlayerController playerController;
    private PlayerStamina stamina;
    private float cooldownTimer = Mathf.Infinity;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
        stamina = GetComponent<PlayerStamina>();
    }

    private void Update()
    {
        if (Input.GetMouseButton(0) && cooldownTimer > attackCooldown && playerController != null
            && Time.timeScale > 0)
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
        SoundManager.instance.PlaySound(fireballSound);
        anim.SetTrigger("attack");
        cooldownTimer = 0;

        int index = FindFireball();
        GameObject fireball = fireballs[index];
        
        fireball.transform.position = firePoint.position;
        // Ensure your Projectile script is set up to handle the direction
        fireball.GetComponent<Projectile>().SetDirection(Mathf.Sign(transform.localScale.x));
    }
    private int FindFireball()
    {
        for (int i = 0; i < fireballs.Length; i++)
        {
            if (!fireballs[i].activeInHierarchy)
                return i;
        }
        return 0;
    }
}