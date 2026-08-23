using Avalonia.Controls;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Navigation;

/// <summary>
/// THE ONE place a <see cref="DtrhLoomWindow"/> is constructed. Both callers go through it:
/// the Studio module panel's <c>THE LOOM — weave your own spiral</c> button (the user path),
/// and the <c>--loom-demo</c> demonstrator in <c>App.axaml.cs</c> (the headed-evidence path
/// that used to construct the window inline). One launch path, two callers.
///
/// <para>WPF parity, <c>Services/Chaos/LoomHostService.cs</c>:</para>
/// <list type="bullet">
///   <item><c>:29-31</c> — "idempotent - refocuses if already open": the second press does not
///   open a second studio, it refocuses the one that is up. Openness is tracked by the FIELD,
///   released on <c>Closed</c> (<c>:68-69</c>), never by window visibility.</item>
///   <item><c>:30-77</c> — there is NO entitlement check anywhere on this path, and the rack
///   entry carries no tier (<c>Views/Tabs/StudioTabView.xaml.cs:490</c>, default
///   <c>tier = 0</c> at <c>:548</c>). The port reproduces the CODE, per
///   wpf-surface-reachability.md §7 @ 7527243e7 ambiguity 3.</item>
///   <item><c>:36-38</c> — the spirals folder is created before navigating. That already lives
///   inside <see cref="DtrhLoomWindow"/> (<c>DtrhLoomWindow.axaml.cs:71-74</c>), so this
///   launcher owns nothing but create / present / refocus.</item>
/// </list>
/// </summary>
public sealed class LoomLaunch
{
    private readonly ApplicationHost _host;
    private readonly Window _owner;

    public LoomLaunch(ApplicationHost host, Window owner)
    {
        _host = host;
        _owner = owner;
    }

    /// <summary>HARNESS-ONLY (<c>--loom-drive</c>): the scripted-pointer step list the studio
    /// window replays. Null on every product path.</summary>
    public string? HarnessDrive { get; set; }

    /// <summary>
    /// The PRESENTATION seam. Product default: show the studio owned by the shell window.
    /// Headless tests replace it, because showing the real studio runs its <c>Opened</c> work —
    /// real audio-device init and, on a WebView2 box, a real embedded navigation
    /// (<c>DtrhLoomWindow.axaml.cs:45-50,99-140</c>) — which a headless frame cannot present and
    /// this packet may not change (<c>Features/**</c> is read-only). The seam moves ONLY the
    /// final <c>Show</c>: the window handed to it is the real <see cref="DtrhLoomWindow"/>.
    /// Same shape as the popup manager's injected factory (<c>MainWindow.axaml.cs</c>).
    /// </summary>
    public Action<DtrhLoomWindow, Window> Present { get; set; } = static (window, owner) => window.Show(owner);

    /// <summary>The open studio, or null. Field-tracked openness is the WPF rule (<c>:30</c>).</summary>
    public DtrhLoomWindow? Current { get; private set; }

    /// <summary>How many times the launch gesture arrived (presses, not windows).</summary>
    public int LaunchCount { get; private set; }

    /// <summary>Raised when the studio window closes, after the singleton is released.</summary>
    public event Action<DtrhLoomWindow>? Closed;

    /// <summary>
    /// Open the Loom studio, or refocus the one already open. No gate, no setup step, no
    /// picker: WPF's click goes straight to <c>Launch()</c> (wpf-surface-reachability.md §4 @ 7527243e7).
    /// </summary>
    public void Launch()
    {
        LaunchCount++;

        if (Current is { } open)
        {
            // LoomHostService.cs:30 — "if (_host != null) { _host.FocusWeb(); return; }".
            open.Activate();
            _host.LogDiagnostic("loom: studio already open — refocused (no second studio)");
            return;
        }

        var window = new DtrhLoomWindow(_host, HarnessDrive);
        Current = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(Current, window))
            {
                Current = null;
            }

            Closed?.Invoke(window);
        };

        Present(window, _owner);
    }
}
