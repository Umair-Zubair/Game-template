using UnityEngine;

public interface IPlayerState
{
    void OnEnter(PlayerController player);
    void OnUpdate(PlayerController player);
    void OnFixedUpdate(PlayerController player);
    void OnExit(PlayerController player);
}
