namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Shared screen-selection helpers for effect surfaces.
/// </summary>
public static class ScreenProviderExtensions
{
    /// <summary>
    /// The set of monitors effects are allowed to appear on. WPF parity: every effect
    /// surface (pink filter, spiral, brain drain, subliminals, flashes, bouncing text,
    /// bubbles) gates its screen set on <c>AppSettings.DualMonitorEnabled</c> and confines
    /// itself to the primary monitor when the setting is off (e.g. OverlayService.cs:753,
    /// FlashService.cs:1632, BubbleService.cs:855). Centralized here so the compositor
    /// engine and every spawn site pick from the same list.
    /// </summary>
    /// <param name="provider">Screen enumerator.</param>
    /// <param name="dualMonitorEnabled">
    /// <c>AppSettings.DualMonitorEnabled</c>: true = all monitors, false = primary only.
    /// </param>
    /// <returns>
    /// All screens when <paramref name="dualMonitorEnabled"/> is true; otherwise a
    /// single-element list containing the primary screen (falling back to the first
    /// enumerated screen when no primary is reported). Empty when no screens exist.
    /// </returns>
    public static IReadOnlyList<ScreenInfo> GetEffectScreens(this IScreenProvider provider, bool dualMonitorEnabled)
    {
        var all = provider.GetAllScreens();
        if (all.Count == 0) return all;
        if (dualMonitorEnabled) return all;

        var primary = provider.GetPrimaryScreen() ?? all[0];
        return new[] { primary };
    }
}
