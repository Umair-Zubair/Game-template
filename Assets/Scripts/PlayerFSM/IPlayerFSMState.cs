/// <summary>
/// Interface for all player FSM bot states.
/// Mirrors IBossState but typed to PlayerFSMController.
///
/// Each state receives the controller on every call so it can
/// read components, query helpers, and drive AI inputs without
/// needing to cache a reference on construction.
/// </summary>
public interface IPlayerFSMState
{
    void OnEnter(PlayerFSMController controller);
    void OnUpdate(PlayerFSMController controller);
    void OnExit(PlayerFSMController controller);
}
