using System.Text.Json;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// SP-049: the loom bridge subset (loom-save / loom-delete / loom-reveal in, loom-result /
/// loom-list out) shared by BOTH web hosts — the game host window (SP-026 b4) and the
/// standalone studio window (the WPF LoomHostService sibling shape). One write path so the
/// two hosts never drift (pre-approach consult binding 1a). All file authority stays in
/// <see cref="DtrhLoom"/>; presence+shape logging only (the slug is a user-authored
/// filename — never logged).
/// </summary>
public sealed class DtrhLoomDispatch
{
    private readonly DtrhLoom _loom;
    private readonly Func<string> _mediaOrigin;
    private readonly Action<object> _send;
    private readonly Action<string> _log;
    private readonly Func<string, string, bool>? _revealLaunch;

    public DtrhLoomDispatch(DtrhLoom loom, Func<string> mediaOrigin, Action<object> send, Action<string> log,
        Func<string, string, bool>? revealLaunch = null)
    {
        _loom = loom;
        _mediaOrigin = mediaOrigin;
        _send = send;
        _log = log;
        // Test seam for the OS reveal (product callers leave null = the real OS launch).
        _revealLaunch = revealLaunch;
    }

    /// <summary>Handle one parsed page message. True when the message was loom-subset
    /// (acted on or typed-refused); false = not ours, the caller's dispatch continues.</summary>
    public bool TryHandle(DtrhProtocol.DtrhPageMessage message)
    {
        switch (message)
        {
            case DtrhProtocol.DtrhPageMessage.LoomSave loomSave:
            {
                // loom-save {name, gifBase64, params, overwrite} (loomStudio.js:251-259) →
                // store validates + writes, page gets the verdict + a fresh list
                // (DtrhHostService.cs:285-293).
                string? gifBase64 = loomSave.Raw.TryGetProperty("gifBase64", out var b64) && b64.ValueKind == JsonValueKind.String
                    ? b64.GetString()
                    : null;
                JsonElement? loomParams = loomSave.Raw.TryGetProperty("params", out var lp) && lp.ValueKind == JsonValueKind.Object
                    ? lp.Clone()
                    : null;
                var saveResult = _loom.Save(loomSave.Name, gifBase64, loomParams, loomSave.Overwrite);
                _send(DtrhProtocol.BuildLoomResult("save", saveResult.Ok, saveResult.Slug, saveResult.Error));
                if (saveResult.Ok) PostList();
                return true;
            }
            case DtrhProtocol.DtrhPageMessage.LoomDelete loomDelete:
            {
                var deleteResult = _loom.Delete(loomDelete.Slug);
                _send(DtrhProtocol.BuildLoomResult("delete", deleteResult.Ok, deleteResult.Slug, deleteResult.Error));
                if (deleteResult.Ok) PostList();
                return true;
            }
            case DtrhProtocol.DtrhPageMessage.LoomReveal loomReveal:
                // 📂 on a rack tile (both homes — loomStudio.js:749). No page reply (WPF
                // parity: fire-and-forget); the outcome is typed + logged host-side.
                DtrhLoomReveal.Reveal(_loom, loomReveal.Slug, _log, _revealLaunch);
                return true;
            default:
                return false;
        }
    }

    /// <summary>PostLoomList (DtrhHostService.cs:327-343): the saved-spiral library —
    /// {slug, url, params} per entry, served through the §4 media origin. Posted at ready
    /// (never before — the page buffers pre-handler messages but WPF's contract is
    /// ready-gated, LoomHostService.cs:66 OnReady) and after each successful mutation.
    /// Unparseable params sidecars post null (WPF TryParseJson).</summary>
    public void PostList()
    {
        try
        {
            var spirals = _loom.List().Select(s =>
            {
                JsonElement? parsed = null;
                if (s.ParamsJson is { Length: > 0 } json)
                {
                    try { parsed = JsonDocument.Parse(json).RootElement.Clone(); }
                    catch { /* unparseable sidecar → null params (WPF TryParseJson parity) */ }
                }

                return new DtrhProtocol.DtrhLoomSpiral(
                    s.Slug,
                    $"{_mediaOrigin()}/spirals/loom_{s.Slug}.gif",
                    parsed);
            }).ToList();
            _send(DtrhProtocol.BuildLoomList(spirals));
            _log($"dtrh: loom-list posted ({spirals.Count} spiral(s))"); // presence+shape only
        }
        catch (Exception ex)
        {
            _log($"dtrh: loom-list failed ({ex.GetType().Name})");
        }
    }
}
