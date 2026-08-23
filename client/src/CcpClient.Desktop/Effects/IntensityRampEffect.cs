using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>Intensity Ramp</b> — WPF's TIMING rack row (<c>Views/Tabs/StudioTabView.xaml.cs:538-541</c>),
/// and <b>the first ported module that draws nothing at all</b>.
///
/// <para><b>Why this module exists.</b> Four modules ran under this spine and every one of
/// them was from WPF's EFFECTS group and painted an overlay, so every seam the port had proven —
/// <see cref="OwnedSessionEffect"/>, <c>OverlaySurfaceSet</c>, the dot's three states, a presenter
/// owning the cadence — had only ever been proven against pixel-pushing. This module is from a
/// different group, has no surface, is never "due", and its whole observable output is a number on
/// ANOTHER module's panel.</para>
///
/// <para><b>What it does, in WPF's own terms.</b> <c>StartEngine</c> starts a 2-second timer
/// (<c>MainWindow/MainWindow.StartStop.cs:265-269</c> -&gt; <c>:413-435</c>) after capturing the
/// current value of every linkable dial. Each tick computes how far through the ramp the session is,
/// shapes that by the chosen curve, and writes each linked dial to
/// <c>base × currentMultiplier</c>, capped (<c>:504-540</c>). The stop restores every captured base
/// (<c>:437-479</c>). That is the entire feature: <b>a settings write is the whole job</b>, in
/// upstream's own words at <c>:451-456</c>.</para>
///
/// <para><b>THE SEAM'S FOURTH VERDICT — what "running" means with nothing to look at.</b></para>
/// <list type="bullet">
/// <item>Paced <see cref="EffectDotState.Live"/> is a claim about the CLOCK: a firing is scheduled.</item>
/// <item>Continuous <c>Live</c> is a claim about the SCREEN: a surface is confirmed up.</item>
/// <item>Moving <c>Live</c> is the screen AND that it will be a different screen a moment from now.</item>
/// <item><b>Non-drawing <c>Live</c> is a claim about CUSTODY: this module holds dials belonging to
/// other modules, is driving them, and owes them back.</b></item>
/// </list>
///
/// <para><b>Why not the clock, when this module genuinely has one.</b> A ramp whose links are all
/// off has a tick scheduled for the whole session and will never change anything anywhere.
/// <c>PacedSessionEffect</c>'s <c>ScheduleArmed</c> would read <c>Live</c> for it — a dot claiming a
/// module is running that the user cannot observe by ANY means, which is the same class of lie as a
/// <c>Live</c> tint on a platform whose overlay refuses. <b>Having a timer is not sufficient to be
/// paced</b>: the 2-second tick is a CADENCE that keeps borrowed dials correct, not an INTERVAL that
/// decides when this module is due (the moving module's distinction, one module further on). A ramp is never
/// due; it is <i>on</i>.</para>
///
/// <para><b>And why not "change", which is the moving module's rule.</b> At full progress the ramp stops moving
/// anything, and at multiplier 1.0× it never moved anything — and in both cases it is still running:
/// it holds your dials, moving the multiplier slider starts them climbing with no re-arm, and STOP
/// still gives them back. A rule that demanded motion would report the finished ramp as not running
/// while it was still holding the user's settings hostage, which is the worse of the two lies.</para>
///
/// <para><b>Multiplier 1.0× and a zero base take the SUBLIMINALS answer, not the PINK FILTER
/// answer.</b> A tint at 0 % opacity has no window at all, so it is <c>Degraded</c> and its dot
/// reads <c>Armed</c>. A ramp at neutral gain is a live machine holding real state — so, like
/// Subliminals over an empty phrase pool, the ARM result is <c>Degraded</c> with a typed reason and
/// the DOT reads <c>Live</c>.</para>
///
/// <para><b>No surface, and none acquired for symmetry.</b> This module takes no
/// <c>IOverlaySurface</c>, no presenter and no frame source, and it must never be given one: an
/// overlay held by a module that never paints is worse than none at all.</para>
///
/// <para><b>What is NOT ported</b>, and is recorded rather than stubbed: the two of WPF's five
/// links whose dial does not exist in this port (master volume and subliminal volume — see
/// <see cref="IntensityRampPresetDocument"/>), and the tray notification that accompanies the
/// auto-stop (<c>MainWindow.StartStop.cs:554</c>, outside that packet's File Scope).</para>
///
/// <para><b>THE STAND-DOWN IS NOW PORTED, and the amendment that deferred it has expired.</b> The
/// original text said the <c>!sessionActive</c> guard's condition "is constantly false here"; the
/// first amendment weakened that to "nothing in the composition root constructs a
/// <see cref="Session.ScriptedSessionRun"/>". Both premises are gone — the Studio rack starts one
/// — so the guard is written. It is upstream's, per-link and read once per tick from
/// <c>_sessionEngine?.IsRunning == true</c> (<c>MainWindow/MainWindow.StartStop.cs:492</c>, where
/// <c>_sessionEngine</c> is the SCRIPTED engine, <c>Services/Session/SessionEngine.cs:81</c>).
/// See <see cref="StandsDownDuringSession"/> for WHICH links stand down and which deliberately do
/// not, and <see cref="Advance"/> for the one port-side strengthening the guard needs.</para>
/// </summary>
public sealed class IntensityRampEffect : OwnedSessionEffect
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:538</c>).</summary>
    public const string EffectId = "ramp";

    /// <summary>The row's label as the shipping app shows it (<c>StudioTabView.xaml.cs:538</c>,
    /// loc key <c>section_intensity_ramp</c> = "⚡ Intensity Ramp", <c>en.json:1447</c>, emoji
    /// stripped as the port's other rows are).</summary>
    public const string DisplayTitle = "Intensity Ramp";

    /// <summary>
    /// How often the ramp re-samples its own progress — WPF's <c>TimeSpan.FromSeconds(2)</c>, with
    /// its own comment "Update every 2 seconds" (<c>MainWindow/MainWindow.StartStop.cs:426-428</c>).
    ///
    /// <para>This is a CADENCE, not a schedule. Nothing fires here and nothing is shown; the tick
    /// exists so a dial that should have crept up by now has crept up by now.</para>
    /// </summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private readonly ISessionClock _clock;
    private readonly PersistenceStore<IntensityRampPresetDocument> _preset;
    private readonly IReadOnlyList<IIntensityDial> _dials;
    private readonly Action<Action> _dispatch;
    private readonly Func<bool> _scriptedSessionActive;

    /// <summary>
    /// A LEAF lock over this module's own fields, and it is deliberately not
    /// <see cref="OwnedSessionEffect.Gate"/>.
    ///
    /// <para><b>The discipline, stated so it is not broken later.</b> The only order this class ever
    /// produces is <c>Gate</c> then this — <see cref="OwnedSessionEffect.Dot"/> holds <c>Gate</c> and
    /// asks <see cref="WorkIsRunning"/>, and <c>EngageIfEligible</c>'s dial-off path holds
    /// <c>Gate</c> and calls <see cref="ReleaseWork"/>. Nothing here ever takes <c>Gate</c> while
    /// holding this, and nothing calls out of this class while holding it: every external call
    /// (reading a dial, writing a dial, scheduling a tick) happens on a snapshot taken and released
    /// first. That is what makes the recorded lock-order inversion unreachable here.</para>
    /// </summary>
    private readonly object _hold = new();

    /// <summary>The dials whose base value this module is holding, by rack key. THIS IS THE CUSTODY
    /// the dot's third clause is a claim about, and the restore it owes at stop.</summary>
    private readonly Dictionary<string, int> _held = [];

    private IDisposable? _tick;
    private DateTimeOffset? _startedAt;
    private bool _completionAnnounced;
    private double _progress;
    private double _currentMultiplier = 1.0;

    /// <param name="clock">The cadence this module re-samples its progress on. It is here rather
    /// than in a presenter for one reason: there is no presenter, because there is nothing to
    /// present. The moving module put a cadence in a surface because the cadence was keeping a SURFACE correct;
    /// this one keeps borrowed DIALS correct, and it belongs where they are driven from.</param>
    /// <param name="dials">What this ramp is allowed to drive. Empty is a legal composition and
    /// produces a typed refusal, never a silent no-op.</param>
    /// <param name="dispatch">Where work that touches another module's LIVE state is run — WPF's own
    /// <c>Dispatcher.Invoke</c> around the tick's writes (<c>MainWindow.StartStop.cs:504</c>). The
    /// persisted half of a dial move does not go through here; see <see cref="IIntensityDial"/>.</param>
    /// <param name="scriptedSessionActive">Whether a SCRIPTED session is on the clock right now —
    /// WPF's <c>_sessionEngine?.IsRunning == true</c>
    /// (<c>MainWindow/MainWindow.StartStop.cs:492</c>). It is a predicate rather than a reference
    /// for the same reason upstream reads it through a nullable field: this module must not know
    /// what a session IS, and in this port's composition the run is built long after the ramp
    /// (<see cref="SessionParticipant"/>). It is REQUIRED, with no "never" default, because a
    /// default of "no session is running" is exactly the silent wrong answer this guard exists to
    /// stop.</param>
    public IntensityRampEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        PersistenceStore<IntensityRampPresetDocument> preset,
        IReadOnlyList<IIntensityDial> dials,
        Action<Action> dispatch,
        Func<bool> scriptedSessionActive)
        : base(owner, signal, "intensity-ramp")
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(dials);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(scriptedSessionActive);
        _clock = clock;
        _preset = preset;
        _dials = dials;
        _dispatch = dispatch;
        _scriptedSessionActive = scriptedSessionActive;
    }

    /// <summary>Raised once per engagement when the ramp reaches full progress and the user asked
    /// for the session to end there — WPF's <c>EndSessionOnRampComplete</c> branch
    /// (<c>MainWindow/MainWindow.StartStop.cs:547-555</c>, which calls <c>StopEngine()</c>).
    ///
    /// <para>It is an EVENT rather than a call into <see cref="SessionEngine"/> because the engine
    /// owns the modules and a module that reached back into it would close the cycle. The
    /// composition root wires it (<see cref="SessionParticipant"/>), which is also the only place
    /// that knows a session exists at all.</para>
    ///
    /// <para>Delivered through the same dispatch the dial writes use, so it lands where WPF's does:
    /// on the UI thread, inside the tick's <c>Dispatcher.Invoke</c> (<c>:551-554</c>).</para></summary>
    public event Action? Completed;

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>The module's persisted dials, for a panel that wants to render them without a
    /// second copy of the defaults.</summary>
    public IntensityRampPresetDocument Preset => _preset.Current;

    /// <summary>Every dial this composition allows the ramp to drive, in composition order.</summary>
    public IReadOnlyList<IIntensityDial> Dials => _dials;

    /// <summary>How far through the climb the last sample was, 0..1. Zero before the first
    /// engagement.</summary>
    public double Progress
    {
        get { lock (_hold) { return _progress; } }
    }

    /// <summary>What a linked dial's base is currently multiplied by — WPF's <c>currentMult</c>
    /// (<c>MainWindow.StartStop.cs:501</c>). 1.0 before the first engagement, and 1.0 for the whole
    /// session when the multiplier dial is at its floor.</summary>
    public double CurrentMultiplier
    {
        get { lock (_hold) { return _currentMultiplier; } }
    }

    /// <summary>The rack keys of the dials this module is currently holding a base value for — the
    /// custody the dot's third clause reports, exposed so a panel can name it and a test can assert
    /// it.</summary>
    public IReadOnlyList<string> HeldDials
    {
        get { lock (_hold) { return [.. _held.Keys]; } }
    }

    /// <summary>The value this module took custody of for <paramref name="dialId"/>, or null when it
    /// holds nothing for that dial. This is what STOP gives back.</summary>
    public int? BaseValueFor(string dialId)
    {
        lock (_hold)
        {
            return _held.TryGetValue(dialId, out var value) ? value : null;
        }
    }

    /// <summary>
    /// <b>The fourth meaning of the dot.</b> The ramp is running exactly while its progress cadence
    /// is scheduled AND it holds at least one dial it has taken from another module.
    ///
    /// <para>Neither clause is redundant. Without the first, a module whose tick died would keep
    /// claiming to be climbing. Without the second, a ramp with nothing linked — a tick running
    /// forever over an empty custody list — would claim to be running while being, by construction,
    /// unobservable.</para>
    /// </summary>
    protected override bool WorkIsRunning
    {
        get { lock (_hold) { return _tick is not null && _held.Count > 0; } }
    }

    /// <inheritdoc/>
    public override void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        _preset.Mutate(p => p.Enabled = enabled);
        RaiseChanged();
    }

    /// <summary>How long the climb takes — WPF's duration slider
    /// (<c>Features/IntensityRampFeatureControl.xaml.cs:91-100</c>), clamped by the document.</summary>
    public void SetDurationMinutes(int minutes) =>
        MutateAndReapply(p => p.DurationMinutes = minutes);

    /// <summary>What a linked dial reaches at full progress — WPF's multiplier slider
    /// (<c>IntensityRampFeatureControl.xaml.cs:102-111</c>).</summary>
    public void SetMultiplier(double multiplier) =>
        MutateAndReapply(p => p.Multiplier = multiplier);

    /// <summary>The shape of the climb — WPF's curve combo
    /// (<c>IntensityRampFeatureControl.xaml.cs:122-136</c>).</summary>
    public void SetCurve(RampCurve curve) => MutateAndReapply(p => p.Curve = curve);

    /// <summary>End the whole session at full progress — WPF's "End at Complete" switch
    /// (<c>IntensityRampFeatureControl.xaml.cs:113-120</c>).</summary>
    public void SetEndSessionOnComplete(bool end) =>
        MutateAndReapply(p => p.EndSessionOnComplete = end);

    /// <summary>Link the Spiral Overlay's opacity to this ramp — WPF's link switches, all written by
    /// one handler (<c>IntensityRampFeatureControl.xaml.cs:138-149</c>).</summary>
    public void SetLinkSpiralOpacity(bool linked) =>
        MutateAndReapply(p => p.LinkSpiralOpacity = linked);

    /// <summary>Link the Pink Filter's opacity to this ramp (same handler upstream).</summary>
    public void SetLinkPinkFilterOpacity(bool linked) =>
        MutateAndReapply(p => p.LinkPinkFilterOpacity = linked);

    /// <summary>
    /// Link the Flash Images module's opacity to this ramp (same handler upstream) — WPF's FIRST
    /// link switch (<c>MainWindow/MainWindow.StartStop.cs:506-510</c>), admissible here only since
    /// flash opacity gained a dial on a ported panel. D93's other two links, master volume and
    /// subliminal volume, are still absent because their dials still are.
    /// </summary>
    public void SetLinkFlashOpacity(bool linked) =>
        MutateAndReapply(p => p.LinkFlashOpacity = linked);

    /// <summary>
    /// Take custody of the linked dials and start driving them.
    ///
    /// <para>The dial and the generation have already been checked by
    /// <see cref="OwnedSessionEffect"/>. What is left is this module's own three answers, and one
    /// invariant: <b>a re-engagement never restarts the climb.</b> WPF starts its ramp clock in
    /// <c>StartRampTimer</c> and nowhere else (<c>MainWindow.StartStop.cs:424</c>), so a user who
    /// moves the duration slider twenty minutes into a session is still twenty minutes into it.</para>
    /// </summary>
    protected override CapabilityState Engage(int generation)
    {
        if (_dials.Count == 0)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.RampNoDial,
                $"the '{Id}' module has no dials in this composition, so there is nothing for it to drive"));
        }

        Advance(generation, raiseChanged: false);
        EnsureTick(generation);

        var preset = _preset.Current;
        var held = HeldDials.Count;
        if (held == 0)
        {
            // The SUMMARY is true either way — the ramp holds nothing, so nothing climbs — but the
            // reason detail must not say "you linked nothing" to a user who linked three things and
            // is watching a session drive all three. The CODE stays ramp-no-linked-dial: it names
            // the operational state (no dial is held) and both causes land in it. Its declaration
            // in Session/EffectReasonCodes.cs still describes only the first cause; that file is
            // outside this packet's File Scope and the gap is reported rather than widened into.
            return new CapabilityState.Degraded(
                "the ramp is engaged and holds no dial, so nothing will climb",
                new CapabilityReason(
                    EffectReasonCodes.RampNoLinkedDial,
                    _scriptedSessionActive() && AnyLinkStandsDown(preset)
                        ? "a scripted session owns the dials the intensity ramp is linked to, so the ramp "
                          + "stands down and leaves them to the session "
                          + "(MainWindow/MainWindow.StartStop.cs:509,515,523)"
                        : "no effect is linked to the intensity ramp, so it will run for the whole session "
                          + "and change nothing"));
        }

        if (preset.Multiplier <= IntensityRampPresetDocument.MinMultiplier)
        {
            return new CapabilityState.Degraded(
                $"the ramp is engaged and holding {held} dial(s), so a multiplier change takes effect at once",
                new CapabilityReason(
                    EffectReasonCodes.RampMultiplierFlat,
                    $"the intensity ramp's multiplier is at {preset.Multiplier:0.0}×, so the dials it holds "
                    + "will not climb"));
        }

        return new CapabilityState.Available(
            $"the ramp is climbing {held} dial(s) to {preset.Multiplier:0.0}× over "
            + $"{preset.DurationMinutes} minutes");
    }

    /// <summary>
    /// Stop driving, and <b>give the dials back</b>.
    ///
    /// <para>WPF's <c>StopRampTimer</c> stops the timer and then writes every captured base value
    /// back (<c>MainWindow/MainWindow.StartStop.cs:437-479</c>). Both halves are here, and the order
    /// is upstream's: the cadence dies first, so no tick can re-raise a dial between the restore and
    /// the caller's next observation.</para>
    ///
    /// <para><b>This is where a non-drawing module differs from every module before it.</b> A paced
    /// module's release drops a pending one-shot and leaves the world alone. A continuous or moving
    /// module's release withdraws a surface it owns. This one has to <b>undo a change it made to
    /// somebody else's state</b> — and if it does not, the user's own opacity is gone, replaced by
    /// whatever the ramp had reached.</para>
    ///
    /// <para><b>The write is synchronous and the re-apply is dispatched, and the split matters
    /// here.</b> On this port's teardown path a post is not delivered — the dispatcher is already
    /// down — so a combined operation would leave the ramped value in the document for
    /// <c>PersistenceStore.FlushAsync</c> to write to disk. Here the VALUE always comes back; only
    /// the re-tint of a window that is being destroyed anyway can be dropped. <b>This is a port-side
    /// hazard, not an upstream one:</b> WPF's every exit path stops the engine before it saves, and
    /// <c>StopEngine</c> -&gt; <c>StopRampTimer</c> IS the restore
    /// (<c>MainWindow/MainWindow.StartStop.cs:388</c> -&gt; <c>:437-479</c>). See
    /// <see cref="IIntensityDial"/> and D95.</para>
    ///
    /// <para><b>Idempotent and lock-free with respect to <see cref="OwnedSessionEffect.Gate"/></b>,
    /// as the base class requires: one <c>Disarm</c> reaches this three times (directly, from the
    /// generation's cancellation callback, and from the parked operation's tail) and the second and
    /// third find nothing held and nothing scheduled.</para>
    /// </summary>
    protected override void ReleaseWork()
    {
        IDisposable? tick;
        KeyValuePair<string, int>[] held;
        lock (_hold)
        {
            tick = _tick;
            _tick = null;
            held = [.. _held];
            _held.Clear();
            _startedAt = null;
            _completionAnnounced = false;
            _progress = 0;
            _currentMultiplier = 1.0;
        }

        tick?.Dispose();
        if (held.Length == 0)
        {
            return;
        }

        // Restore the values first, on this thread, then ask the owning modules to re-apply them.
        var restored = new List<IIntensityDial>(held.Length);
        foreach (var (id, baseValue) in held)
        {
            var dial = DialFor(id);
            if (dial is null)
            {
                continue;
            }

            if (dial.Read() != baseValue)
            {
                dial.Write(baseValue);
                restored.Add(dial);
            }
        }

        if (restored.Count == 0)
        {
            return;
        }

        _dispatch(() =>
        {
            foreach (var dial in restored)
            {
                dial.Reapply();
            }
        });
    }

    /// <summary>
    /// One sample of the ramp: take custody of anything newly linked, work out where the climb is,
    /// and write the dials that have actually moved.
    ///
    /// <para><b>The arithmetic is upstream's, line for line</b>
    /// (<c>MainWindow/MainWindow.StartStop.cs:484-540</c>): raw progress is
    /// <c>min(elapsedMinutes / duration, 1)</c>, the curve shapes it, the multiplier interpolates
    /// from 1.0, and each dial lands on <c>(int)min(base × mult, ceiling)</c> — an integer
    /// TRUNCATION, not a round, which is what makes a 10 % dial at 1.15× stay at 11 rather than
    /// jumping to 12.</para>
    ///
    /// <para><b>One deliberate divergence: a dial is written only when its integer really moves.</b>
    /// WPF writes and reconciles all five links every 2 seconds. In this port a re-apply reaches
    /// <c>OverlaySurfaceSet.Place</c>, which calls <c>Present</c> — a walk of the OS's whole
    /// top-level z-order plus a hit test in both polarities, momentarily clearing click-through
    /// (<c>Overlay/Win32OverlayPresence.cs:547-576</c>). Spending that twice a second for a
    /// value that has not changed is the same cost that makes a 60 Hz module unportable (D84). The
    /// user-visible outcome is identical and the port does it about twenty times an hour instead of
    /// eighteen hundred. Recorded as D96.</para>
    ///
    /// <para><b>THE STAND-DOWN, and the one place this port has to be STRICTER than upstream.</b>
    /// Upstream skips only the WRITE (<c>MainWindow/MainWindow.StartStop.cs:509,515,523</c>): its
    /// five base values were captured once at <c>StartRampTimer</c> (<c>:420-424</c>), from the
    /// user's own settings, before any session could exist, and are never re-captured. This port
    /// captures a base the first time a dial is LINKED (D97, below), so a tick that ran during a
    /// session would capture the SESSION's imposed opacity as if the user had chosen it — and hand
    /// that back to the user at STOP. That is upstream's own #471/#476 defect, in the one place the
    /// port could still reproduce it. <b>So a standing-down dial is skipped whole: no read, no
    /// capture, no write</b>, which is upstream's observable outcome (the session owns the dial)
    /// arrived at through the port's own capture rule.</para>
    ///
    /// <para>Everything else keeps running, exactly as upstream's does: the cadence stays on the
    /// clock, progress and the multiplier keep climbing off the raw elapsed (<c>:495-503</c>, which
    /// upstream computes ABOVE its guard and unconditionally), custody taken before the session is
    /// kept, and <see cref="ReleaseWork"/> still owes every held dial back. Standing down is about
    /// WRITES, not about stopping.</para>
    /// </summary>
    private void Advance(int generation, bool raiseChanged)
    {
        // Gate first, and never while holding _hold: this is the whole lock discipline.
        if (!IsArmedFor(generation))
        {
            return;
        }

        // Read ONCE per tick, where upstream reads it (:492), so every branch below — the dial loop
        // and the completion check — decides on the same answer even if a session ends mid-tick.
        var sessionActive = _scriptedSessionActive();

        var preset = _preset.Current;
        var linked = new List<(IIntensityDial Dial, int Reading)>(_dials.Count);
        foreach (var dial in _dials)
        {
            if (IsLinked(preset, dial.Id) && !(sessionActive && StandsDownDuringSession(dial.Id)))
            {
                linked.Add((dial, dial.Read()));
            }
        }

        var now = _clock.UtcNow;
        DateTimeOffset started;
        var bases = new List<(IIntensityDial Dial, int Base)>(linked.Count);
        double progress;
        double multiplier;
        lock (_hold)
        {
            // WPF captures its five base values once, in StartRampTimer (:417-421). Here a dial's
            // base is captured the first time it is LINKED, which is the same value at session
            // start and a truer one afterwards: a dial nothing is driving still holds exactly what
            // the user last set it to, so a link ticked mid-session ramps from — and restores to —
            // the user's current choice rather than a value from before they changed it. D97.
            _startedAt ??= now;
            started = _startedAt.Value;
            foreach (var (dial, reading) in linked)
            {
                if (!_held.TryGetValue(dial.Id, out var captured))
                {
                    captured = reading;
                    _held[dial.Id] = captured;
                }

                bases.Add((dial, captured));
            }

            var elapsedMinutes = (now - started).TotalMinutes;
            progress = Math.Min(elapsedMinutes / preset.DurationMinutes, 1.0);
            multiplier = 1.0 + ((preset.Multiplier - 1.0) * RampCurves.ApplyCurve(progress, preset.Curve));
            _progress = progress;
            _currentMultiplier = multiplier;
        }

        var moved = new List<IIntensityDial>(bases.Count);
        foreach (var (dial, baseValue) in bases)
        {
            var target = (int)Math.Min(baseValue * multiplier, dial.Ceiling);
            if (dial.Read() != target)
            {
                dial.Write(target);
                moved.Add(dial);
            }
        }

        if (moved.Count > 0)
        {
            _dispatch(() =>
            {
                foreach (var dial in moved)
                {
                    dial.Reapply();
                }
            });
        }

        // Upstream tests COMPLETION on the raw linear progress, not the eased value, and says why in
        // as many words at :494-496: "the completion check below still uses the raw linear progress
        // so the ramp ends at its configured duration regardless of curve".
        // THE FOURTH GUARDED SITE (:549), and upstream says why in as many words at :544-548: a
        // shorter ramp calling StopEngine() here "tore down the manual services but left the preset
        // timer counting down (#444)". The port's Completed handler is literally Engine.Stop()
        // (SessionParticipant), so the same defect is one raise away.
        //
        // It is deliberately NOT latched while suppressed: upstream re-tests this condition on
        // every tick and has no "already announced" flag, so a ramp that reached full progress
        // during a session ends the manual engine on the FIRST tick after the session lets go.
        var announce = false;
        if (progress >= 1.0 && preset.EndSessionOnComplete && !sessionActive)
        {
            lock (_hold)
            {
                announce = !_completionAnnounced;
                _completionAnnounced = true;
            }
        }

        if (announce)
        {
            // Posted, as WPF posts its own stop (:549-553), and re-checked on arrival because a
            // delivered post may outlive the generation that queued it (contract §5.5).
            _dispatch(() =>
            {
                if (GenerationIsLive(generation))
                {
                    Completed?.Invoke();
                }
            });
        }

        if (raiseChanged)
        {
            RaiseChanged();
        }
    }

    private static bool IsLinked(IntensityRampPresetDocument preset, string dialId) => dialId switch
    {
        SpiralOverlayEffect.EffectId => preset.LinkSpiralOpacity,
        PinkFilterEffect.EffectId => preset.LinkPinkFilterOpacity,
        FlashImagesEffect.EffectId => preset.LinkFlashOpacity,
        // A dial this build does not know how to link is not linked. It cannot happen in the
        // product composition, and a default of "linked" would let an unrecognised dial be driven
        // by a switch that does not exist on any panel.
        _ => false,
    };

    /// <summary>
    /// <b>Which links stand down while a scripted session runs — and the guard is PER-LINK, not
    /// wholesale.</b>
    ///
    /// <para>Upstream writes <c>!sessionActive</c> into three of its five link branches and
    /// deliberately into neither of the other two: flash opacity
    /// (<c>MainWindow/MainWindow.StartStop.cs:509</c>), spiral opacity (<c>:515</c>) and pink
    /// filter opacity (<c>:523</c>) stand down; <b>master volume (<c>:529</c>) and subliminal
    /// volume (<c>:537</c>) keep ramping right through a session</b>. That asymmetry is not an
    /// oversight and the reason is checkable in upstream's own session code: a scripted session
    /// imposes exactly those three opacities and then drives them itself
    /// (<c>Services/Session/SessionEngine.cs:196-198</c> initialises <c>_currentFlashOpacity</c>,
    /// <c>_currentPinkOpacity</c> and <c>_currentSpiralOpacity</c>; <c>UpdateRampingValues</c> at
    /// <c>:564-661</c> climbs them), and it imposes NO volume at all. So the rule is "the ramp
    /// yields the dials a session is already driving", and the two audio links have no second
    /// driver to fight with — upstream's own words at <c>:490-491</c>: "sessions have their own
    /// built-in ramping … prevents the two systems from fighting and causing values to jump
    /// around".</para>
    ///
    /// <para>The port's three dials happen to be exactly upstream's three guarded links, so today
    /// every dial here stands down. It is still written per-link rather than as a wholesale
    /// "the ramp is off during a session", because the difference is real and load-bearing the day
    /// this port gains a master-volume dial: a wholesale guard would silently stop a link upstream
    /// keeps live. An id this build does not recognise does not stand down, for the same reason it
    /// is not linked — it cannot be driven, so it has nothing to yield.</para>
    /// </summary>
    private static bool StandsDownDuringSession(string dialId) => dialId switch
    {
        FlashImagesEffect.EffectId => true,
        SpiralOverlayEffect.EffectId => true,
        PinkFilterEffect.EffectId => true,
        _ => false,
    };

    /// <summary>A composed dial the user HAS linked and that is standing down right now — the
    /// difference between "you linked nothing" and "what you linked belongs to the running
    /// session", which is what <see cref="Engage"/>'s refusal has to say out loud.</summary>
    private bool AnyLinkStandsDown(IntensityRampPresetDocument preset)
    {
        foreach (var dial in _dials)
        {
            if (IsLinked(preset, dial.Id) && StandsDownDuringSession(dial.Id))
            {
                return true;
            }
        }

        return false;
    }

    private IIntensityDial? DialFor(string id)
    {
        foreach (var dial in _dials)
        {
            if (string.Equals(dial.Id, id, StringComparison.Ordinal))
            {
                return dial;
            }
        }

        return null;
    }

    private void MutateAndReapply(Action<IntensityRampPresetDocument> mutation)
    {
        _preset.Mutate(mutation);
        Refresh();
    }

    /// <summary>The tick, and the two liveness checks around it. A cadence that outlived its
    /// generation would drive a stopped session's dials, so the callback re-checks before doing
    /// anything and again before scheduling the next one (contract §5.5).</summary>
    private void OnTick(int generation)
    {
        if (!IsArmedFor(generation) || !GenerationIsLive(generation))
        {
            return;
        }

        Advance(generation, raiseChanged: true);

        if (!IsArmedFor(generation) || !GenerationIsLive(generation))
        {
            return;
        }

        RescheduleTick(generation);
    }

    private void EnsureTick(int generation)
    {
        lock (_hold)
        {
            if (_tick is not null)
            {
                return;
            }
        }

        RescheduleTick(generation);
    }

    /// <summary>
    /// Put the next sample on the clock. The handle is created OUTSIDE <see cref="_hold"/> and
    /// swapped in under it, so the lock is never held across a call into the clock; a handle that
    /// loses the swap is disposed rather than leaked, which is what makes a double
    /// <see cref="EnsureTick"/> converge on exactly one live timer.
    /// </summary>
    private void RescheduleTick(int generation)
    {
        var handle = _clock.Schedule(TickInterval, () => OnTick(generation));
        IDisposable? previous;
        var armed = IsArmedFor(generation);
        lock (_hold)
        {
            previous = _tick;
            _tick = armed ? handle : null;
        }

        previous?.Dispose();
        if (!armed)
        {
            handle.Dispose();
        }
    }
}
