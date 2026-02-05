using UnityEngine;

/// <summary>
/// Attach to ANY GameObject to see its layer info and what's around it
/// </summary>
public class LayerDebugger : MonoBehaviour
{
    private void Start()
    {
        Debug.Log("=== LAYER DEBUGGER ===");
        Debug.Log($"GameObject: {gameObject.name}");
        Debug.Log($"Layer NUMBER: {gameObject.layer}");
        Debug.Log($"Layer NAME: {LayerMask.LayerToName(gameObject.layer)}");
        
        // Check colliders
        var collider = GetComponent<Collider2D>();
        if (collider != null)
        {
            Debug.Log($"Has Collider: {collider.GetType().Name}");
            Debug.Log($"Is Trigger: {collider.isTrigger}");
        }
        else
        {
            Debug.LogWarning("NO COLLIDER!");
        }
        
        // Check rigidbody
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            Debug.Log($"Has Rigidbody2D: TRUE");
            Debug.Log($"Body Type: {rb.bodyType}");
            Debug.Log($"Gravity Scale: {rb.gravityScale}");
        }
        
        Debug.Log("=====================");
    }
    
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            Debug.Log("\n=== CHECKING WHAT'S BELOW ME ===");
            
            // Cast down to see what's there
            RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 10f);
            
            if (hit.collider != null)
            {
                Debug.Log($"Found: {hit.collider.name}");
                Debug.Log($"Layer NUMBER: {hit.collider.gameObject.layer}");
                Debug.Log($"Layer NAME: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
                Debug.Log($"Distance: {hit.distance}");
            }
            else
            {
                Debug.LogWarning("NOTHING below!");
            }
            
            Debug.Log("================================\n");
        }
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, Vector3.down * 10f);
    }
}
