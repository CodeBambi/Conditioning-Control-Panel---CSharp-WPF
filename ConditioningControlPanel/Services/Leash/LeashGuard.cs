using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>STUB. Owned by the "cutsafety" lane, which replaces this file at merge. App startup
/// sets <see cref="IsLeashed"/>; the remote refusals (no Strict Lock on, no panic off while
/// leashed) read it.</summary>
public static class LeashGuard
{
    public static Func<bool> IsLeashed { get; set; } = () => false;
}
