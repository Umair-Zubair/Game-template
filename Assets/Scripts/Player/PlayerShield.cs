using UnityEngine;

public class PlayerShield : MonoBehaviour
{
    [Header("Shield Parameters")]
    [SerializeField] private AudioClip blockSound;
    [SerializeField] private AudioClip blockHitSound;

    private Animator anim;
    private PlayerController playerController;
    private bool isBlocking;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        // HOLD Right Click to block
        if (Input.GetMouseButton(1))
        {
            // Only start blocking if we aren't already
            if (!isBlocking && playerController != null)
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
        // playerController.isBlocking = true; // TODO: Add blocking state to controller if needed
        anim.SetBool("block", true);
        if(blockSound != null) SoundManager.instance.PlaySound(blockSound);
    }

    private void StopBlock()
    {
        isBlocking = false;
        // playerController.isBlocking = false; // Unfreeze Movement
        anim.SetBool("block", false);
    }

    public bool IsBlocking() => isBlocking;

    public void TriggerBlockEffect()
    {
        if(blockHitSound != null) SoundManager.instance.PlaySound(blockHitSound);
    }
}