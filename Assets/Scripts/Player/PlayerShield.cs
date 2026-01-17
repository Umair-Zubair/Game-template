using UnityEngine;

public class PlayerShield : MonoBehaviour
{
    [Header("Shield Parameters")]
    [SerializeField] private AudioClip blockSound;
    [SerializeField] private AudioClip blockHitSound;

    private Animator anim;
    private PlayerMovement playerMovement;
    private bool isBlocking;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        // HOLD Right Click to block
        if (Input.GetMouseButton(1))
        {
            // Only start blocking if we aren't already
            if (!isBlocking && playerMovement.canAttack())
            {
                StartBlock();
            }
        }
        else
        {
            // Stop blocking when button released
            if (isBlocking)
                StopBlock();
        }
    }

    private void StartBlock()
    {
        isBlocking = true;
        playerMovement.isBlocking = true; // Freeze Movement
        anim.SetBool("block", true);
        if(blockSound != null) SoundManager.instance.PlaySound(blockSound);
    }

    private void StopBlock()
    {
        isBlocking = false;
        playerMovement.isBlocking = false; // Unfreeze Movement
        anim.SetBool("block", false);
    }

    public bool IsBlocking() => isBlocking;

    public void TriggerBlockEffect()
    {
        if(blockHitSound != null) SoundManager.instance.PlaySound(blockHitSound);
    }
}