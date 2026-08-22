using System.Security.Cryptography;
using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Goon;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Goon practice host: the payload it serves, the frames it transcribes, and the
/// four doors it refuses.
///
/// <para><b>What these facts are and are not.</b> They pin SHAPE and SOURCE: that the payload
/// really lands in the build output, that no byte of it is forked, that <c>init</c> and
/// <c>manifest</c> carry exactly the fields the two upstream copies carry, and that the caps are
/// COMPUTED from the door refusals rather than written beside them. <b>None of them is evidence
/// that the page loaded, that the handshake completed, or that a duel is playable</b> — a compile
/// is not a load and a load is not a duel, and no test in this assembly can reach either.</para>
/// </summary>
public sealed class GoonPracticeTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] UpstreamGoonParts = ["ConditioningControlPanel", "Resources", "web", "goon"];

    /// <summary>Walks up to the directory holding client/CcpClient.sln — present in every
    /// checkout AND in worktrees (where .git is a file). Failure to resolve throws: a guard that
    /// cannot find its repo must fail, not skip.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} "
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the goon payload guard refuses to skip");
    }

    private static string UpstreamGoonRoot() => Path.Combine([FindRepoRoot(), .. UpstreamGoonParts]);

    private static string OutputGoonRoot() => Path.Combine(AppContext.BaseDirectory, "payload", "goon");

    // ==================================================================================
    // 1. THE PAYLOAD SHIPS — a build-output assertion, and the only claim of the three in
    //    the packet's trap 5 that this assembly can discharge.
    // ==================================================================================

    [Fact]
    public void PayloadGlob_CopiesTheWholeUpstreamGoonTree_IntoTheBuildOutput()
    {
        var upstream = RequireTree(UpstreamGoonRoot(), "the upstream goon tree");
        var output = RequireTree(
            OutputGoonRoot(),
            "payload/goon in the build output — the linked glob in CcpClient.Desktop.csproj is "
            + "what puts it there, and without it the host serves nothing");

        var upstreamRelative = Relatives(upstream);
        var outputRelative = Relatives(output);
        Assert.NotEmpty(upstreamRelative);

        // Every upstream file, by relative path. Equality both ways: a NARROWED glob loses files,
        // and an extra file in the output would mean something other than the glob wrote there.
        Assert.Equal(upstreamRelative, outputRelative);
        Assert.Equal(GoonServingRoots.FileCountAtBaseline, outputRelative.Count);
    }

    [Fact]
    public void PayloadGlob_CopiesTheBytesUNMODIFIED()
    {
        var upstream = UpstreamGoonRoot();
        var output = OutputGoonRoot();
        var relatives = Relatives(upstream);

        // Non-vacuity, at depth 0: an empty tree would make every nested assertion below
        // unreachable and this test green over nothing.
        Assert.Equal(GoonServingRoots.FileCountAtBaseline, relatives.Count);

        foreach (var relative in relatives)
        {
            var a = Sha256(Path.Combine(upstream, relative));
            var b = Sha256(Path.Combine(output, relative));
            Assert.True(a == b, $"payload/goon/{relative} differs from the upstream byte — the glob copies, it never transforms");
        }
    }

    [Fact]
    public void EveryFileThePageBootsFrom_IsPresentInTheOutput()
    {
        var shipped = Relatives(OutputGoonRoot());
        Assert.Equal(GoonServingRoots.FileCountAtBaseline, shipped.Count);

        // index.html is the document; boot.js is its only module script (index.html:96); bridge.js
        // is boot.js's transport import; soloDriver + loopbackTransport are Practice itself
        // (goon-game-census.md §7.1 — "Practice runs entirely in the page on the loopback pair").
        foreach (var required in new[]
                 {
                     "index.html", "boot.js", "bridge.js", "goon.css",
                     "ui/screens/title.js", "ui/soloDriver.js", "net/loopbackTransport.js",
                 })
        {
            Assert.Contains(required, shipped);
        }
    }

    /// <summary>
    /// ZERO BYTES FORKED. Every file the client tree owns is hashed against the upstream goon
    /// tree; a match means a payload file was copied into <c>client/</c>, which the packet calls
    /// a blocking defect rather than a shortcut. Build outputs are excluded because that is
    /// exactly where the glob is SUPPOSED to put copies.
    /// </summary>
    [Fact]
    public void NoGoonPayloadByte_IsForkedIntoTheClientTree()
    {
        var repoRoot = FindRepoRoot();
        var upstream = UpstreamGoonRoot();
        var upstreamHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relative in Relatives(upstream))
        {
            upstreamHashes[Sha256(Path.Combine(upstream, relative))] = relative;
        }

        var swept = ClientOwnedFiles(repoRoot);
        // Non-vacuity, at depth 0: a sweep that walked nothing would report no offenders.
        Assert.Equal(GoonServingRoots.FileCountAtBaseline, upstreamHashes.Count);
        Assert.True(swept.Count > 500, $"the client-owned sweep found only {swept.Count} files — it walked the wrong tree");

        var offenders = new List<string>();
        foreach (var file in swept)
        {
            if (upstreamHashes.TryGetValue(Sha256(file), out var upstreamRelative))
            {
                offenders.Add(
                    $"{Path.GetRelativePath(repoRoot, file).Replace('\\', '/')} is a byte-copy of Resources/web/goon/{upstreamRelative}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "payload bytes forked into client/: " + string.Join("; ", offenders)
            + " — the goon tree is served by a LINKED read-only glob and the legacy tree stays its only owner");
    }

    // ==================================================================================
    // 2. THE FOUR DOORS — and the derivation the caps hang from.
    // ==================================================================================

    [Fact]
    public void ExactlyFourDoors_AreRefused_OneForEachOwnerGatedTitleItem()
    {
        Assert.Equal(4, GoonDoors.Refused.Count);
        Assert.Equal(
            new[] { GoonDoors.GoonDoor.Host, GoonDoors.GoonDoor.Join, GoonDoors.GoonDoor.VoiceNotes, GoonDoors.GoonDoor.MediaSetup },
            GoonDoors.Refused.Select(r => r.Door).ToArray());
        Assert.Equal(4, GoonDoors.Refused.Select(r => r.Door).Distinct().Count());
    }

    [Theory]
    [InlineData(GoonDoors.GoonDoor.Host)]
    [InlineData(GoonDoors.GoonDoor.Join)]
    [InlineData(GoonDoors.GoonDoor.VoiceNotes)]
    [InlineData(GoonDoors.GoonDoor.MediaSetup)]
    public void EveryRefusal_NamesWhatIsMissing_AndCarriesItsOwnerGate(GoonDoors.GoonDoor door)
    {
        var refusal = GoonDoors.For(GoonDoors.Refused, door);
        Assert.NotNull(refusal);
        Assert.False(string.IsNullOrWhiteSpace(refusal!.Headline));
        // The honesty payload. A one-word refusal is not one.
        Assert.True(refusal.Missing.Length > 60, $"{door}: the refusal must NAME what is missing, not merely decline");
        Assert.Contains("goon-game-census.md", refusal.OwnerGate, StringComparison.Ordinal);
        Assert.Contains("D24", refusal.OwnerGate, StringComparison.Ordinal);
        // The page's own (false-here) sentence is recorded beside it, with its citation, so the
        // two cannot drift apart unnoticed.
        Assert.Contains(".js:", refusal.PageSaysInstead, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusals must not adopt the page's framing. Upstream's four sentences say "supporter
    /// perk" (ui/strings.js:41, :731) or "running in a plain browser" (:751-752) — a purchase and
    /// a browser, neither of which exists here. A refusal that borrowed those words would be the
    /// "coming soon that implies work rather than a decision" the packet bans.
    /// </summary>
    [Theory]
    [InlineData("perk")]
    [InlineData("coming soon")]
    [InlineData("not yet")]
    [InlineData("for now")]
    [InlineData("tier")]
    [InlineData("upgrade")]
    [InlineData("subscri")]
    [InlineData("plain browser")]
    public void NoRefusal_BorrowsThePagesFalseFraming(string banned)
    {
        Assert.Equal(4, GoonDoors.Refused.Count);   // depth-0 non-vacuity pin for the loop below
        foreach (var refusal in GoonDoors.Refused)
        {
            var said = refusal.Headline + " " + refusal.Missing;
            Assert.DoesNotContain(banned, said, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ThisBuildsCaps_RefuseAllThreeGatedMembers()
    {
        var caps = GoonDoors.Caps;
        Assert.False(caps.CanHost);
        Assert.False(caps.MediaTransfer);
        Assert.False(caps.AssetCache);
    }

    /// <summary>
    /// <b>THE DERIVATION FACT.</b> Caps are COMPUTED from the refusal set, so removing a door's
    /// refusal re-opens its cap. This is the fact that reds if the caps are ever rewritten as
    /// literals beside the list — a literal-equality pin over today's values would pass happily
    /// while <c>CapsFor</c> ignored its argument, which is the exact shape of a guard whose
    /// description outruns its mechanism.
    /// </summary>
    [Fact]
    public void Caps_AreComputedFromTheRefusalSet_NotWrittenBesideIt()
    {
        var withoutHost = GoonDoors.Refused.Where(r => r.Door != GoonDoors.GoonDoor.Host).ToList();
        Assert.True(GoonDoors.CapsFor(withoutHost).CanHost,
            "canHost must be DERIVED from the Host refusal — remove the refusal and the cap re-opens");

        var withoutMediaSetup = GoonDoors.Refused.Where(r => r.Door != GoonDoors.GoonDoor.MediaSetup).ToList();
        Assert.True(GoonDoors.CapsFor(withoutMediaSetup).AssetCache,
            "assetCache must be DERIVED from the media-setup refusal");

        // mediaTransfer gates BOTH lanes upstream — sending media (GoonHostService.cs:894) and
        // voice notes, which ride the same cap (ui/screens/voice.js:125, ui/screens/lobby.js:403)
        // — so it re-opens only when BOTH doors do.
        Assert.False(GoonDoors.CapsFor(withoutMediaSetup).MediaTransfer);
        var withNeitherSendDoor = GoonDoors.Refused
            .Where(r => r.Door is not (GoonDoors.GoonDoor.MediaSetup or GoonDoors.GoonDoor.VoiceNotes))
            .ToList();
        Assert.True(GoonDoors.CapsFor(withNeitherSendDoor).MediaTransfer);

        // And with nothing refused, every derived cap is open. If this line ever fails, the
        // derivation has been replaced by a constant.
        var none = GoonDoors.CapsFor([]);
        Assert.True(none.CanHost && none.MediaTransfer && none.AssetCache);
    }

    [Fact]
    public void TheFiveTranscribedCaps_DoNotMoveWithTheDoors()
    {
        // GoonHostService.cs:333-337: haptics false, brainDrain (:880 => true), spiral true,
        // camera false, video true. No door governs these, so an empty refusal set must not
        // change them — the derivation is scoped, not a blanket.
        var shipped = GoonDoors.Caps;
        var withNothingRefused = GoonDoors.CapsFor([]);

        Assert.False(shipped.Haptics);
        Assert.True(shipped.BrainDrain);
        Assert.True(shipped.Spiral);
        Assert.False(shipped.Camera);
        Assert.True(shipped.Video);

        Assert.False(withNothingRefused.Haptics);
        Assert.True(withNothingRefused.BrainDrain);
        Assert.True(withNothingRefused.Spiral);
        Assert.False(withNothingRefused.Camera);
        Assert.True(withNothingRefused.Video);
    }

    [Fact]
    public void TheNetPostRefusal_IsTheJoinDoors()
    {
        Assert.Same(GoonDoors.For(GoonDoors.Refused, GoonDoors.GoonDoor.Join), GoonDoors.NetRefusal);
        // With no door refused there is no honest sentence to answer with, and the host says so
        // rather than inventing one.
        Assert.Null(GoonDoors.NetRefusalFor([]));
    }

    // ==================================================================================
    // 3. THE init FRAME — transcribed, not invented.
    // ==================================================================================

    private static JsonElement InitFrame() =>
        JsonDocument.Parse(GoonProtocol.SerializeForPage(
            GoonProtocol.BuildInit("Player", "0.1.0", GoonDoors.Caps, fullscreen: false))).RootElement.Clone();

    [Fact]
    public void Init_CarriesExactlyTheUpstreamRootFields()
    {
        // GoonHostService.cs:311-354 carries type/protocol/identity/net/caps/consent/fullscreen
        // (+ discord, which this build has none of). bridge.js:388-391 adds `solo` at the ROOT.
        Assert.Equal(
            new[] { "type", "protocol", "solo", "identity", "net", "caps", "consent", "fullscreen" },
            InitFrame().EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Init_IsProtocolOne_AndSoloIsOnAtTheFrameRoot()
    {
        var init = InitFrame();
        Assert.Equal("init", init.GetProperty("type").GetString());
        Assert.Equal(1, init.GetProperty("protocol").GetInt32());
        // bridge.js:391 defaults solo ON; boot.js:323 reads it as m.solo. It is NOT a caps member.
        Assert.True(init.GetProperty("solo").GetBoolean());
        Assert.False(init.GetProperty("caps").TryGetProperty("solo", out _));
    }

    [Fact]
    public void Init_Identity_IsTheUpstreamThreeFields_WithNoAccountAndNoAnonymousFlag()
    {
        var identity = InitFrame().GetProperty("identity");
        Assert.Equal(new[] { "unifiedId", "displayName", "appVersion" }, identity.EnumerateObject().Select(p => p.Name).ToArray());
        // Upstream: App.UnifiedUserId ?? "" (:317). There are no accounts here at all.
        Assert.Equal("", identity.GetProperty("unifiedId").GetString());
        // `anonymous` is set only by bridge.standaloneInit, and boot.js:335 reads it with === true,
        // so a host that never sends it is correctly NOT anonymous. Sending it would be an invention.
        Assert.False(identity.TryGetProperty("anonymous", out _));
    }

    [Fact]
    public void Init_Net_NamesNoServerAndCarriesNoToken_AndKeepsViaHostTrue()
    {
        var net = InitFrame().GetProperty("net");
        Assert.Equal(new[] { "serverBase", "authToken", "viaHost" }, net.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("", net.GetProperty("serverBase").GetString());
        Assert.Equal("", net.GetProperty("authToken").GetString());
        // TRUE on purpose: it routes every server call into net-post, which this host refuses
        // in-process. FALSE would make the page issue a real fetch() itself (bridge.js:181-192).
        Assert.True(net.GetProperty("viaHost").GetBoolean());
    }

    [Fact]
    public void Init_Caps_AreExactlyEightMembers_InTheUpstreamOrder()
    {
        Assert.Equal(
            new[] { "haptics", "brainDrain", "spiral", "camera", "video", "mediaTransfer", "canHost", "assetCache" },
            InitFrame().GetProperty("caps").EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Init_Consent_IsTheEnginesOwnThreeDefaults()
    {
        var consent = InitFrame().GetProperty("consent");
        Assert.Equal(new[] { "liveDurationSec", "toyCap", "payloadMinGapMs" }, consent.EnumerateObject().Select(p => p.Name).ToArray());
        // GoonContracts.cs:97 / :297 / :108, reaching the frame at GoonHostService.cs:343-345.
        Assert.Equal(720, consent.GetProperty("liveDurationSec").GetInt32());
        Assert.Equal(0.7, consent.GetProperty("toyCap").GetDouble());
        Assert.Equal(30000, consent.GetProperty("payloadMinGapMs").GetInt32());
    }

    [Fact]
    public void Init_CarriesNoDiscordBlock()
    {
        // Upstream builds one at :353. There is no Discord integration in the port.
        Assert.False(InitFrame().TryGetProperty("discord", out _));
    }

    [Fact]
    public void Init_EchoesTheRealFullscreenState()
    {
        var windowed = JsonDocument.Parse(GoonProtocol.SerializeForPage(
            GoonProtocol.BuildInit("Player", "0.1.0", GoonDoors.Caps, fullscreen: false))).RootElement;
        var full = JsonDocument.Parse(GoonProtocol.SerializeForPage(
            GoonProtocol.BuildInit("Player", "0.1.0", GoonDoors.Caps, fullscreen: true))).RootElement;
        Assert.False(windowed.GetProperty("fullscreen").GetBoolean());
        Assert.True(full.GetProperty("fullscreen").GetBoolean());
    }

    [Fact]
    public void TheThreeConsentConstants_AreTheEnginesValues()
    {
        Assert.Equal(720, GoonProtocol.GoonConsent.LiveDurationSec);
        Assert.Equal(0.7, GoonProtocol.GoonConsent.ToyCap);
        Assert.Equal(30000, GoonProtocol.GoonConsent.PayloadMinGapMs);
    }

    // ==================================================================================
    // 4. THE manifest FRAME — the shipped enumerator, the shipped frame, and one stub.
    // ==================================================================================

    [Fact]
    public void Manifest_IsTheDtrhFramePlusAnEmptyReceived()
    {
        var frame = JsonDocument.Parse(GoonProtocol.SerializeForPage(
            GoonProtocol.BuildManifest([], [], skipped: 0, truncated: false))).RootElement;
        // GoonHostService.cs:372-378 posts {type, images, videos, skipped, truncated, received};
        // DtrhProtocol.cs:268-277 already builds the first five, field-for-field.
        Assert.Equal(
            new[] { "type", "images", "videos", "skipped", "truncated", "received" },
            frame.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("manifest", frame.GetProperty("type").GetString());
        // The documented stub: this build has no media channel and no inbox to fill it from.
        Assert.Equal(0, frame.GetProperty("received").GetArrayLength());
    }

    [Fact]
    public void Manifest_CarriesTheUsersOwnMedia_AsLoopbackUrlsOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-goon-media-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images"));
            Directory.CreateDirectory(Path.Combine(root, "videos"));
            File.WriteAllBytes(Path.Combine(root, "images", "a.png"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "images", "b.jpg"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "videos", "c.mp4"), [1, 2, 3]);

            var media = DtrhUserMedia.Build(root, "http://127.0.0.1:49999", _ => { });
            var frame = JsonDocument.Parse(GoonProtocol.SerializeForPage(
                GoonProtocol.BuildManifest(media.Images, media.Videos, media.Skipped, media.Truncated))).RootElement;

            Assert.Equal(2, frame.GetProperty("images").GetArrayLength());
            Assert.Equal(1, frame.GetProperty("videos").GetArrayLength());
            foreach (var entry in frame.GetProperty("images").EnumerateArray()
                         .Concat(frame.GetProperty("videos").EnumerateArray()))
            {
                Assert.Equal(new[] { "name", "url" }, entry.EnumerateObject().Select(p => p.Name).ToArray());
                var url = entry.GetProperty("url").GetString()!;
                // §6.2: this frame must never become a route by which the inventory leaves the
                // machine. Every URL it carries is loopback, and the frame is the whole of its life.
                Assert.StartsWith("http://127.0.0.1:", url, StringComparison.Ordinal);
                Assert.Contains("/umedia/", url, StringComparison.Ordinal);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    // ==================================================================================
    // 5. net-post — the refusal that is the "no network" enforcement point.
    // ==================================================================================

    [Fact]
    public void NetPost_IsAnsweredWithATypedRefusal_NamingWhatIsMissing()
    {
        var frame = JsonDocument.Parse(GoonProtocol.SerializeForPage(
            GoonProtocol.BuildNetPostResult("n1", GoonDoors.NetRefusal!))).RootElement;

        // The catalogue's shape (GoonHostService.cs:34): net-post-result {id, status, body}.
        Assert.Equal(new[] { "type", "id", "status", "body" }, frame.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("net-post-result", frame.GetProperty("type").GetString());
        Assert.Equal("n1", frame.GetProperty("id").GetString());
        Assert.Equal(501, frame.GetProperty("status").GetInt32());

        var body = JsonDocument.Parse(frame.GetProperty("body").GetString()!).RootElement;
        Assert.Equal("not_in_this_build", body.GetProperty("error").GetString());
        Assert.Equal(GoonDoors.NetRefusal!.Missing, body.GetProperty("detail").GetString());
    }

    [Fact]
    public void NetPost_IsRefusedWithoutEverNamingAServer()
    {
        var json = GoonProtocol.SerializeForPage(GoonProtocol.BuildNetPostResult("n1", GoonDoors.NetRefusal!));
        Assert.DoesNotContain("http://", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vercel", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/v2/goon/", json, StringComparison.Ordinal);
    }

    // ==================================================================================
    // 6. THE PAGE VOCABULARY — 9 handled, everything else typed and tolerated.
    // ==================================================================================

    [Theory]
    [InlineData("{\"type\":\"ready\",\"protocol\":1}", typeof(GoonProtocol.GoonPageMessage.Ready))]
    [InlineData("{\"type\":\"log\",\"msg\":\"hi\"}", typeof(GoonProtocol.GoonPageMessage.Log))]
    [InlineData("{\"type\":\"heartbeat\",\"t\":1,\"vis\":\"visible\"}", typeof(GoonProtocol.GoonPageMessage.Heartbeat))]
    [InlineData("{\"type\":\"pong\",\"t\":1}", typeof(GoonProtocol.GoonPageMessage.Pong))]
    [InlineData("{\"type\":\"boot-error\",\"msg\":\"x\"}", typeof(GoonProtocol.GoonPageMessage.BootError))]
    [InlineData("{\"type\":\"fullscreen-set\",\"on\":true}", typeof(GoonProtocol.GoonPageMessage.FullscreenSet))]
    [InlineData("{\"type\":\"exit\"}", typeof(GoonProtocol.GoonPageMessage.Exit))]
    [InlineData("{\"type\":\"exit-done\"}", typeof(GoonProtocol.GoonPageMessage.ExitDone))]
    [InlineData("{\"type\":\"net-post\",\"id\":\"n1\",\"path\":\"/v2/goon/join\"}", typeof(GoonProtocol.GoonPageMessage.NetPost))]
    public void TheNineHandledFrames_ParseTyped(string json, Type expected)
    {
        var parsed = Assert.IsType<GoonProtocol.GoonPageParseResult.Parsed>(GoonProtocol.ParsePageMessage(json));
        Assert.IsType(expected, parsed.Message);
    }

    [Fact]
    public void Heartbeat_KeepsPaintNullWhenThePageOmitsIt()
    {
        // boot.js:2606-2610: `paint` is OMITTED, not zeroed, on a host without rAF — "no frame
        // counter" is a different fact from "no frames", and flattening it to 0 would read to a
        // watchdog as a permanent stall.
        var withoutPaint = Assert.IsType<GoonProtocol.GoonPageParseResult.Parsed>(
            GoonProtocol.ParsePageMessage("{\"type\":\"heartbeat\",\"t\":1,\"vis\":\"visible\"}"));
        Assert.Null(Assert.IsType<GoonProtocol.GoonPageMessage.Heartbeat>(withoutPaint.Message).Paint);

        var withPaint = Assert.IsType<GoonProtocol.GoonPageParseResult.Parsed>(
            GoonProtocol.ParsePageMessage("{\"type\":\"heartbeat\",\"t\":1,\"paint\":42,\"vis\":\"visible\"}"));
        Assert.Equal(42L, Assert.IsType<GoonProtocol.GoonPageMessage.Heartbeat>(withPaint.Message).Paint);
    }

    /// <summary>Every frame belonging to a subsystem this unit does not build is TOLERATED and
    /// typed — logged by name, never acted on, never dropped in silence.</summary>
    [Theory]
    [InlineData("cache-req")]
    [InlineData("cache-put")]
    [InlineData("encode-done")]
    [InlineData("goon-recv-begin")]
    [InlineData("goon-recv-commit")]
    [InlineData("discord-prefs")]
    [InlineData("peer-card-req")]
    [InlineData("rp-state")]
    [InlineData("last-opponent-clear")]
    [InlineData("toy-pattern")]
    [InlineData("toy-stop")]
    [InlineData("match-result")]
    public void TheOwnerGatedFrames_AreOutOfVocabulary_AndTolerated(string type)
    {
        var result = GoonProtocol.ParsePageMessage("{\"type\":\"" + type + "\"}");
        Assert.Equal(type, Assert.IsType<GoonProtocol.GoonPageParseResult.UnknownType>(result).Type);
    }

    [Fact]
    public void ForwardVersionAndMalformedFrames_AreTypedOutcomes_NeverThrows()
    {
        var forward = Assert.IsType<GoonProtocol.GoonPageParseResult.ForwardVersion>(
            GoonProtocol.ParsePageMessage("{\"type\":\"ready\",\"protocol\":2}"));
        Assert.Equal(2, forward.Protocol);

        Assert.IsType<GoonProtocol.GoonPageParseResult.Malformed>(GoonProtocol.ParsePageMessage("not json"));
        Assert.IsType<GoonProtocol.GoonPageParseResult.Malformed>(GoonProtocol.ParsePageMessage("[1,2,3]"));
        Assert.IsType<GoonProtocol.GoonPageParseResult.Malformed>(GoonProtocol.ParsePageMessage("{\"type\":7}"));
        Assert.IsType<GoonProtocol.GoonPageParseResult.Malformed>(GoonProtocol.ParsePageMessage("{}"));
    }

    [Theory]
    [InlineData("init", GoonProtocol.GoonEmitClass.EmittedByHost)]
    [InlineData("manifest", GoonProtocol.GoonEmitClass.EmittedByHost)]
    [InlineData("fullscreen", GoonProtocol.GoonEmitClass.EmittedByHost)]
    [InlineData("end-run", GoonProtocol.GoonEmitClass.EmittedByHost)]
    [InlineData("net-post-result", GoonProtocol.GoonEmitClass.EmittedByHost)]
    [InlineData("ping", GoonProtocol.GoonEmitClass.PageAttestedNeverEmitted)]
    [InlineData("cache-state", GoonProtocol.GoonEmitClass.OwnerGatedNeverEmitted)]
    [InlineData("discord", GoonProtocol.GoonEmitClass.OwnerGatedNeverEmitted)]
    [InlineData("peer-card", GoonProtocol.GoonEmitClass.OwnerGatedNeverEmitted)]
    [InlineData("haptics-state", GoonProtocol.GoonEmitClass.OwnerGatedNeverEmitted)]
    [InlineData("quiz-result", GoonProtocol.GoonEmitClass.NotGoonVocabulary)]
    public void TheHostEmitTable_IsPinned(string type, GoonProtocol.GoonEmitClass expected) =>
        Assert.Equal(expected, GoonProtocol.ClassifyHostEmit(type));

    // ==================================================================================
    // 7. SERVING — the probe, and the 415 set this host inherits.
    // ==================================================================================

    [Fact]
    public void PayloadProbe_TypesPresentMissingAndIncomplete()
    {
        Assert.Equal(GoonServingRoots.GoonPayloadState.Present, GoonServingRoots.Probe(OutputGoonRoot()).State);

        var missing = GoonServingRoots.Probe(Path.Combine(Path.GetTempPath(), "ccp-goon-absent-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(GoonServingRoots.GoonPayloadState.Missing, missing.State);
        Assert.Equal(0, missing.FileCount);

        var partial = Path.Combine(Path.GetTempPath(), "ccp-goon-partial-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(partial);
            File.WriteAllText(Path.Combine(partial, "index.html"), "<html></html>");
            var probe = GoonServingRoots.Probe(partial);
            Assert.Equal(GoonServingRoots.GoonPayloadState.Incomplete, probe.State);
            // Names the first required file whose absence typed it — never a bare "incomplete".
            Assert.Equal("boot.js", probe.MissingFile);
        }
        finally
        {
            try { Directory.Delete(partial, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// D258 — the cost of reusing the DTRH §4 server, measured rather than assumed. The allowlist
    /// at <c>LoopbackServer.cs:24-34</c> is pinned to the DTRH tree's nine extensions; anything
    /// else answers 415 (<c>:447-449</c>, which reads <c>Path.GetExtension</c> — so the two
    /// extensionless LICENSE files miss the table too). The denial set is named FILE BY FILE, so
    /// a new extension upstream reds here instead of 415-ing silently in front of a player.
    /// </summary>
    [Fact]
    public void TheServedTreesDeniedFiles_AreExactlyTheFourNamedOnes()
    {
        var allowed = new HashSet<string>(
            [".css", ".gif", ".html", ".js", ".json", ".mp3", ".png", ".webm", ".webp"],
            StringComparer.OrdinalIgnoreCase);

        var denied = Relatives(OutputGoonRoot())
            .Where(r => !allowed.Contains(Path.GetExtension(r)))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "manifest.webmanifest",
                "vendor/fflate/LICENSE",
                "vendor/mp4-muxer/LICENSE",
                "vendor/mp4-muxer/mp4-muxer.mjs",
            },
            denied.Select(r => r.Replace('\\', '/')).ToArray());
    }

    [Fact]
    public void NoDeniedFile_IsOnThePracticeBootPath()
    {
        var root = OutputGoonRoot();
        // index.html's own words: the PWA chrome is inert under WebView2 (index.html:18), and the
        // muxer is imported only by the encode lane, which caps.assetCache=false switches off.
        var bootPath = string.Concat(
            File.ReadAllText(Path.Combine(root, "boot.js")),
            File.ReadAllText(Path.Combine(root, "bridge.js")),
            File.ReadAllText(Path.Combine(root, "ui", "screens", "title.js")),
            File.ReadAllText(Path.Combine(root, "ui", "soloDriver.js")));

        Assert.NotEmpty(bootPath);
        Assert.DoesNotContain("mp4-muxer.mjs", bootPath, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.webmanifest", bootPath, StringComparison.Ordinal);
        // The muxer IS imported — by the encode lane, which caps.assetCache=false switches off.
        Assert.Contains(
            "mp4-muxer.mjs",
            File.ReadAllText(Path.Combine(root, "encode", "encodeWorker.js")),
            StringComparison.Ordinal);
    }

    // ==================================================================================
    // 8. NO NETWORK, NO SENSOR, NO TOKEN — asserted over the feature's own source text.
    // ==================================================================================

    /// <summary>
    /// The whole <c>Features/Goon</c> tree is read and searched for the shapes this unit is
    /// forbidden to grow: an HTTP client, a capture device, a token read, an upstream origin.
    /// Comment prose is stripped first, so the file that DESCRIBES the microphone decision does
    /// not fail the guard that forbids opening one.
    /// </summary>
    [Theory]
    [InlineData("HttpClient")]
    [InlineData("WebSocket")]
    [InlineData("Socket(")]
    [InlineData("getUserMedia")]
    [InlineData("MediaRecorder")]
    [InlineData("AuthToken")]
    [InlineData("HasPremiumAccess")]
    [InlineData("HasLabAccess")]
    [InlineData("codebambi")]
    [InlineData("vercel.app")]
    [InlineData("https://")]
    public void TheGoonFeature_ContainsNoOutboundCallNoSensorAndNoTokenRead(string banned)
    {
        var featureRoot = RequireTree(GoonFeatureRoot(), "the Goon feature tree");
        var files = Directory.EnumerateFiles(featureRoot, "*", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count >= 6, $"the Goon feature sweep found only {files.Count} files");

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var code = CodeOf(file);
            if (code.Contains(banned, StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(offenders.Count == 0, $"'{banned}' appears in CODE (not comment) in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheGoonFeature_NamesOnlyTheLoopbackOrigin()
    {
        var files = Directory.EnumerateFiles(GoonFeatureRoot(), "*", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count >= 6, $"the Goon feature sweep found only {files.Count} files");

        foreach (var file in files)
        {
            var code = CodeOf(file);
            var index = code.IndexOf("http://", StringComparison.Ordinal);
            while (index >= 0)
            {
                // The only absolute origin this feature may write is the server's own loopback
                // one, and it writes that only by interpolating LoopbackServer.PageOrigin.
                var window = code.Substring(index, Math.Min(40, code.Length - index));
                Assert.True(
                    window.Contains("127.0.0.1", StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} names a non-loopback http origin: {window}");
                index = code.IndexOf("http://", index + 1, StringComparison.Ordinal);
            }
        }
    }

    // ==================================================================================
    // helpers
    // ==================================================================================

    private static string GoonFeatureRoot() =>
        Path.Combine(FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Features", "Goon");

    /// <summary>Asserts a tree is really there and returns it. The existence PREDICATE lives
    /// here rather than in a test body on purpose: a filesystem predicate inside a [Fact] is a
    /// silencing shape, and this one must fail the suite rather than quiet it.</summary>
    private static string RequireTree(string path, string what)
    {
        Assert.True(Directory.Exists(path), $"{what} is missing at {path}");
        return path;
    }

    /// <summary>Every file the CLIENT tree owns — build outputs excluded, because that is
    /// exactly where the linked glob is supposed to put copies.</summary>
    private static List<string> ClientOwnedFiles(string repoRoot)
    {
        var files = new List<string>();
        foreach (var sourceDir in new[] { "src", "tests", "docs", "tools" })
        {
            var root = Path.Combine(repoRoot, "client", sourceDir);
            if (!Directory.Exists(root))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var rel = Path.GetRelativePath(repoRoot, f).Replace('\\', '/');
                    return !rel.Contains("/bin/", StringComparison.Ordinal)
                        && !rel.Contains("/obj/", StringComparison.Ordinal);
                }));
        }

        return files;
    }

    private static List<string> Relatives(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>
    /// The searchable CODE of one feature file: C# with its comments removed, or markup with its
    /// XML comments removed AND its <c>xmlns</c> declarations dropped. An Avalonia namespace URI
    /// (<c>https://github.com/avaloniaui</c>) is an IDENTIFIER the framework never fetches, and
    /// counting it as an outbound call would make this guard cry wolf on every window in the
    /// tree — which is how a guard gets holes drilled in it later. Everything else in the markup
    /// is still swept.
    /// </summary>
    private static string CodeOf(string file)
    {
        var text = File.ReadAllText(file);
        if (!file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
        {
            return StripComments(text);
        }

        var withoutComments = System.Text.RegularExpressions.Regex.Replace(
            text, "<!--.*?-->", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        // Drop the xmlns DECLARATIONS themselves (attribute and value), wherever they sit.
        return System.Text.RegularExpressions.Regex.Replace(
            withoutComments, "xmlns(:[A-Za-z0-9_]+)?=\"[^\"]*\"", " ");
    }

    /// <summary>Removes <c>//</c> and <c>/* */</c> comment bodies so a guard over CODE is not
    /// tripped by the prose that explains the very thing it forbids. String-aware enough for this
    /// tree: it skips over double-quoted literals so a URL inside a comment about a URL cannot
    /// hide, and a <c>//</c> inside a string literal is not read as a comment start.</summary>
    private static string StripComments(string source)
    {
        var output = new System.Text.StringBuilder(source.Length);
        var inString = false;
        var inLine = false;
        var inBlock = false;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLine)
            {
                if (c == '\n') { inLine = false; output.Append(c); }
                continue;
            }

            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                continue;
            }

            if (inString)
            {
                output.Append(c);
                if (c == '\\' && next != '\0') { output.Append(next); i++; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; output.Append(c); continue; }
            if (c == '/' && next == '/') { inLine = true; i++; continue; }
            if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            output.Append(c);
        }

        return output.ToString();
    }
}
