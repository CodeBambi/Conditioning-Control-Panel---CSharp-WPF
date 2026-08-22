using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One subliminal that came due: which number it was, how long its card was on screen, and when.
///
/// <para><b>The phrase is not here.</b> Content-free by construction, the same rule
/// <see cref="FlashEvent"/> holds: a subscriber, a log line and a bug report get a COUNT and a
/// duration, never the words. The text travels on <see cref="SubliminalFiring"/>, which only the
/// surface ever sees.</para>
/// </summary>
public sealed record SubliminalEvent(int Ordinal, int HeldMilliseconds, DateTimeOffset At);

/// <summary>One subliminal's two halves: the content-free record subscribers get, and the card only
/// the surface is handed.</summary>
public sealed record SubliminalFiring(SubliminalEvent Event, SubliminalCard Card);

/// <summary>
/// <b>Subliminals</b> — WPF's third EFFECTS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:488-489</c>), started by <c>StartEngine</c> at
/// <c>MainWindow/MainWindow.StartStop.cs:186-187</c> and stopped by <c>StopEngineCore</c> at
/// <c>:337</c>.
///
/// <para><b>Why this module exists in the port (SP-101).</b> Not because subliminals were the next
/// most valuable feature — because the session spine had exactly one implementation and thirteen
/// more were queued behind it. This is the module that had to be written to find out whether the
/// first one's shape was a template or an accident. What it found is in
/// <see cref="PacedSessionEffect{TFiring}"/>, which is now where the shared body lives.</para>
///
/// <para><b>Where it genuinely differs from Flash Images</b>, each difference being a place a copied
/// implementation would have been wrong:</para>
/// <list type="number">
/// <item>The dial counts per MINUTE and the floor is one second, not per hour and three
/// (<see cref="SubliminalSchedule"/>).</item>
/// <item>The module ships <b>off</b> (<c>CCP.Core/Models/AppSettings.cs:1234</c>), and upstream's
/// start body reaches it only when the dial is on (<c>MainWindow.StartStop.cs:186</c>) where it calls
/// the flash service unconditionally (<c>:178</c>). Here the session arms every module and the
/// module's own dial decides — same outcome, and the arm result now SAYS which happened.</item>
/// <item>An empty pool is <b>not a firing</b>: <c>FlashSubliminal</c> returns at
/// <c>Services/Subliminal/SubliminalService.cs:207-212</c>, before the counter and the event at
/// <c>:611-612</c>, and the timer is re-armed regardless (<c>:189-201</c>). Flash Images counts an
/// empty flash; this one does not. That single difference is why
/// <see cref="PacedSessionEffect{TFiring}.Compose"/> may return null.</item>
/// <item>The pool is words in the module's own settings, not files in a folder.</item>
/// <item>The card is one full-screen surface for a fifth of a second, not a burst of placed
/// rectangles for six.</item>
/// </list>
///
/// <para><b>What is NOT ported</b>, and is not pretended: the linked whisper audio and its ducking
/// (<c>:216-240</c>), the haptic anticipation delay (<c>:577-600</c>), the XP awards (<c>:243</c>,
/// <c>:255</c>), the "Bambi Freeze"/"Bambi Reset" follow-up (<c>:276-404</c>), the remote-control and
/// Deeper one-shot entry points (<c>:258-275</c>), focus-stealing, solid mode, the compositor layer,
/// and multi-monitor. Each is a subsystem this port does not have; a card that appeared without them
/// is still the module's core behaviour, and every one of them is recorded as a divergence rather
/// than stubbed.</para>
/// </summary>
public sealed class SubliminalsEffect : PacedSessionEffect<SubliminalFiring>
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:486</c>), and the same key
    /// its quick-toggle switches on (<c>MainWindow/MainWindow.Presets.cs:1253</c>).</summary>
    public const string EffectId = "subliminal";

    /// <summary>The row's label as the shipping app shows it (<c>StudioTabView.xaml.cs:486</c>).</summary>
    public const string DisplayTitle = "Subliminals";

    /// <summary>WPF's milliseconds per duration frame — its own comment says "~16.6 ms per frame" and
    /// its arithmetic uses 17 (<c>SubliminalService.cs:615-617</c>). The integer is the behaviour.</summary>
    public const int MillisecondsPerFrame = 17;

    /// <summary>WPF's floor on the hold, in milliseconds (<c>SubliminalService.cs:616-617</c>).</summary>
    public const int MinimumHoldMilliseconds = 100;

    private readonly ISubliminalPhrasePool _pool;
    private readonly PersistenceStore<SubliminalPresetDocument> _preset;
    private readonly ISubliminalSurface? _surface;
    private readonly Random _random;
    private readonly IHapticLimb? _haptics;

    /// <param name="owner">This module's operation owner.</param>
    /// <param name="signal">The one boundary a state change is allowed to arrive on.</param>
    /// <param name="clock">The clock the schedule paces on.</param>
    /// <param name="pool">Where the phrases come from.</param>
    /// <param name="preset">This module's persisted dials.</param>
    /// <param name="random">The jitter's source; injected so a fact can pin the arithmetic.</param>
    /// <param name="surface">Where the card goes.</param>
    /// <param name="haptics">SP-126: the haptic limb, told once per card shown, with the card's own
    /// words. Null is ABSENT rather than silent.</param>
    public SubliminalsEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        ISubliminalPhrasePool pool,
        PersistenceStore<SubliminalPresetDocument> preset,
        Random? random = null,
        ISubliminalSurface? surface = null,
        IHapticLimb? haptics = null)
        : base(owner, signal, clock, "subliminal-schedule")
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(preset);
        _pool = pool;
        _preset = preset;
        _random = random ?? new Random();
        _surface = surface;
        _haptics = haptics;
    }

    /// <summary>
    /// How long one card holds at its target opacity: WPF's
    /// <c>Math.Max(100, SubliminalDuration * 17)</c> (<c>SubliminalService.cs:615-617</c>). At the
    /// shipped default of 2 frames the multiply gives 34 and the floor wins, so the dial does
    /// nothing at all until it passes 6 — kept exactly, because a user's existing number has to keep
    /// meaning what it meant.
    /// </summary>
    public static int HoldMilliseconds(int durationFrames) =>
        Math.Max(MinimumHoldMilliseconds, durationFrames * MillisecondsPerFrame);

    /// <summary>
    /// The whole time a card occupies the screen: WPF's 50 ms fade-in, the hold, and its 50 ms
    /// fade-out (<c>SubliminalService.cs:1253-1281</c>). The port shows the card for this long at a
    /// constant opacity rather than ramping — the divergence is
    /// <see cref="SubliminalSurfacePresenter"/>'s, and the DURATION is the part that is kept exact.
    /// </summary>
    public static TimeSpan CardLifetime(int durationFrames) =>
        TimeSpan.FromMilliseconds(
            SubliminalSurfacePresenter.FadeInMilliseconds
            + HoldMilliseconds(durationFrames)
            + SubliminalSurfacePresenter.FadeOutMilliseconds);

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>Subliminals that really came due — the ones with a phrase behind them. A firing over
    /// an empty pool is not one of these, which is upstream's own counting
    /// (<c>SubliminalService.cs:207-212</c> vs <c>:611</c>).</summary>
    public int SubliminalCount => FireCount;

    /// <summary>The most recent subliminal, or null if none has happened yet.</summary>
    public SubliminalEvent? Last => LastFiring?.Event;

    /// <summary>How many active phrases the pool currently has. The module panel shows this so an
    /// empty pool has an answer, exactly as the flash panel names its images folder.</summary>
    public int ActivePhraseCount => _pool.ActiveCount;

    /// <summary>Raised on the UI thread, inside the dispatch boundary, once per subliminal that
    /// really came due.</summary>
    public event Action<SubliminalEvent>? Fired;

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

    /// <inheritdoc/>
    protected override TimeSpan NextInterval() =>
        SubliminalSchedule.NextInterval(_preset.Current.PerMinute, _random);

    /// <summary>
    /// Draw one phrase and compose its card — or return null, which is this module's whole
    /// difference from Flash Images. WPF logs "No active subliminal texts" and returns before it
    /// counts anything or raises anything (<c>SubliminalService.cs:207-212</c>), while the timer is
    /// re-armed by the caller regardless (<c>:189-201</c>). So: nothing counted, nothing shown, and
    /// the schedule keeps running.
    /// </summary>
    protected override SubliminalFiring? Compose()
    {
        var phrase = _pool.Draw();
        if (phrase is null)
        {
            return null;
        }

        var preset = _preset.Current;
        var hold = HoldMilliseconds(preset.DurationFrames);
        return new SubliminalFiring(
            new SubliminalEvent(0, hold, default),
            new SubliminalCard(phrase, preset.OpacityPercent, CardLifetime(preset.DurationFrames)));
    }

    /// <inheritdoc/>
    protected override SubliminalFiring Stamp(SubliminalFiring firing, int ordinal, DateTimeOffset at) =>
        firing with { Event = firing.Event with { Ordinal = ordinal, At = at } };

    /// <summary>
    /// The card is shown FIRST and <see cref="Fired"/> raised second, for the same reason the flash
    /// does it in that order: the visible half is the user's outcome and must not be hostage to
    /// whatever a UI subscriber does.
    /// </summary>
    protected override void Deliver(SubliminalFiring firing)
    {
        _surface?.Show(firing.Card);

        // SP-126, census sites 14 and 15. Upstream fires TriggerSubliminalPatternAsync from both
        // branches of FlashSubliminal — the with-whisper one (SubliminalService.cs:230) and the
        // silent one inside TriggerSubliminalWithHapticPattern (:588) — and this port has no whisper
        // audio, so the two are one path here. The PHRASE travels rather than a duration, because
        // the duration is keyed off the trigger's own wording (HapticService.cs:899-909).
        //
        // WHAT THE COLLAPSE LOSES, recorded rather than invented: upstream fires the haptic FIRST
        // and delays the visual by SubliminalAnticipationMs (250 ms, 1300 on Buttplug —
        // HapticService.cs:88); this port shows the card immediately. Reproducing the anticipation
        // would mean delaying a shipped module's PICTURE by a quarter second to lead a vibration
        // that no admitted provider can deliver, which is a visible regression bought for nothing.
        _haptics?.SubliminalShown(firing.Card.Text);
        Fired?.Invoke(firing.Event);
    }

    /// <summary>
    /// A module that is paced but cannot show anything says so, in type
    /// (<see cref="EffectReasonCodes.SubliminalNoActivePhrase"/>). This is the hook SP-098's review
    /// asked for and the reason <see cref="ISessionEffect.Arm"/> stopped returning void: the
    /// schedule really is armed — <c>Degraded</c>, never <c>Unavailable</c> — and the surviving
    /// semantics say exactly which half holds.
    /// </summary>
    protected override CapabilityState Ready(CapabilityState scheduled) =>
        scheduled is CapabilityState.Available && _pool.ActiveCount == 0
            ? new CapabilityState.Degraded(
                "the schedule is armed and paced, and every firing will be a no-op",
                new CapabilityReason(
                    EffectReasonCodes.SubliminalNoActivePhrase,
                    "no phrase in the subliminal pool is active, so nothing will be shown and nothing counted"))
            : scheduled;

    /// <summary>Take the card off the screen. WPF blanks and hides its keep-alive windows on stop
    /// (<c>SubliminalService.cs:116-127</c>) — the half of stop the user can see.</summary>
    protected override void OnDisarmed()
    {
        if (_surface is null)
        {
            return;
        }

        Signal.Post(_surface.HideAll);
    }
}
