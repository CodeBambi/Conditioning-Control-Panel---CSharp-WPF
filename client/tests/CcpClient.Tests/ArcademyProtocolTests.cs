using System.Text.Json;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Motion;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slice 2 of the Arcademy row: the init projection's fields and the set-setting echo loop that
/// keeps page and app agreed, plus slice 1's boot handshake shape.
///
/// <para><b>What these facts are NOT.</b> No page receives any of these frames here — there is no
/// browser in this assembly. They pin the FRAMES (fields, values, clamps and order) and the
/// STORE (what a write leaves behind), never that a page parsed one or that a slider moved.</para>
/// </summary>
public sealed class ArcademyProtocolTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly List<object> _posted = [];
    private readonly string _dir;
    private readonly PersistenceStore<ArcademySettingsDocument> _store;

    public ArcademyProtocolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-proto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new PersistenceStore<ArcademySettingsDocument>(
            new OperationRegistry().OwnerFor("ArcademyProtocolTests"),
            new SinkAdapter(_log),
            Path.Combine(_dir, ArcademySettingsDocument.FileName),
            ArcademySettingsDocument.CurrentSchemaVersion);
        // StartAsync loads on the calling thread and hands back an ALREADY-COMPLETE task (its own
        // remarks, pinned by PersistenceStoreTests), so there is nothing here to wait on.
        _ = _store.StartAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        try
        {
            _ = _store.StopAsync();   // cancels the owner; also already complete, and NOT a flush
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // best-effort teardown
        }
    }

    private ArcademySession NewSession(ArcademyAppFacts? facts = null) =>
        new(_store, facts ?? new ArcademyAppFacts(), frame => _posted.Add(frame), new SinkAdapter(_log));

    private JsonElement Frame(int index) =>
        JsonDocument.Parse(ArcademyProtocol.SerializeForPage(_posted[index])).RootElement.Clone();

    private static JsonElement? Value(JsonElement frame, string name) =>
        frame.TryGetProperty(name, out var p) ? p : null;

    private JsonElement PostInit(ArcademyAppFacts? facts = null)
    {
        NewSession(facts).Ready();
        return Frame(0);
    }

    // ==================================================================================
    // The boot handshake (slice 1).
    // ==================================================================================

    [Fact]
    public void BootHandshake_IsInitThenFullscreen_ExactlyOncePerBoot()
    {
        var session = NewSession();
        Assert.False(session.InitPosted);

        session.Ready();

        // The observable shape, in upstream's order (ArcademyHostService.cs:396-401).
        Assert.Equal(2, _posted.Count);
        Assert.Equal("init", Frame(0).GetProperty("type").GetString());
        Assert.Equal("fullscreen", Frame(1).GetProperty("type").GetString());
        Assert.False(Frame(1).GetProperty("on").GetBoolean());
        Assert.True(session.InitPosted);

        // The once-per-boot guard sits AHEAD of the fullscreen post upstream, so a second `ready`
        // produces NOTHING — not a second init, and not a second fullscreen either.
        session.Ready();
        session.Handle("""{"type":"ready","protocol":1}""");
        Assert.Equal(2, _posted.Count);
    }

    [Fact]
    public void BootHandshake_FullscreenCarriesTheRealWindowState()
    {
        var session = NewSession();
        session.FullscreenState = () => true;
        session.Ready();
        Assert.True(Frame(1).GetProperty("on").GetBoolean());

        // A page request is answered with the REAL state, never the requested one: the page's own
        // affordances read only the echo (ArcademyHostService.cs:515).
        session.FullscreenState = () => false;
        session.Handle("""{"type":"fullscreen-request","on":true}""");
        Assert.Equal(3, _posted.Count);
        Assert.Equal("fullscreen", Frame(2).GetProperty("type").GetString());
        Assert.False(Frame(2).GetProperty("on").GetBoolean());
    }

    [Fact]
    public void PageVocabulary_LaterSliceFrames_AreNamedAndNotActedOn()
    {
        // meta-command (slice 3) and the three class frames (slice 4) have LANDED and are handled
        // here now — their facts are in ArcademyMetaTests. The two rows still named for a later
        // slice are resume-request (5) and assets-request (7): acknowledged in the transcript,
        // never acted on, and never answered with a frame.
        Assert.Equal(ArcademyProtocol.ArcademyHandling.HandledHere, ArcademyProtocol.Classify("meta-command"));
        Assert.Equal(ArcademyProtocol.ArcademyHandling.HandledHere, ArcademyProtocol.Classify("class-ended"));
        Assert.Equal(ArcademyProtocol.ArcademyHandling.LaterSlice, ArcademyProtocol.Classify("resume-request"));
        Assert.Equal(ArcademyProtocol.ArcademyHandling.LaterSlice, ArcademyProtocol.Classify("assets-request"));
        Assert.Equal(ArcademyProtocol.ArcademyHandling.HandledHere, ArcademyProtocol.Classify("set-setting"));
        Assert.Equal(ArcademyProtocol.ArcademyHandling.NotVocabulary, ArcademyProtocol.Classify("net-post"));

        var session = NewSession();
        session.Ready();
        _posted.Clear();
        session.Handle("""{"type":"resume-request","reqId":"r1"}""");
        session.Handle("""{"type":"assets-request","kind":"loop","count":8}""");
        session.Handle("""{"type":"not-a-frame-we-know"}""");
        session.Handle("{ this is not json");
        Assert.Empty(_posted);
        Assert.Contains(_log, l => l.Contains("resume-request") && l.Contains("later slice"));
        Assert.Contains(_log, l => l.Contains("assets-request") && l.Contains("later slice"));
        Assert.Contains(_log, l => l.Contains("unhandled message 'not-a-frame-we-know'"));
        Assert.Contains(_log, l => l.Contains("malformed"));

        // A boot-error is recorded rather than swallowed, and answers nothing.
        session.Handle("""{"type":"boot-error","msg":"module graph failed"}""");
        Assert.True(session.BootFailed);
        Assert.Empty(_posted);
    }

    // ==================================================================================
    // The init projection (slice 2).
    // ==================================================================================

    [Fact]
    public void Init_ProjectsTheArcademyDocument_FieldForField()
    {
        _store.Mutate(d =>
        {
            d.MasterIntensity = 0.42;
            d.CapFlashRate = 0.3;
            d.CapFlashOpacity = 0.31;
            d.CapSubDensity = 0.32;
            d.CapDuckDepth = 0.33;
            d.CapBubbleRate = 0.34;
            d.CapBinauralDepth = 0.35;
            d.CapBgIntensity = 0.36;
            d.AudioMute = true;
            d.HideTutorial = true;
            d.KeybindsJson = """{"pop":"Space"}""";
            d.SettingsJson = """{"dt_hard_mode":true}""";
        });

        var init = PostInit();
        Assert.Equal("init", init.GetProperty("type").GetString());
        Assert.Equal(1, init.GetProperty("protocol").GetInt32());
        Assert.Equal(0.42, init.GetProperty("masterIntensity").GetDouble(), 6);

        var caps = init.GetProperty("caps");
        Assert.Equal(0.3, caps.GetProperty("flashRate").GetDouble(), 6);
        Assert.Equal(0.31, caps.GetProperty("flashOpacity").GetDouble(), 6);
        Assert.Equal(0.32, caps.GetProperty("subDensity").GetDouble(), 6);
        Assert.Equal(0.33, caps.GetProperty("duckDepth").GetDouble(), 6);
        Assert.Equal(0.34, caps.GetProperty("bubbleRate").GetDouble(), 6);
        // The canon is binauralDepth, never audioDepth (SYNTHESIS-NOTES #9).
        Assert.Equal(0.35, caps.GetProperty("binauralDepth").GetDouble(), 6);
        Assert.Null(Value(caps, "audioDepth"));
        Assert.Equal(0.36, caps.GetProperty("bgIntensity").GetDouble(), 6);

        var levels = init.GetProperty("audioLevels");
        Assert.Equal(0.48, levels.GetProperty("fx").GetDouble(), 6);
        Assert.Equal(0.85, levels.GetProperty("voice").GetDouble(), 6);
        Assert.Equal(0.85, levels.GetProperty("tutorial").GetDouble(), 6);
        Assert.Equal(0.4, levels.GetProperty("drops").GetDouble(), 6);
        Assert.Equal(1.0, levels.GetProperty("music").GetDouble(), 6);

        Assert.True(init.GetProperty("audioMute").GetBoolean());
        Assert.True(init.GetProperty("hideTutorial").GetBoolean());
        Assert.Equal("Space", init.GetProperty("keybinds").GetProperty("pop").GetString());
        // The per-game bag rides init.settings, with its keys VERBATIM (never camelCased).
        Assert.True(init.GetProperty("settings").GetProperty("dt_hard_mode").GetBoolean());
    }

    [Fact]
    public void Init_ProjectsAppWideFactsAlreadyResolved()
    {
        var init = PostInit(new ArcademyAppFacts
        {
            HasHaptics = true,
            ModId = "builtin-bambisleep",
            EffectIntensity = 0.5,
            MasterVolume = 64,
            RemoteMediaRatio = 30,
            AudioAudible = true,
            ProtectBrowserVideo = false,
            PanicKeyEnabled = false,
            PanicKey = "F8",
        });

        var platform = init.GetProperty("platform");
        Assert.False(platform.GetProperty("isTouch").GetBoolean());
        Assert.True(platform.GetProperty("hasHaptics").GetBoolean());
        Assert.Equal("desktop", platform.GetProperty("host").GetString());
        Assert.Equal("builtin-bambisleep", init.GetProperty("modId").GetString());

        // Stored 0-100, projected 0..1 — the page never sees the raw scale.
        Assert.Equal(0.64, init.GetProperty("masterVolume").GetDouble(), 6);
        Assert.Equal(0.30, init.GetProperty("remoteMediaRatio").GetDouble(), 6);
        Assert.Equal(0.5, init.GetProperty("effectIntensity").GetDouble(), 6);
        Assert.True(init.GetProperty("audioAudible").GetBoolean());
        Assert.False(init.GetProperty("protectBrowserVideo").GetBoolean());
        Assert.False(init.GetProperty("panicKeyEnabled").GetBoolean());
        Assert.Equal("F8", init.GetProperty("panicKey").GetString());

        // ALWAYS false (ArcademyHostService.cs:555-557): the gate refuses on an audio-only day, so
        // a class can only ever meet one starting mid-run, which is a `suspend` push (slice 5).
        Assert.False(init.GetProperty("audioOnlySession").GetBoolean());

        // No remote-media broker in this build, and the page reads offlineMode as "kill every
        // remote fetch" (arcademy/provider/remote.js:72).
        Assert.False(init.GetProperty("remoteMediaEnabled").GetBoolean());
        Assert.True(init.GetProperty("offlineMode").GetBoolean());

        // This session has no meta store attached; empty is upstream's own value on that line
        // (ArcademyHostService.cs:568). The populated projection is in ArcademyMetaTests.
        Assert.Equal(JsonValueKind.Object, init.GetProperty("meta").ValueKind);
        Assert.Empty(init.GetProperty("meta").EnumerateObject());
    }

    [Fact]
    public void Init_MotionLevelIsInvertedNeverCast()
    {
        // The enum counts Full = 0; the engine's scale counts 0 = NO motion. A cast would tell the
        // engine that full motion means "strobe nothing" (ArcademyHostService.cs:700-711).
        Assert.Equal(2, ArcademyProtocol.ResolvedMotionLevel(MotionLevel.Full));
        Assert.Equal(1, ArcademyProtocol.ResolvedMotionLevel(MotionLevel.Reduced));
        Assert.Equal(0, ArcademyProtocol.ResolvedMotionLevel(MotionLevel.Off));
        Assert.Equal(0, (int)MotionLevel.Full);

        var full = PostInit(new ArcademyAppFacts { Motion = MotionLevel.Full });
        Assert.Equal(2, full.GetProperty("motionLevel").GetInt32());
        Assert.False(full.GetProperty("reducedMotion").GetBoolean());

        _posted.Clear();
        var off = PostInit(new ArcademyAppFacts { Motion = MotionLevel.Off });
        Assert.Equal(0, off.GetProperty("motionLevel").GetInt32());
        Assert.True(off.GetProperty("reducedMotion").GetBoolean());
    }

    [Fact]
    public void Init_UtcSeedsTheContent_AndTheLocalDateRollsAttendance()
    {
        // 2026-08-23 23:30 at +02:00 is still 2026-08-23 UTC 21:30 — same day. Push it to 01:30
        // local and the two dates DIVERGE, which is exactly the split upstream regression #978 is
        // about: UTC seeds the day's classes, the LOCAL date rolls the streak.
        var sameDay = PostInit(new ArcademyAppFacts
        {
            Now = new DateTimeOffset(2026, 8, 23, 23, 30, 0, TimeSpan.FromHours(2)),
        });
        Assert.Equal("2026-08-23", sameDay.GetProperty("utcDateSeed").GetString());
        Assert.Equal("2026-08-23", sameDay.GetProperty("localDate").GetString());

        _posted.Clear();
        var split = PostInit(new ArcademyAppFacts
        {
            Now = new DateTimeOffset(2026, 8, 24, 1, 30, 0, TimeSpan.FromHours(2)),
        });
        Assert.Equal("2026-08-23", split.GetProperty("utcDateSeed").GetString());
        Assert.Equal("2026-08-24", split.GetProperty("localDate").GetString());
    }

    [Fact]
    public void Init_WordsAreTheEnabledPhrases_TrimmedAndCappedAtSixty()
    {
        var phrases = Enumerable.Range(0, 75).Select(i => $"phrase {i}").ToList();
        phrases.Add("   ");
        phrases.Add("  padded  ");

        var init = PostInit(new ArcademyAppFacts { SubliminalPhrases = phrases });
        var words = init.GetProperty("words").EnumerateArray().Select(w => w.GetString()!).ToList();

        Assert.Equal(60, words.Count);                       // capped so init stays small
        Assert.DoesNotContain(words, w => string.IsNullOrWhiteSpace(w));
        Assert.DoesNotContain(words, w => w != w.Trim());
        Assert.All(words, w => Assert.Contains(w, phrases.Select(p => p.Trim())));

        // MAY BE EMPTY, and that is a contract, not a failure.
        _posted.Clear();
        Assert.Empty(PostInit(new ArcademyAppFacts()).GetProperty("words").EnumerateArray());
    }

    [Fact]
    public void Init_LocalAssetsSplitGifsFromStills_AndHonourTheDeselection()
    {
        var media = Path.Combine(_dir, "assets");
        var images = Path.Combine(media, "images");
        Directory.CreateDirectory(Path.Combine(images, "nested"));
        File.WriteAllText(Path.Combine(images, "loop one.gif"), "G");
        File.WriteAllText(Path.Combine(images, "still.png"), "P");
        File.WriteAllText(Path.Combine(images, "nested", "deep.webp"), "W");
        File.WriteAllText(Path.Combine(images, "notes.txt"), "T");
        File.WriteAllText(Path.Combine(images, "hidden.jpg"), "J");

        var init = PostInit(new ArcademyAppFacts
        {
            UserMediaRoot = media,
            MediaOrigin = "http://127.0.0.1:9",
            DisabledAssets = CcpClient.Desktop.Features.Dtrh.DtrhUserMedia.BuildDisabledSet(["images/hidden.jpg"]),
            UseAssetWhitelist = true,
        });

        var local = init.GetProperty("settings").GetProperty("localAssets");
        var gifs = local.GetProperty("gifs").EnumerateArray().Select(g => g.GetString()!).ToList();
        var stills = local.GetProperty("stills").EnumerateArray().Select(s => s.GetString()!).ToList();

        // Two pools off ONE folder; the url is root-relative and segment-escaped.
        Assert.Single(gifs);
        Assert.Equal("http://127.0.0.1:9/umedia/images/loop%20one.gif", gifs[0]);
        Assert.Contains("http://127.0.0.1:9/umedia/images/still.png", stills);
        Assert.Contains("http://127.0.0.1:9/umedia/images/nested/deep.webp", stills);
        // A non-image is not media; a DESELECTED image is hidden from the Arcademy too.
        Assert.DoesNotContain(stills, s => s.Contains("notes.txt"));
        Assert.DoesNotContain(stills, s => s.Contains("hidden.jpg"));
    }

    // ==================================================================================
    // The set-setting echo loop (slice 2).
    // ==================================================================================

    [Fact]
    public void SetSetting_ClampsPersistsAndEchoesThePostClampValue()
    {
        var session = NewSession();
        session.Ready();
        _posted.Clear();

        // A page asking for MORE than the ceiling is answered with the ceiling — the page's slider
        // lands where the echo says, not where the page guessed (shell/settings.js:17-21).
        session.Handle("""{"type":"set-setting","key":"caps.flashRate","value":1.5}""");
        var echo = Frame(0);
        Assert.Equal("setting", echo.GetProperty("type").GetString());
        Assert.Equal("caps.flashRate", echo.GetProperty("key").GetString());
        Assert.Equal(1.0, echo.GetProperty("value").GetDouble(), 6);
        Assert.Equal(1.0, _store.Current.CapFlashRate, 6);

        // The BARE form is accepted too, and lands on the same setting.
        session.Handle("""{"type":"set-setting","key":"flashRate","value":-3}""");
        Assert.Equal(0.0, Frame(1).GetProperty("value").GetDouble(), 6);
        Assert.Equal(0.0, _store.Current.CapFlashRate, 6);

        // App-wide keys move the app-wide facts, not a second Arcademy copy of them.
        ArcademyAppFacts? changed = null;
        session.AppFactsChanged += f => changed = f;
        session.Handle("""{"type":"set-setting","key":"masterVolume","value":0.5}""");
        Assert.Equal(0.5, Frame(2).GetProperty("value").GetDouble(), 6);
        Assert.Equal(50, session.Facts.MasterVolume);
        Assert.Equal(50, changed?.MasterVolume);

        // The write is dirty on the store, so the document survives the session.
        Assert.True(_store.IsDirty);
    }

    [Fact]
    public void SetSetting_AudioGroupsClampToTheirOwnCeiling()
    {
        var session = NewSession();

        session.Handle("""{"type":"set-setting","key":"audioLevels.fx","value":4}""");
        Assert.Equal(1.0, Frame(0).GetProperty("value").GetDouble(), 6);

        // music is a 0..2 MULTIPLIER over the ambient bed, not a 0..1 gain.
        session.Handle("""{"type":"set-setting","key":"audioLevels.music","value":4}""");
        Assert.Equal(2.0, Frame(1).GetProperty("value").GetDouble(), 6);
        Assert.Equal(2.0, _store.Current.AudioLevels["music"], 6);
        Assert.Equal(1.0, _store.Current.AudioLevels["fx"], 6);

        // The other four groups keep the values the user tuned.
        Assert.Equal(0.85, _store.Current.AudioLevels["voice"], 6);
    }

    [Fact]
    public void SetSetting_UnknownKeysArePerGameKnobs_ScalarsOnly_AndBounded()
    {
        var session = NewSession();

        session.Handle("""{"type":"set-setting","key":"dt_hard_mode","value":true}""");
        Assert.True(Frame(0).GetProperty("value").GetBoolean());
        Assert.Contains("dt_hard_mode", _store.Current.SettingsJson);

        // Only scalars: a blob belongs in the meta store, and is refused with a null echo.
        session.Handle("""{"type":"set-setting","key":"dt_blob","value":{"a":1}}""");
        Assert.Equal(JsonValueKind.Null, Frame(1).GetProperty("value").ValueKind);
        Assert.DoesNotContain("dt_blob", _store.Current.SettingsJson);

        // A 257-character string is refused (the 256 bound).
        session.Handle($$"""{"type":"set-setting","key":"dt_long","value":"{{new string('x', 257)}}"}""");
        Assert.Equal(JsonValueKind.Null, Frame(2).GetProperty("value").ValueKind);
        Assert.DoesNotContain("dt_long", _store.Current.SettingsJson);

        // localAssets rides init.settings but is host-built: it must never be PERSISTED.
        session.Handle("""{"type":"set-setting","key":"dt_other","value":1}""");
        Assert.DoesNotContain("localAssets", _store.Current.SettingsJson);

        // The bag is bounded at 200 keys; the 201st is dropped rather than growing the file.
        for (var i = 0; i < 210; i++)
        {
            session.Handle($$"""{"type":"set-setting","key":"g_{{i}}","value":{{i}}}""");
        }

        var bag = JsonNode.Parse(_store.Current.SettingsJson)!.AsObject();
        Assert.Equal(200, bag.Count);
        Assert.True(_store.Current.SettingsJson.Length < ArcademySettingsDocument.MaxSettingsJsonChars,
            "the effective budget must stay below the document cap, whose setter discards the WHOLE bag");
    }

    [Fact]
    public void SetSetting_KeybindsRefuse_NeverWipe()
    {
        var session = NewSession();
        session.Handle("""{"type":"set-setting","key":"keybinds","value":{"pop":"Space"}}""");
        Assert.Equal("Space", Frame(0).GetProperty("value").GetProperty("pop").GetString());
        Assert.Equal("""{"pop":"Space"}""", _store.Current.KeybindsJson);

        // A page that sent the blob as a JSON *string* (or anything else) used to wipe every
        // rebind the player had made. Refused, existing binds kept, and the ECHO is what is
        // STORED so the page's pending row converges on the truth.
        session.Handle("""{"type":"set-setting","key":"keybinds","value":"{\"pop\":\"Enter\"}"}""");
        Assert.Equal("Space", Frame(1).GetProperty("value").GetProperty("pop").GetString());
        Assert.Equal("""{"pop":"Space"}""", _store.Current.KeybindsJson);

        // Over the effective budget: refused, and the surviving blob is echoed. The budget is
        // BELOW the document's own cap, whose setter answers an over-long value by discarding it.
        var huge = new JsonObject();
        for (var i = 0; i < 800; i++)
        {
            huge[$"verb_{i}"] = "Digit1";
        }

        session.SetSetting("keybinds", JsonDocument.Parse(huge.ToJsonString()).RootElement);
        Assert.Equal("""{"pop":"Space"}""", _store.Current.KeybindsJson);
        Assert.Equal("Space", Frame(2).GetProperty("value").GetProperty("pop").GetString());
    }

    [Fact]
    public void SetSetting_AnUnwritableKeyIsNotAnsweredAtAll()
    {
        var session = NewSession();

        session.Handle("""{"type":"set-setting","key":"","value":1}""");
        session.Handle("""{"type":"set-setting","key":"   ","value":1}""");
        session.SetSetting(new string('k', 65), JsonDocument.Parse("1").RootElement);
        Assert.Empty(_posted);

        // 64 characters exactly is inside the bound, so it IS answered (as a per-game knob).
        session.SetSetting(new string('k', 64), JsonDocument.Parse("1").RootElement);
        Assert.Single(_posted);
        Assert.Equal(new string('k', 64), Frame(0).GetProperty("key").GetString());
    }

    [Fact]
    public void SetSetting_AWrongKindValueLeavesTheStoredValueAloneAndEchoesIt()
    {
        var session = NewSession();
        session.Handle("""{"type":"set-setting","key":"caps.subDensity","value":0.25}""");
        Assert.Equal(0.25, _store.Current.CapSubDensity, 6);

        // A bool on a numeric key: the stored value is untouched (upstream throws and answers
        // nothing), and the echo carries the TRUE value so the page's pending row converges
        // instead of hanging pending forever.
        session.Handle("""{"type":"set-setting","key":"caps.subDensity","value":true}""");
        Assert.Equal(0.25, _store.Current.CapSubDensity, 6);
        Assert.Equal(0.25, Frame(1).GetProperty("value").GetDouble(), 6);

        // A JSON null is upstream's `?? fallback`, not a "leave it alone".
        session.Handle("""{"type":"set-setting","key":"caps.subDensity","value":null}""");
        Assert.Equal(1.0, _store.Current.CapSubDensity, 6);
        Assert.Equal(1.0, Frame(2).GetProperty("value").GetDouble(), 6);
    }

    [Fact]
    public void ASettingsInstanceSwap_RepushesTheWholeProjection()
    {
        var session = NewSession();
        session.Ready();
        _posted.Clear();

        var restored = new ArcademySettingsDocument { MasterIntensity = 0.11, AudioMute = true };
        restored.AudioLevels["music"] = 1.75;
        _ = _store.Replace(restored);

        // The page's model only ever moves on an echo, so a restore that changed values under the
        // session must re-echo every projected key (ArcademyHostService.cs:1798-1806).
        var keys = _posted.Select((_, i) => Frame(i).GetProperty("key").GetString()!).ToList();
        Assert.Equal(18, keys.Count);
        Assert.Equal(
            ArcademySettingsEcho.Projected(restored, new ArcademyAppFacts()).Select(p => p.Key),
            keys);
        Assert.All(_posted.Select((_, i) => Frame(i)), f => Assert.Equal("setting", f.GetProperty("type").GetString()));

        var masterIntensity = _posted.Select((_, i) => Frame(i)).First(f => f.GetProperty("key").GetString() == "masterIntensity");
        Assert.Equal(0.11, masterIntensity.GetProperty("value").GetDouble(), 6);
        var music = _posted.Select((_, i) => Frame(i)).First(f => f.GetProperty("key").GetString() == "audioLevels");
        Assert.Equal(1.75, music.GetProperty("value").GetProperty("music").GetDouble(), 6);

        // A disposed session stops following the store: no echo after teardown.
        session.Dispose();
        _posted.Clear();
        _ = _store.Replace(new ArcademySettingsDocument());
        Assert.Empty(_posted);
    }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }
}
