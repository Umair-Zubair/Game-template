using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Health : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float startingHealth;
    public float currentHealth { get; private set; }

    /// <summary>Maximum health (used by AIDecisionEngine for normalization).</summary>
    public float MaxHealth => startingHealth;

    /// <summary>Layer 2 AI: Fired when this object takes damage. Arg = actual damage applied (after shield).</summary>
    public System.Action<float> OnDamageTaken;
    private Animator anim;
    private bool dead;

    [Header("Components")]
    [SerializeField] private Behaviour[] components; 

    private PlayerShield playerShield;
    private PlayerStamina playerStamina;

    // Attack scripts (auto-found) — used to interrupt attacks on hit
    private MeleeAttack meleeAttack;
    private DragonAttack dragonAttack;
    private BlobRangedAttack blobRangedAttack;

    // Boss controller — used to stop the state machine on death
    private BossController bossController;
    private Rigidbody2D rb;
    private float originalGravityScale;

    private void Awake()
    {
        currentHealth = startingHealth;
        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        if (rb != null) originalGravityScale = rb.gravityScale;
        playerShield = GetComponent<PlayerShield>();
        playerStamina = GetComponent<PlayerStamina>();
        meleeAttack = GetComponent<MeleeAttack>();
        dragonAttack = GetComponent<DragonAttack>();
        blobRangedAttack = GetComponent<BlobRangedAttack>();
        bossController = GetComponent<BossController>();

        // 1. Auto-Find all scripts so you don't have to drag them
        if (components == null || components.Length == 0)
        {
            List<Behaviour> foundComponents = new List<Behaviour>();
            // Player scripts
            if (GetComponent<PlayerController>()) foundComponents.Add(GetComponent<PlayerController>());
            if (GetComponent<MeleeAttack>()) foundComponents.Add(GetComponent<MeleeAttack>());
            if (GetComponent<DragonAttack>()) foundComponents.Add(GetComponent<DragonAttack>());
            if (GetComponent<PlayerShield>()) foundComponents.Add(GetComponent<PlayerShield>());
            if (GetComponent<PlayerStamina>()) foundComponents.Add(GetComponent<PlayerStamina>());
            // Boss scripts — disabling stops the state machine so the boss can't act after death
            if (GetComponent<BossController>()) foundComponents.Add(GetComponent<BossController>());
            components = foundComponents.ToArray();
        }
    }

    public void TakeDamage(float _damage)
    {
        // 2. Shield Logic
        if (playerShield != null && playerShield.IsBlocking())
        {
            playerShield.TriggerBlockEffect();
            _damage = _damage / 2;
        }

        // 3. Apply Damage
        currentHealth = Mathf.Clamp(currentHealth - _damage, 0, startingHealth);
        OnDamageTaken?.Invoke(_damage); // Layer 2: notify trackers

        // 4. Hit Stun — interrupt current attack and lock out new attacks briefly
        if (meleeAttack != null) meleeAttack.OnHitStun();
        if (dragonAttack != null) dragonAttack.OnHitStun();
        if (blobRangedAttack != null) blobRangedAttack.OnHitStun();

        if (currentHealth > 0)
        {
            anim.SetTrigger("hurt");
        }
        else
        {
            if (!dead)
            {
                dead = true;

                // Freeze physics immediately so the character can't drift or fall
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.gravityScale = 0f;
                }

                // Disable all movement/attack components BEFORE triggering animation
                foreach (Behaviour component in components)
                    if (component != null) component.enabled = false;

                anim.SetTrigger("die");

                if (bossController != null) bossController.OnDeath();

                if (playerShield != null)
                {
                    anim.SetBool("block", false);
                    playerShield.enabled = false;
                }

                StartCoroutine(DeactivateAfterDeath());
            }
        }
    }

    public void Respawn()
    {
        StopAllCoroutines();

        dead = false;
        AddHealth(startingHealth);
        anim.Rebind();
        anim.Update(0f);

        if (rb != null)
            rb.gravityScale = originalGravityScale;

        if (playerStamina != null)
            playerStamina.ResetStamina();

        foreach (Behaviour component in components)
            if(component != null) component.enabled = true;
    }

    private IEnumerator DeactivateAfterDeath()
    {
        // Wait until the animator has transitioned into the death state.
        // Different characters use "Die" or "Death" as the state name.
        float timeout = 1f;
        float waited = 0f;
        while (waited < timeout)
        {
            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Die") || info.IsName("Death")
                || info.IsName("Death_Gun") || info.IsName("Death_CrossBow") || info.IsName("Death_Shotgun")
                || info.IsName("Air-Death_Gun") || info.IsName("Air-Death_CrossBow") || info.IsName("Death_Air_Shotgun")
                || info.IsName("Death NoEffect"))
            {
                break;
            }
            waited += Time.deltaTime;
            yield return null;
        }

        // Now wait for the full clip to finish playing
        float clipLength = anim.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(clipLength);
        gameObject.SetActive(false);
    }

    public void AddHealth(float _value)
    {
        currentHealth = Mathf.Clamp(currentHealth + _value, 0, startingHealth);
    }
}