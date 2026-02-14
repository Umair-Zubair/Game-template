using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    [Header("Characters")]
    [SerializeField] private GameObject[] characterPrefabs; // 0: Blob Ranged, 1: Blob Melee
    [SerializeField] private Transform spawnPoint;

    [Header("Dependencies")]
    [SerializeField] private Healthbar healthBar;
    [SerializeField] private StaminaBar staminaBar;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";

    private void Start()
    {
        // Read selected character from PlayerPrefs (set by Main Menu)
        // Default to Blob Ranged (0) if no selection was made
        int selectedCharacter = PlayerPrefs.GetInt(SELECTED_CHARACTER_KEY, 0);
        Debug.Log($"CharacterManager: Spawning character at index {selectedCharacter} (0=Blob Ranged, 1=Blob Melee)");
        SpawnCharacter(selectedCharacter);
    }

    private void SpawnCharacter(int index)
    {
        // Spawn the new player
        GameObject newPlayer = Instantiate(characterPrefabs[index], spawnPoint.position, Quaternion.identity);

        // 3. Tell the Healthbar to check the new player
        if (healthBar != null) 
            healthBar.SetPlayer(newPlayer.GetComponent<Health>());

        // 4. Tell the StaminaBar to check the new player
        if (staminaBar != null)
            staminaBar.SetPlayer(newPlayer.GetComponent<PlayerStamina>());
    }
}