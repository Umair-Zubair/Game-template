/// <summary>
/// Minimal state machine for player FSM bots.
/// Identical in structure to BossStateMachine — exits the old state,
/// enters the new state, and drives OnUpdate each frame.
/// </summary>
public class PlayerFSMStateMachine
{
    public IPlayerFSMState CurrentState { get; private set; }

    public void Initialize(IPlayerFSMState startState, PlayerFSMController controller)
    {
        CurrentState = startState;
        startState.OnEnter(controller);
    }

    public void ChangeState(IPlayerFSMState newState, PlayerFSMController controller)
    {
        CurrentState?.OnExit(controller);
        CurrentState = newState;
        newState.OnEnter(controller);
    }

    public void Update(PlayerFSMController controller)
    {
        CurrentState?.OnUpdate(controller);
    }
}
