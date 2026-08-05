using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-024 slice b2: protocol v1 full vocabulary. Every page→host literal below is copied
/// from the READ-ONLY payload's send sites / the WPF host's reads (SP-024 record Step 1
/// tables); every host→page builder is asserted against the payload's handler field reads
/// (boot.js:132-181). Tolerance: unknown/forward-version/malformed are typed outcomes —
/// no crash, no silent drop.
/// </summary>
public class DtrhProtocolTests
{
    // ---------- page → host: the full 22-type vocabulary round-trips ----------

    public static IEnumerable<object?[]> PageVocabulary()
    {
        // json, expected runtime type, expected classification (null = Handled)
        yield return ["{\"type\":\"ready\",\"protocol\":1}", typeof(DtrhProtocol.DtrhPageMessage.Ready), null];
        yield return ["{\"type\":\"log\",\"msg\":\"engine live (game mode)\"}", typeof(DtrhProtocol.DtrhPageMessage.Log), null];
        yield return ["{\"type\":\"heartbeat\",\"t\":1234.5}", typeof(DtrhProtocol.DtrhPageMessage.Heartbeat), null];
        yield return ["{\"type\":\"exit\"}", typeof(DtrhProtocol.DtrhPageMessage.Exit), null];
        yield return ["{\"type\":\"exit-done\"}", typeof(DtrhProtocol.DtrhPageMessage.ExitDone), null];
        yield return ["{\"type\":\"fullscreen-set\",\"on\":true}", typeof(DtrhProtocol.DtrhPageMessage.FullscreenSet), null];
        yield return ["{\"type\":\"boot-error\",\"msg\":\"WebGL context creation failed\"}", typeof(DtrhProtocol.DtrhPageMessage.BootError), null];
        yield return ["{\"type\":\"pong\",\"t\":7}", typeof(DtrhProtocol.DtrhPageMessage.Pong), null];
        yield return ["{\"type\":\"vn-speaking\",\"on\":true}", typeof(DtrhProtocol.DtrhPageMessage.VnSpeaking), null];
        yield return ["{\"type\":\"sfx\",\"name\":\"wave_clear\",\"scale\":0.8}", typeof(DtrhProtocol.DtrhPageMessage.Sfx), null];
        yield return ["{\"type\":\"fire-payload\",\"kind\":\"video\",\"strength\":60,\"durationMult\":1.5}", typeof(DtrhProtocol.DtrhPageMessage.FirePayload), null];
        yield return ["{\"type\":\"freeze-state\",\"on\":true}", typeof(DtrhProtocol.DtrhPageMessage.FreezeState), null];
        // SP-032 q2: bark upgraded Deferred → Handled (the quips/sound-arbitration row's
        // content pipeline now owns it — routed in the host window through BarkPipeline).
        yield return ["{\"type\":\"bark\",\"event\":\"wave-cleared\",\"wave\":3}", typeof(DtrhProtocol.DtrhPageMessage.Bark), null];
        yield return ["{\"type\":\"meta-command\",\"op\":\"add-gold\",\"amount\":50}", typeof(DtrhProtocol.DtrhPageMessage.MetaCommand), null];
        yield return ["{\"type\":\"request-run\",\"setup\":{\"difficulty\":\"Hard\"}}", typeof(DtrhProtocol.DtrhPageMessage.RequestRun), null];
        yield return ["{\"type\":\"run-started\",\"difficulty\":\"Gentle\",\"mode\":\"dtrh-web\"}", typeof(DtrhProtocol.DtrhPageMessage.RunStarted), null];
        yield return ["{\"type\":\"run-ended\",\"score\":1234.5,\"durationSec\":180,\"difficulty\":\"Gentle\",\"sessionStats\":{\"bubblesPopped\":42}}", typeof(DtrhProtocol.DtrhPageMessage.RunEnded), null];
        yield return ["{\"type\":\"asset-stats\",\"deltas\":{\"img1.png\":{\"watch\":3.5}}}", typeof(DtrhProtocol.DtrhPageMessage.AssetStats), null];
        yield return ["{\"type\":\"loom-save\",\"name\":\"dream\",\"gifBase64\":\"R0lG\",\"params\":{\"rings\":4},\"overwrite\":false}", typeof(DtrhProtocol.DtrhPageMessage.LoomSave), null];
        yield return ["{\"type\":\"loom-delete\",\"slug\":\"dream\"}", typeof(DtrhProtocol.DtrhPageMessage.LoomDelete), null];
        // SP-049: the v6.6.3 studio rack's 📂 (loomStudio.js:749 — emitted from BOTH homes).
        yield return ["{\"type\":\"loom-reveal\",\"slug\":\"dream\"}", typeof(DtrhProtocol.DtrhPageMessage.LoomReveal), null];
        yield return ["{\"type\":\"report-bug\"}", typeof(DtrhProtocol.DtrhPageMessage.ReportBug), "unassigned/host-ui"];
    }

    [Theory]
    [MemberData(nameof(PageVocabulary))]
    public void PageMessage_EveryVocabularyType_ParsesAndClassifies(string json, Type expectedType, string? deferredSlice)
    {
        var result = DtrhProtocol.ParsePageMessage(json);
        var parsed = Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(result);
        Assert.IsType(expectedType, parsed.Message);

        var classification = DtrhProtocol.Classify(parsed.Message);
        if (deferredSlice is null)
        {
            Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(classification);
        }
        else
        {
            var deferred = Assert.IsType<DtrhProtocol.DtrhDispatchClass.Deferred>(classification);
            Assert.Equal(deferredSlice, deferred.Slice);
        }
    }

    [Fact]
    public void PageMessage_TypedFields_MatchPayloadShapes()
    {
        var ready = Assert.IsType<DtrhProtocol.DtrhPageMessage.Ready>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage("{\"type\":\"ready\",\"protocol\":1}")).Message);
        Assert.Equal(1, ready.Protocol);

        var sfx = Assert.IsType<DtrhProtocol.DtrhPageMessage.Sfx>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage("{\"type\":\"sfx\",\"name\":\"ripple_cast\",\"scale\":0.8}")).Message);
        Assert.Equal("ripple_cast", sfx.Name);
        Assert.Equal(0.8, sfx.Scale);

        // WPF default: scale falls back to 0.6 when absent (DtrhHostService.cs:226).
        var sfxDefault = Assert.IsType<DtrhProtocol.DtrhPageMessage.Sfx>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage("{\"type\":\"sfx\",\"name\":\"chime\"}")).Message);
        Assert.Equal(0.6, sfxDefault.Scale);

        var fire = Assert.IsType<DtrhProtocol.DtrhPageMessage.FirePayload>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage("{\"type\":\"fire-payload\",\"kind\":\"audio\",\"strength\":70}")).Message);
        Assert.Equal("audio", fire.Kind);
        Assert.Equal(70, fire.Strength);
        Assert.Null(fire.DurationMult);

        var runEnded = Assert.IsType<DtrhProtocol.DtrhPageMessage.RunEnded>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage(
                    "{\"type\":\"run-ended\",\"score\":900,\"durationSec\":180,\"difficulty\":\"Gentle\",\"sessionStats\":{\"bubblesPopped\":42}}")).Message);
        Assert.Equal(900, runEnded.Score);
        Assert.Equal(180, runEnded.DurationSec);
        Assert.Equal("Gentle", runEnded.Difficulty);
        Assert.Equal(42, runEnded.Raw.GetProperty("sessionStats").GetProperty("bubblesPopped").GetInt32());

        var requestRun = Assert.IsType<DtrhProtocol.DtrhPageMessage.RequestRun>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage("{\"type\":\"request-run\",\"setup\":{\"difficulty\":\"Hard\"}}")).Message);
        Assert.Equal("Hard", requestRun.Setup!.Value.GetProperty("difficulty").GetString());

        // request-run without setup is legal (WPF: setup optional, DtrhHostService.cs:431).
        var bare = Assert.IsType<DtrhProtocol.DtrhPageMessage.RequestRun>(
            Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(
                DtrhProtocol.ParsePageMessage("{\"type\":\"request-run\"}")).Message);
        Assert.Null(bare.Setup);
    }

    // ---------- tolerance: never silent, never crash ----------

    [Fact]
    public void UnknownType_TypedTolerance()
    {
        var result = DtrhProtocol.ParsePageMessage("{\"type\":\"quantum-leap\",\"payload\":{}}");
        var unknown = Assert.IsType<DtrhProtocol.DtrhPageParseResult.UnknownType>(result);
        Assert.Equal("quantum-leap", unknown.Type);
    }

    [Fact]
    public void ForwardVersion_TypedTolerance_KnownAndUnknownTypes()
    {
        var known = Assert.IsType<DtrhProtocol.DtrhPageParseResult.ForwardVersion>(
            DtrhProtocol.ParsePageMessage("{\"type\":\"ready\",\"protocol\":2}"));
        Assert.Equal("ready", known.Type);
        Assert.Equal(2, known.Protocol);

        // A newer page's NEW type with a newer protocol is still ForwardVersion (checked first).
        var newer = Assert.IsType<DtrhProtocol.DtrhPageParseResult.ForwardVersion>(
            DtrhProtocol.ParsePageMessage("{\"type\":\"brand-new\",\"protocol\":3}"));
        Assert.Equal(3, newer.Protocol);
    }

    [Theory]
    [InlineData("not json {{{")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"type\":42}")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void Malformed_TypedOutcome_NeverThrows(string json)
    {
        Assert.IsType<DtrhProtocol.DtrhPageParseResult.Malformed>(DtrhProtocol.ParsePageMessage(json));
    }

    // ---------- host → page: builders match the payload's handler reads ----------

    private static JsonElement RoundTrip(object message)
    {
        var json = DtrhProtocol.SerializeForPage(message);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void BuildInit_ShapeMatches_BootJsHandler_AndWpfPost()
    {
        var setup = new DtrhProtocol.DtrhRunSetup(
            "Easy", 180, 5, "Mixed", null, 0.85, true, true, true, true, "Q", "E");
        var root = RoundTrip(DtrhProtocol.BuildInit(80, "builtin-sissyhypno", null, setup, false));

        Assert.Equal("init", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("protocol").GetInt32());
        Assert.Equal(80, root.GetProperty("settings").GetProperty("masterVolume").GetInt32());
        Assert.Equal("builtin-sissyhypno", root.GetProperty("modId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("modContent").ValueKind);
        Assert.False(root.GetProperty("m2Test").GetBoolean());
        var rs = root.GetProperty("runSetup");
        Assert.Equal("Easy", rs.GetProperty("difficulty").GetString());
        Assert.Equal(180, rs.GetProperty("durationSec").GetInt32());
        Assert.Equal(5, rs.GetProperty("waveCount").GetInt32());
        Assert.Equal("Mixed", rs.GetProperty("motion").GetString());
        Assert.Equal(JsonValueKind.Null, rs.GetProperty("enabledVariants").ValueKind);
        Assert.Equal(0.85, rs.GetProperty("effectIntensity").GetDouble());
        Assert.True(rs.GetProperty("colorFlashes").GetBoolean());
        Assert.True(rs.GetProperty("boonDraftEnabled").GetBoolean());
        Assert.True(rs.GetProperty("allowCurses").GetBoolean());
        Assert.True(rs.GetProperty("dartersEnabled").GetBoolean());
        Assert.Equal("Q", rs.GetProperty("key1").GetString());
        Assert.Equal("E", rs.GetProperty("key2").GetString());
    }

    [Fact]
    public void BuildManifest_ShapeMatches_BootJsHandler()
    {
        var root = RoundTrip(DtrhProtocol.BuildManifest(
            [new DtrhProtocol.DtrhManifestEntry("bubble.png", "http://127.0.0.1:9/media/bubble.png")],
            [new DtrhProtocol.DtrhManifestEntry("spiral.webm", "http://127.0.0.1:9/media/spiral.webm")],
            2, true));

        Assert.Equal("manifest", root.GetProperty("type").GetString());
        var img = root.GetProperty("images")[0];
        Assert.Equal("bubble.png", img.GetProperty("name").GetString());
        Assert.EndsWith("bubble.png", img.GetProperty("url").GetString()!);
        Assert.Equal(1, root.GetProperty("videos").GetArrayLength());
        Assert.Equal(2, root.GetProperty("skipped").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void BuildMeta_Favorites_RunConfig_PayloadState_Fullscreen_EndRun_Ping_Shapes()
    {
        var meta = RoundTrip(DtrhProtocol.BuildMeta(JsonDocument.Parse("{\"sparks\":5}").RootElement, 7));
        Assert.Equal("meta", meta.GetProperty("type").GetString());
        Assert.Equal(5, meta.GetProperty("state").GetProperty("sparks").GetInt32());
        Assert.Equal(7, meta.GetProperty("rev").GetInt32());

        var fav = RoundTrip(DtrhProtocol.BuildFavorites(["a.png", "b.png"]));
        Assert.Equal("favorites", fav.GetProperty("type").GetString());
        Assert.Equal("a.png", fav.GetProperty("names")[0].GetString());

        var rc = RoundTrip(DtrhProtocol.BuildRunConfig(JsonDocument.Parse("{\"difficulty\":\"Easy\"}").RootElement));
        Assert.Equal("run-config", rc.GetProperty("type").GetString());
        Assert.Equal("Easy", rc.GetProperty("runConfig").GetProperty("difficulty").GetString());

        var ps = RoundTrip(DtrhProtocol.BuildPayloadState("video", true));
        Assert.Equal("payload-state", ps.GetProperty("type").GetString());
        Assert.Equal("video", ps.GetProperty("kind").GetString());
        Assert.True(ps.GetProperty("on").GetBoolean());

        var fs = RoundTrip(DtrhProtocol.BuildFullscreen(true));
        Assert.Equal("fullscreen", fs.GetProperty("type").GetString());
        Assert.True(fs.GetProperty("on").GetBoolean());

        var er = RoundTrip(DtrhProtocol.BuildEndRun("host"));
        Assert.Equal("end-run", er.GetProperty("type").GetString());
        Assert.Equal("host", er.GetProperty("reason").GetString());

        var ping = RoundTrip(DtrhProtocol.BuildPing(42.5));
        Assert.Equal("ping", ping.GetProperty("type").GetString());
        Assert.Equal(42.5, ping.GetProperty("t").GetDouble());
    }

    [Fact]
    public void BuildPayoutResult_ShapeMatches_BootJsHandler()
    {
        var root = RoundTrip(DtrhProtocol.BuildPayoutResult(
            new DtrhProtocol.DtrhPayoutResult(100, 1.5, 150, 12, 9000, "Slipping", false)));

        Assert.Equal("payout-result", root.GetProperty("type").GetString());
        Assert.Equal(100, root.GetProperty("baseXp").GetDouble());
        Assert.Equal(1.5, root.GetProperty("skillMult").GetDouble());
        Assert.Equal(150, root.GetProperty("finalXp").GetDouble());
        Assert.Equal(12, root.GetProperty("sparksEarned").GetInt32());
        Assert.Equal(9000, root.GetProperty("previousBest").GetInt64());
        Assert.Equal("Slipping", root.GetProperty("rankUp").GetString());
        Assert.False(root.GetProperty("dryRun").GetBoolean());
    }

    [Fact]
    public void BuildLoomList_ParamsKeyword_SerializesAsParams()
    {
        var spiral = new DtrhProtocol.DtrhLoomSpiral(
            "dream", "https://ccp.spirals/loom_dream.gif",
            JsonDocument.Parse("{\"rings\":4}").RootElement);
        var root = RoundTrip(DtrhProtocol.BuildLoomList([spiral]));

        Assert.Equal("loom-list", root.GetProperty("type").GetString());
        var s = root.GetProperty("spirals")[0];
        Assert.Equal("dream", s.GetProperty("slug").GetString());
        Assert.Equal("https://ccp.spirals/loom_dream.gif", s.GetProperty("url").GetString());
        // The payload reads `s.params` (DtrhHostService.cs:341-355) — the C# keyword must not leak as "Params".
        Assert.Equal(4, s.GetProperty("params").GetProperty("rings").GetInt32());
    }

    [Fact]
    public void BuildLoomResult_ShapeMatches_BootJsHandler()
    {
        var root = RoundTrip(DtrhProtocol.BuildLoomResult("save", false, "dream", "name taken"));
        Assert.Equal("loom-result", root.GetProperty("type").GetString());
        Assert.Equal("save", root.GetProperty("op").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("dream", root.GetProperty("slug").GetString());
        Assert.Equal("name taken", root.GetProperty("error").GetString());
    }
}
