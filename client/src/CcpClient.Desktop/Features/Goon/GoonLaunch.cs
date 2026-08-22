using Avalonia.Controls;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Goon;

/// <summary>
/// THE one place a <see cref="GoonHostWindow"/> is constructed — the
/// <see cref="Navigation.LoomLaunch"/> / <see cref="Dtrh.DtrhLaunch"/> /
/// <see cref="Intake.IntakeLaunch"/> pattern: one construction site, several callers, no second
/// launcher. TWO callers reach this object: the Play page's Goon card (the user path), and
/// <c>--goon-demo</c>, which was built through all four of its sites — parsed in
/// <c>Program.cs</c> beside <c>--intake-demo</c>, threaded through <c>BuildAvaloniaApp</c> into
/// <c>new App(...)</c>, and classified <c>Demo</c> in <c>Lifecycle/HarnessEntryPoints.cs</c>.
/// An earlier packet was granted that flag and correctly REFUSED to half-wire it (D259), because a flag in
/// <c>App.axaml.cs</c> alone would have been a dial nothing could turn; D265 records what changed.
/// The flag carries no drive and no auto-close: the headed evidence for this surface is taken
/// through real input, and either modifier would be a HARNESS entry point rather than a Demo one.
///
/// <para><b>There is no gate here, and that is the ported fact, not an omission.</b> Upstream's
/// Goon card is an unconditional door — <c>Views/Tabs/PlayTabView.xaml:547-549</c> refuses to
/// draw a padlock over it because joining is free — and <c>GoonHostService.Launch</c>
/// (<c>:128-130</c>) asks nothing before opening; the two paid rungs live INSIDE, on hosting and
/// on sending (<c>:894</c>, <c>:909</c>), which is where this port refuses them too
/// (<see cref="GoonDoors"/>). A gate on this door would be an invention.</para>
///
/// <para>Already-open behaviour is upstream's: <c>Launch</c> refocuses a live run and returns
/// (<c>:130</c>, <c>_host.FocusWeb()</c>). Here that is <see cref="Window.Activate"/>.</para>
/// </summary>
public sealed class GoonLaunch
{
    private readonly ApplicationHost _host;
    private readonly Window _owner;
    private GoonHostWindow? _window;
    private GoonParticipant? _participant;

    public GoonLaunch(ApplicationHost host, Window owner)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(owner);
        _host = host;
        _owner = owner;
    }

    /// <summary>
    /// The OPEN seam. Product default: really show the window. Headless tests replace it only
    /// because that window binds a loopback origin and a real embedded browser surface no
    /// headless frame can present — the same reason <c>IntakeLaunch.Open</c> exists. The seam
    /// moves nothing but the final call, which is what makes "one construction site" checkable.
    /// </summary>
    public Action<GoonHostWindow> Open { get; set; } = static window => window.Show();

    /// <summary>Where the participant's data directory lives. Null is the product default (the
    /// install's own data root through the data-root choke point); set only by tests.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>How many times the launch gesture arrived (presses, not windows).</summary>
    public int LaunchCount { get; private set; }

    /// <summary>Raised when the launch flow THREW, so the page can render the fault instead of
    /// losing it (the launch-fault lesson: a discarded task's exception is seen by nobody).</summary>
    public event Action<Exception>? Faulted;

    /// <summary>The exception the last faulted launch threw, or null if none has.</summary>
    public Exception? LastFault { get; private set; }

    /// <summary>True while a Goon window is open.</summary>
    public bool IsOpen => _window is not null;

    /// <summary>
    /// Open the practice host, or refocus the one already open (<c>GoonHostService.cs:130</c>).
    /// Never throws: a fault is raised on <see cref="Faulted"/> and recorded.
    /// </summary>
    public void Practice()
    {
        LaunchCount++;
        try
        {
            if (_window is not null)
            {
                _window.Activate();
                _host.LogDiagnostic("goon: launch gesture on an already-open host — refocused");
                return;
            }

            var participant = new GoonParticipant(new LogSinkAdapter(_host), DataDirectory);
            var probe = participant.Start();
            _participant = participant;

            var window = new GoonHostWindow(_host, participant, _owner, new DtrhWatchdog());
            _window = window;
            window.Closed += (_, _) =>
            {
                _window = null;
                _participant = null;
                participant.Dispose();
                _host.LogDiagnostic("goon: host window closed — participant disposed");
            };

            _host.LogDiagnostic($"goon: opening practice host (payload {probe.State}, {probe.FileCount} files)");
            Open(window);
        }
        catch (Exception ex)
        {
            LastFault = ex;
            _host.LogDiagnostic($"goon: launch faulted ({ex.GetType().Name}: {ex.Message})");
            try { _participant?.Dispose(); } catch { /* best-effort */ }
            _participant = null;
            _window = null;
            Faulted?.Invoke(ex);
        }
    }

    /// <summary>Adapts the host's diagnostic log to the transport contracts' ILogSink (the
    /// <c>IntakeHostContext.cs:223</c> adapter, same shape).</summary>
    private sealed class LogSinkAdapter(ApplicationHost host) : ILogSink
    {
        public void Log(string message) => host.LogDiagnostic(message);
    }
}
