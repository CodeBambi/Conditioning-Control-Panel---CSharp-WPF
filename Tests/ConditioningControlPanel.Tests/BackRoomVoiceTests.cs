using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The spoken subliminal word (CONTRACT 10.21): the phrase normaliser the manifest is keyed on, what
/// <c>words.json</c> is allowed to say, and the order the four sources are tried in. Everything runs on
/// the class's own seams, so nothing here opens a device or synthesises speech.
/// </summary>
public class BackRoomVoiceTests
{
    // ============================ the normaliser ============================

    [Theory]
    [InlineData("Drop", "drop")]
    [InlineData("Let Go", "let go")]
    [InlineData("Let  Go!", "let go")]
    [InlineData("  LET GO  ", "let go")]
    [InlineData("Don't think.", "don t think")]
    [InlineData("GOOD GIRL", "good girl")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("!!!", "")]
    public void Normalize_IsTheManifestKey(string input, string expected)
        => Assert.Equal(expected, BackRoomVoice.Normalize(input));

    [Fact]
    public void Normalize_IsNullSafe() => Assert.Equal(string.Empty, BackRoomVoice.Normalize(null));

    [Theory]
    [InlineData("Let Go!", "let-go")]
    [InlineData("Drop", "drop")]
    [InlineData("Good  Girl", "good-girl")]
    public void Slug_IsTheFileStem(string input, string expected)
        => Assert.Equal(expected, BackRoomVoice.Slug(input));

    [Fact]
    public void Normalize_FoldsTheSpellingsOfOnePhraseTogether()
    {
        var key = BackRoomVoice.Normalize("Let Go");
        Assert.Equal(key, BackRoomVoice.Normalize("let  go"));
        Assert.Equal(key, BackRoomVoice.Normalize("LET GO!"));
        Assert.Equal(key, BackRoomVoice.Normalize("  Let, Go  "));
        Assert.NotEqual(key, BackRoomVoice.Normalize("letgo"));
    }

    // ============================ the manifest ============================

    private static string WriteManifest(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "br-words-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, BackRoomVoice.ManifestName);
        File.WriteAllText(file, json);
        return file;
    }

    [Fact]
    public void Manifest_KeysAreNormalisedOnTheWayIn()
    {
        var file = WriteManifest(@"{ ""version"": 1, ""words"": { ""Let Go!"": ""let-go.mp3"", ""DROP"": ""drop.wav"" } }");
        var map = BackRoomVoice.ReadManifest(file);
        Assert.Equal("let-go.mp3", map["let go"]);
        Assert.Equal("drop.wav", map["drop"]);
        Assert.False(map.ContainsKey("Let Go!"));
    }

    [Fact]
    public void Manifest_DropsAnythingThatIsNotAPlainFileNameInItsOwnFolder()
    {
        var file = WriteManifest(@"{ ""words"": {
            ""ok"": ""ok.mp3"",
            ""up"": ""../../secret.mp3"",
            ""rooted"": ""C:/Windows/System32/beep.wav"",
            ""slashed"": ""nested/clip.mp3"",
            ""dotted"": ""..\\evil.mp3"",
            ""blank"": ""  "",
            ""number"": 7,
            ""!!"": ""punctuation-only-key.mp3""
        } }");
        var map = BackRoomVoice.ReadManifest(file);
        Assert.Equal(new[] { "ok" }, map.Keys);
        Assert.Equal("ok.mp3", map["ok"]);
    }

    [Fact]
    public void Manifest_AMissingOrBrokenFileIsAnEmptyMapNotAThrow()
    {
        Assert.Empty(BackRoomVoice.ReadManifest(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".json")));
        Assert.Empty(BackRoomVoice.ReadManifest(WriteManifest("{ not json at all")));
        Assert.Empty(BackRoomVoice.ReadManifest(WriteManifest(@"{ ""version"": 1 }")));
        Assert.Empty(BackRoomVoice.ReadManifest(WriteManifest(@"{ ""words"": [] }")));
    }

    [Fact]
    public void Manifest_ShippedFileParsesAndIsEmptyUntilTheOwnerFillsIt()
    {
        // Ships with the manifest only, so a build with no clips in it still behaves.
        var repo = FindRepoRoot();
        var shipped = Path.Combine(repo, "ConditioningControlPanel", "Resources", "Audio", "backroom", "words", "words.json");
        Assert.True(File.Exists(shipped), "words.json is missing from " + shipped);
        var doc = JObject.Parse(File.ReadAllText(shipped));
        Assert.Equal(1, (int)doc["version"]!);
        Assert.NotNull(doc["words"] as JObject);
        Assert.Empty(BackRoomVoice.ReadManifest(shipped));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "ConditioningControlPanel", "Resources"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }

    // ============================ the order ============================

    private static Func<string, string?> Has(string? value, List<string>? asked = null)
        => s => { asked?.Add(s); return value; };

    [Fact]
    public void Resolve_ThePlayersOwnClipWins()
    {
        var (source, path) = BackRoomVoice.Resolve("Let Go", Has("clip.mp3"), Has("preset.mp3"), Has("tts.wav"));
        Assert.Equal("clip", source);
        Assert.Equal("clip.mp3", path);
    }

    [Fact]
    public void Resolve_ThenTheBundledPresetThenWindowsSpeech()
    {
        var (s1, p1) = BackRoomVoice.Resolve("Let Go", Has(null), Has("preset.mp3"), Has("tts.wav"));
        Assert.Equal("preset", s1);
        Assert.Equal("preset.mp3", p1);

        var (s2, p2) = BackRoomVoice.Resolve("Let Go", Has(null), Has(null), Has("tts.wav"));
        Assert.Equal("tts", s2);
        Assert.Equal("tts.wav", p2);
    }

    [Fact]
    public void Resolve_NothingAtAllIsNoneSoThePageSpeaksForItself()
    {
        var (source, path) = BackRoomVoice.Resolve("Let Go", Has(null), Has(null), Has(null));
        Assert.Equal("none", source);
        Assert.Null(path);
    }

    [Fact]
    public void Resolve_ThePresetIsLookedUpByTheNormalisedPhrase()
    {
        var asked = new List<string>();
        BackRoomVoice.Resolve("  Let  GO!  ", Has(null), Has("let-go.mp3", asked), Has(null));
        Assert.Equal(new[] { "let go" }, asked);
    }

    [Fact]
    public void Resolve_AnEmptyPhraseNeverReachesASource()
    {
        var asked = new List<string>();
        var (source, _) = BackRoomVoice.Resolve("!!!", Has("clip.mp3", asked), Has("preset.mp3", asked), Has("tts.wav", asked));
        Assert.Equal("none", source);
        Assert.Empty(asked);
    }

    [Fact]
    public void Resolve_ALookupThatThrowsOrReturnsBlankFallsThroughInsteadOfBreakingTheBeat()
    {
        var (s1, p1) = BackRoomVoice.Resolve("Drop", _ => throw new IOException("gone"), Has("preset.mp3"), Has("tts.wav"));
        Assert.Equal("preset", s1);
        Assert.Equal("preset.mp3", p1);

        var (s2, p2) = BackRoomVoice.Resolve("Drop", Has("   "), Has(null), Has("tts.wav"));
        Assert.Equal("tts", s2);
        Assert.Equal("tts.wav", p2);
    }

    // ============================ Speak ============================

    private static BackRoomVoice Rig(string? clip, string? preset, string? tts, List<string> played,
        Func<string, string?>? reverse = null, int durationMs = 500, bool play = true)
        => new(Has(clip), Has(preset), Has(tts), reverse ?? (p => p + ".rev"), _ => durationMs,
               p => { played.Add(p); return play; }, () => played.Add("<stop>"));

    [Fact]
    public void Speak_PlaysTheResolvedClipAndAcksItsSourceAndDuration()
    {
        var played = new List<string>();
        var ack = Rig("clip.mp3", null, null, played).Speak("Let Go", reversed: false, seed: 7);
        Assert.Equal("clip", ack.Source);
        Assert.Equal(500, ack.DurationMs);
        Assert.Equal(new[] { "clip.mp3" }, played);
    }

    [Fact]
    public void Speak_ReversedPlaysTheReversedRender()
    {
        var played = new List<string>();
        var ack = Rig(null, "let-go.mp3", null, played).Speak("Let Go", reversed: true, seed: 7);
        Assert.Equal("preset", ack.Source);
        Assert.Equal(new[] { "let-go.mp3.rev" }, played);
    }

    [Fact]
    public void Speak_AReversalThatCannotBeRenderedStillPlaysForwards()
    {
        var played = new List<string>();
        var ack = Rig(null, null, "tts.wav", played, reverse: _ => null).Speak("Drop", reversed: true, seed: 1);
        Assert.Equal("tts", ack.Source);
        Assert.Equal(new[] { "tts.wav" }, played);
    }

    [Fact]
    public void Speak_AMuteReportsItsSourceWithNoDurationSoThePageStaysQuiet()
    {
        var played = new List<string>();
        var ack = Rig("clip.mp3", null, null, played, play: false).Speak("Drop", reversed: false, seed: 1);
        Assert.Equal("clip", ack.Source);
        Assert.Equal(0, ack.DurationMs);
    }

    [Fact]
    public void Speak_NothingToSayIsNoneAndPlaysNothing()
    {
        var played = new List<string>();
        var rig = Rig(null, null, null, played);
        Assert.Equal("none", rig.Speak("Drop", false, 1).Source);
        Assert.Equal("none", rig.Speak("   ", false, 1).Source);
        Assert.Equal("none", rig.Speak(new string('x', BackRoomVoice.MaxTextLength + 1), false, 1).Source);
        Assert.Empty(played);
    }

    [Fact]
    public void Stop_GoesStraightThrough()
    {
        var played = new List<string>();
        Rig("clip.mp3", null, null, played).Stop();
        Assert.Equal(new[] { "<stop>" }, played);
    }

    [Fact]
    public void NullVoice_SaysNoneSoThePageKeepsItsOwnSpeech()
    {
        var ack = NullBackRoomVoice.Instance.Speak("Drop", true, 3);
        Assert.Equal("none", ack.Source);
        Assert.Equal(0, ack.DurationMs);
        NullBackRoomVoice.Instance.Stop();
    }
// ============================ through the bridge ============================

    private sealed class RecordingVoice : IBackRoomVoice
    {
        public readonly List<(string Text, bool Reversed, int Seed)> Said = new();
        public int Stops;
        public BackRoomVoiceAck Next = new("clip", 640);
        public Exception? Throw;
        public BackRoomVoiceAck Speak(string text, bool reversed, int seed)
        {
            if (Throw != null) throw Throw;
            Said.Add((text, reversed, seed));
            return Next;
        }
        public void Stop() => Stops++;
    }

    private static (BackRoomBridge Bridge, RecordingVoice Voice, List<JObject> Posted) VoiceRig()
    {
        var voice = new RecordingVoice();
        var posted = new List<JObject>();
        var bridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = m => posted.Add(JObject.FromObject(m)),
            Relay = new BackRoomBridgeTests.Relay(),
            Voice = voice,
            BuildInit = () => new { type = "init", protocol = 1 },
            CloseWindow = () => { },
            Schedule = new BackRoomBridgeTests.Clock().Schedule,
        });
        return (bridge, voice, posted);
    }

    private static JObject Ack(List<JObject> posted)
        => Assert.Single(posted.FindAll(p => (string?)p["type"] == "word-ack"));

    [Fact]
    public void WordSpeak_ReachesTheVoiceAndGetsExactlyOneAck()
    {
        var (bridge, voice, posted) = VoiceRig();
        bridge.Handle(new JObject
        {
            ["type"] = "word.speak", ["token"] = "tok", ["text"] = "Let Go", ["reversed"] = true, ["seed"] = 42,
        });
        Assert.Equal(new[] { ("Let Go", true, 42) }, voice.Said);
        var ack = Ack(posted);
        Assert.Equal("tok", (string?)ack["token"]);
        Assert.Equal("clip", (string?)ack["source"]);
        Assert.Equal(640, (int)ack["durationMs"]!);
    }

    [Fact]
    public void WordSpeak_ABlankOrOverlongPhraseIsAckedNoneWithoutTouchingTheVoice()
    {
        foreach (var text in new[] { "", "   ", new string('x', BackRoomBridge.MaxWordLength + 1) })
        {
            var (bridge, voice, posted) = VoiceRig();
            bridge.Handle(new JObject { ["type"] = "word.speak", ["token"] = "t", ["text"] = text });
            Assert.Empty(voice.Said);
            Assert.Equal("none", (string?)Ack(posted)["source"]);
        }
    }

    [Fact]
    public void WordSpeak_AVoiceThatThrowsStillAcksOnce()
    {
        var (bridge, voice, posted) = VoiceRig();
        voice.Throw = new InvalidOperationException("no device");
        bridge.Handle(new JObject { ["type"] = "word.speak", ["token"] = "t", ["text"] = "Drop" });
        Assert.Equal("none", (string?)Ack(posted)["source"]);
    }

    [Fact]
    public void WordSpeak_ABadReversedOrSeedReadsAsFalseAndZero()
    {
        var (bridge, voice, _) = VoiceRig();
        bridge.Handle(new JObject { ["type"] = "word.speak", ["token"] = "t", ["text"] = "Drop", ["reversed"] = "true", ["seed"] = "42" });
        Assert.Equal(new[] { ("Drop", false, 0) }, voice.Said);
    }

    [Fact]
    public void WordStop_AndSuspendAndCloseAllCutTheLine()
    {
        var (bridge, voice, _) = VoiceRig();
        bridge.Handle(new JObject { ["type"] = "word.stop" });
        Assert.Equal(1, voice.Stops);
        bridge.Suspend(true, "panic");
        Assert.Equal(2, voice.Stops);
        bridge.Handle(new JObject { ["type"] = "exit", ["reason"] = "back" });
        Assert.True(voice.Stops >= 3);
    }

    [Fact]
    public void WordSpeak_WhileSuspendedIsAckedNoneWithoutSpeaking()
    {
        var (bridge, voice, posted) = VoiceRig();
        bridge.Suspend(true, "panic");
        bridge.Handle(new JObject { ["type"] = "word.speak", ["token"] = "t", ["text"] = "Drop" });
        Assert.Empty(voice.Said);
        Assert.Equal("none", (string?)Ack(posted)["source"]);
    }
}
