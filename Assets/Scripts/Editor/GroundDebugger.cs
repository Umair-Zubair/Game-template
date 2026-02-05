using UnityEngine;

/// <summary>
/// DEBUG SCRIPT - Attach to player or boss to diagnose ground collision issues
/// </summary>
public class GroundDebugger : MonoBehaviour
{
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private bool showLogs = true;
    
    private BoxCollider2D boxCollider;
    private Rigidbody2D rb;

    private void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        Debug.Log("=== GROUND DEBUGGER START ===");
        Debug.Log($"GameObject: {gameObject.name}");
        Debug.Log($"Layer: {LayerMask.LayerToName(gameObject.layer)}");
        Debug.Log($"Has BoxCollider2D: {boxCollider != null}");
        Debug.Log($"Has Rigidbody2D: {rb != null}");
        
        if (boxCollider != null)
        {
            Debug.Log($"BoxCollider2D IsTrigger: {boxCollider.isTrigger}");
        }
        
        if (rb != null)
        {
            Debug.Log($"Rigidbody2D BodyType: {rb.bodyType}");
            Debug.Log($"Rigidbody2D GravityScale: {rb.gravityScale}");
        }
        
        Debug.Log($"Ground Layer Mask: {groundLayer.value}");
        Debug.Log("=========================");
    }

    private void FixedUpdate()
    {
        if (!showLogs || boxCollider == null) return;

        // Check for ground
        float extraHeight = 0.15f;
        RaycastHit2D hit = Physics2D.BoxCast(
            boxCollider.bounds.center,
            boxCollider.bounds.size,
            0f,
            Vector2.down,
            extraHeight,
            groundLayer
        );

        if (Time.frameCount % 120 == 0) // Every 2 seconds
        {
            Debug.Log($"[{gameObject.name}] Ground Check: {hit.collider != null}");
            
            if (hit.collider != null)
            {
                Debug.Log($"  -> Hit: {hit.collider.name} on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
            }
            else
            {
                Debug.LogWarning($"  -> NO GROUND DETECTED! Falling...");
                Debug.Log($"  -> Position Y: {transform.position.y}");
                Debug.Log($"  -> Velocity Y: {(rb != null ? rb.linearVelocity.y : 0f)}");
                
                // Check if ANYTHING is below
                RaycastHit2D anyHit = Physics2D.BoxCast(
                    boxCollider.bounds.center,
                    boxCollider.bounds.size,
                    0f,
                    Vector2.down,
                    extraHeight
                    // No layer mask - detect everything
                );
                
                if (anyHit.collider != null)
                {
                    Debug.LogWarning($"  -> Found SOMETHING below but wrong layer: {anyHit.collider.name} (Layer: {LayerMask.LayerToName(anyHit.collider.gameObject.layer)})");
                }
                else
                {
                    Debug.LogError($"  -> NOTHING AT ALL below! Ground might be missing colliders!");
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (boxCollider == null) return;

        // Draw ground check box
        Gizmos.color = Color.red;
        Vector3 boxCenter = boxCollider.bounds.center + Vector3.down * 0.15f;
        Gizmos.DrawWireCube(boxCenter, boxCollider.bounds.size);
    }
}
