using System;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The launcher's lifecycle, kept apart from its looks.
///
/// The launcher is a picker window, not a second app: it has no tray presence and no state worth
/// keeping alive. The panel keeps its tray and its close-to-tray behaviour unchanged. What this
/// class owns is the handful of transitions between the three surfaces:
/// <list type="bullet">
/// <item><see cref="Show"/>: bring the launcher up (creating it on first use).</item>
/// <item><see cref="OpenPanel"/>: hide the launcher, show the panel from the tray.</item>
/// <item><see cref="BackToLauncher"/>: the panel's "back to client" button. Tucks the panel into
/// the tray with the engine still running and brings the launcher forward.</item>
/// <item><see cref="LaunchGame"/>: hide the launcher, start the game, and poll its host until the
/// window is gone, then come back.</item>
/// <item><see cref="RequestClose"/>: the launcher's own close button. Hides when something is
/// still running behind it, exits the process when nothing is.</item>
/// </list>
/// The window itself is supplied by the partial <see cref="CreateWindow"/>, implemented next to
/// the XAML, so this file compiles with or without the UI.
/// </summary>
public static partial class LauncherHost
{
    private static Window? _window;
    private static DispatcherTimer? _returnPoll;
    private static LauncherEntry? _awaiting;

    /// <summary>Raised on the UI thread whenever the launcher's visibility changes.</summary>
    public static event Action? VisibilityChanged;

    public static bool IsShown => _window is { IsVisible: true };

    public static bool IsCreated => _window != null;

    /// <summary>The game the launcher started and is waiting on, or null.</summary>
    public static LauncherEntry? AwaitingGame => _awaiting;

    // Implemented in LauncherHost.Window.cs next to the XAML. Leaves window null when the UI is
    // not part of this build, and every transition below degrades to "show the panel".
    static partial void CreateWindow(ref Window? window);

    public static void Show()
    {
        try
        {
            if (_window == null)
            {
                Window? w = null;
                CreateWindow(ref w);
                if (w == null)
                {
                    Log.Warning("[Launcher] no window factory in this build; showing the panel instead");
                    OpenPanel();
                    return;
                }
                _window = w;
                _window.Closed += (_, _) => { _window = null; RaiseVisibility(); };
                _window.IsVisibleChanged += (_, _) => RaiseVisibility();
            }

            if (!_window.IsVisible) _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Launcher] Show failed");
        }
    }

    public static void Hide()
    {
        try { _window?.Hide(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] Hide failed"); }
    }

    /// <summary>The launcher's panel tile and the second-instance "--panel" handoff.</summary>
    public static void OpenPanel()
    {
        Hide();
        var mw = App.MainWindowRef;
        if (mw == null) { Log.Warning("[Launcher] OpenPanel with no main window"); return; }
        try { mw.ShowFromTray(); }
        catch (Exception ex) { Log.Error(ex, "[Launcher] ShowFromTray failed"); }
        ReleaseStartupLadder();
    }

    /// <summary>The startup ladder (What's New, recap) waits while the panel is tucked away.</summary>
    public static void HoldStartupLadder()
    {
        try { App.StartupLadder?.Hold(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ladder hold failed"); }
    }

    public static void ReleaseStartupLadder()
    {
        try { App.StartupLadder?.Release(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ladder release failed"); }
    }

    /// <summary>
    /// A second instance started with no surface named (the old desktop icon, a double-click on
    /// the exe). The panel on screen means "bring it forward", as always. Otherwise the answer is
    /// the same as a bare boot: the launcher, unless the user asked for the panel directly.
    /// </summary>
    public static void OnBareRelaunch()
    {
        var mw = App.MainWindowRef;
        if (mw is { IsVisible: true })
        {
            try { mw.ShowFromTray(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] ShowFromTray on bare relaunch failed"); }
            return;
        }
        if (App.Settings?.Current?.LauncherSkipToPanel == true) OpenPanel();
        else Show();
    }

    /// <summary>
    /// The panel's "back to client" button. Refused during Lockdown, exactly like closing the panel
    /// is: the launcher must never be a way around the veil.
    /// </summary>
    public static bool BackToLauncher()
    {
        if (App.Lockdown?.IsActive == true)
        {
            try { App.Lockdown.NotifyEscapeAttempt(Possession.EscapeKinds.Close); } catch { }
            return false;
        }
        var mw = App.MainWindowRef;
        try { mw?.HideForLauncher(); }
        catch (Exception ex) { Log.Error(ex, "[Launcher] HideForLauncher failed"); }
        Show();
        return true;
    }

    /// <summary>
    /// Start a game from the launcher. The launcher hides while the game is up and returns when
    /// the host reports the window gone. A refused launch (locked tile, unknown id) leaves the
    /// launcher where it is so the refusal toast has something to sit on.
    /// </summary>
    public static bool LaunchGame(string id)
    {
        var entry = LauncherCatalogue.Find(id);
        if (entry == null) return false;

        if (entry.Locked)
        {
            // The host owns the refusal toast; the launcher stays up behind it.
            LauncherCatalogue.TryLaunch(entry.Id);
            return false;
        }

        if (!LauncherCatalogue.TryLaunch(entry.Id)) return false;

        // A host that failed to boot reports inactive at once. Check on the next pump, not now,
        // because every host creates its window synchronously inside Launch.
        _awaiting = entry;
        Hide();
        StartReturnPoll();
        return true;
    }

    /// <summary>
    /// The launcher's own close button. Lockdown vetoes it. With the engine running or a game up,
    /// the launcher hides and the panel's tray icon stays the way back. With nothing running and
    /// the panel hidden, closing the launcher means leaving, so the process exits through the
    /// panel's exit path.
    /// </summary>
    public static void RequestClose()
    {
        if (App.Lockdown?.IsActive == true)
        {
            try { App.Lockdown.NotifyEscapeAttempt(Possession.EscapeKinds.Close); } catch { }
            return;
        }

        var mw = App.MainWindowRef;
        bool somethingRunning = App.IsEngineRunning || App.IsSessionRunning || LauncherCatalogue.AnyActive;
        bool panelOnScreen = mw is { IsVisible: true };

        if (somethingRunning || panelOnScreen || mw == null)
        {
            Hide();
            return;
        }

        try { mw.RequestExit(); }
        catch (Exception ex) { Log.Error(ex, "[Launcher] RequestExit failed"); }
    }

    private static void StartReturnPoll()
    {
        _returnPoll ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _returnPoll.Tick -= ReturnPollTick;
        _returnPoll.Tick += ReturnPollTick;
        _returnPoll.Start();
    }

    private static void ReturnPollTick(object? sender, EventArgs e)
    {
        var entry = _awaiting;
        if (entry == null) { _returnPoll?.Stop(); return; }
        if (entry.Active) return;

        _returnPoll?.Stop();
        _awaiting = null;

        // The panel came forward on its own (a game that restores it on close, or the user
        // clicked the tray). Then the launcher stays out of the way.
        if (App.MainWindowRef is { IsVisible: true }) return;
        Show();
    }

    private static void RaiseVisibility()
    {
        try { VisibilityChanged?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] VisibilityChanged handler threw"); }
    }
}
