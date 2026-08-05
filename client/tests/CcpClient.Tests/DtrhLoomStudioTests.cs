using System.Runtime.InteropServices;
using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-049: the Loom studio promotion — loom-reveal (typed protocol + GifPathFor + the OS
/// reveal seam with an injected launcher, never a real process) and the shared
/// <see cref="DtrhLoomDispatch"/> (save/delete/reveal → result/list over a recording send;
/// presence+shape logging — the slug never appears in logs).
/// </summary>
public sealed class DtrhLoomStudioTests : IDisposable
{
    private readonly string _root;
    private readonly string _spirals;
    private readonly List<string> _log = [];

    public DtrhLoomStudioTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-sp049-loom-" + Guid.NewGuid().ToString("N"));
        _spirals = Path.Combine(_root, "Spirals");
        Directory.CreateDirectory(_spirals);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private DtrhLoom NewLoom() => new(_spirals, _log.Add);

    private static byte[] TinyGif(int size = 64)
    {
        var b = new byte[size];
        b[0] = (byte)'G'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'8'; b[4] = (byte)'9'; b[5] = (byte)'a';
        b[^1] = 0x3B;
        return b;
    }

    private sealed record Sent(object Message)
    {
        public JsonElement Json => JsonDocument.Parse(DtrhProtocol.SerializeForPage(Message)).RootElement;
    }

    private (DtrhLoomDispatch dispatch, List<Sent> sent) NewDispatch(DtrhLoom loom, string mediaOrigin = "http://127.0.0.1:9")
    {
        var sent = new List<Sent>();
        // Injected reveal seam — tests never spawn a real file manager.
        return (new DtrhLoomDispatch(loom, () => mediaOrigin, m => sent.Add(new Sent(m)), _log.Add, (_, _) => true), sent);
    }

    private static DtrhProtocol.DtrhPageMessage Parse(string json) =>
        Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(DtrhProtocol.ParsePageMessage(json)).Message;

    // ---------- protocol: loom-reveal ----------

    [Fact]
    public void LoomReveal_Parses_TypedFields()
    {
        var reveal = Assert.IsType<DtrhProtocol.DtrhPageMessage.LoomReveal>(
            Parse("{\"type\":\"loom-reveal\",\"slug\":\"dream\"}"));
        Assert.Equal("dream", reveal.Slug);
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(DtrhProtocol.Classify(reveal));
    }

    [Fact]
    public void LoomReveal_MissingSlug_ToleratedAsNull()
    {
        var reveal = Assert.IsType<DtrhProtocol.DtrhPageMessage.LoomReveal>(
            Parse("{\"type\":\"loom-reveal\"}"));
        Assert.Null(reveal.Slug);
    }

    // ---------- GifPathFor (DtrhLoomStore.cs:114-123 parity) ----------

    [Fact]
    public void GifPathFor_ExistingFile_ReturnsPath()
    {
        var loom = NewLoom();
        var path = Path.Combine(_spirals, "loom_dream.gif");
        File.WriteAllBytes(path, TinyGif());
        Assert.Equal(path, loom.GifPathFor("dream"));
    }

    [Fact]
    public void GifPathFor_MissingOrBadSlug_ReturnsNull()
    {
        var loom = NewLoom();
        Assert.Null(loom.GifPathFor("dream"));            // no such file
        Assert.Null(loom.GifPathFor(null));
        Assert.Null(loom.GifPathFor("../escape"));        // traversal-shaped
        Assert.Null(loom.GifPathFor("UPPER"));            // outside the slug whitelist
        Assert.Null(loom.GifPathFor("a/b"));
    }

    // ---------- DtrhLoomReveal (injected OS seam — never a real process) ----------

    [Fact]
    public void Reveal_ExistingSpiral_LaunchesOsSeam()
    {
        var loom = NewLoom();
        var path = Path.Combine(_spirals, "loom_dream.gif");
        File.WriteAllBytes(path, TinyGif());
        var launches = new List<(string Program, string Args)>();
        var outcome = DtrhLoomReveal.Reveal(loom, "dream", _log.Add, (p, a) => { launches.Add((p, a)); return true; });

        Assert.Equal(DtrhLoomReveal.Outcome.Revealed.Instance, outcome);
        var (program, args) = Assert.Single(launches);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal("explorer.exe", program);
            Assert.Contains(path, args); // /select,"<path>" (WPF verbatim)
        }
        else
        {
            Assert.Equal("xdg-open", program);
            Assert.Contains(_spirals, args); // the containing folder (recorded divergence)
        }
    }

    [Fact]
    public void Reveal_BadSlugOrMissing_Refused_NothingLaunched()
    {
        var loom = NewLoom();
        var launches = 0;
        Assert.IsType<DtrhLoomReveal.Outcome.Refused>(
            DtrhLoomReveal.Reveal(loom, "../escape", _log.Add, (_, _) => { launches++; return true; }));
        Assert.IsType<DtrhLoomReveal.Outcome.Refused>(
            DtrhLoomReveal.Reveal(loom, "ghost", _log.Add, (_, _) => { launches++; return true; }));
        Assert.Equal(0, launches);
        Assert.Contains(_log, l => l.Contains("reveal refused"));
    }

    [Fact]
    public void Reveal_LaunchFailure_Typed_NeverThrows()
    {
        var loom = NewLoom();
        File.WriteAllBytes(Path.Combine(_spirals, "loom_dream.gif"), TinyGif());
        Assert.IsType<DtrhLoomReveal.Outcome.LaunchFailed>(
            DtrhLoomReveal.Reveal(loom, "dream", _log.Add, (_, _) => false));
        Assert.IsType<DtrhLoomReveal.Outcome.LaunchFailed>(
            DtrhLoomReveal.Reveal(loom, "dream", _log.Add, (_, _) => throw new InvalidOperationException("boom")));
    }

    // ---------- DtrhLoomDispatch (the shared subset) ----------

    [Fact]
    public void Save_Ok_SendsResultAndFreshList()
    {
        var loom = NewLoom();
        var (dispatch, sent) = NewDispatch(loom);
        var save = Parse("{\"type\":\"loom-save\",\"name\":\"Dream\",\"overwrite\":false,"
            + "\"params\":{\"arms\":4},\"gifBase64\":\"" + Convert.ToBase64String(TinyGif()) + "\"}");

        Assert.True(dispatch.TryHandle(save));
        Assert.Equal(2, sent.Count);

        var result = sent[0].Json;
        Assert.Equal("loom-result", result.GetProperty("type").GetString());
        Assert.Equal("save", result.GetProperty("op").GetString());
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("dream", result.GetProperty("slug").GetString());

        var list = sent[1].Json;
        Assert.Equal("loom-list", list.GetProperty("type").GetString());
        var spiral = Assert.Single(list.GetProperty("spirals").EnumerateArray());
        Assert.Equal("dream", spiral.GetProperty("slug").GetString());
        Assert.Equal("http://127.0.0.1:9/spirals/loom_dream.gif", spiral.GetProperty("url").GetString());
        Assert.Equal(4, spiral.GetProperty("params").GetProperty("arms").GetInt32());
        // File-content proof at the store layer: the GIF + params sidecar landed.
        Assert.True(DtrhLoom.LooksLikeGif(File.ReadAllBytes(Path.Combine(_spirals, "loom_dream.gif"))));
        Assert.Contains("\"arms\"", File.ReadAllText(Path.Combine(_spirals, "loom_dream.json")));
    }

    [Fact]
    public void Save_BadGif_SendsError_NoList()
    {
        var loom = NewLoom();
        var (dispatch, sent) = NewDispatch(loom);
        var save = Parse("{\"type\":\"loom-save\",\"name\":\"dream\",\"overwrite\":false,"
            + "\"gifBase64\":\"" + Convert.ToBase64String(new byte[32]) + "\"}");

        Assert.True(dispatch.TryHandle(save));
        var result = Assert.Single(sent).Json;
        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("bad-gif", result.GetProperty("error").GetString());
    }

    [Fact]
    public void Delete_RoundTrip_ResultThenList()
    {
        var loom = NewLoom();
        File.WriteAllBytes(Path.Combine(_spirals, "loom_dream.gif"), TinyGif());
        var (dispatch, sent) = NewDispatch(loom);

        Assert.True(dispatch.TryHandle(Parse("{\"type\":\"loom-delete\",\"slug\":\"dream\"}")));
        Assert.Equal(2, sent.Count);
        Assert.True(sent[0].Json.GetProperty("ok").GetBoolean());
        Assert.Empty(sent[1].Json.GetProperty("spirals").EnumerateArray());
        Assert.False(File.Exists(Path.Combine(_spirals, "loom_dream.gif")));

        sent.Clear();
        Assert.True(dispatch.TryHandle(Parse("{\"type\":\"loom-delete\",\"slug\":\"dream\"}")));
        var missing = Assert.Single(sent).Json; // no list after a failed mutation
        Assert.Equal("io-failed", missing.GetProperty("error").GetString());
    }

    [Fact]
    public void Reveal_FireAndForget_TypedLog_NoPageReply()
    {
        var loom = NewLoom();
        File.WriteAllBytes(Path.Combine(_spirals, "loom_dream.gif"), TinyGif());
        var (dispatch, sent) = NewDispatch(loom);

        Assert.True(dispatch.TryHandle(Parse("{\"type\":\"loom-reveal\",\"slug\":\"dream\"}")));
        Assert.Empty(sent); // WPF parity: no loom-result for reveal
        Assert.Contains(_log, l => l.Contains("reveal"));
    }

    [Fact]
    public void TryHandle_NotLoomSubset_ReturnsFalse()
    {
        var (dispatch, _) = NewDispatch(NewLoom());
        Assert.False(dispatch.TryHandle(Parse("{\"type\":\"heartbeat\",\"t\":1}")));
        Assert.False(dispatch.TryHandle(Parse("{\"type\":\"sfx\",\"name\":\"boon_pick\",\"scale\":0.4}")));
    }

    [Fact]
    public void PostList_PresenceShapeLogging_SlugNeverLogged()
    {
        var loom = NewLoom();
        var (dispatch, sent) = NewDispatch(loom);
        dispatch.TryHandle(Parse("{\"type\":\"loom-save\",\"name\":\"dream\",\"overwrite\":false,"
            + "\"gifBase64\":\"" + Convert.ToBase64String(TinyGif()) + "\"}"));
        Assert.NotEmpty(sent);
        Assert.DoesNotContain(_log, l => l.Contains("dream", StringComparison.OrdinalIgnoreCase));
    }
}
