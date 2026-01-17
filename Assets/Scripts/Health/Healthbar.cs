using UnityEngine;
using UnityEngine.UI;

public class Healthbar : MonoBehaviour
{
    [SerializeField] private Health playerHealth;
    [SerializeField] private Image totalhealthBar;
    [SerializeField] private Image currenthealthBar;

    // --- THIS IS THE MISSING FUNCTION ---
    public void SetPlayer(Health _newPlayer)
    {
        playerHealth = _newPlayer;
        // Update the bar immediately so it doesn't show empty
        if (playerHealth != null)
        {
            totalhealthBar.fillAmount = playerHealth.currentHealth / 10;
        }
    }
    // ------------------------------------

    private void Start()
    {
        // Auto-find if missing
        if (playerHealth == null)
            playerHealth = FindObjectOfType<Health>();

        if (playerHealth != null)
            totalhealthBar.fillAmount = playerHealth.currentHealth / 10;
    }

    private void Update()
    {
        if (playerHealth != null)
            currenthealthBar.fillAmount = playerHealth.currentHealth / 10;
    }
}