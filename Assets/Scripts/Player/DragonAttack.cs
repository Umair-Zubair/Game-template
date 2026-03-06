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

    // Hit stun — prevents attacking for a brief period after taking damage
    [Header("Hit Stun")]
    [Tooltip("How long the player is locked out of attacking after being hit.")]
    [SerializeField] private float hitStunDuration = 0.4f;
    private float hitStunTimer;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
        stamina = GetComponent<PlayerStamina>();
    }

    private void Update()
    {
        // Tick down hit stun timer
        if (hitStunTimer > 0)
        {
            hitStunTimer -= Time.deltaTime;
            cooldownTimer += Time.deltaTime;
            return; // Block all attack input while stunned
        }

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
    /// <summary>
    /// Called by Health when the player takes damage.
    /// Interrupts the current attack and locks out attacks briefly.
    /// </summary>
    public void OnHitStun()
    {
        hitStunTimer = hitStunDuration;
        cooldownTimer = 0; // Reset cooldown so they can't instantly attack after stun ends

        // Reset attack animation trigger so it doesn't fire after stun
        if (anim != null)
        {
            anim.ResetTrigger("attack");
        }
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