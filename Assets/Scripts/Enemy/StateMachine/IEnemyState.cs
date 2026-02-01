using UnityEngine;

/// <summary>
/// Interface for all enemy states in the FSM.
/// Mirrors IPlayerState pattern for consistency.
/// </summary>
public interface IEnemyState
{
    void OnEnter(EnemyController enemy);
    void OnUpdate(EnemyController enemy);
    void OnFixedUpdate(EnemyController enemy);
    void OnExit(EnemyController enemy);
}
