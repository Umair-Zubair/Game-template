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
        if (playerHealth != null)
        {
            totalhealthBar.fillAmount = playerHealth.currentHealth / playerHealth.MaxHealth;
        }
    }
    // ------------------------------------

    private void Start()
    {
        if (playerHealth == null)
            playerHealth = FindFirstObjectByType<Health>();

        if (playerHealth != null)
            totalhealthBar.fillAmount = playerHealth.currentHealth / playerHealth.MaxHealth;
    }

    private void Update()
    {
        if (playerHealth != null)
            currenthealthBar.fillAmount = playerHealth.currentHealth / playerHealth.MaxHealth;
    }
}