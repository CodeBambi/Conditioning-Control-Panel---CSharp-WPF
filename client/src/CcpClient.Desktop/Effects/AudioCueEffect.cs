using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One audio cue that came due: which number it was and when.
///
/// <para><b>The file name is not here</b>, and that is the same rule <see cref="FlashEvent"/> and
/// <see cref="SubliminalEvent"/> hold: a subscriber, a log line and a bug report get a COUNT, never
/// the media. The path travels on <see cref="AudioCueFiring"/>, which only the audio presence ever
/// sees.</para>
/// </summary>
public sealed record AudioCueEvent(int Ordinal, DateTimeOffset At);

/// <summary>One cue's two halves: the content-free record subscribers get, and the clip only the
/// device is handed.</summary>
public sealed record AudioCueFiring(AudioCueEvent Event, string Path, float Volume);

/// <summary>
/// The body every ROLLED audio module has, written once — and the first module body in this port
/// whose <c>Live</c> depends on something outside the process.
///
/// <para><b>Why a shared body and not two copies (the shared-body lesson, applied before the copies
/// existed).</b> Mind Wipe and Brain Drain are the same machine with different numbers: a fixed
/// window on the clock, a probability rolled every window, one clip drawn from a folder, one player
/// at a time replaced by the next. Everything that differs between them —
/// <see cref="Window"/>, <see cref="TriggerProbability"/>, <see cref="Volume"/>, the pool and the
/// dial — is a hook here, and nothing else is duplicated.</para>
///
/// <para><b>A ROLL is not an INTERVAL, and this is the first port module of that shape.</b> Flash
/// Images and Subliminals compute the time to the next firing from their dial. These two do not:
/// they tick on a fixed window and roll a probability
/// (<c>Services/LockCard/MindWipeService.cs:741-742</c>,
/// <c>Services/LockCard/BrainDrainService.cs:185-188</c>). <see cref="PacedSessionEffect{TFiring}"/>
/// fits anyway, and fits WITHOUT modification, because the roll lands exactly on the hook the shared body
/// built for Subliminals: <b><see cref="PacedSessionEffect{TFiring}.Compose"/> may return
/// null</b>, meaning "this firing came due and produced nothing; count nothing, deliver nothing, and
/// re-schedule regardless". A lost roll IS that. The schedule stays a claim about the clock; the
/// roll is a claim about this window.</para>
///
/// <para><b>THE DOT, and the fifth thing it has meant.</b>
/// <see cref="OwnedSessionEffect.WorkIsRunning"/> here has two clauses and neither is redundant:</para>
/// <code>
/// Live  =  a firing is on the clock  &amp;&amp;  the OS reports an active render session for this process
/// </code>
/// <para>The dot has meant the CLOCK (paced), the SCREEN (continuous), CHANGE (moving) and CUSTODY
/// (non-drawing). All four are claims about state this process owns and can see. This one is a claim
/// about a resource the process does <b>not</b> own and cannot see directly — it has to ASK the
/// operating system, and the answer can change without this process doing anything at all. Call it
/// <b>REACH</b>: the module's output can get to the user.</para>
/// <para>Without the first clause a module whose tick died would keep claiming to be firing. Without
/// the second, a module would light its dot on a machine with no output device, which is the Pink
/// Filter failure in a medium nobody can check by looking — and upstream draws exactly the same line
/// in exactly the same place, refusing to play at all when the endpoint is down
/// (<c>MindWipeService.cs:771</c>, <c>BrainDrainService.cs:211</c>: "endpoint down — stay quiet,
/// don't spin").</para>
///
/// <para><b>What an empty clip folder does NOT do.</b> It does not darken the dot. That case takes
/// the Subliminals answer — <c>Degraded</c> in the arm result, <c>Live</c> in the dot — because a
/// pool is CONTENT and a device is a CHANNEL: dropping a clip in mid-session starts producing sound
/// with no re-arm, whereas no render session means nothing this module does can reach anybody.</para>
///
/// <para><b>Threading.</b> Inherited unchanged. The roll and the draw happen in
/// <see cref="PacedSessionEffect{TFiring}.Compose"/>, outside the gate, because the draw may stat a
/// folder. The cue is delivered on the signal thread through the one dispatch boundary, and the
/// presence beneath it has no thread affinity (upstream says the same of NAudio, and stops audio
/// from its panic path for exactly that reason — <c>MindWipeService.cs:243-251</c>).</para>
/// </summary>
public abstract class AudioCueEffect : PacedSessionEffect<AudioCueFiring>
{
    private readonly IAudioCuePool _pool;
    private readonly IAudioPresence _presence;
    private readonly Random _random;
    private CapabilityState? _lastCue;

    protected AudioCueEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        string operationName,
        IAudioCuePool pool,
        IAudioPresence presence,
        Random? random = null)
        : base(owner, signal, clock, operationName)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(presence);
        _pool = pool;
        _presence = presence;
        _random = random ?? new Random();
    }

    /// <summary>The audio capability this module plays through. Public so a panel can render the OS's
    /// own answer rather than a sentence about a platform — the same reason every drawing module
    /// exposes its surface.</summary>
    public IAudioPresence Presence => _presence;

    /// <summary>How many clips this module's folder holds. The panel shows it so an empty folder has
    /// an answer.</summary>
    public int ClipCount => _pool.ActiveCount;

    /// <summary>Where this module's clips are read from. A path, never a file name.</summary>
    public string ClipFolder => _pool.Folder;

    /// <summary>Cues that really played — the ones a roll won AND a clip existed for. A lost roll is
    /// not one of these, which is upstream's own counting (nothing is counted on a lost roll or an
    /// empty pool).</summary>
    public int CueCount => FireCount;

    /// <summary>The most recent cue, or null if none has happened yet.</summary>
    public AudioCueEvent? Last => LastFiring?.Event;

    /// <summary>
    /// What the audio presence said about the last cue this module delivered, verbatim — including
    /// the <c>Degraded</c> case where a player really started and the OS stopped confirming the
    /// render session. Null before anything has been cued. The panel renders this, so what the user
    /// reads about sound is the capability's own typed outcome rather than a fixed sentence.
    /// </summary>
    public CapabilityState? LastCue
    {
        get { lock (Gate) { return _lastCue; } }
    }

    /// <summary>Raised on the UI thread, inside the dispatch boundary, once per cue that really
    /// played.</summary>
    public event Action<AudioCueEvent>? Fired;

    /// <summary>The fixed tick this module rolls on. Not derived from the dial — that is the whole
    /// difference between a rolled module and a paced one.</summary>
    protected abstract TimeSpan Window { get; }

    /// <summary>The chance that THIS window fires, from this module's own dial.</summary>
    protected abstract double TriggerProbability { get; }

    /// <summary>The gain a cue plays at, 0..1.</summary>
    protected abstract float Volume { get; }

    /// <summary>The fixed window. <see cref="PacedSessionEffect{TFiring}"/> asks for an interval and
    /// this module's interval is a constant, which is legitimate and is the honest answer here — the
    /// dial does not change the spacing, it changes the odds.</summary>
    protected sealed override TimeSpan NextInterval() => Window;

    /// <summary>
    /// A claim about the clock AND about a resource this process does not own. See the class remarks:
    /// this is the dot's fifth meaning and the second clause is the new half.
    /// </summary>
    protected sealed override bool WorkIsRunning => ScheduleArmed && _presence.IsRendering;

    /// <summary>
    /// One window comes due. Upstream's tick body, in upstream's order: the empty-pool return first
    /// (<c>MindWipeService.cs:704-708</c>, <c>BrainDrainService.cs:181</c>), then the roll
    /// (<c>:741-742</c>, <c>:185</c>), then the draw (<c>:755</c>, <c>:197</c>). The suppressed-output
    /// check sits with them — upstream applies it one call later, inside <c>PlayAudio</c>
    /// (<c>:770</c>, <c>:211</c>), which is the same outcome and one less object built.
    /// </summary>
    /// <summary>
    /// Does a window that comes due while this module's own clip is still playing get SKIPPED
    /// rather than displacing it? Default false, which is the displacing behaviour the audio slot
    /// has always had.
    ///
    /// <para>Only Brain Drain turns this on. Its clips are long enough to outlive a window (500 ms
    /// in high-refresh mode, 5 s otherwise), so displacement chops a track mid-word; Mind Wipe's
    /// clips are meant to replace each other.</para>
    /// </summary>
    /// <summary>
    /// Does arming re-scan the clip folder? Default false, which is the cache-once behaviour the
    /// pool has always had.
    ///
    /// <para>Brain Drain turns this on because its folder is a plain drop target that lives
    /// OUTSIDE the app: a user adds a clip while the app runs and expects it to appear. Upstream
    /// re-scans on every session start for exactly that reason and records the cost as free next
    /// to what a start already does (BrainDrainService.cs:222-227). Mind Wipe does NOT - upstream
    /// re-scans it only from its own panel, never from Start - so this stays opt-in rather than
    /// becoming a behaviour the shared base hands to both.</para>
    /// </summary>
    protected virtual bool RescanOnArm => false;

    protected virtual bool SkipWhileSounding => false;

    protected sealed override AudioCueFiring? Compose()
    {
        // "Endpoint down — stay quiet, don't spin" (MindWipeService.cs:771). Not counted, not
        // played, and the schedule keeps running so a device that comes back is picked up at the
        // next window rather than at the next session.
        if (!_presence.IsRendering)
        {
            return null;
        }

        if (_pool.ActiveCount == 0)
        {
            return null;
        }

        // A module that must not interrupt itself skips the window entirely while its own clip is
        // still sounding — the roll is not even taken, so this cannot be read as "rolled and lost".
        // Upstream guards the same way and in the same place, before the draw
        // (BrainDrainService.PlayAudioNow: `lock (_audioLock) { if (_waveOut != null) return; }`).
        //
        // OPT-IN, because displacement is CORRECT for the other module here. Mind Wipe is meant to
        // cut itself off — upstream fixed only Brain Drain — and a guard applied to the shared base
        // would have quietly changed a behaviour nobody asked about.
        if (SkipWhileSounding && _presence.IsSounding(Id))
        {
            return null;
        }

        // The roll. Strictly less-than, upstream's own comparison, so probability 0 never fires and
        // a probability at or above 1 always does (upstream's comment at :740).
        if (_random.NextDouble() >= TriggerProbability)
        {
            return null;
        }

        var path = _pool.Draw();
        return path is null ? null : new AudioCueFiring(new AudioCueEvent(0, default), path, Volume);
    }

    /// <inheritdoc/>
    protected sealed override AudioCueFiring Stamp(AudioCueFiring firing, int ordinal, DateTimeOffset at) =>
        firing with { Event = firing.Event with { Ordinal = ordinal, At = at } };

    /// <summary>
    /// Put the clip on the device, then raise <see cref="Fired"/> — that order, for the same reason
    /// the drawing modules place their surface before they notify: the audible half is the user's
    /// outcome and must not be hostage to whatever a UI subscriber does.
    ///
    /// <para>The cue's own typed outcome is REMEMBERED rather than discarded. It is the only place a
    /// user or a bug report can learn that a clip started and the OS stopped confirming the session,
    /// which is precisely the state a module that trusted its own call would show as working.</para>
    /// </summary>
    protected sealed override void Deliver(AudioCueFiring firing)
    {
        var outcome = _presence.Cue(new AudioCue(Id, firing.Path, firing.Volume));
        lock (Gate)
        {
            _lastCue = outcome;
        }

        Fired?.Invoke(firing.Event);
    }

    /// <summary>
    /// Stop this module's clip. Upstream's <c>StopCurrentAudio</c>, which both services call
    /// unconditionally and FIRST on the way down so a panic can always reach the audio even with a
    /// wedged UI thread (<c>MindWipeService.cs:243-251</c>,
    /// <c>BrainDrainService.cs:149-158</c>). Here the slot is the module id, so one module's stop can
    /// never silence the other's.
    /// </summary>
    protected override void OnDisarmed() => _presence.Silence(Id);

    /// <summary>
    /// Narrow the arm result to what this module can honestly claim — and the two narrowings are
    /// deliberately DIFFERENT states, because a missing channel and missing content are not the same
    /// failure.
    /// </summary>
    protected override CapabilityState Ready(CapabilityState scheduled)
    {
        if (scheduled is not CapabilityState.Available)
        {
            return scheduled;
        }

        // THE DEVICE IS OPENED HERE, and the placement is a compromise the spine forced — recorded
        // rather than hidden.
        //
        // It must not be at app start: a render device opened in phase 3 would take a WASAPI session
        // (and a row in the user's volume mixer) on every launch, including every launch by a user
        // who never enables either audio module — and both of them ship OFF. It must not be in
        // Compose(): a device open on a clock callback is a half-second native call on a pool thread
        // once per window. Arming is the right moment, and Ready is the ONLY hook this module has
        // that runs on an arm — Engage is sealed by PacedSessionEffect and Arm is not virtual.
        //
        // The consequence, and the reason for the RaiseChanged below: OwnedSessionEffect.Arm raises
        // Changed BEFORE it calls Ready, so a UI that repainted on that notification would have read
        // the dot while the device was still shut. The second notification is what makes the row's
        // dot correct on the first frame after START. It costs one extra repaint per arm — an arm is
        // a gesture, not a firing, so that is affordable where a per-firing notification would not be.
        //
        // A SIXTH module will hit this again. The spine has no per-module "acquire your capability"
        // hook that runs before the arm's notification, and that is the gap.
        // The clip folder is re-read here, not on the clock. One Directory enumeration per ARM is
        // free next to the device open on the next line; the same read on a window callback would
        // be filesystem work on a pool thread once per window. Before the open, so a folder that
        // is slow to stat never sits behind a native audio call.
        if (RescanOnArm)
        {
            _pool.Invalidate();
        }

        _presence.Open();
        RaiseChanged();

        if (!_presence.IsRendering)
        {
            // The whole output channel is gone. Unavailable, not Degraded: there is no surviving
            // half to name. The presence's own reason is carried through verbatim where it has one,
            // so a Linux run reads the manual gate and a device-less Windows run reads the endpoint
            // count — never a sentence this module made up about a platform.
            var detail = _presence.LastOpen switch
            {
                CapabilityState.Unavailable u => u.Reason.Detail,
                CapabilityState.DependencyMissing m => m.Reason.Detail,
                CapabilityState.Faulted f => f.Reason.Detail,
                CapabilityState.PermissionRequired p => p.Reason.Detail,
                _ => "the audio capability has not confirmed a render session for this process",
            };

            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.AudioRenderUnavailable,
                $"the '{Id}' module is armed and nothing it plays can reach anybody: {detail}"));
        }

        if (_pool.ActiveCount == 0)
        {
            return new CapabilityState.Degraded(
                "the schedule is armed and rolling on a confirmed render session, and every window that "
                + "wins its roll will play nothing",
                new CapabilityReason(
                    EffectReasonCodes.AudioNoClip,
                    $"there is no audio clip in {_pool.Folder}, so nothing will be played and nothing "
                    + "counted. Dropping a .mp3/.wav/.ogg in there is picked up at the next window, "
                    + "without restarting the session"));
        }

        return scheduled;
    }
}
