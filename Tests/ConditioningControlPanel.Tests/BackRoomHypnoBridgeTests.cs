using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM protocol, Hypno v3 half (CONTRACT 10.13): <c>fx.args</c> and the token reach the
/// dispatcher, <c>fx-tunnel</c> / <c>fx-release</c> are forwarded with no reply, <c>station-close</c>
/// releases that station, <c>media-request.count</c>, the cards and roulette relay rows, and the host's
/// <c>gates</c> and <c>br_</c> lexicon projections.
/// </summary>
public class BackRoomHypnoBridgeTests
{
    private sealed class RecordingFx : IBackRoomFx
    {
        public readonly List<string> Calls = new();
        public BackRoomFxArgs? LastArgs;
        public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal)
            => throw new InvalidOperationException("the bridge must use the args overload");
        public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal, BackRoomFxArgs? args, string? token)
        {
            LastArgs = args;
            Calls.Add($"fire:{fxId}:{station}:{token}:{deal.Gifs.Count}");
            return new BackRoomFxAck(new[] { "wash" }, Array.Empty<BackRoomFxSkip>());
        }
        public void Release(string token, string station) => Calls.Add($"release:{token}:{station}");
        public void Tunnel(string station, double level) => Calls.Add(FormattableString.Invariant($"tunnel:{station}:{level}"));
        public void ReleaseStation(string station) => Calls.Add($"station:{station}");
        public void CancelAll() => Calls.Add("cancel");
    }

    private sealed class RecordingMedia : IBackRoomMedia
    {
        public readonly List<int> Counts = new();
        public BackRoomMediaDeal Deal(string station, int seed, int count = 4)
        {
            Counts.Add(count);
            return new NullBackRoomMedia(null).Deal(station, seed, count);
        }
    }

    private static (BackRoomBridge Bridge, RecordingFx Fx, RecordingMedia Media, List<JObject> Posted) Make()
    {
        var fx = new RecordingFx();
        var media = new RecordingMedia();
        var posted = new List<JObject>();
        var bridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = m => posted.Add(JObject.FromObject(m)),
            Relay = new BackRoomBridgeTests.Relay(),
            Fx = fx,
            Media = media,
            BuildInit = () => new { type = "init" },
            CloseWindow = () => { },
            Schedule = (_, _) => () => { },
        });
        return (bridge, fx, media, posted);
    }

    [Fact]
    public void Fx_CarriesArgsAndToken_AndStillAcks()
    {
        var (bridge, fx, _, posted) = Make();
        bridge.Handle(JObject.Parse("""{"type":"media-request","reqId":"m1","station":"wheel","count":4}"""));
        bridge.Handle(JObject.Parse("""{"type":"fx","token":"0123456789abcdef0123456789abcdef","fxId":"fx.gif_from","station":"wheel","symbols":["g2"],"args":{"from":{"x":612,"y":188,"w":60,"h":44},"ms":3400,"scale":1}}"""));
        Assert.Equal("fire:fx.gif_from:wheel:0123456789abcdef0123456789abcdef:4", fx.Calls.Last());
        Assert.Equal(new BackRoomFxArgs(From: new FxCssRect(612, 188, 60, 44), Ms: 3400, Scale: 1), fx.LastArgs);
        Assert.Equal(new[] { "wash" }, posted.Last(p => (string?)p["type"] == "fx-ack")["fired"]!.ToObject<string[]>());

        bridge.Handle(JObject.Parse($$"""{"type":"fx","token":"{{new string('a', 65)}}","fxId":"fx.wash","station":"wheel"}"""));
        Assert.Equal("fire:fx.wash:wheel::4", fx.Calls.Last());   // an oversized token is never remembered
    }

    [Fact]
    public void TunnelAndRelease_AreForwarded_WithNoReply()
    {
        var (bridge, fx, _, posted) = Make();
        bridge.Handle(JObject.Parse("""{"type":"fx-tunnel","station":"wheel","level":0.62}"""));
        bridge.Handle(JObject.Parse("""{"type":"fx-tunnel","station":"wheel","level":"0.9"}"""));
        bridge.Handle(JObject.Parse("""{"type":"fx-tunnel","station":"../x","level":1}"""));
        bridge.Handle(JObject.Parse("""{"type":"fx-release","token":"tok","station":"roulette"}"""));
        bridge.Handle(JObject.Parse("""{"type":"fx-release","station":"roulette"}"""));
        Assert.Equal(new[] { "tunnel:wheel:0.62", "release:tok:roulette" }, fx.Calls);
        Assert.Empty(posted);

        bridge.Handle(JObject.Parse("""{"type":"exit","reason":"back"}"""));
        bridge.Handle(JObject.Parse("""{"type":"fx-tunnel","station":"wheel","level":0.5}"""));
        Assert.DoesNotContain("tunnel:wheel:0.5", fx.Calls);
    }

    [Fact]
    public void Suspended_DropsTunnelAndAcksFxBusy_UntilResumed()
    {
        var (bridge, fx, _, posted) = Make();
        bridge.Suspend(true, "panic");
        bridge.Handle(JObject.Parse("""{"type":"fx-tunnel","station":"wheel","level":0.8}"""));
        bridge.Handle(JObject.Parse("""{"type":"fx","token":"t1","fxId":"fx.wash","station":"wheel"}"""));
        Assert.Equal(new[] { "cancel" }, fx.Calls);
        Assert.Equal("busy", (string?)posted.Last(p => (string?)p["type"] == "fx-ack")["skipped"]![0]!["why"]);

        bridge.Suspend(false, "panic");
        bridge.Handle(JObject.Parse("""{"type":"fx-tunnel","station":"wheel","level":0.8}"""));
        Assert.Equal("tunnel:wheel:0.8", fx.Calls.Last());
    }

    [Fact]
    public void StationClose_ReleasesThatStation()
    {
        var (bridge, fx, _, _) = Make();
        bridge.Handle(JObject.Parse("""{"type":"station-close","station":"roulette"}"""));
        Assert.Equal(new[] { "station:roulette" }, fx.Calls);
    }

    [Theory]
    [InlineData("13", 13)]
    [InlineData("1", 1)]
    [InlineData("0", 4)]
    [InlineData("14", 4)]
    [InlineData("4.5", 4)]
    [InlineData("\"13\"", 4)]
    [InlineData("null", 4)]
    public void MediaRequest_CountIsAnInteger1To13_Else4(string count, int want)
    {
        var (bridge, _, media, posted) = Make();
        bridge.Handle(JObject.Parse($$"""{"type":"media-request","reqId":"m1","station":"cards","count":{{count}}}"""));
        Assert.Equal(want, Assert.Single(media.Counts));
        Assert.Single(posted, p => (string?)p["type"] == "media");
    }

    [Theory]
    [InlineData("cards", "state", "GET")]
    [InlineData("cards", "deal", "POST")]
    [InlineData("cards", "hit", "POST")]
    [InlineData("cards", "stand", "POST")]
    [InlineData("cards", "double", "POST")]
    [InlineData("cards", "split", "POST")]
    [InlineData("roulette", "state", "GET")]
    [InlineData("roulette", "spin", "POST")]
    [InlineData("roulette", "cursor", "POST")]
    public void CardsAndRoulette_Rows(string station, string op, string method)
    {
        Assert.True(BackRoomApi.TryResolve(station, op, out var m, out var path));
        Assert.Equal((method, $"/v2/backroom/{station}/{op}"), (m, path));
    }

    [Theory]
    [InlineData("cards", "insurance")]
    [InlineData("cards", "cursor")]
    [InlineData("roulette", "deal")]
    public void CardsAndRoulette_NothingElse(string station, string op)
        => Assert.False(BackRoomApi.TryResolve(station, op, out _, out _));

    /// <summary>
    /// Owner, 2026-09-18: the casino ignores the panel's feature toggles. The four hypno gates are always on, the
    /// same object the web shim sends, so a player who never switched Flash / Subliminal / Brain Drain on still gets
    /// the whole show. Only the room's own tunnel and melt switches still come off the settings.
    /// </summary>
    [Fact]
    public void Gates_IgnoreThePanelToggles_OnlyTheRoomSwitchesFollowSettings()
    {
        var allOff = new AppSettings { FlashEnabled = false, SubliminalEnabled = false, SpiralEnabled = false, BrainDrainEnabled = false,
            BackRoomTunnel = true, BackRoomMelt = false };
        Assert.Equal("""{"flash":true,"subliminal":true,"spiral":true,"brainDrain":true,"tunnel":true,"melt":false}""",
            JObject.FromObject(BackRoomHostService.GatesWire(allOff)).ToString(Newtonsoft.Json.Formatting.None));
        var roomOn = new AppSettings { FlashEnabled = false, SubliminalEnabled = false, BrainDrainEnabled = false, BackRoomTunnel = false, BackRoomMelt = true };
        Assert.Equal("""{"flash":true,"subliminal":true,"spiral":true,"brainDrain":true,"tunnel":false,"melt":true}""",
            JObject.FromObject(BackRoomHostService.GatesWire(roomOn)).ToString(Newtonsoft.Json.Formatting.None));
        // No settings at all: the four are still on; the room switches read as off.
        Assert.All(JObject.FromObject(BackRoomHostService.GatesWire(null)).Properties(),
            p => Assert.Equal(p.Name is not ("tunnel" or "melt"), (bool)p.Value));
        foreach (var name in new[] { "MotionLevel", "BackRoomFxIntensity", "BackRoomTunnel", "BackRoomMelt" })
            Assert.Contains(name, BackRoomHostService.SettingsFrameProperties);
        // The panel toggles no longer push a settings frame: the gates do not follow them.
        foreach (var name in new[] { "FlashEnabled", "SubliminalEnabled", "SpiralEnabled", "BrainDrainEnabled" })
            Assert.DoesNotContain(name, BackRoomHostService.SettingsFrameProperties);
    }

    /// <summary>
    /// The room's hypno dressing is not the panel's fullscreen Spiral Overlay. It used to carry
    /// AppSettings.SpiralEnabled, which RandomizeAndStart coin-flips, so "jump right in" decided at
    /// random whether the Daily Daze wheel had a Loom spiral in its hub or a brass star. It renders on
    /// the web playtest because a page with no settings frame reads every gate as true.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SpiralDressing_DoesNotFollowTheFullscreenOverlayToggle(bool overlay)
    {
        var gates = JObject.FromObject(BackRoomHostService.GatesWire(new AppSettings { SpiralEnabled = overlay }));
        Assert.True((bool)gates["spiral"]!);
    }

    [Fact]
    public void Lex_IsEveryBrKeyInEnJson_Once()
    {
        var en = FindEnJson();
        using var doc = JsonDocument.Parse(File.ReadAllText(en));
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).Where(k => k.StartsWith(BackRoomHostService.LexPrefix)).ToList();
        Assert.Contains("br_room_title", keys);
        Assert.Contains("br_wheel_slice_dazed", keys);

        var lex = BackRoomHostService.Lex(keys.Concat(keys), k => "T:" + k);
        Assert.Equal(keys.Distinct().Count(), lex.Count);
        Assert.Equal("T:br_room_title", lex["br_room_title"]);
    }

    // The catalogue moved to CCP.Core, which is a SIBLING of ConditioningControlPanel, so the
    // parent walk above could never find it. SourceRoots probes every product root instead.
    private static string FindEnJson() =>
        Path.Combine(SourceRoots.LanguagesDirectory, "en.json");
}
