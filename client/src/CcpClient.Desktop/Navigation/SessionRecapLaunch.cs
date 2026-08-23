using Avalonia.Controls;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;

namespace CcpClient.Desktop.Navigation;

/// <summary>
/// THE ONE place a <see cref="SessionRecapWindow"/> or a <see cref="SessionHistoryWindow"/> is
/// constructed — the <see cref="LoomLaunch"/> convention, and there are three callers to keep
/// honest: the media log's <c>LogReady</c> when a session ends, the <b>Recent sessions</b> button on
/// the Studio door, and a row inside the history window itself.
///
/// <para><b>Upstream has exactly this problem and states it.</b> Its recap can be shown modally or
/// non-modally, and the non-modal path "does not block the next session, so two runs ending in
/// quick succession would stack two live cards. Keep exactly one"
/// (<c>MainWindow/MainWindow.Presets.cs:1690-1697</c>, with <c>CloseLiveSessionRecap</c> at
/// <c>:1616-1623</c>). This port's recap is never modal — nothing in the shell blocks on its result
/// — so the same rule applies to every recap it opens: the second one CLOSES the first rather than
/// stacking on it, and openness is tracked by the FIELD and released on <c>Closed</c>, never read
/// off window visibility (<c>Services/Chaos/LoomHostService.cs:30</c>, <c>:68-69</c>, the
/// convention <see cref="LoomLaunch"/> already follows).</para>
///
/// <para>The history window is a singleton for the same reason and refocuses rather than opening a
/// second (upstream's is modal, <c>MainWindow.Presets.cs:1445</c>, so a second is impossible
/// there).</para>
/// </summary>
public sealed class SessionRecapLaunch
{
    private readonly ScriptedSessionLogStore _store;
    private readonly Window _owner;

    /// <param name="store">The log store the session writes into and the history reads back.</param>
    /// <param name="owner">The shell window every one of these is owned by, as upstream owns its
    /// dialogs to the main window (<c>MainWindow.Presets.cs:1444</c>, <c>:1683</c>).</param>
    public SessionRecapLaunch(ScriptedSessionLogStore store, Window owner)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(owner);
        _store = store;
        _owner = owner;
    }

    /// <summary>
    /// The PRESENTATION seam, the same shape <see cref="LoomLaunch.Present"/> has and for a
    /// narrower reason: a headless fact wants the window BUILT — its rows, its sentences, its
    /// refusals — without a second window being shown over the one the test is driving. The window
    /// handed to it is the real one.
    /// </summary>
    public Action<Window, Window> Present { get; set; } = static (window, owner) => window.Show(owner);

    /// <summary>The recap on screen, or null.</summary>
    public SessionRecapWindow? CurrentRecap { get; private set; }

    /// <summary>The history window on screen, or null.</summary>
    public SessionHistoryWindow? CurrentHistory { get; private set; }

    /// <summary>How many times a recap has been asked for (asks, not windows).</summary>
    public int RecapCount { get; private set; }

    /// <summary>How many times the history has been asked for.</summary>
    public int HistoryCount { get; private set; }

    /// <summary>
    /// Show the recap for one finished session, replacing any recap already up. Called for
    /// COMPLETION AND ABORT alike, because upstream's is
    /// (<c>MainWindow/MainWindow.xaml.cs:373-375</c>).
    /// </summary>
    public SessionRecapWindow ShowRecap(ScriptedSessionLog log) => ShowRecap(log, _owner);

    /// <summary>The same, owned by a specific window — the history's rows open their recap owned by
    /// the history rather than by the shell, as upstream's do
    /// (<c>SessionLogHistoryWindow.xaml.cs:46-49</c>).</summary>
    public SessionRecapWindow ShowRecap(ScriptedSessionLog log, Window owner)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(owner);
        RecapCount++;

        CloseRecap();
        var window = new SessionRecapWindow(log);
        CurrentRecap = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(CurrentRecap, window))
            {
                CurrentRecap = null;
            }
        };

        Present(window, owner);
        return window;
    }

    /// <summary>Open the history, or refocus the one already open (the
    /// <see cref="LoomLaunch.Launch"/> rule).</summary>
    public SessionHistoryWindow ShowHistory()
    {
        HistoryCount++;

        if (CurrentHistory is { } open)
        {
            open.Activate();
            return open;
        }

        var window = new SessionHistoryWindow(_store, (log, owner) => ShowRecap(log, owner));
        CurrentHistory = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(CurrentHistory, window))
            {
                CurrentHistory = null;
            }
        };

        Present(window, _owner);
        return window;
    }

    private void CloseRecap()
    {
        var stale = CurrentRecap;
        CurrentRecap = null;
        stale?.Close();
    }
}
