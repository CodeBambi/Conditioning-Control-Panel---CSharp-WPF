using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// The one place that decides whether the Brain Drain overlay surface is hidden from screen
/// capture, and the one place that pokes <c>SetWindowDisplayAffinity</c> for it.
///
/// WHY THE EXCLUSION EXISTS AT ALL: the brain-drain host is the only compositor surface that is
/// <c>WDA_EXCLUDEFROMCAPTURE</c>'d (<see cref="IWpfLayer.ExcludeFromCapture"/> is true for exactly
/// one layer). Everything else - subliminals, flashes, spiral - is visible in recordings BY DESIGN.
/// The exclusion is belt-and-braces against self-capture feedback: the pump's grab is a plain
/// SRCCOPY off the desktop DC with NO <c>CAPTUREBLT</c>, which already excludes layered windows
/// outright, so dropping the affinity cannot reintroduce the feedback loop (see
/// <see cref="BrainDrainCapturePump"/>'s SELF-CAPTURE note).
///
/// WHY IT IS NOW OPT-OUT: the same flag also hides the effect from OBS, the Game Bar, Discord
/// screen share and PrintScreen, so users could never show the effect off - screenshots came back
/// with the overlay simply missing. <c>AppSettings.AllowOverlayCapture</c> (default FALSE = the
/// historical behaviour) flips this surface to <c>WDA_NONE</c>.
///
/// SCOPE: Brain Drain only. Other capture-excluded windows in the app (the keyword-highlight
/// reader) are a different feature with a different reason and are deliberately untouched.
/// </summary>
internal static class OverlayCaptureAffinity
{
    private const uint WDA_NONE = 0x0000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x0011;

    /// <summary>True when the user has opted in to letting the Brain Drain overlay appear in
    /// screenshots and recordings. Reads settings defensively: this runs from window construction,
    /// which can happen before/after a settings swap (cloud restore) and must never throw.</summary>
    public static bool AllowCapture
    {
        get
        {
            try { return App.Settings?.Current?.AllowOverlayCapture ?? false; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Apply the current affinity to a Brain Drain overlay window. Safe to call repeatedly and on
    /// a handle that does not exist yet (no-op) - it is invoked once at window creation and again
    /// on every live toggle of the setting. Failures are swallowed: the API is unavailable before
    /// Windows 10 2004, where the historical behaviour was already "visible in captures".
    /// </summary>
    public static void Apply(nint hwnd)
    {
        if (hwnd == 0) return;
        try { SetWindowDisplayAffinity(hwnd, AllowCapture ? WDA_NONE : WDA_EXCLUDEFROMCAPTURE); }
        catch (System.Exception ex)
        {
            App.Logger?.Debug("OverlayCaptureAffinity.Apply failed: {Error}", ex.Message);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
