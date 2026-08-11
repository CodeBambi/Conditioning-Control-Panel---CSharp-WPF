using System.Security.Cryptography;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the intake host's owned transport infrastructure (the DtrhParticipant shape,
/// SP-004): a SECOND §4 LoopbackServer (own ephemeral ports, own per-session bridge token,
/// own inbox), started/stopped with the intake window (host-local construction — the
/// DtrhHostWindow bark-pipeline precedent; CompositionRoot is out of this packet's File
/// Scope and the app-wide lift is a future row).
///
/// **The overlay-first borrow (pre-approach consult ruling 1, sweep-clean):** the server is
/// the LoopbackServer class UNMODIFIED with overlayRoot = the intake tree and payloadRoot =
/// the dtrh tree. The hardcoded /dtrh/ route therefore serves every intake page URL from
/// the intake tree FIRST, and every borrowed dtrh asset (vendor/three, the chime mp3s at
/// /dtrh/assets/bubbles/sfx/chime{1..3}.mp3 — render/audio.js:222-229) falls through to the
/// dtrh tree. The intake tree shadows — never the reverse; nothing on THIS server serves
/// the DTRH page (the DTRH host runs its own server). Payload stays READ-ONLY.
///
/// **Loom root is the SHARED b4 store** (&lt;dataDir&gt;/Spirals — the same folder the DTRH
/// loom writes; never a second loom root). The inbox exists because the §4 class requires
/// one; the intake page's web-shim has no §3.3 long-poll transport (WebView2
/// chrome.webview only — web-shim.js:28-52), so the /bridge/ route sits unused (typed:
/// token unguessable, route-class logged only). Linux dialog path: NOT ADMITTED for intake
/// this packet (no page-side inbox transport — a standalone page would silently lose quiz
/// results; the honest unsupported surface shows instead. Linux = the named limit, WSL
/// zero distros, never faked).
/// </summary>
public sealed class IntakeParticipant : IDisposable
{
    private readonly ILogSink _log;
    private readonly Inbox _inbox = new();
    private LoopbackServer? _server;
    private int _started;

    public IntakeParticipant(ILogSink log, string? dataDirectory = null)
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

    /// <summary>The intake data directory (settings/punch card/drafts/keepsakes; the SHARED
    /// loom + user-media roots below match the DTRH participant's data directory — one
    /// &lt;dataDir&gt; per install).</summary>
    public string DataDirectory { get; }

    /// <summary>The SHARED b4 Loom store folder (DtrhLoomStore.cs:28 parity — the same
    /// &lt;dataDir&gt;/Spirals the DTRH host writes; never a second loom root).</summary>
    public string SpiralsRoot => Path.Combine(DataDirectory, "Spirals");

    /// <summary>The user-media folder contract root (shared with DTRH: &lt;dataDir&gt;/assets).</summary>
    public string UserMediaRoot => Path.Combine(DataDirectory, "assets");

    /// <summary>The intake keepsake folder (intake-save-image; IntakeHostService.cs:552
    /// parity — %APPDATA%/…/intake_spirals).</summary>
    public string IntakeSpiralsRoot => Path.Combine(DataDirectory, "intake_spirals");

    /// <summary>The drafted-session sink folder (never-runnable drafts).</summary>
    public string DraftedSessionsRoot => Path.Combine(DataDirectory, "intake", "drafted_sessions");

    public Inbox Inbox => _inbox;

    public LoopbackServer Server => _server
        ?? throw new InvalidOperationException("IntakeParticipant has not started.");

    public bool Running { get; private set; }

    /// <summary>The URL the host navigates to; the bridge token rides in the query (unused
    /// by the intake page's WebView2 transport — §3.3 shape kept for the §4 class).</summary>
    public string PageUrl(string page = "index.html") =>
        $"{Server.PageOrigin}/dtrh/{page}?bridge={BridgeToken}";

    /// <summary>Bind the origins (idempotent). The intake payload probe is logged BEFORE
    /// the bind so the transcript is self-evidencing (SP-048 discipline).</summary>
    public IntakeServingRoots.IntakePayloadProbe Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return IntakeServingRoots.Probe();
        }

        Running = true;
        var probe = IntakeServingRoots.Probe();
        _log.Log($"intake: payload root '{probe.Root}' -> {probe.State} ({probe.FileCount} files)");
        _server = new LoopbackServer(
            DtrhParticipant.PayloadRoot,       // payload fallback: the dtrh tree (the borrow)
            IntakeServingRoots.PayloadRoot,    // overlay-first: the intake tree
            DtrhParticipant.MediaRoot,         // /media/* → dtrh assets (payload class)
            _inbox, BridgeToken, _log,
            spiralsRoot: SpiralsRoot,          // /spirals/* → the SHARED b4 loom store
            userMediaRoot: UserMediaRoot);     // /umedia/* → the shared user-media pool
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
        _log.Log("intake: participant stop");
        _server?.Dispose();
    }
}
