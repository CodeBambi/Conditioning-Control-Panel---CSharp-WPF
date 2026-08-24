using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One flash that came due: how many images the draw actually produced, and when.
/// Content-free by construction — a COUNT, never a file name (the media-logging rule the DTRH
/// manifest already holds, <c>Features/Dtrh/DtrhUserMedia.cs</c>: counts only, never names).
/// </summary>
public sealed record FlashEvent(int Ordinal, int ImagesDrawn, DateTimeOffset At)
{
    /// <summary>True when the draw came back empty: the pool has nothing active in it.</summary>
    public bool PoolWasEmpty => ImagesDrawn == 0;
}

/// <summary>
/// One flash's two halves: the content-free record subscribers get, and the paths only the surface
/// is ever handed. They travel together inside the effect and separate at the projection, which is
/// the mechanism behind "a flash is a COUNT everywhere a log or a UI can see it".
/// </summary>
public sealed record FlashFiring(FlashEvent Event, IReadOnlyList<string> Drawn);

/// <summary>
/// <b>Flash Images</b> — WPF's first EFFECTS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:484</c>), the first service <c>StartEngine</c> starts
/// (<c>MainWindow/MainWindow.StartStop.cs:178</c>) and the first one <c>StopEngineCore</c> stops
/// (<c>:305</c>, "Stop flash first").
///
/// <para><b>WHAT THIS PORTS.</b> WPF's flash has two halves. The first is a scheduler over the
/// user's image pool: an interval derived from a dial, a variance band, a floor, and a draw of N
/// images per firing. That half is pure, and it is ported here, exactly. The second half
/// puts those images on the screen ABOVE every other application — one layered, always-on-top,
/// <c>WS_EX_TRANSPARENT</c> click-through window per flash, re-asserted to <c>HWND_TOPMOST</c> as
/// other layers fight it (<c>Services/Flash/FlashService.cs:3615</c>, <c>:3667-3668</c>,
/// <c>:3862-3868</c>, <c>:206-240</c>). That half is a platform capability with its own evidence
/// (<see cref="Overlay.IOverlayPresence"/>), and this module hands the drawn paths to it.</para>
///
/// <para><b>The drawing is downstream, and it never leads.</b> The pacing, the pool, the count and
/// the stop are exactly what they were before a surface existed, and they do not consult one: a
/// flash comes due, draws from the pool, counts, re-schedules and — if there is somewhere to draw —
/// appears. Where there is no overlay (every non-Windows build refuses one honestly, divergence D56)
/// the flash still comes due, still counts and still stops; it is a flash nobody sees, which is
/// neither a crash nor a refusal to run. Nothing in this class pretends the difference away: the
/// presenter keeps the surface's typed <c>CapabilityState</c> verbatim.</para>
///
/// <para><b>What is left in this file.</b> The arm, the disarm, the one-shot, the
/// generation, the counter, the dot and the projection moved to
/// <see cref="PacedSessionEffect{TFiring}"/> when Subliminals was built and turned out to need
/// exactly the same body. Nothing here changed behaviour — every earlier fact passes
/// unaltered — and what remains is what is genuinely Flash Images': its rack key, its dial, its
/// pacing law, its pool, and the one ordering rule below.</para>
/// </summary>
public sealed class FlashImagesEffect : PacedSessionEffect<FlashFiring>
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:484</c>), and the same
    /// key its quick-toggle switches on (<c>MainWindow.Presets.cs:1250</c>).</summary>
    public const string EffectId = "flash";

    /// <summary>The row's label as the shipping app shows it (<c>StudioTabView.xaml.cs:484</c>,
    /// confirmed in the live v6.8.1 rack survey — <c>wpf-surface-reachability.md</c> §8.3 @ 7527243e7).</summary>
    public const string DisplayTitle = "Flash Images";

    private readonly IFlashImagePool _pool;
    private readonly PersistenceStore<SessionPresetDocument> _preset;
    private readonly IFlashSurface? _surface;
    private readonly EffectSounds? _sounds;
    private readonly Random _random;

    /// <param name="sounds">Where this flash's clip is played, or null in a composition with no app
    /// audio - which is every test host that wants no device. See <see cref="Deliver"/>.</param>
    public FlashImagesEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        IFlashImagePool pool,
        PersistenceStore<SessionPresetDocument> preset,
        Random? random = null,
        IFlashSurface? surface = null,
        EffectSounds? sounds = null)
        : base(owner, signal, clock, "flash-schedule")
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(preset);
        _pool = pool;
        _preset = preset;
        _random = random ?? new Random();
        _surface = surface;
        _sounds = sounds;
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.FlashEnabled;

    /// <summary>Flashes that have come due since this effect was first armed.</summary>
    public int FlashCount => FireCount;

    /// <summary>The most recent firing, or null if none has happened yet.</summary>
    public FlashEvent? Last => LastFiring?.Event;

    /// <summary>
    /// Raised on the UI thread, inside the dispatch boundary, once per flash that really came
    /// due. The surface binds this; nothing else may.
    /// </summary>
    public event Action<FlashEvent>? Fired;

    /// <summary>
    /// What the last flash's clip answered, verbatim, or null when no flash has tried to make a
    /// sound yet. An empty clip folder and a dead endpoint both look like a typed
    /// <see cref="Audio.SoundOutcome.Unavailable"/> from here, and each names itself - neither is
    /// rendered as silence with no reason.
    /// </summary>
    public Audio.SoundOutcome? LastSound => _sounds?.LastFlash;

    /// <summary>Where the user's flash clips are read from, or null in a composition with no app
    /// audio. A path, never a file name - the answer to "where do I put them", which matters here
    /// because this build ships none.</summary>
    public string? ClipFolder => _sounds?.FlashClipFolder;

    /// <inheritdoc/>
    public override void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        _preset.Mutate(p => p.FlashEnabled = enabled);
        RaiseChanged();
    }

    /// <inheritdoc/>
    protected override TimeSpan NextInterval() =>
        FlashSchedule.NextInterval(_preset.Current.FlashesPerHour, _random);

    /// <summary>
    /// The draw. WPF takes <c>SimultaneousImages</c> independent picks per firing, read at the
    /// moment the flash fires rather than when it was scheduled (<c>FlashService.cs:586</c>).
    ///
    /// <para>Never null: an empty pool is still a flash. WPF's own empty outcome returns an empty
    /// list and the flash shows nothing (<c>FlashService.cs:2589-2593</c>, <c>:585-597</c>) — it is
    /// counted, it is reported, and the schedule keeps running. Subliminals is the module where
    /// that is NOT true, which is why <see cref="PacedSessionEffect{TFiring}.Compose"/> is
    /// nullable at all.</para>
    /// </summary>
    protected override FlashFiring Compose()
    {
        var drawn = _pool.Draw(_preset.Current.ImagesPerFlash);
        return new FlashFiring(new FlashEvent(0, drawn.Count, default), drawn);
    }

    /// <inheritdoc/>
    protected override FlashFiring Stamp(FlashFiring firing, int ordinal, DateTimeOffset at) =>
        firing with { Event = firing.Event with { Ordinal = ordinal, At = at } };

    /// <summary>
    /// The surface is drawn FIRST and <see cref="Fired"/> raised second: the visible half of a flash
    /// is the user's outcome, and it must not be hostage to whatever a UI subscriber does. The order
    /// is a property a fact holds (<c>FlashSurfacePresenterTests</c>), not a comment — swapping these
    /// two lines reds the suite.
    ///
    /// <para><b>THE CLIP IS PLAYED AFTER THE PICTURE, AND UPSTREAM PLAYS IT BEFORE.</b> Upstream's
    /// <c>ShowImages</c> plays the sound at <c>Services/Flash/FlashService.cs:1042</c> and spawns its
    /// windows from <c>:1073</c> onward, and it HAS to: the clip's own length is what it uses as the
    /// flash's duration (<c>duration = PlaySound(...)</c> at <c>:1042</c>, then
    /// <c>lifetimeMs = (int)(duration * 1000) + 1000</c> at <c>:1073</c>). This port does not have
    /// that link (see below), so all upstream's order would buy here is putting an audio device in
    /// front of a picture - and player construction on this port's backend blocks its caller for up
    /// to two seconds against a wedged endpoint (<c>Audio/AudioSeams.cs:244-245</c>), on this very
    /// thread. The rule above already says the visible half of a flash must not be hostage to
    /// anything; an audio endpoint is exactly such a hostage-taker. <b>A divergence, recorded rather
    /// than smoothed away.</b></para>
    ///
    /// <para><b>Four halves of upstream's flash audio are NOT here, each refused with its
    /// reason.</b> (1) The <c>FlashAudioEnabled</c> switch (<c>Models/AppSettings.cs:925-926</c>,
    /// default <c>true</c>) - this port's flash dials live in documents outside this module, so the
    /// DEFAULT outcome matches and the switch is a later row; emptying the clip folder is what turns
    /// it off today. (2) The duration link at <c>:1042</c>/<c>:1073</c> - the arbitration reports no
    /// clip length at all (<c>SoundOutcome.Started</c> carries a channel and a generation and
    /// nothing else), so linking would mean inventing a number. (3) The audio duck
    /// (<c>:1050-1064</c>) - this port's duck sink is deliberately an <c>UnavailableDuckSink</c>
    /// until cross-app ducking has an owner decision (<c>Audio/AudioParticipant.cs</c>'s own
    /// composition). (4) <c>FlashAudioPlaying</c> (<c>:1047</c>), which puts the clip's text in the
    /// companion's speech bubble - there is no avatar window in this build. What IS ported is the
    /// gate that matters most: <b>once per flash EVENT</b>. Upstream's
    /// <c>_soundPlayingForCurrentFlash</c> latch (<c>:1037</c>, <c>:1041</c>) exists to keep hydra
    /// spawns silent, and this port has no hydra at all - one flash, one <see cref="Deliver"/>, one
    /// clip.</para>
    ///
    /// <para><b>Why the drawn paths ride on the firing and not on <see cref="FlashEvent"/>.</b> The
    /// event is content-free by construction — a COUNT, never a file name, which is the
    /// media-logging rule the whole port holds. The surface needs the paths themselves, so they are
    /// handed straight to it, on the one thread that may touch a native window, and they are never
    /// carried by anything a log or a UI can subscribe to.</para>
    /// </summary>
    protected override void Deliver(FlashFiring firing)
    {
        _surface?.Show(firing.Drawn);
        _sounds?.Flash();
        Fired?.Invoke(firing.Event);
    }

    /// <summary>
    /// Take every surface off the screen. WPF's stop closes every live flash window
    /// (<c>FlashService.cs:3878-3884</c>), and that is the half of stop the user can see: a panic
    /// button that leaves pictures on the screen has not stopped anything.
    ///
    /// <para>It goes through the SAME dispatch boundary as the draw, because a native window
    /// belongs to the thread that made it, and disarm is reached from the UI thread (the button,
    /// the rack toggle) AND from a teardown thread. Skip-until-bound applies: with no UI there is
    /// no surface, because the draw could never have happened either.</para>
    /// </summary>
    protected override void OnDisarmed()
    {
        if (_surface is null)
        {
            return;
        }

        Signal.Post(_surface.HideAll);
    }
}
