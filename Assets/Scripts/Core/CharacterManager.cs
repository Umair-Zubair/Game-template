using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    [Header("Characters")]
    [SerializeField] private GameObject[] characterPrefabs; // 0: Dragon, 1: Knight
    [SerializeField] private Transform spawnPoint;

    [Header("Dependencies")]
    [SerializeField] private Healthbar healthBar;

    private const string SELECTED_CHARACTER_KEY = "SelectedCharacter";

    private void Start()
    {
        // Read selected character from PlayerPrefs (set by Main Menu)
        // Default to Dragon (0) if no selection was made
        int selectedCharacter = PlayerPrefs.GetInt(SELECTED_CHARACTER_KEY, 0);
        Debug.Log($"CharacterManager: Spawning character at index {selectedCharacter} (0=Dragon, 1=Knight)");
        SpawnCharacter(selectedCharacter);
    }

    private void SpawnCharacter(int index)
    {
        // Spawn the new player
        GameObject newPlayer = Instantiate(characterPrefabs[index], spawnPoint.position, Quaternion.identity);

        // 3. Tell the Healthbar to check the new player
        if (healthBar != null) 
            healthBar.SetPlayer(newPlayer.GetComponent<Health>());

    }
}