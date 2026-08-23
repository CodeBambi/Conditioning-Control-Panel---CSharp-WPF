using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Dev;

/// <summary>
/// Dev rig for the EMERGENCY EXIT friction door (launched via
/// <c>ConditioningControlPanel.exe --emergency-exit-preview &lt;game&gt;</c>).
///
/// <para>It opens the REAL host window against the REAL page - same
/// <see cref="ChaosWebViewHost"/>, same ccp.game / ccp.assets mappings, same protocol - with a
/// synthetic <c>init</c> and <b>no lockdown running</b>. The verdict is still rolled and logged, but
/// <see cref="Services.EmergencyExit.EmergencyExitHostService"/> refuses to apply it in preview
/// mode, so nothing here can deactivate or restart a timer.</para>
///
/// <para>That gate is the whole reason the rig exists in this shape. The sibling
/// <see cref="PossessionPreview"/> follows the same rule for the same reason: a verification run is
/// unattended, and a real lockdown is deliberately hard to get out of. NEVER arm one from a rig.</para>
///
/// <para>Dead code in every normal launch - nothing reaches it without the argument.</para>
/// </summary>
internal static class EmergencyExitPreview
{
    private static readonly string[] Games = { "labyrinth", "password", "jigsaw", "captcha" };

    /// <summary>Pull the game id out of the command line: the token after the flag, unless it is
    /// another flag. Anything unrecognised falls back to the labyrinth, which is the one game with a
    /// deterministic verdict and therefore the best default for a smoke run.</summary>
    public static string ResolveGame(string[] args)
    {
        var idx = Array.IndexOf(args, "--emergency-exit-preview");
        var raw = idx >= 0 && idx + 1 < args.Length && !args[idx + 1].StartsWith("--")
            ? args[idx + 1].Trim().ToLowerInvariant()
            : "";
        return Games.Contains(raw) ? raw : "labyrinth";
    }

    /// <summary>Open the preview once the shell is arranged. Deferred to Loaded for the same reason
    /// the other rigs defer: MainWindow is the owner this window glues itself above, and gluing to a
    /// window that has no HWND yet silently does nothing.</summary>
    public static void Run(Window window, string game)
    {
        if (window.IsLoaded) Launch(game);
        else window.Loaded += (_, _) => Launch(game);
    }

    private static void Launch(string game)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;

        // A short settle before the window goes up: MainWindow's own startup work (and any card it
        // still wants to show) otherwise races the WebView2 environment creation for the UI thread.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                App.Logger?.Information("EmergencyExitPreview: opening '{Game}' (verdicts will NOT be applied)", game);
                Services.EmergencyExit.EmergencyExitHostService.OpenPreview(game);
            }
            catch (Exception ex) { App.Logger?.Error(ex, "EmergencyExitPreview failed"); }
        };
        timer.Start();
    }
}
