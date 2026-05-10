using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject[] playerPrefabs; // Element 0: Hunter, Element 1: Warrior
    [SerializeField] private Transform spawnPoint;

    [Header("Dependencies")]
    [SerializeField] private Healthbar healthBar;
    [SerializeField] private StaminaBar staminaBar;

    [Header("AI Training")]
    [Tooltip("When ON, ignores the player's selection and picks a random character on every spawn/reset.")]
    [SerializeField] private bool randomizeCharacterForAITraining = false;

    [Tooltip("When ON, randomly enables ONE PlayerFSMController on the spawned player and disables the rest. Turn OFF to use whatever FSM enable state is set on the prefab.")]
    [SerializeField] private bool randomizeFSMForAITraining = false;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";
    private const int BLOB_RANGED_INDEX = 0;
    private const int BLOB_MELEE_INDEX = 1;

    private GameObject currentPlayer;

    public bool IsRandomizing => randomizeCharacterForAITraining;
    public bool IsRandomizingFSM => randomizeFSMForAITraining;

    private void Awake() { }

    private void Start()
    {
        SpawnFreshCharacter();
    }

    public void SpawnFreshCharacter()
    {
        if (playerPrefabs == null || playerPrefabs.Length == 0) return;

        if (currentPlayer != null)
        {
            Destroy(currentPlayer);
            currentPlayer = null;
        }

        int selected = randomizeCharacterForAITraining
            ? Random.Range(0, playerPrefabs.Length)
            : PlayerPrefs.GetInt(SELECTED_CHARACTER_KEY, BLOB_RANGED_INDEX);

        SpawnCharacter(selected);
    }

    private void SpawnCharacter(int index)
    {
        if (index < 0 || index >= playerPrefabs.Length) return;

        GameObject prefabToSpawn = playerPrefabs[index];
        if (prefabToSpawn == null) return;

        GameObject newPlayer = Instantiate(prefabToSpawn, spawnPoint.position, Quaternion.identity);
        currentPlayer = newPlayer;

        // Randomize which FSM is active BEFORE other wiring so the FSM's OnEnable
        // (which sets IsAIControlled etc.) fires in a clean state.
        if (randomizeFSMForAITraining)
            RandomizeActiveFSM(newPlayer);

        Health playerHealth = newPlayer.GetComponentInChildren<Health>();

        if (healthBar != null)
        {
            healthBar.SetPlayer(playerHealth);
        }
        else
        {
            var allBars = Object.FindObjectsByType<Healthbar>(FindObjectsSortMode.None);
            foreach (var bar in allBars)
                bar.SetPlayer(playerHealth);
        }

        if (staminaBar != null)
            staminaBar.SetPlayer(newPlayer.GetComponent<PlayerStamina>());

        var restartManager = Object.FindFirstObjectByType<RestartManager>();
        if (restartManager != null)
            restartManager.InitPlayer(newPlayer.GetComponent<PlayerController>());
    }

    private static void RandomizeActiveFSM(GameObject player)
    {
        // includeInactive: true so we see FSMs that are disabled in the prefab.
        var fsms = player.GetComponentsInChildren<PlayerFSMController>(true);
        if (fsms == null || fsms.Length == 0) return;

        // Disable all first to guarantee a clean single-active state regardless of
        // how the prefab was authored.
        foreach (var f in fsms) f.enabled = false;

        int chosen = Random.Range(0, fsms.Length);
        fsms[chosen].enabled = true;

        Debug.Log($"[CharacterManager] Randomized FSM → {fsms[chosen].GetType().Name} (out of {fsms.Length}).");
    }
}