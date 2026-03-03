using UnityEngine;

/// <summary>
/// State Machine that handles transitioning between different IBossState implementations.
/// </summary>
public class BossStateMachine
{
    public IBossState CurrentState { get; private set; }

    public void Initialize(IBossState startingState, BossController boss)
    {
        CurrentState = startingState;
        CurrentState.OnEnter(boss);
    }

    public void ChangeState(IBossState newState, BossController boss)
    {
        if (CurrentState == newState) return;

        CurrentState?.OnExit(boss);
        CurrentState = newState;
        CurrentState.OnEnter(boss);
    }

    public void Update(BossController boss)
    {
        CurrentState?.OnUpdate(boss);
    }

    public void FixedUpdate(BossController boss)
    {
        CurrentState?.OnFixedUpdate(boss);
    }
}
