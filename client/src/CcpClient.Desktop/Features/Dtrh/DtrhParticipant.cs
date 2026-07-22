using System.Security.Cryptography;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The DTRH host's owned transport infrastructure (SP-004): the §4 loopback origins, the
/// §3.3 inbox, and the per-session unguessable bridge token. Construction starts nothing
/// (SP-003 §4.4); <see cref="StartAsync"/> binds the origins; <see cref="StopAsync"/> is
/// idempotent teardown. The web surfaces themselves are phase-4 (window/dialog), selected
/// by the probed capability states — never created here.
/// </summary>
public sealed class DtrhParticipant : IBackgroundParticipant
{
    private readonly ILogSink _log;
    private readonly Inbox _inbox = new();
    private LoopbackServer? _server;
    private int _started;

    public DtrhParticipant(AsyncOperationOwner owner, ILogSink log, string? dataDirectory = null)
    {
        Owner = owner;
        _log = log;
        // b4: the Loom store + user-media root live in the DTRH data directory (the same
        // folder the slot documents persist into — WPF UserDataPath parity).
        DataDirectory = dataDirectory
            ?? Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!;
        // §3.3: per-session unguessable token, generated at construction, never logged.
        BridgeToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public AsyncOperationOwner Owner { get; }

    /// <summary>The per-session bridge token (§3.3). Delivered in the navigated URL query; never logged.</summary>
    public string BridgeToken { get; }

    public Inbox Inbox => _inbox;

    public LoopbackServer Server => _server
        ?? throw new InvalidOperationException("DtrhParticipant has not started (SP-004: start is phase 3).");

    public string Name => "DtrhHost";

    public bool Running { get; private set; }

    /// <summary>Output-relative content roots (SP-009 copied-asset convention, SP-023).</summary>
    public static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payload", "dtrh");

    public static string OverlayRoot => Path.Combine(AppContext.BaseDirectory, "payload-overlay");

    public static string MediaRoot => Path.Combine(PayloadRoot, "assets");

    /// <summary>The DTRH data directory (slot documents + Loom store + user media).</summary>
    public string DataDirectory { get; }

    /// <summary>The Loom store folder (WPF: App.UserDataPath/Spirals, DtrhLoomStore.cs:28).</summary>
    public string SpiralsRoot => Path.Combine(DataDirectory, "Spirals");

    /// <summary>The user-media folder contract root (WPF App.EffectiveAssetsPath default
    /// parity): <dataDir>/assets with images/ + videos/ subfolders.</summary>
    public string UserMediaRoot => Path.Combine(DataDirectory, "assets");

    /// <summary>The URL the host navigates to; the bridge token rides in the query (§3.3).</summary>
    public string PageUrl(string page) =>
        $"{Server.PageOrigin}/dtrh/{page}?bridge={BridgeToken}";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask; // idempotent (SP-003)
        }

        Running = true;
        var generation = Owner.Begin();
        _ = generation; // the server is synchronous-bind + listener tasks owned by Dispose (below)
        _server = new LoopbackServer(PayloadRoot, OverlayRoot, MediaRoot, _inbox, BridgeToken, _log,
            spiralsRoot: SpiralsRoot, userMediaRoot: UserMediaRoot);
        _server.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!Running)
        {
            return Task.CompletedTask;
        }

        Running = false;
        _log.Log("dtrh: participant stop begin");
        Owner.Cancel();
        _server?.Dispose(); // idempotent; releases hanging long-polls before listeners stop
        _log.Log("dtrh: participant stop end");
        return Task.CompletedTask;
    }
}
