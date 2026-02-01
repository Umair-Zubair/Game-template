using UnityEngine;

/// <summary>
/// State machine for enemy AI. Handles state transitions and updates.
/// Mirrors PlayerStateMachine pattern for consistency.
/// </summary>
public class EnemyStateMachine
{
    public IEnemyState CurrentState { get; private set; }

    public void Initialize(IEnemyState startingState, EnemyController enemy)
    {
        CurrentState = startingState;
        CurrentState.OnEnter(enemy);
    }

    public void ChangeState(IEnemyState newState, EnemyController enemy)
    {
        if (enemy.DebugMode)
            Debug.Log($"[Enemy FSM] {CurrentState?.GetType().Name} → {newState.GetType().Name}");

        CurrentState?.OnExit(enemy);
        CurrentState = newState;
        CurrentState.OnEnter(enemy);
    }

    public void Update(EnemyController enemy)
    {
        CurrentState?.OnUpdate(enemy);
    }

    public void FixedUpdate(EnemyController enemy)
    {
        CurrentState?.OnFixedUpdate(enemy);
    }
}
