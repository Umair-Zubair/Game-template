using System.Collections;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private float aheadDistance = 3f;
    [SerializeField] private float cameraSpeed = 5f;

    private Transform player;
    private float lookAhead;

    private void Start()
    {
        FindPlayer();
        if (player == null)
            StartCoroutine(RetryFindPlayer());
    }

    private void Update()
    {
        if (player == null) return;

        lookAhead = Mathf.Lerp(lookAhead, aheadDistance * player.localScale.x, Time.deltaTime * cameraSpeed);
        transform.position = new Vector3(
            player.position.x + lookAhead,
            transform.position.y,
            transform.position.z
        );
    }

    // Called by CharacterManager or anything else that knows when the player changes
    public void SetPlayer(Transform newPlayer)
    {
        player = newPlayer;
    }

    // Kept for room-camera backwards compatibility (no-op now but won't break old callers)
    public void MoveToNewRoom(Transform _newRoom) { }

    private void FindPlayer()
    {
        GameObject obj = GameObject.FindGameObjectWithTag("Player");
        if (obj != null) player = obj.transform;
    }

    private IEnumerator RetryFindPlayer()
    {
        yield return null; // wait one frame for CharacterManager to spawn the player
        FindPlayer();
    }
}
