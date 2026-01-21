using UnityEngine;

// --- IDLE STATE ---
public class IdleState : IPlayerState
{
    public void OnEnter(PlayerController player)
    {
        player.SetVelocityX(0);
        player.Anim.SetBool("run", false);
        player.Anim.SetBool("grounded", true);
    }

    public void OnUpdate(PlayerController player)
    {
        if (player.JumpBufferCounter > 0 && player.IsGrounded())
        {
            player.StateMachine.ChangeState(player.JumpState, player);
            return;
        }

        if (Mathf.Abs(player.HorizontalInput) > 0.01f)
        {
            player.StateMachine.ChangeState(player.RunState, player);
            return;
        }

        if (!player.IsGrounded())
        {
            player.StateMachine.ChangeState(player.FallState, player);
        }
    }

    public void OnFixedUpdate(PlayerController player) { }
    public void OnExit(PlayerController player) { }
}

// --- RUN STATE ---
public class RunState : IPlayerState
{
    public void OnEnter(PlayerController player)
    {
        player.Anim.SetBool("run", true);
        player.Anim.SetBool("grounded", true);
    }

    public void OnUpdate(PlayerController player)
    {
        player.CheckFlip();

        if (player.JumpBufferCounter > 0 && player.IsGrounded())
        {
            player.StateMachine.ChangeState(player.JumpState, player);
            return;
        }

        if (Mathf.Abs(player.HorizontalInput) < 0.01f)
        {
            player.StateMachine.ChangeState(player.IdleState, player);
            return;
        }

        if (!player.IsGrounded())
        {
            player.StateMachine.ChangeState(player.FallState, player);
        }
    }

    public void OnFixedUpdate(PlayerController player)
    {
        player.SetVelocityX(player.HorizontalInput * player.Data.runSpeed);
    }

    public void OnExit(PlayerController player) { }
}

// --- JUMP STATE ---
public class JumpState : IPlayerState
{
    public void OnEnter(PlayerController player)
    {
        player.SetVelocityY(player.Data.jumpForce);
        player.Anim.SetTrigger("jump");
        player.Anim.SetBool("grounded", false);
        player.JumpBufferCounter = 0; // Consume buffer
        player.CoyoteTimeCounter = 0; // Consume coyote
    }

    public void OnUpdate(PlayerController player)
    {
        player.CheckFlip();

        if (player.RB.linearVelocity.y < 0)
        {
            player.StateMachine.ChangeState(player.FallState, player);
        }
    }

    public void OnFixedUpdate(PlayerController player)
    {
         player.SetVelocityX(player.HorizontalInput * player.Data.runSpeed);
    }

    public void OnExit(PlayerController player) { }
}

// --- FALL STATE ---
public class FallState : IPlayerState
{
    public void OnEnter(PlayerController player)
    {
        player.Anim.SetBool("grounded", false);
    }

    public void OnUpdate(PlayerController player)
    {
        player.CheckFlip();

        // Coyote Jump
        if (player.JumpBufferCounter > 0 && player.CoyoteTimeCounter > 0)
        {
            player.StateMachine.ChangeState(player.JumpState, player);
            return;
        }

        if (player.IsGrounded())
        {
             player.StateMachine.ChangeState(player.IdleState, player);
             return;
        }

        if (player.IsTouchingWall() && player.IsPushingAgainstWall())
        {
             player.StateMachine.ChangeState(player.WallSlideState, player);
        }
    }

    public void OnFixedUpdate(PlayerController player)
    {
         player.SetVelocityX(player.HorizontalInput * player.Data.runSpeed);
         
         // Clamp Fall Speed
         if(player.RB.linearVelocity.y < -player.Data.maxFallSpeed)
         {
             player.SetVelocityY(-player.Data.maxFallSpeed);
         }
    }

    public void OnExit(PlayerController player) { }
}

// --- WALL SLIDE STATE ---
public class WallSlideState : IPlayerState
{
    public void OnEnter(PlayerController player)
    {
        player.Anim.SetBool("grounded", false);
        // player.Anim.SetBool("wallSlide", true); // Assuming you add this later
    }

    public void OnUpdate(PlayerController player)
    {
        if (player.JumpBufferCounter > 0)
        {
            player.StateMachine.ChangeState(player.WallJumpState, player);
            return;
        }

        // If player moves AWAY from wall or wall ends
        if (player.HorizontalInput != player.FacingDirection || !player.IsTouchingWall())
        {
             player.StateMachine.ChangeState(player.FallState, player);
             return;
        }

        if (player.IsGrounded())
        {
            player.StateMachine.ChangeState(player.IdleState, player);
        }
    }

    public void OnFixedUpdate(PlayerController player)
    {
        player.RB.gravityScale = 0;
        player.SetVelocityY(-player.Data.wallSlideSpeed);
    }

    public void OnExit(PlayerController player) 
    {
        player.RB.gravityScale = player.Data.gravityScale;
        // player.Anim.SetBool("wallSlide", false);
    }
}

// --- WALL JUMP STATE ---
public class WallJumpState : IPlayerState
{
    public void OnEnter(PlayerController player)
    {
        player.JumpBufferCounter = 0;
        
        // Push AWAY from wall
        int wallDir = player.FacingDirection;
        Vector2 force = new Vector2(-wallDir * player.Data.wallJumpForce.x, player.Data.wallJumpForce.y);
        
        // Force velocity immediately for snappiness
        player.RB.linearVelocity = force;
        
        // Flip sprite to face jump direction (away from wall)
        // wallDir is the direction of the wall (e.g. 1 if wall is Right)
        // We jump Left (-1). So we want to face Left (-1).
        // So we face -wallDir.
        player.ForceFlip(-wallDir); 
        
        player.WallJumpLockCounter = player.Data.wallJumpInputLockTime;
        player.Anim.SetTrigger("jump");
    }

    public void OnUpdate(PlayerController player)
    {
        // Transition to fall when moving down
        if (player.RB.linearVelocity.y < 0)
        {
            player.StateMachine.ChangeState(player.FallState, player);
        }
    }

    public void OnFixedUpdate(PlayerController player) 
    {
        // Allow air control after the lock expires
        if (player.WallJumpLockCounter <= 0)
        {
             player.SetVelocityX(player.HorizontalInput * player.Data.runSpeed);
        }
    }

    public void OnExit(PlayerController player) { }
}
