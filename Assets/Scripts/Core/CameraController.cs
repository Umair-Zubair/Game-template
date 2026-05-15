using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Follow Smoothing")]
    [SerializeField] private float horizontalSmoothing = 0.12f;
    [SerializeField] private float verticalSmoothing   = 0.2f;

    [Header("Look-Ahead")]
    [SerializeField] private float lookAheadDistance = 2.5f;
    [SerializeField] private float lookAheadSpeed    = 4f;

    [Header("Vertical Deadzone")]
    [Tooltip("Camera only moves vertically once the player leaves this band (world units).")]
    [SerializeField] private float verticalDeadzone = 1f;

    [Header("Level Bounds")]
    [Tooltip("Camera will never show outside these world-space limits.")]
    [SerializeField] private bool  useBounds = true;
    [SerializeField] private float minX = -30f;
    [SerializeField] private float maxX =  30f;
    [SerializeField] private float minY = -10f;
    [SerializeField] private float maxY =  20f;

    private Camera    cam;
    private Transform player;

    private float currentLookAhead;
    private float xVel;
    private float yVel;
    private float targetY;
    private bool  yInitialized;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        FindPlayer();
        if (player == null)
            StartCoroutine(RetryFindPlayer());
        else
            SnapToPlayer();
    }

    private void LateUpdate()
    {
        if (player == null) return;

        // ── Look-ahead ──────────────────────────────────────────────────
        float facingSign = player.localScale.x >= 0f ? 1f : -1f;
        currentLookAhead = Mathf.Lerp(
            currentLookAhead,
            lookAheadDistance * facingSign,
            Time.deltaTime * lookAheadSpeed
        );

        // ── Target X ────────────────────────────────────────────────────
        float targetX = player.position.x + currentLookAhead;

        // ── Target Y (deadzone) ──────────────────────────────────────────
        if (!yInitialized)
        {
            targetY      = player.position.y;
            yInitialized = true;
        }
        else
        {
            float diff = player.position.y - targetY;
            if (Mathf.Abs(diff) > verticalDeadzone)
                targetY += diff - Mathf.Sign(diff) * verticalDeadzone;
        }

        // ── Smooth damp ─────────────────────────────────────────────────
        float newX = Mathf.SmoothDamp(transform.position.x, targetX, ref xVel, horizontalSmoothing);
        float newY = Mathf.SmoothDamp(transform.position.y, targetY, ref yVel, verticalSmoothing);

        // ── Clamp to level bounds ────────────────────────────────────────
        if (useBounds)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            newX = Mathf.Clamp(newX, minX + halfW, maxX - halfW);
            newY = Mathf.Clamp(newY, minY + halfH, maxY - halfH);
        }

        transform.position = new Vector3(newX, newY, transform.position.z);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void SetPlayer(Transform newPlayer)
    {
        player       = newPlayer;
        yInitialized = false;
        if (player != null) SnapToPlayer();
    }

    // Kept for backwards compatibility — no-op
    public void MoveToNewRoom(Transform _) { }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void SnapToPlayer()
    {
        if (player == null) return;

        targetY      = player.position.y;
        yInitialized = true;

        float x = player.position.x;
        float y = player.position.y;

        if (useBounds)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            x = Mathf.Clamp(x, minX + halfW, maxX - halfW);
            y = Mathf.Clamp(y, minY + halfH, maxY - halfH);
        }

        transform.position = new Vector3(x, y, transform.position.z);
    }

    private void FindPlayer()
    {
        var obj = GameObject.FindGameObjectWithTag("Player");
        if (obj != null) player = obj.transform;
    }

    private IEnumerator RetryFindPlayer()
    {
        yield return null;
        FindPlayer();
        if (player != null) SnapToPlayer();
    }

    private void OnDrawGizmosSelected()
    {
        if (!useBounds) return;
        Gizmos.color = Color.cyan;
        var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
        var size   = new Vector3(maxX - minX, maxY - minY, 0f);
        Gizmos.DrawWireCube(center, size);
    }
}
