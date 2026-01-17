using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    [Header("Characters")]
    [SerializeField] private GameObject[] characterPrefabs; // 0: Dragon, 1: Knight
    [SerializeField] private Transform spawnPoint;

    [Header("Dependencies")]
    [SerializeField] private Healthbar healthBar;


    private bool characterSelected = false;

    private void Update()
    {
        if (!characterSelected)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SpawnCharacter(0); // Press 1
            if (Input.GetKeyDown(KeyCode.Alpha2)) SpawnCharacter(1); // Press 2
        }
    }

    private void SpawnCharacter(int index)
    {
        characterSelected = true;

        // Spawn the new player
        GameObject newPlayer = Instantiate(characterPrefabs[index], spawnPoint.position, Quaternion.identity);

        // 3. Tell the Healthbar to check the new player
        if (healthBar != null) 
            healthBar.SetPlayer(newPlayer.GetComponent<Health>());

    }
}