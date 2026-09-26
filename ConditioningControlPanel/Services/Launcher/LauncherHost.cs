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
    /// <summary>The longest the window may hold its exit beat before it hides, in ms.</summary>
    public const int MaxHideDelayMs = 1500;

    private static Window? _window;
    private static DispatcherTimer? _returnPoll;
    private static LauncherEntry? _awaiting;
    private static int _hideDelayMs;
    private static bool _panelRequested;

    /// <summary>
    /// How long the launcher stays on screen after Play or the CTA so its exit beat (the burst,
    /// the shockwave, the shake) can be seen, in ms. The window sets this from its own
    /// choreography when it shows; 0 hides at once. Clamped to <see cref="MaxHideDelayMs"/>.
    /// </summary>
    public static int HideDelayMs
    {
        get => _hideDelayMs;
        set => _hideDelayMs = ClampHideDelay(value);
    }

    public static int ClampHideDelay(int ms) => Math.Clamp(ms, 0, MaxHideDelayMs);

    /// <summary>How long the launcher takes to fade to nothing before it hides, in ms.</summary>
    public const int FadeOutMs = 220;

    /// <summary>
    /// The window's fade-out, registered by the window itself. Called with the step that hides
    /// and moves on; returns false when it cannot animate right now (reduced motion, not on
    /// screen), and then the host hides at once. Null when no window is up.
    /// </summary>
    public static Func<Action, bool>? FadeOut { get; set; }

    /// <summary>
    /// The fade is the tail of an exit beat, never added after it: with a 450 ms beat and a 220 ms
    /// fade, the fade starts at 230. A beat shorter than the fade starts fading at once.
    /// </summary>
    public static int FadeLeadMs(int delayMs) => Math.Max(0, ClampHideDelay(delayMs) - FadeOutMs);

    private static DateTime _beatArmedUntil = DateTime.MinValue;

    /// <summary>
    /// The window calls this as it starts an exit beat. The next <see cref="OpenPanel"/> or
    /// <see cref="LaunchGame"/> within a second then holds its hide for <see cref="HideDelayMs"/>;
    /// everything else that opens the panel (the gear, the footer links) hides at once.
    /// </summary>
    public static void ArmExitBeat() => _beatArmedUntil = DateTime.UtcNow.AddSeconds(1);

    /// <summary>True once per armed beat, then the arm is spent.</summary>
    public static bool ConsumeArmedBeat()
    {
        bool armed = DateTime.UtcNow <= _beatArmedUntil;
        _beatArmedUntil = DateTime.MinValue;
        return armed;
    }

    private static int PendingHideDelay(int? hideDelayMs) =>
        ClampHideDelay(hideDelayMs ?? (ConsumeArmedBeat() ? HideDelayMs : 0));

    /// <summary>Raised on the UI thread whenever the launcher's visibility changes.</summary>
    public static event Action? VisibilityChanged;

    public static bool IsShown => _window is { IsVisible: true };

    public static bool IsCreated => _window != null;

    /// <summary>The launcher window itself, or null while it has never been built. Read by the
    /// game hosts, which place a new window on the monitor the launcher is on rather than on the
    /// primary one (#1239). Nobody outside this assembly needs it.</summary>
    internal static Window? WindowRef => _window;

    /// <summary>
    /// True while the launcher is part of this run: it has been created, or the boot did not ask
    /// for the panel outright (<c>--panel</c>, <c>--startup</c>, the skip-to-panel preference).
    /// The tray's "Back to CC Labs" row shows on this; the title-bar button is always there.
    /// </summary>
    public static bool SurfaceInPlay => IsCreated || App.Boot.Surface != BootSurface.Panel;

    /// <summary>The game the launcher started and is waiting on, or null.</summary>
    public static LauncherEntry? AwaitingGame => _awaiting;

    /// <summary>
    /// Set by the launcher window while it exists: opens its sign-in flow. A game asked for with
    /// nobody signed in (a tile, a shortcut, <c>--game</c>) lands here instead of launching.
    /// </summary>
    public static Action? RequestSignIn { get; set; }

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

    /// <summary>Hides at once, no animation. The exit path and the close button use this.</summary>
    public static void Hide()
    {
        try { _window?.Hide(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] Hide failed"); }
    }

    /// <summary>
    /// Fade the launcher out, then hide it and run <paramref name="then"/>. Falls back to an
    /// immediate hide when no fade is registered or the window declines. <paramref name="stillWanted"/>
    /// is asked again at the end of the fade; a hide that stopped being wanted in the meantime (the
    /// game died and the launcher is coming back) stands down.
    /// </summary>
    internal static void FadeThenHide(Action? then = null, Func<bool>? stillWanted = null)
    {
        bool done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            if (stillWanted != null && !stillWanted()) return;
            Hide();
            try { then?.Invoke(); }
            catch (Exception ex) { Log.Error(ex, "[Launcher] step after hide failed"); }
        }

        var fade = FadeOut;
        if (fade != null)
        {
            try { if (fade(Finish)) return; }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] fade-out hook threw; hiding at once"); }
        }
        Finish();
    }

    /// <summary>
    /// The launcher's panel tile and the second-instance "--panel" handoff. With the launcher on
    /// screen and a positive delay (<paramref name="hideDelayMs"/>, or <see cref="HideDelayMs"/>
    /// when null and a beat is armed), the whole step waits that long so the beat plays first.
    /// </summary>
    public static void OpenPanel(int? hideDelayMs = null) => OpenPanel(hideDelayMs, null);

    /// <summary>As <see cref="OpenPanel(int?)"/>, then <paramref name="then"/> once the panel is
    /// on screen: the door for anything that needs the panel as an owner (a modal of its own).</summary>
    private static void OpenPanel(int? hideDelayMs, Action? then)
    {
        _panelRequested = true;
        int delay = PendingHideDelay(hideDelayMs);
        if (delay > 0 && IsShown) { After(FadeLeadMs(delay), () => OpenPanelNow(then)); return; }
        OpenPanelNow(then);
    }

    /// <summary>
    /// The launcher's "Manage mods" row. The Mod Manager is a modal OWNED BY THE PANEL, and a
    /// tray-hidden panel cannot own a dialog (no shown owner), so the panel comes up first and the
    /// manager opens over it; the panel's own return path applies whatever was changed.
    /// </summary>
    public static void OpenPanelModManager()
    {
        OpenPanel(null, () =>
        {
            try { App.MainWindowRef?.OpenModManagerFromLauncher(); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] Mod Manager from the launcher failed"); }
        });
    }

    /// <summary>
    /// A tile that lives in the panel (the Graded Intake). Hides the launcher through
    /// <see cref="OpenPanel"/>, so an armed exit beat still plays, and puts the panel on
    /// <paramref name="tab"/>. Catalogue lambdas have no window in hand, hence static.
    /// </summary>
    public static void OpenPanelTab(string tab)
    {
        OpenPanel();
        try { App.MainWindowRef?.ShowTab(tab); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] ShowTab {Tab} failed", tab); }
    }

    /// <summary>The launcher fades and hides, then the panel comes up and fades in.</summary>
    private static void OpenPanelNow(Action? then = null)
        => FadeThenHide(then == null ? ShowPanel : () => { ShowPanel(); then(); });

    private static void ShowPanel()
    {
        var mw = App.MainWindowRef;
        if (mw == null) { Log.Warning("[Launcher] OpenPanel with no main window"); return; }
        try { mw.ShowFromLauncher(); }
        catch (Exception ex) { Log.Error(ex, "[Launcher] ShowFromLauncher failed"); }
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
        if (mw == null) { Show(); return true; }
        // The panel fades to nothing, tucks into the tray, and the launcher fades up in its place.
        try { mw.FadeOutForLauncher(() => { HidePanel(mw); Show(); }); }
        catch (Exception ex)
        {
            Log.Error(ex, "[Launcher] FadeOutForLauncher failed; hiding at once");
            HidePanel(mw);
            Show();
        }
        return true;
    }

    private static void HidePanel(MainWindow mw)
    {
        try { mw.HideForLauncher(); }
        catch (Exception ex) { Log.Error(ex, "[Launcher] HideForLauncher failed"); }
    }

    /// <summary>
    /// Start a game from the launcher. The launcher hides while the game is up and returns when
    /// the host reports the window gone. A refused launch (locked tile, unknown id, nobody signed
    /// in) leaves the launcher where it is so the refusal toast has something to sit on.
    /// </summary>
    public static bool LaunchGame(string id, int? hideDelayMs = null)
    {
        var entry = LauncherCatalogue.Find(id);
        if (entry == null) return false;

        if (LauncherCatalogue.NeedsAccount)
        {
            // Every game needs an account. With the launcher up, its sign-in flow takes over;
            // otherwise the caller shows the launcher, which wears the same rule on every tile.
            Log.Information("[Launcher] {Id} refused: nobody is signed in", entry.Id);
            if (IsShown)
            {
                try { RequestSignIn?.Invoke(); }
                catch (Exception ex) { Log.Warning(ex, "[Launcher] sign-in request failed"); }
            }
            return false;
        }

        if (entry.Locked)
        {
            // The host owns the refusal toast; the launcher stays up behind it.
            LauncherCatalogue.TryLaunch(entry.Id);
            return false;
        }

        // A leash punishment pending: every game tile leads to the gate first. The panel comes
        // up and the gate lands there (MainWindow.Leash.cs); Panic and Cut leash are on it.
        if (App.MainWindowRef?.LeashBlocksGames == true)
        {
            Log.Information("[Launcher] {Id} waits: a leash punishment is pending", entry.Id);
            OpenPanel(null, () => App.MainWindowRef?.PresentLeashGateFromLauncher());
            return true;
        }

        _panelRequested = false;
        if (!LauncherCatalogue.TryLaunch(entry.Id)) return false;

        // A tile that lives in the panel opened it through OpenPanel during Launch. The panel
        // owns the hide (and the beat) and there is no window to wait on, so the poll stays off:
        // it would otherwise see "not active" on its first tick and bring the launcher back.
        if (_panelRequested) return true;

        // A host that failed to boot reports inactive at once. Check on the next pump, not now,
        // because every host creates its window synchronously inside Launch.
        _awaiting = entry;
        int delay = PendingHideDelay(hideDelayMs);
        bool StillWaiting() => ReferenceEquals(_awaiting, entry);
        if (delay > 0)
        {
            // The exit beat plays over the game's first frames, the fade being its tail. A host
            // that died in the meantime has already cleared the wait (and shown the launcher), so
            // the late hide stands down.
            After(FadeLeadMs(delay), () => { if (StillWaiting()) FadeThenHide(stillWanted: StillWaiting); });
        }
        else FadeThenHide(stillWanted: StillWaiting);
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

    private static void After(int ms, Action step)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { step(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] delayed step failed"); }
        };
        timer.Start();
    }

    private static void RaiseVisibility()
    {
        try { VisibilityChanged?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] VisibilityChanged handler threw"); }
    }
}
