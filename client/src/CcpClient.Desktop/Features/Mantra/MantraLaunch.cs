using Avalonia.Controls;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Mantra;

/// <summary>
/// THE one place a <see cref="MantraWindow"/> is constructed — the <see cref="Goon.GoonLaunch"/> /
/// <see cref="Dtrh.DtrhLaunch"/> / <see cref="Intake.IntakeLaunch"/> pattern: one construction
/// site, no second launcher.
///
/// <para><b>THE DOOR IS THE PLAY PAGE'S MANTRAS CARD</b> — <c>Views/Pages/PlayPage.axaml</c>, whose
/// Begin button is this build's only caller of <see cref="Open"/>. Upstream's is the same card on
/// the same page, and it came OFF the Play tab in the 2026-08-12 relayout, which is why
/// <c>MainWindow/MainWindow.PlayTab.cs:262</c> says in capitals that <c>MantraWindow</c> is "a
/// window nothing opens". That removal was DE-DUPLICATION whose premise was false for this one
/// card — "only the duplicate Play-page card is gone"
/// (<c>Views/Tabs/PlayTabView.xaml:20-24</c>), while this card was the window's ONLY caller, which
/// the relayout's own commit records as "MantraWindow entry point orphaned - re-home pending owner
/// call" (<c>a9859e7b6</c>). Upstream's rescue note (<c>:269</c>) puts the cost of re-homing the
/// game at "exactly one <c>StartMantraSession(reps)</c> call"; here that is one <see cref="Open"/>
/// call, and the ordering hazard that note exists to preserve is gone — a
/// <see cref="MantraSession"/> is started by its own constructor.</para>
///
/// <para><b>THE XP DECISION LIVES HERE.</b> The ledger is opened for the life of ONE window and
/// disposed with it, which is the shape <c>Features/Progression/ProgressionLedger.Open</c> was
/// written for and says so: "ONE file per install, opened by more than one host, each with its own
/// uniquely-named registry owner. The three hosts that call this are modal windows the user opens
/// one at a time" (<c>:180-185</c>). A typed mantra window is a fourth such host, not a fourth
/// concurrent WRITER: <see cref="IsOpen"/> refuses a second one, and the store is opened after that
/// refusal and disposed on <see cref="Window.Closed"/>. This is the opposite situation to
/// <c>PopQuizEffect</c>, which correctly refused a ledger because it is a SESSION-lifetime module
/// and would have held a second in-memory copy of <c>progression.json</c> across another window's
/// grants (<c>Session/SessionParticipant.cs:481-489</c>). Upstream banks at the same moment this
/// does — inside the completion, <c>Services/MantraService.cs:86</c>.</para>
///
/// <para><b>The mantras never reach the log.</b> The two diagnostics this class writes carry counts
/// and nothing else, for the reason on <see cref="MantraSession"/>: the pool is text the user
/// wrote.</para>
/// </summary>
public sealed class MantraLaunch
{
    /// <summary>This launch's <c>OperationRegistry</c> owner name for the progression store. Unique
    /// per <c>ProgressionLedger.Open</c>'s contract (<c>:186-187</c>), and it names the surface
    /// rather than the feature so a diagnostic says which window banked.</summary>
    public const string ProgressionOwner = "MantraProgression";

    private readonly ApplicationHost _host;
    private readonly Window _owner;
    private MantraWindow? _window;
    private ProgressionLedger? _ledger;

    public MantraLaunch(ApplicationHost host, Window owner)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(owner);
        _host = host;
        _owner = owner;
    }

    /// <summary>The OPEN seam. Product default: really show the window, owned by the shell so it
    /// stays above it. Replaced only by tests, and it moves nothing but the final call — the same
    /// reason <c>GoonLaunch.Open</c> and <c>IntakeLaunch.Open</c> exist.</summary>
    public Action<MantraWindow, Window> Show { get; set; } =
        static (window, owner) => window.Show(owner);

    /// <summary>Where <c>progression.json</c> lives. Null is the product default — the install's own
    /// data root through the data-root choke point, which is what makes the level a property of the
    /// person rather than of the surface that banked it. Set only by tests.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>The user's mantras. Null falls back to upstream's built-in pool
    /// (<see cref="MantraSession.DefaultPool"/>). <b>There is no persisted pool in this build</b>:
    /// upstream's lives in <c>AppSettings.MantraPool</c> with an editor beside it, this port has no
    /// mantra editor and no phrase document to hang one off, and inventing a store nothing writes
    /// would be inventing a feature. So a caller supplies one or the built-ins are played.</summary>
    public IReadOnlyList<string>? Pool { get; set; }

    /// <summary>The injected clock, threaded to the session. Null is the real one.</summary>
    public Func<DateTimeOffset>? Clock { get; set; }

    /// <summary>The pool draw. Null is <see cref="Random.Shared"/>.</summary>
    public Random? Random { get; set; }

    /// <summary>True while a mantra window is open.</summary>
    public bool IsOpen => _window is not null;

    /// <summary>How many times the launch gesture arrived — presses, not windows, so a refused
    /// second press is visible.</summary>
    public int LaunchCount { get; private set; }

    /// <summary>The window that is up, or null. For a caller that wants the run's outcome.</summary>
    public MantraWindow? Window => _window;

    /// <summary>The exception the last faulted launch threw, or null if none has.</summary>
    public Exception? LastFault { get; private set; }

    /// <summary>
    /// Open the game. Upstream's <c>StartMantraSession</c>
    /// (<c>MainWindow/MainWindow.PlayTab.cs:287-315</c>), including its two decisions:
    ///
    /// <para><b>A second press focuses the live window rather than restarting</b> (<c>:294-303</c>)
    /// — "a second <c>StartSession</c> would reset Completions and Streak mid-run, i.e. silently
    /// delete the user's progress."</para>
    ///
    /// <para><b>There is no tier gate</b> (<c>:282-285</c>) — "free by design ... gating the typed
    /// game would be gating the cheaper half of something already given away."</para>
    /// </summary>
    /// <param name="targetReps">Repetitions to ask for. Clamped by the session to upstream's 1..100
    /// (<c>Services/MantraService.cs:28</c>).</param>
    /// <returns>The window, live or newly opened; null only if the launch faulted.</returns>
    public MantraWindow? Open(int targetReps = MantraSession.DefaultTargetReps)
    {
        LaunchCount++;

        if (_window is { } live)
        {
            live.Activate();                                                  // :297-300
            live.Focus();
            return live;
        }

        try
        {
            var dataDir = DataDirectory ?? Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!;
            _ledger = ProgressionLedger.Open(_host, dataDir, ProgressionOwner);

            var session = new MantraSession(targetReps, Pool ?? MantraSession.DefaultPool, _ledger, Clock, Random);
            var window = new MantraWindow(session);
            _window = window;
            window.Closed += (_, _) =>
            {
                _window = null;
                _ledger?.Dispose();
                _ledger = null;
                // Counts only. The mantras themselves never appear here.
                _host.LogDiagnostic(
                    $"mantra: run ended — {session.Completions}/{session.TargetCount} repetitions, best streak {session.BestStreak}");
            };

            _host.LogDiagnostic($"mantra: run started — {session.TargetCount} repetitions asked");
            Show(window, _owner);
            return window;
        }
        catch (Exception ex)
        {
            // Upstream shows a modal warning here (:308-313). There is no page to render one from
            // yet, so the fault is recorded rather than lost — and the half-opened ledger is closed,
            // because a store left running over progression.json is a second writer.
            LastFault = ex;
            _window = null;
            try { _ledger?.Dispose(); } catch { /* best-effort */ }
            _ledger = null;
            _host.LogDiagnostic($"mantra: launch failed ({ex.GetType().Name})");
            return null;
        }
    }
}
