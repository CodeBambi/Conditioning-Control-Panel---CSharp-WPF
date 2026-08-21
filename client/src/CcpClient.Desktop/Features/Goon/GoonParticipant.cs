using System.Security.Cryptography;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Goon;

/// <summary>
/// SP-130: the Goon host's owned transport infrastructure — the <see cref="Intake.IntakeParticipant"/>
/// shape exactly: a THIRD §4 <see cref="LoopbackServer"/> (own ephemeral ports, own per-session
/// bridge token, own inbox), started and stopped with the Goon window.
///
/// <para><b>The server class is reused UNMODIFIED</b>, on its hardcoded <c>/dtrh/</c> route class.
/// The reason for reuse rather than a Goon-shaped origin is concrete: the page plays the user's own
/// <b>videos</b>, and this class already ships Range 206/416, the page/media CORS split that keeps
/// taint checks meaningful, traversal refusal, nosniff and deny-by-default 415 — all tested.
/// Re-deriving Range handling to obtain a prettier route prefix would be a cosmetic risk.</para>
///
/// <para><b>THE BORROW, and why SP-130's construction was wrong (SP-132, D266).</b> SP-130 passed
/// the goon tree as BOTH roots, so that "nothing else is reachable on this origin". The first run
/// that ever loaded the page found what that costs: <b>the goon page hotlinks twelve assets out of
/// the DTRH tree by ABSOLUTE path</b> — <c>ui/audio.js:112</c> declares
/// <c>DTRH_SFX_DIR = '/dtrh/assets/bubbles/sfx/'</c> and six cues resolve through it
/// (<c>:203</c>, <c>:219</c>, <c>:221</c>, <c>:225</c>, <c>:230</c>, <c>:235</c>), and
/// <c>exec/fx.css:271</c> plus <c>:304-308</c> pull the bubble sprite and five effect icons from
/// <c>/dtrh/assets/bubbles/</c>. The page's own comment states the design in as many words:
/// <i>"HOTLINKED, not copied. Resources/web is the page origin's root under WebView2 and every
/// harness mounts /dtrh/ at that exact prefix"</i> (<c>ui/audio.js:43-46</c>). With both roots on
/// the goon tree all twelve answered <b>404</b>, and the page said so on the host log:
/// <c>warn: [GG audio] asset failed: /dtrh/assets/bubbles/sfx/Pop2.mp3 (HTTP 404)</c>.</para>
///
/// <para>So the construction is now the <see cref="Intake.IntakeParticipant"/> OVERLAY-FIRST BORROW
/// verbatim (<c>IntakeParticipant.cs:94-100</c>): the goon tree is the overlay and shadows
/// everything it has, and the dtrh tree is the payload fallback the twelve hotlinks resolve
/// against.</para>
///
/// <para><b>WHAT THIS GIVES UP, AT ITS REAL SIZE — measured, not characterised.</b> The fallback
/// admits the WHOLE DTRH payload tree, not the twelve files that motivated it: <b>1542 dtrh files
/// minus the 3 this tree shadows (<c>index.html</c>, <c>boot.js</c>, <c>bridge.js</c>) = 1539 files
/// that were unreachable on this origin before and are reachable at <c>/dtrh/*</c> now</b>, of
/// which 1537 serve and 2 (<c>.md</c>) answer 415. SP-130's "nothing else is reachable on this
/// origin" is deliberately given up, and one line reverts it.
/// <c>GoonServingTests.TheBorrowsAdmissionSurface_IsMeasured_AndBoundedToTheDtrhPayloadTree</c>
/// pins that number so it cannot grow without review.</para>
///
/// <para><b>Why it is acceptable anyway</b>, beside the number rather than instead of it: the
/// MECHANISM is not new — the intake origin has borrowed these same <c>bubbles/sfx</c> files since
/// SP-054 (<c>IntakeServingTests.The_Borrow_Falls_Through_To_The_Dtrh_Tree</c>) — and this
/// construction is NARROWER than that one in one respect, because <c>/media/*</c> keeps the goon
/// tree's own root rather than the dtrh one. Every §4 control is unchanged and still applies to the
/// fallback: GET-only, MIME allowlist + 415, traversal refusal, nosniff. <b>No user media is
/// admitted at all</b> — <c>/umedia/*</c> keeps its own root on its own port. And
/// <c>GoonServingTests</c> pins both halves at the wire: the goon tree still shadows, and exactly
/// the borrowed set falls through.</para>
///
/// <para><b>The inherited cost, measured rather than assumed (D258).</b> The goon tree holds
/// eight extensions plus two extensionless files; <see cref="LoopbackServer"/>'s §4.4-pinned
/// allowlist has nine and covers six of them. <b>Four files therefore answer 415</b> —
/// <c>manifest.webmanifest</c>, <c>vendor/mp4-muxer/mp4-muxer.mjs</c>, and the two
/// <c>vendor/*/LICENSE</c> files, whose extension is the empty string and so misses the table at
/// <c>LoopbackServer.cs:447-449</c> as well. None is on the practice boot path: the PWA manifest
/// is inert under WebView2 by <c>index.html:18</c>'s own statement, the muxer is imported only by
/// <c>encode/encodeWorker.js:37</c> (the encode lane, which <c>assetCache:false</c> switches off)
/// and by <c>test/</c>, and a LICENSE file is never fetched by a browser.
/// <c>GoonPracticeTests</c> pins the denial set by NAME, so a new extension upstream reds the
/// suite instead of 415-ing silently in front of a player.</para>
///
/// <para><b>The bridge inbox exists because the §4 class requires one, and is unused here.</b>
/// The goon page's own bridge speaks <c>window.chrome.webview</c> only (<c>bridge.js:45-47</c>,
/// <c>:65-67</c>, <c>:93-95</c>) — it has no §3.3 long-poll transport — so the <c>/bridge/</c>
/// route sits idle (token unguessable, route-class logged only). That is also why the Linux
/// NativeWebDialog path is NOT admitted for goon: a dialog would run the page STANDALONE, where
/// it synthesizes its own <c>init</c> (the <c>!isHosted</c> branch at <c>bridge.js:473</c>) and would silently ignore this
/// host's caps — including every door refusal. Linux gets the typed honest-unsupported surface.</para>
/// </summary>
public sealed class GoonParticipant : IDisposable
{
    private readonly ILogSink _log;
    private readonly Inbox _inbox = new();
    private LoopbackServer? _server;
    private int _started;

    public GoonParticipant(ILogSink log, string? dataDirectory = null)
    {
        _log = log;
        DataDirectory = dataDirectory
            ?? Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!;
        // §3.3 parity: per-session unguessable token, generated at construction, never logged.
        BridgeToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>The per-session bridge token (delivered in the navigated URL query; never logged).</summary>
    public string BridgeToken { get; }

    /// <summary>One &lt;dataDir&gt; per install — the same one DTRH and intake use.</summary>
    public string DataDirectory { get; }

    /// <summary>The user-media folder contract root, SHARED with DTRH and intake
    /// (&lt;dataDir&gt;/assets). Never a second pool: one uncheck hides a file everywhere.</summary>
    public string UserMediaRoot => Path.Combine(DataDirectory, "assets");

    public Inbox Inbox => _inbox;

    public LoopbackServer Server => _server
        ?? throw new InvalidOperationException("GoonParticipant has not started.");

    public bool Running { get; private set; }

    /// <summary>The URL the host navigates to. The <c>/dtrh/</c> segment is the reused server's
    /// hardcoded route class, not a DTRH page: overlay-first resolves it against the goon tree.</summary>
    public string PageUrl(string page = "index.html") =>
        $"{Server.PageOrigin}/dtrh/{page}?bridge={BridgeToken}";

    /// <summary>Bind the origins (idempotent). The payload probe is logged BEFORE the bind so the
    /// transcript is self-evidencing about where the page is served from (SP-048 discipline).</summary>
    public GoonServingRoots.GoonPayloadProbe Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return GoonServingRoots.Probe();
        }

        Running = true;
        var probe = GoonServingRoots.Probe();
        _log.Log($"goon: payload root '{probe.Root}' -> {probe.State} ({probe.FileCount} files)");
        _server = new LoopbackServer(
            DtrhParticipant.PayloadRoot,    // payload FALLBACK: the dtrh tree (THE BORROW — see below)
            GoonServingRoots.PayloadRoot,   // overlay-first: the goon tree, which shadows it
            GoonServingRoots.MediaRoot,     // /media/* -> the goon tree's own assets (never used by the page)
            _inbox, BridgeToken, _log,
            userMediaRoot: UserMediaRoot);  // /umedia/* -> the shared user-media pool, CORS-scoped
        _server.Start();
        return probe;
    }

    /// <summary>Idempotent teardown (SP-003 discipline).</summary>
    public void Dispose()
    {
        if (!Running)
        {
            return;
        }

        Running = false;
        _log.Log("goon: participant stop");
        _server?.Dispose();
    }
}
