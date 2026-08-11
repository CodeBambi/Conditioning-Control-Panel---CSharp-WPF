using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Intake;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-054: the bridge vocabulary — parse/classify/tolerance matrix (b2 discipline), the
/// ping/payload-state authoring-obligation pins (typed, PageAttestedNeverEmitted —
/// archaeology refuted every C# emit site), the out-of-vocabulary pins for DTRH/studio
/// loom types (intake sends loom-save ONLY), the 6-out builder shapes, and the SHARED
/// loom write path (consult 7b: loom-result ONLY, never a loom-list from this host).
/// </summary>
public sealed class IntakeProtocolTests
{
    // ---------- the 12 in-types parse with their shapes ----------

    [Theory]
    [InlineData("{\"type\":\"ready\",\"protocol\":1}", typeof(IntakeProtocol.IntakePageMessage.Ready))]
    [InlineData("{\"type\":\"log\",\"msg\":\"hi\"}", typeof(IntakeProtocol.IntakePageMessage.Log))]
    [InlineData("{\"type\":\"heartbeat\",\"t\":12.5}", typeof(IntakeProtocol.IntakePageMessage.Heartbeat))]
    [InlineData("{\"type\":\"pong\",\"t\":9.0}", typeof(IntakeProtocol.IntakePageMessage.Pong))]
    [InlineData("{\"type\":\"quiz-result\",\"result\":{\"niche\":\"bambi\"}}", typeof(IntakeProtocol.IntakePageMessage.QuizResult))]
    [InlineData("{\"type\":\"boot-error\",\"msg\":\"no webgl\"}", typeof(IntakeProtocol.IntakePageMessage.BootError))]
    [InlineData("{\"type\":\"fullscreen-set\",\"on\":true}", typeof(IntakeProtocol.IntakePageMessage.FullscreenSet))]
    [InlineData("{\"type\":\"exit\"}", typeof(IntakeProtocol.IntakePageMessage.Exit))]
    [InlineData("{\"type\":\"exit-done\"}", typeof(IntakeProtocol.IntakePageMessage.ExitDone))]
    [InlineData("{\"type\":\"intake-close\"}", typeof(IntakeProtocol.IntakePageMessage.IntakeClose))]
    [InlineData("{\"type\":\"loom-save\",\"name\":\"x\",\"overwrite\":false}", typeof(IntakeProtocol.IntakePageMessage.LoomSave))]
    [InlineData("{\"type\":\"intake-save-image\",\"pngBase64\":\"AA==\",\"index\":2}", typeof(IntakeProtocol.IntakePageMessage.IntakeSaveImage))]
    public void Twelve_In_Types_Parse(string json, Type expected)
    {
        var parsed = Assert.IsType<IntakeProtocol.IntakePageParseResult.Parsed>(IntakeProtocol.ParsePageMessage(json));
        Assert.IsType(expected, parsed.Message);
    }

    [Fact]
    public void Field_Shapes_Parse()
    {
        var ready = (IntakeProtocol.IntakePageMessage.Ready)((IntakeProtocol.IntakePageParseResult.Parsed)
            IntakeProtocol.ParsePageMessage("{\"type\":\"ready\",\"protocol\":1}")).Message;
        Assert.Equal(1, ready.Protocol);

        var quiz = (IntakeProtocol.IntakePageMessage.QuizResult)((IntakeProtocol.IntakePageParseResult.Parsed)
            IntakeProtocol.ParsePageMessage("{\"type\":\"quiz-result\",\"result\":{\"niche\":\"circe\",\"peakDepth\":0.4}}")).Message;
        Assert.Equal("circe", quiz.Raw.GetProperty("niche").GetString());

        var loom = (IntakeProtocol.IntakePageMessage.LoomSave)((IntakeProtocol.IntakePageParseResult.Parsed)
            IntakeProtocol.ParsePageMessage("{\"type\":\"loom-save\",\"name\":\"keepsake\",\"overwrite\":true,\"gifBase64\":\"AA==\",\"params\":{\"arms\":4}}")).Message;
        Assert.Equal("keepsake", loom.Name);
        Assert.True(loom.Overwrite);
        Assert.Equal("AA==", loom.Raw.GetProperty("gifBase64").GetString());

        var image = (IntakeProtocol.IntakePageMessage.IntakeSaveImage)((IntakeProtocol.IntakePageParseResult.Parsed)
            IntakeProtocol.ParsePageMessage("{\"type\":\"intake-save-image\",\"pngBase64\":\"AA==\",\"index\":3}")).Message;
        Assert.Equal("AA==", image.PngBase64);
        Assert.Equal(3, image.Index);
    }

    // ---------- out-of-vocabulary pins (DTRH/studio loom types are NOT intake's) ----------

    [Theory]
    [InlineData("{\"type\":\"loom-delete\",\"slug\":\"x\"}")]
    [InlineData("{\"type\":\"loom-reveal\",\"slug\":\"x\"}")]
    [InlineData("{\"type\":\"loom-list\"}")]
    [InlineData("{\"type\":\"sfx\",\"name\":\"chime1\"}")]
    [InlineData("{\"type\":\"made-up\"}")]
    public void Out_Of_Vocabulary_Is_Typed_Unknown(string json)
    {
        var unknown = Assert.IsType<IntakeProtocol.IntakePageParseResult.UnknownType>(IntakeProtocol.ParsePageMessage(json));
        Assert.False(string.IsNullOrEmpty(unknown.Type));
    }

    // ---------- tolerance ----------

    [Fact]
    public void Forward_Version_Is_Typed_Not_Acted_On()
    {
        var forward = Assert.IsType<IntakeProtocol.IntakePageParseResult.ForwardVersion>(
            IntakeProtocol.ParsePageMessage("{\"type\":\"ready\",\"protocol\":2}"));
        Assert.Equal("ready", forward.Type);
        Assert.Equal(2, forward.Protocol);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("{\"type\":5}")]
    public void Malformed_Is_Typed(string json) =>
        Assert.IsType<IntakeProtocol.IntakePageParseResult.Malformed>(IntakeProtocol.ParsePageMessage(json));

    // ---------- the authoring-obligation pins (ping / payload-state) ----------

    [Fact]
    public void Six_Out_Are_Emitted_Ping_And_PayloadState_Are_Page_Attested_Never_Emitted()
    {
        foreach (var type in new[] { "init", "fullscreen", "end-run", "session-drafted", "loom-result", "intake-save-image-result" })
        {
            Assert.Equal(IntakeProtocol.IntakeEmitClass.EmittedByHost, IntakeProtocol.ClassifyHostEmit(type));
        }

        Assert.Equal(IntakeProtocol.IntakeEmitClass.PageAttestedNeverEmitted, IntakeProtocol.ClassifyHostEmit(IntakeProtocol.PingType));
        Assert.Equal(IntakeProtocol.IntakeEmitClass.PageAttestedNeverEmitted, IntakeProtocol.ClassifyHostEmit(IntakeProtocol.PayloadStateType));
        Assert.Equal(IntakeProtocol.IntakeEmitClass.NotIntakeVocabulary, IntakeProtocol.ClassifyHostEmit("loom-list"));
        Assert.Equal(IntakeProtocol.IntakeEmitClass.NotIntakeVocabulary, IntakeProtocol.ClassifyHostEmit("pingx"));
    }

    // ---------- the 6-out builder shapes ----------

    [Fact]
    public void Init_Envelope_Shape()
    {
        var json = IntakeProtocol.SerializeForPage(IntakeProtocol.BuildInit(
            new { niche = "bambi", micEnabled = false },
            new IntakeProtocol.IntakeAiProvision(IntakeProtocol.AiProxyBaseUrl, "")));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("init", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("protocol").GetInt32());
        Assert.Equal("bambi", root.GetProperty("config").GetProperty("niche").GetString());
        Assert.Equal("https://codebambi-proxy.vercel.app", root.GetProperty("ai").GetProperty("serverBase").GetString());
        // The empty token — WPF's logged-out branch; the page runs its local stub, NO network.
        Assert.Equal("", root.GetProperty("ai").GetProperty("authToken").GetString());
    }

    [Fact]
    public void Result_Builders_Shape()
    {
        using var drafted = JsonDocument.Parse(IntakeProtocol.SerializeForPage(
            IntakeProtocol.BuildSessionDrafted(true, "Deep Bambi Intake", "/tmp/x.session.json")));
        Assert.Equal("session-drafted", drafted.RootElement.GetProperty("type").GetString());
        Assert.True(drafted.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Deep Bambi Intake", drafted.RootElement.GetProperty("name").GetString());

        using var loom = JsonDocument.Parse(IntakeProtocol.SerializeForPage(
            IntakeProtocol.BuildLoomResult("save", false, null, "cap-reached")));
        Assert.Equal("loom-result", loom.RootElement.GetProperty("type").GetString());
        Assert.Equal("save", loom.RootElement.GetProperty("op").GetString());
        Assert.Equal("cap-reached", loom.RootElement.GetProperty("error").GetString());

        using var image = JsonDocument.Parse(IntakeProtocol.SerializeForPage(
            IntakeProtocol.BuildSaveImageResult(false, null, "too-big")));
        Assert.Equal("intake-save-image-result", image.RootElement.GetProperty("type").GetString());
        Assert.Equal("too-big", image.RootElement.GetProperty("error").GetString());

        using var full = JsonDocument.Parse(IntakeProtocol.SerializeForPage(IntakeProtocol.BuildFullscreen(true)));
        Assert.Equal("fullscreen", full.RootElement.GetProperty("type").GetString());
        Assert.True(full.RootElement.GetProperty("on").GetBoolean());

        using var end = JsonDocument.Parse(IntakeProtocol.SerializeForPage(IntakeProtocol.BuildEndRun("host")));
        Assert.Equal("end-run", end.RootElement.GetProperty("type").GetString());
        Assert.Equal("host", end.RootElement.GetProperty("reason").GetString());
    }

    // ---------- the SHARED loom write path (consult 7b) ----------

    private static byte[] TinyGif()
    {
        // GIF89a magic + pad + 0x3B trailer (DtrhLoom's LooksLikeGif discipline).
        var bytes = new byte[20];
        var magic = "GIF89a"u8.ToArray();
        magic.CopyTo(bytes, 0);
        bytes[^1] = 0x3B;
        return bytes;
    }

    [Fact]
    public void HandleSave_Writes_The_Shared_Store_And_Posts_LoomResult_ONLY()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-sp054-loom-" + Guid.NewGuid().ToString("N"));
        var sent = new List<string>();
        try
        {
            var loom = new DtrhLoom(Path.Combine(root, "Spirals"), _ => { });
            var dispatch = new DtrhLoomDispatch(loom, () => "http://127.0.0.1:9",
                m => sent.Add(IntakeProtocol.SerializeForPage(m)), _ => { });
            var raw = JsonDocument.Parse(
                "{\"gifBase64\":\"" + Convert.ToBase64String(TinyGif()) + "\",\"params\":{\"arms\":4}}").RootElement;

            var result = dispatch.HandleSave("keepsake", overwrite: false, raw);

            Assert.True(result.Ok);
            // The store artifact landed in the SHARED folder shape.
            Assert.True(File.Exists(Path.Combine(root, "Spirals", "loom_keepsake.gif")));
            // loom-result posted…
            var reply = Assert.Single(sent);
            using var doc = JsonDocument.Parse(reply);
            Assert.Equal("loom-result", doc.RootElement.GetProperty("type").GetString());
            // …and NO loom-list — the intake 6-out table has none (7b).
            Assert.DoesNotContain(sent, s => s.Contains("loom-list"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
