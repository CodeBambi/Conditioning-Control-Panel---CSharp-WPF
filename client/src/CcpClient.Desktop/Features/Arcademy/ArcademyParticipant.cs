using System.Security.Cryptography;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The Arcademy's §4 loopback origins — the FOURTH consumer of <see cref="LoopbackServer"/>
/// (after dtrh, intake and goon), never a second server. Construction is
/// <see cref="Goon.GoonParticipant"/>'s line for line, with one difference that is the whole
/// point of <see cref="ArcademyServingRoots"/>: <b>both page roots are the payload MIRROR</b>,
/// so the arcademy tree and the DTRH tree it borrows spirals from sit under one origin exactly
/// as they do upstream under <c>ccp.game</c> (<c>ArcademyHostService.cs:167-171</c>).
///
/// <para>Overlay-first still applies and still resolves: with both roots equal, a request hits
/// the same file either way, which is the honest shape for a tree that shadows nothing. Goon
/// and intake pass DIFFERENT roots because their trees shadow dtrh's; this one does not.</para>
///
/// <para><b>Nothing here decides whether the Arcademy may open.</b> That is
/// <see cref="ArcademyDoor"/>, and <see cref="ArcademyLaunch"/> asks it before this type is
/// ever constructed — binding an origin IS opening something.</para>
/// </summary>
public sealed class ArcademyParticipant : IDisposable
{
    private readonly ILogSink _log;
    private readonly Inbox _inbox = new();
    private LoopbackServer? _server;
    private int _started;

    public ArcademyParticipant(ILogSink log, string? dataDirectory = null)
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

    /// <summary>One &lt;dataDir&gt; per install — the same one DTRH, intake and goon use.</summary>
    public string DataDirectory { get; }

    /// <summary>The user-media folder contract root, SHARED with every other web core
    /// (&lt;dataDir&gt;/assets). Never a second pool: one uncheck hides a file everywhere, which
    /// is also what <c>ArcademyHostService.BuildLocalAssets</c> (<c>:656-690</c>) honours.</summary>
    public string UserMediaRoot => Path.Combine(DataDirectory, "assets");

    /// <summary>Where <c>init.overrideCalendar</c> is read from
    /// (<c>ArcademyHostService.LoadOverrideCalendar</c>, <c>:715-728</c> —
    /// <c>App.UserDataPath/arcademy_calendar.json</c>). Absent is the designed offline
    /// fallback, not an error.</summary>
    public string OverrideCalendarPath => Path.Combine(DataDirectory, "arcademy_calendar.json");

    public Inbox Inbox => _inbox;

    public LoopbackServer Server => _server
        ?? throw new InvalidOperationException("ArcademyParticipant has not started.");

    public bool Running { get; private set; }

    /// <summary>The url a host navigates to. The <c>/dtrh/</c> segment is the reused server's
    /// hardcoded route class; the <c>arcademy/</c> segment after it is upstream's own
    /// (<c>ArcademyHostService.cs:188</c>) and is what makes the spiral borrow resolve.</summary>
    public string PageUrl(string page = ArcademyServingRoots.PageRoute) =>
        $"{Server.PageOrigin}/dtrh/{page}?bridge={BridgeToken}";

    /// <summary>Bind the origins (idempotent). The payload probe is logged BEFORE the bind so the
    /// transcript is self-evidencing about where the page is served from.</summary>
    public ArcademyServingRoots.ArcademyPayloadProbe Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return ArcademyServingRoots.Probe();
        }

        Running = true;
        var probe = ArcademyServingRoots.Probe();
        _log.Log($"arcademy: payload root '{probe.Root}' -> {probe.State} ({probe.FileCount} files)");
        _server = new LoopbackServer(
            ArcademyServingRoots.WebRoot,     // payload: the web-root mirror (the spiral borrow)
            ArcademyServingRoots.WebRoot,     // overlay-first: nothing shadows, so the same root
            DtrhParticipant.MediaRoot,        // /media/* -> dtrh assets (payload class; unused here)
            _inbox, BridgeToken, _log,
            userMediaRoot: UserMediaRoot);    // /umedia/* -> the shared user-media pool, CORS-scoped
        _server.Start();
        return probe;
    }

    /// <summary>Idempotent teardown (lifecycle discipline).</summary>
    public void Dispose()
    {
        if (!Running)
        {
            return;
        }

        Running = false;
        _log.Log("arcademy: participant stop");
        _server?.Dispose();
    }
}
