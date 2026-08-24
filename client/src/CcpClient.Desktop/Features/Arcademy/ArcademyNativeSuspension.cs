using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// <b>Slice 5's wire: the port's mandatory video freezes a running Arcademy class, and the class
/// comes back when the video is over.</b> Upstream keeps this in <c>HookVideoEvents</c>
/// (<c>ArcademyHostService.cs:1648-1671</c>), subscribed BEFORE the window is shown
/// (<c>:210-212</c>) and unsubscribed in <c>DisposeAll</c> (<c>:2014</c>).
///
/// <para><b>Upstream watches three native producers and this build has one.</b> The mandatory
/// video is real here. <c>AudioOnlySession</c> (<c>:1832-1852</c>) has no setting behind it and
/// the browser-media watch (<c>HookBrowserVideoEvents</c>, <c>:1682-1712</c>) has no browser
/// behind it — this port has no general-purpose browsing surface and no browser-media session
/// concept at all (<c>client/docs/port-completeness-census.md:772</c>). Neither is stubbed: a
/// producer wired to a constant <c>false</c> would report "no video is playing" with the same
/// confidence whether or not one was, which is the shape this port has already refused twice on
/// this row.</para>
///
/// <para><b>Why it follows the MODULE rather than the surface.</b> <c>IVideoSurface</c> reports
/// state and raises nothing; the module is what upstream's <c>App.Video</c> is — the thing that
/// starts a clip and knows when one ended. <see cref="MandatoryVideoEffect.Playing"/> is the live
/// read (the surface is up, <c>Effects/VideoSurfacePresenter.cs</c>), and the two signals below
/// bracket every transition of it this module can produce: a clip that really starts raises
/// <c>Fired</c> (<c>MandatoryVideoEffect.cs:310</c>), and every way one goes away — the natural
/// end, the max-length cap, the surface dropping it, and the session disarming the module — lands
/// on <c>Changed</c> (<c>:420</c> via <c>OnClipEnded</c>, and <c>Session/OwnedSessionEffect.cs:237</c>
/// after <c>OnDisarmed</c> has taken the picture down).</para>
///
/// <para><b>LEVEL, NOT EDGE.</b> <c>Changed</c> is also raised for dial writes, refusals and
/// arming, so this compares the module's live state against what the session was last told and
/// crosses the bridge only on a real transition. Upstream cannot meet this problem — it hangs off
/// two distinct events — but the page can: <c>suspend</c> is a level there too
/// (<c>arcademy/boot.js:198-202</c>, "buffer the LAST state only … an on/off pair collapses to
/// off correctly"), so a duplicate would be harmless and a MISSING un-freeze would not.</para>
///
/// <para><b>A clip that never appeared must not freeze a class.</b> <c>Fired</c> is raised on both
/// arms of the module's delivery, including the one where the surface REFUSED the placement
/// (<c>MandatoryVideoEffect.cs:287-310</c>) — and there <see cref="MandatoryVideoEffect.Playing"/>
/// is false, so the level test drops it. This is exactly why the wire reads the module's state
/// instead of treating the firing as the event.</para>
///
/// <para><b>Threading.</b> Both signals arrive on the effect signal boundary
/// (<c>Session/EffectSignal.cs:74-84</c>; the module's own delivery is inside
/// <c>Signal.Post</c>, <c>Session/PacedSessionEffect.cs:276</c>), so this type is used from one
/// thread and holds no lock for the same reason the modules do not.</para>
///
/// <para><b>ORDER, at both ends, and it is upstream's.</b> Construct this BEFORE the page can say
/// <c>ready</c> — upstream hooks before <c>Show()</c> because "a flip landing in that window used
/// to be missed entirely … and the page opened classes during an audio-only session"
/// (<c>:205-209</c>, hooks at <c>:210-212</c>, the window shown at <c>:214</c>) — and dispose it
/// BEFORE the session, because upstream unhooks at the TOP of
/// <c>DisposeAll</c> (<c>:2014-2016</c>), ahead of the meta flush and the host's own disposal. A
/// video that ends during teardown must find nothing subscribed rather than a session that is
/// half gone.</para>
/// </summary>
public sealed class ArcademyNativeSuspension : IDisposable
{
    private readonly MandatoryVideoEffect _video;
    private readonly ArcademySession _session;
    private bool _covering;
    private bool _disposed;

    /// <param name="video">The port's mandatory video — upstream's <c>App.Video</c> on this
    /// path.</param>
    /// <param name="session">The session whose page is frozen while a video covers the screen.</param>
    public ArcademyNativeSuspension(MandatoryVideoEffect video, ArcademySession session)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(session);
        _video = video;
        _session = session;

        // The LIVE predicate, which is what the boot seed and the panic-resume hold both read
        // (upstream asks App.Video?.IsPlaying at :419 and :361). Set before the subscriptions so a
        // `ready` that lands between the two still finds a truthful answer.
        session.NativeStateOwnsScreen = () => video.Playing;

        // The state the session is already in. A video that is ALREADY playing when this is wired
        // is the boot seed's business (:409-440), not an edge — posting one here would suspend a
        // page that has not had its init yet, and the page would drop it (arcademy/boot.js:195).
        _covering = video.Playing;
        video.Fired += OnFired;
        video.Changed += OnChanged;
    }

    /// <summary>What the session was last told: true while a native video covers the class.</summary>
    public bool Covering => _covering;

    /// <summary>
    /// Unhook. Idempotent, and it puts the session's predicate back the way it found it — what
    /// this wire suspended, it restores. Leaving the predicate pointed at a video nobody is
    /// watching for any more would hold a panic <c>resume-request</c> (<c>:359-364</c>) with
    /// nothing left alive to lift it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _video.Fired -= OnFired;
        _video.Changed -= OnChanged;
        _session.NativeStateOwnsScreen = static () => false;
    }

    private void OnFired(MandatoryVideoEvent fired) => Sync();

    private void OnChanged() => Sync();

    private void Sync()
    {
        if (_disposed)
        {
            return;
        }

        var covering = _video.Playing;
        if (covering == _covering)
        {
            return;
        }

        _covering = covering;
        _session.NativeVideoChanged(covering);
    }
}
