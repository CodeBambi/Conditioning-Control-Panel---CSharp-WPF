using System;
using System.Windows;
using System.Windows.Interop;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// Makes an auto-dismissing corner toast (Pink Rush, achievement, item unlock, announcement)
/// incapable of stealing focus from whatever the user is actually doing.
///
/// <para><c>ShowActivated="False"</c> in XAML is NOT enough. It only suppresses the activation
/// of the *initial* Show(); the window is still a normal activatable top-level window afterwards,
/// so the moment it enters the topmost z-band Windows can hand it the foreground - which yanks
/// mouse capture away from an exclusive/borderless fullscreen game and kills mouse-look
/// (ccp-bugs #1000: a Pink Rush toast breaking Overwatch aiming mid-match).</para>
///
/// <para>The fix is the same two-part treatment the flash overlays already use
/// (see <c>FlashService.HideFromAltTab</c> / <c>FlashService.ForceTopmost</c>):</para>
/// <list type="number">
/// <item><c>WS_EX_NOACTIVATE</c> so the window can never take the foreground, even when clicked -
/// mouse messages still arrive, so click-to-dismiss keeps working.</item>
/// <item><c>WS_EX_TOOLWINDOW</c> so it stays out of Alt+Tab.</item>
/// <item>A <c>SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)</c> re-assert once loaded, so the toast
/// still wins the z-order against other topmost surfaces (e.g. the app's own fullscreen video,
/// which was activated more recently) without activating itself.</item>
/// </list>
/// </summary>
internal static class PassiveToastWindow
{
    /// <summary>
    /// Applies the non-activating treatment to <paramref name="window"/>. Safe to call from the
    /// window's constructor (right after <c>InitializeComponent</c>) - the extended styles are
    /// applied on SourceInitialized and the z-order assert on Loaded. Never throws.
    /// </summary>
    public static void Apply(Window window)
    {
        if (window == null) return;

        // Extended styles must land before the window is first shown, otherwise Windows has
        // already decided it is activatable for this Show().
        window.SourceInitialized += (_, _) => ApplyNoActivateStyles(window);

        // Re-assert the top of the topmost band once we have a visual tree, without activating.
        window.Loaded += (_, _) => ForceToTopMost(window);

        // Defensive: if the caller wires us up after the HWND already exists, do it now too.
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyNoActivateStyles(window);
    }

    private static void ApplyNoActivateStyles(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("PassiveToastWindow: could not apply no-activate styles: {Error}", ex.Message);
        }
    }

    private static void ForceToTopMost(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("PassiveToastWindow: ForceToTopMost failed: {Error}", ex.Message);
        }
    }
}
