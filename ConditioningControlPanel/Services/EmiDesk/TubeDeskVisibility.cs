namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>In-memory visibility lease; user dismissal cancels a pending restoration.</summary>
internal sealed class TubeDeskVisibility
{
    public bool Suppressed { get; private set; }
    private bool _restore;
    public (bool Hide, bool Restore) Update(bool suppress, bool visible)
    {
        if (suppress == Suppressed) return (false, false);
        Suppressed = suppress;
        if (suppress) { _restore = visible; return (visible, false); }
        var restore = _restore;
        _restore = false;
        return (false, restore);
    }
    public void Dismiss() => _restore = false;
}
