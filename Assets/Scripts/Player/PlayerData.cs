using UnityEngine;

[CreateAssetMenu(fileName = "PlayerData", menuName = "Player/Data")]
public class PlayerData : ScriptableObject
{
    [Header("Movement")]
    public float runSpeed = 8f;
    public float airAcceleration = 50f; // Higher because we use Force or Lerp often, but for velocity set it acts differently. 
                                        // Since we are setting velocity directly in this controller, this might be used for 'smoothing' if needed.
                                        // For direct velocity set, we just use runSpeed.

    [Header("Jump")]
    public float jumpForce = 10f; 
    public float coyoteTime = 0.15f;
    public float jumpBufferTime = 0.1f;
    public float variableJumpMultiplier = 0.5f; 

    [Header("Wall")]
    public float wallSlideSpeed = 3f;  
    public Vector2 wallJumpForce = new Vector2(10f, 14f);
    public float wallJumpInputLockTime = 0.2f;

    [Header("Physics")]
    public float gravityScale = 2.0f; 
    public float fallGravityMultiplier = 1.2f; 
    public float maxFallSpeed = 20f;
}
