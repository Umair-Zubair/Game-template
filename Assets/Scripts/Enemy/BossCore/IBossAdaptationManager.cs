/// <summary>
/// Common interface for all boss adaptation managers.
/// Allows BossAIDebugHUD to read the current player style without knowing
/// which specific adaptation manager is attached.
/// </summary>
public interface IBossAdaptationManager
{
    PlayerStyle CurrentStyle { get; }
}
