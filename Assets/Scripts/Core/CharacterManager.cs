using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject[] playerPrefabs; // Element 0: Hunter, Element 1: Warrior
    [SerializeField] private Transform spawnPoint;

    [Header("Dependencies")]
    [SerializeField] private Healthbar healthBar;
    [SerializeField] private StaminaBar staminaBar;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";
    private const int BLOB_RANGED_INDEX = 0;
    private const int BLOB_MELEE_INDEX = 1;

    private void Awake()
    {
    }

    private void Start()
    {
        if (playerPrefabs == null || playerPrefabs.Length == 0) return;

        int selected = PlayerPrefs.GetInt(SELECTED_CHARACTER_KEY, BLOB_RANGED_INDEX);
        SpawnCharacter(selected);
    }

    private void SpawnCharacter(int index)
    {
        if (index < 0 || index >= playerPrefabs.Length) return;

        GameObject prefabToSpawn = playerPrefabs[index];
        if (prefabToSpawn == null) return;

        GameObject newPlayer = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity);

        // We use GetComponentInChildren just in case the Health script is on a child object
        Health playerHealth = newPlayer.GetComponentInChildren<Health>();

        // Ensure the UI follows the player
        if (healthBar != null)
        {
            healthBar.SetPlayer(playerHealth);
        }
        else
        {
            // If No direct reference, find all bars and force them to the player.
            // This stops them from accidentally 'finding' the Boss health instead of yours.
            var allBars = Object.FindObjectsByType<Healthbar>(FindObjectsSortMode.None);
            foreach (var bar in allBars)
                bar.SetPlayer(playerHealth);
        }

        if (staminaBar != null)
            staminaBar.SetPlayer(newPlayer.GetComponent<PlayerStamina>());

        // Tell RestartManager about the freshly-spawned player so it can subscribe to its
        // Health.OnDamageTaken event — this is necessary because RestartManager.Start()
        // may run before CharacterManager.Start() instantiates the player.
        var restartManager = Object.FindFirstObjectByType<RestartManager>();
        if (restartManager != null)
            restartManager.InitPlayer(newPlayer.GetComponent<PlayerController>());
    }
}