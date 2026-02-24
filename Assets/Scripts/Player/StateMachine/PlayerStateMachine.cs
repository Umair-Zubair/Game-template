using UnityEngine;

public class PlayerStateMachine
{
    public IPlayerState CurrentState { get; private set; }

    public void Initialize(IPlayerState startingState, PlayerController player)
    {
        CurrentState = startingState;
        CurrentState.OnEnter(player);
    }

    public void ChangeState(IPlayerState newState, PlayerController player)
    {
        if (GlobalData.DebugMode)
            Debug.Log($"Transitioning from {CurrentState?.GetType().Name} to {newState.GetType().Name}");

        CurrentState.OnExit(player);
        CurrentState = newState;
        CurrentState.OnEnter(player);
    }

    public void Update(PlayerController player)
    {
        if (CurrentState != null)
            CurrentState.OnUpdate(player);
    }

    public void FixedUpdate(PlayerController player)
    {
        if (CurrentState != null)
            CurrentState.OnFixedUpdate(player);
    }
}

// Simple static helper for optional debugging
public static class GlobalData
{
    public static bool DebugMode = false; 
}
