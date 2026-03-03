using UnityEngine;

/// <summary>
/// Interface for all boss states in the FSM.
/// Provides a standard template for boss behaviors.
/// </summary>
public interface IBossState
{
    void OnEnter(BossController boss);
    void OnUpdate(BossController boss);
    void OnFixedUpdate(BossController boss);
    void OnExit(BossController boss);
}
