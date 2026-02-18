using UnityEngine;
using System.Collections.Generic;

public class Health : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float startingHealth;
    public float currentHealth { get; private set; }

    /// <summary>Layer 2 AI: Fired when this object takes damage. Arg = actual damage applied (after shield).</summary>
    public System.Action<float> OnDamageTaken;
    private Animator anim;
    private bool dead;

    [Header("Components")]
    [SerializeField] private Behaviour[] components; 

    private PlayerShield playerShield;
    private PlayerStamina playerStamina;

    private void Awake()
    {
        currentHealth = startingHealth;
        anim = GetComponent<Animator>();
        playerShield = GetComponent<PlayerShield>();
        playerStamina = GetComponent<PlayerStamina>();

        // 1. Auto-Find all scripts so you don't have to drag them
        if (components == null || components.Length == 0)
        {
            List<Behaviour> foundComponents = new List<Behaviour>();
            if (GetComponent<PlayerController>()) foundComponents.Add(GetComponent<PlayerController>());
            if (GetComponent<MeleeAttack>()) foundComponents.Add(GetComponent<MeleeAttack>());
            if (GetComponent<DragonAttack>()) foundComponents.Add(GetComponent<DragonAttack>());
            if (GetComponent<PlayerShield>()) foundComponents.Add(GetComponent<PlayerShield>());
            if (GetComponent<PlayerStamina>()) foundComponents.Add(GetComponent<PlayerStamina>());
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

        if (currentHealth > 0)
        {
            anim.SetTrigger("hurt");
        }
        else
        {
            if (!dead)
            {
                anim.SetTrigger("die");

                // --- THE FIX IS HERE ---
                // We manually force the Block animation OFF immediately
                if (playerShield != null)
                {
                    anim.SetBool("block", false);
                    playerShield.enabled = false; 
                }

                // Disable Movement and Attacks
                foreach (Behaviour component in components)
                    if(component != null) component.enabled = false;

                dead = true;
            }
        }
    }

    public void Respawn()
    {
        dead = false;
        AddHealth(startingHealth);
        anim.ResetTrigger("die");
        anim.Play("Idle");

        // Reset stamina to full on respawn
        if (playerStamina != null)
            playerStamina.ResetStamina();

        foreach (Behaviour component in components)
            if(component != null) component.enabled = true;
    }

    public void AddHealth(float _value)
    {
        currentHealth = Mathf.Clamp(currentHealth + _value, 0, startingHealth);
    }
}