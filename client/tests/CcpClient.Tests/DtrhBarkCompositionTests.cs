using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Companion;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The DTRH bark composition, driven.
///
/// <para><b>Why this file exists.</b> The zero-execution census found that
/// <c>DtrhHostWindow.InitBarkPipeline</c> was the SOLE construction site of four types with zero
/// executed lines, inside an 833-line <c>Window</c> that also had zero. The window is not drivable
/// — it needs a real <c>ApplicationHost</c> carrying a <c>DtrhParticipant</c> and a
/// <c>DtrhSaveSlots</c>, an Avalonia <c>InitializeComponent()</c>, and an <c>Opened</c> handler
/// that boots a real audio device — so the WIRING inside it had never run either. These facts drive
/// the lifted composition half of <see cref="DtrhBarkRouting"/>
/// (<c>DtrhBarkRouting.Composition.cs</c>), which constructs exactly what the window constructed:
/// same objects, same arguments, same order.</para>
///
/// <para><b>What a wiring fact buys that a unit fact does not.</b> Every failure available here is
/// SILENT in production: a mistyped audio folder makes every bark resolve to a miss and surface
/// text-only with a typed reason that looks exactly like "no voice assets shipped yet" (which is
/// the port's real current state), and the wrong duck sink makes ducking quietly stop happening.
/// Nothing in the suite would have noticed either.</para>
///
/// <para>The backend is a recording fake — the real one is SoundFlow over a hardware device — and
/// <c>Initialize(null)</c> is called exactly as the window calls it (<c>DtrhHostWindow.axaml.cs</c>),
/// because an uninitialised arbitration answers <c>Unavailable</c> and every voice outcome below
/// would collapse to the same text-only shape for the wrong reason.</para>
/// </summary>
public sealed class DtrhBarkCompositionTests
{
    [Fact]
    public void AVoiceAssetPresentUnderCompanionAudio_ResolvesThroughTheRealWiring()
    {
        // The fact that pins the folder name. attention_check_fail is the one built-in rule with a
        // SINGLE audio variant, cooldown_ms 0, and global-min-gap exemption, so it is deterministic
        // under the composition's stock BarkPipelineOptions (whose Rng is Random.Shared).
        using var h = Harness.New();
        var expected = h.WriteVoiceAsset("attention_check_fail_1.mp3");

        var outcome = h.Pipeline.Raise("AttentionCheckFail");

        var surfaced = Assert.IsType<BarkOutcome.Surfaced>(outcome);
        Assert.Equal(expected, surfaced.Payload.AudioPath);
        Assert.Equal("attention_check_fail", surfaced.Payload.RuleId);
    }

    [Fact]
    public void AVoiceAssetThatIsNotThere_SurfacesTEXTONLY_WithTypedAudioResolveFailed()
    {
        // The port's actual state today: no voice assets ship, so <DataDirectory>/companion_audio
        // does not even exist. The bark must still SURFACE, with a typed reason — never silence,
        // and never AudioUnavailable, which is a different failure (the device) wearing the same
        // text-only shape.
        using var h = Harness.New();

        var outcome = h.Pipeline.Raise("AttentionCheckFail");

        var textOnly = Assert.IsType<BarkOutcome.SurfacedTextOnly>(outcome);
        Assert.Equal(BarkSuppression.AudioResolveFailed, textOnly.Reason);
        Assert.Null(textOnly.Payload.AudioPath);
    }

    [Fact]
    public void TheComposedArbitration_RefusesADuck_TYPED_RatherThanPretendingToHoldOne()
    {
        // Drives the UnavailableDuckSink the APP-WIDE owner composes (Audio/AudioParticipant.cs,
        // where this half of the graph moved) through SoundArbitration.AcquireDuck
        // (SoundArbitration.cs:665-670). Cross-app session ducking is a pending-owner platform
        // decision (audio-backend-spike.md named limit 8), so the honest answer is a refusal that
        // says so — and the refcount must treat the hold as NOT taken (WPF symmetric-failure
        // parity, AudioService.cs:869-873), or the next release would be a volume ratchet.
        using var h = Harness.New();

        var attempt = h.Arbitration.AcquireDuck(0.5f);

        Assert.False(attempt.Held);
        Assert.Null(attempt.Handle);
        Assert.Contains("not admitted", attempt.Error);
        Assert.Equal(0, h.Arbitration.DuckCount);
    }

    [Fact]
    public void TheAppsMasterVolumeReachesTheBarkPATH_AndZeroMakesItTextOnly()
    {
        // The other half of the app-wide audio lift: BarkPipeline.MasterVolume
        // (Companion/BarkPipeline.cs:262) had been landed and unset since it shipped, because there
        // was no app-wide document to set it from. Now the window passes the audio owner's reading
        // (DtrhHostWindow.InitBarkPipeline), and this drives that same expression.
        //
        // Zero is asserted through BEHAVIOUR rather than by reading the property back: a user whose
        // master volume is 0 has muted the app, and upstream's every voice path early-returns while
        // the text still shows (AvatarTube/AvatarTubeWindow.Speech.cs:2188-2189). The asset is
        // really on disk, so the text-only outcome cannot be the missing-asset one wearing the same
        // shape — the reason has to be VolumeZero.
        using var muted = Harness.New(masterVolume: 0);
        muted.WriteVoiceAsset("attention_check_fail_1.mp3");

        var silent = Assert.IsType<BarkOutcome.SurfacedTextOnly>(muted.Pipeline.Raise("AttentionCheckFail"));

        Assert.Equal(BarkSuppression.VolumeZero, silent.Reason);
        Assert.Null(silent.Payload.AudioPath);
        Assert.Equal(0, muted.Pipeline.MasterVolume);

        // A volume the user can hear leaves the same bark with its voice, so the fact above is
        // about the VOLUME and not about this harness being unable to speak at all.
        using var audible = Harness.New(masterVolume: 41);
        var expected = audible.WriteVoiceAsset("attention_check_fail_1.mp3");

        var spoken = Assert.IsType<BarkOutcome.Surfaced>(audible.Pipeline.Raise("AttentionCheckFail"));

        Assert.Equal(expected, spoken.Payload.AudioPath);
        Assert.Equal(41, audible.Pipeline.MasterVolume);
    }

    [Fact]
    public void AnUnknownTrigger_IsTypedNoRule_NeverSilence()
    {
        using var h = Harness.New();

        var outcome = h.Pipeline.Raise("NoSuchTriggerExistsInTheBuiltInManifest");

        var noRule = Assert.IsType<BarkOutcome.NoRule>(outcome);
        Assert.Equal("NoSuchTriggerExistsInTheBuiltInManifest", noRule.Trigger);
        Assert.Equal("no rule for trigger", noRule.Detail);
    }

    [Fact]
    public void TheCompositionParsesTheWholeBuiltInManifest_AndRefusesNullWiringAtTheSite()
    {
        // Two properties of the composition itself. The rule count says the manifest really parsed
        // through the composition (a loader that dropped rules would otherwise show up only as
        // barks that never fire); the refusals say a wiring mistake fails on the thread that made
        // it rather than as a NullReferenceException from inside a bark hours later.
        using var h = Harness.New();
        Assert.Equal(8, h.Pipeline.RuleCount);

        Assert.Throws<ArgumentNullException>(
            () => DtrhBarkRouting.CreatePipeline(null!, h.Store, h.DataDirectory, _ => { }));
        Assert.Throws<ArgumentNullException>(
            () => DtrhBarkRouting.CreatePipeline(h.Arbitration, h.Store, null!, _ => { }));
    }

    // ---------- harness: the window's composition, with a fake backend and a temp data dir ----------

    private sealed class Harness : IDisposable
    {
        private Harness(string dataDirectory, SoundArbitration arbitration,
            PersistenceStore<CompanionStateDocument> store, BarkPipeline pipeline)
        {
            DataDirectory = dataDirectory;
            Arbitration = arbitration;
            Store = store;
            Pipeline = pipeline;
        }

        public string DataDirectory { get; }

        public SoundArbitration Arbitration { get; }

        public PersistenceStore<CompanionStateDocument> Store { get; }

        public BarkPipeline Pipeline { get; }

        /// <param name="masterVolume">The app's master volume as the audio owner holds it. Null
        /// leaves the document's own default, which is what a fresh install has.</param>
        public static Harness New(int? masterVolume = null)
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(), "ccp-dtrh-bark-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);

            // Exactly DtrhHostWindow.InitBarkPipeline's order: the APP-WIDE audio owner's
            // arbitration, its device, the store, the pipeline. Only the backend and the log differ
            // from the product path — the window no longer builds an arbitration of its own, so
            // neither does this harness (Audio/AudioParticipant.cs).
            var audio = new AudioParticipant(
                new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new SilentLogSink()),
                dataDirectory,
                backend: new FakeBackend());
            if (masterVolume is { } master)
            {
                audio.Settings.Mutate(d => d.MasterVolume = master);
            }

            var arbitration = audio.Arbitration;
            audio.EnsureDevice();

            // Literal, for the same reason WriteVoiceAsset uses one: the companion state file lives
            // beside a user's DTRH save, so its name is a compatibility surface, and a fact that
            // reads the constant back would not notice it changing.
            var store = new PersistenceStore<CompanionStateDocument>(
                new OperationRegistry().OwnerFor("DtrhBarkCompanion"),
                new SilentLogSink(),
                Path.Combine(dataDirectory, "companion.json"),
                CompanionStateDocument.CurrentSchemaVersion);
            store.StartAsync(CancellationToken.None).GetAwaiter().GetResult(); // wallclock-allow: PersistenceStore.StartAsync loads on the calling thread and hands back an already-complete task (pinned by PersistenceStoreTests) — this bridge waits on nothing

            // `audio.MasterVolume` is the window's own expression (DtrhHostWindow.InitBarkPipeline).
            var pipeline = DtrhBarkRouting.CreatePipeline(
                arbitration, store, dataDirectory, _ => { }, masterVolume: audio.MasterVolume);
            return new Harness(dataDirectory, arbitration, store, pipeline);
        }

        /// <summary>
        /// Places a zero-length asset where the composition is REQUIRED to look for it.
        ///
        /// <para>The folder name is a LITERAL here, deliberately, and the first draft of this file
        /// got it wrong: writing the asset through <c>DtrhBarkRouting.CompanionAudioFolder</c>
        /// makes both sides of the fact move together, so mutating the constant to "companionaudio"
        /// left every fact green. A constant referenced by the test and the product pins nothing —
        /// the fact has to state the name independently or it is only checking that a string equals
        /// itself.</para>
        /// </summary>
        public string WriteVoiceAsset(string fileName)
        {
            var folder = Path.Combine(DataDirectory, "companion_audio");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            Arbitration.Dispose();
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }

    private sealed class SilentLogSink : ILogSink
    {
        public void Log(string message) { }
    }

    private sealed class FakeBackend : IAudioBackend
    {
        public IReadOnlyList<string> EnumerateDevices() => ["Fake Endpoint"];

        public bool TryInit(string? deviceName, out string? error)
        {
            error = null;
            return true;
        }

        public IAudioPlayer CreatePlayer(string path, float volume) => new FakePlayer();

        public void Dispose() { }
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        private bool _playing;

        /// <summary>Explicitly inert: nothing here reaches natural completion, so no fact below can
        /// accidentally depend on an end event this fake would have to invent a schedule for.</summary>
        public event EventHandler? PlaybackEnded { add { } remove { } }

        public AudioPlayerState State => _playing ? AudioPlayerState.Playing : AudioPlayerState.Stopped;

        public double PositionSec => 0;

        public float Volume { get; set; }

        public void Play() => _playing = true;

        public void Pause() { }

        public void Stop() => _playing = false;

        public void Dispose() => _playing = false;
    }
}
