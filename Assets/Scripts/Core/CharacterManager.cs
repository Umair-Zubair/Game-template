using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject playerPrefab; // The one Blob prefab with both attack scripts
    [SerializeField] private Transform spawnPoint;

    [Header("Dependencies")]
    [SerializeField] private Healthbar healthBar;
    [SerializeField] private StaminaBar staminaBar;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";
    private const int BLOB_RANGED_INDEX = 0;
    private const int BLOB_MELEE_INDEX = 1;

    private void Start()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("CharacterManager: playerPrefab is not assigned in the Inspector!");
            return;
        }

        if (!PlayerPrefs.HasKey(SELECTED_CHARACTER_KEY))
            Debug.LogWarning("CharacterManager: No character selection found in PlayerPrefs! Defaulting to Blob Ranged. Did you select a character in the main menu?");

        int selected = PlayerPrefs.GetInt(SELECTED_CHARACTER_KEY, BLOB_RANGED_INDEX);
        Debug.Log($"CharacterManager: Selected index = {selected} (0=Ranged, 1=Melee)");

        SpawnCharacter(selected);
    }

    private void SpawnCharacter(int index)
    {
        GameObject newPlayer = Instantiate(playerPrefab, spawnPoint.position, Quaternion.identity);
        Debug.Log($"CharacterManager: Spawned '{newPlayer.name}'");

        MeleeAttack melee    = newPlayer.GetComponent<MeleeAttack>();
        BlobRangedAttack ranged = newPlayer.GetComponent<BlobRangedAttack>();

        Debug.Log($"CharacterManager: MeleeAttack found = {melee != null} | BlobRangedAttack found = {ranged != null}");

        // Disable both first
        if (melee  != null) melee.enabled  = false;
        if (ranged != null) ranged.enabled = false;

        // Enable only the selected one
        if (index == BLOB_MELEE_INDEX)
        {
            if (melee != null)
            {
                melee.enabled = true;
                Debug.Log("CharacterManager: MeleeAttack ENABLED, BlobRangedAttack DISABLED.");
            }
            else
                Debug.LogError("CharacterManager: MeleeAttack not found on prefab!");
        }
        else // BLOB_RANGED_INDEX
        {
            if (ranged != null)
            {
                ranged.enabled = true;
                Debug.Log("CharacterManager: BlobRangedAttack ENABLED, MeleeAttack DISABLED.");
            }
            else
                Debug.LogError("CharacterManager: BlobRangedAttack not found on prefab!");
        }

        // Hook up UI
        if (healthBar != null)
            healthBar.SetPlayer(newPlayer.GetComponent<Health>());
        else
            Debug.LogWarning("CharacterManager: Healthbar not assigned.");

        if (staminaBar != null)
            staminaBar.SetPlayer(newPlayer.GetComponent<PlayerStamina>());
        else
            Debug.LogWarning("CharacterManager: StaminaBar not assigned.");
    }
}