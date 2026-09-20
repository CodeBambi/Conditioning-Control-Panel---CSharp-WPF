namespace ConditioningControlPanel.Services.Race;

// UI-thread protocol latch: completion consumes the active run before any reward is written.
internal sealed class RaceRunLifecycle
{
    public bool IsActive { get; private set; }
    public bool TryStart()
    {
        if (IsActive) return false;
        IsActive = true;
        return true;
    }
    public bool TryEnd()
    {
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }
    public void Reset() => IsActive = false;
}
