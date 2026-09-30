using System;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>Where the Chaster chip on the tab sends the player, and the short time it shows.
/// Pure so the url rule is tested: the lock id comes off the API and is only ever used as one
/// hex path segment, never pasted in whole.</summary>
public static class ChasterWebLinks
{
    public const string Home = "https://chaster.app";

    /// <summary>The lock's own page on chaster.app, or the home page (where a lock is made)
    /// when there is no lock or the id is not a plain Chaster id.</summary>
    public static string For(LockSnapshot? snapshot)
    {
        var id = snapshot?.Id;
        if (string.IsNullOrEmpty(id) || id.Length > 64 || !id.All(Uri.IsHexDigit)) return Home;
        return $"{Home}/locks/{id}";
    }

    /// <summary>Time left as the chip reads it: "3d 4h", "4h 12m", "12m", "0m".</summary>
    public static string Short(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d {left.Hours}h";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h {left.Minutes}m";
        return $"{left.Minutes}m";
    }
}
