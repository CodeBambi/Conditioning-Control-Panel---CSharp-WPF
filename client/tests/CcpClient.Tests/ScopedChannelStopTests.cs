using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Companion;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>One window closing must not silence another consumer's sound.</b>
///
/// <para><b>The regression these facts exist for.</b> <see cref="SoundArbitration"/> became
/// app-wide when it moved into <c>Audio/AudioParticipant.cs</c>, but the DTRH host window's close
/// path kept calling <see cref="SoundArbitration.PanicReset"/> — which stops EVERY channel. While
/// DTRH was the only consumer that was the same act; the flash clip
/// (<see cref="SoundArbitration.PlayWhisper"/>, <c>Effects/EffectSounds.cs:212</c>) and the bubble
/// pops (<see cref="SoundArbitration.PlaySfx"/>, <c>:247</c>) made it a defect a user hears:
/// closing the DTRH window cut off a whisper and a pop that were never DTRH's.</para>
///
/// <para><b>Why the harness builds the REAL bark pipeline.</b> The first consumer here is not a
/// hand-made <c>PlayVoice</c> call standing in for DTRH — it is
/// <see cref="DtrhBarkRouting.CreatePipeline"/>, the composition the host window itself builds, so
/// the fact carries the window's real channel usage (voice, and only voice — the DTRH page's own
/// whispers and one-shots are the DTRH-local engine on its own device, which
/// <c>Audio/SoundArbitration.cs:104-106</c> keeps outside this core). The stop is likewise the
/// product's own expression, <see cref="DtrhBarkRouting.StopPipelineAudio"/>, and not a restatement
/// of it: a fact that named the channel itself would still pass if the product picked a different
/// one.</para>
///
/// <para><b>What these do NOT prove.</b> Nothing at the device. The backend is a recording fake, so
/// this is routing and lifetime — that a player was told to stop, or was not. Whether a real
/// endpoint kept a whisper audible through a window close is a headed/audible claim and no headless
/// fact here establishes it.</para>
/// </summary>
public sealed class ScopedChannelStopTests
{
    /// <summary>
    /// The fact the regression is about. Mutations that red it: give
    /// <see cref="DtrhBarkRouting.StopPipelineAudio"/> its old body (<c>PanicReset()</c>, which is
    /// exactly the code this replaced); point it at <see cref="SoundChannel.Whisper"/> or
    /// <see cref="SoundChannel.Sfx"/>; make <c>DetachChannels</c> bump <c>_whisperGeneration</c> for
    /// channels it was not asked to stop (the surviving whisper then never clears its busy flag,
    /// and <c>Companion/BarkPipeline.cs:416</c> suppresses every later bark with
    /// <c>whisper-active</c> forever).
    /// </summary>
    [Fact]
    public async Task ClosingTheDtrhWindow_StopsItsBark_AndLeavesTheFlashAndThePopPlaying()
    {
        using var h = await Harness.NewAsync();

        // Consumer 1 — the DTRH host window's bark, through the composition the window builds.
        var bark = h.Pipeline.Raise("AttentionCheckFail");
        Assert.IsType<BarkOutcome.Surfaced>(bark);
        h.Clock.Advance(TimeSpan.Zero); // an ORDINARY bark queues behind the pacing floor; fire it
        Assert.True(h.Arbitration.VoiceActive);
        var voice = Assert.Single(h.Backend.Players);
        Assert.EndsWith("attention_check_fail_1.mp3", voice.Path, StringComparison.Ordinal);

        // Consumer 2 — the flash clip and one bubble pop (EffectSounds.cs:212,:247).
        Assert.IsType<SoundOutcome.Started>(h.Arbitration.PlayWhisper("flash-clip.wav", 0.7f));
        var flash = h.Backend.Players[^1];
        Assert.IsType<SoundOutcome.Started>(h.Arbitration.PlaySfx("pop.mp3", 0.9f));
        var pop = h.Backend.Players[^1];
        Assert.True(flash.Playing && pop.Playing);

        // The DTRH host window closes (DtrhHostWindow.TeardownBarkPipeline).
        DtrhBarkRouting.StopPipelineAudio(h.Arbitration);

        // Its bark is gone…
        Assert.True(voice.Stopped);
        Assert.True(voice.Disposed);
        Assert.False(h.Arbitration.VoiceActive);
        Assert.Equal(0, h.Arbitration.QueuedVoiceCount);

        // …and the other consumer is untouched: still playing, never stopped, never disposed.
        Assert.True(flash.Playing);
        Assert.False(flash.Stopped);
        Assert.False(flash.Disposed);
        Assert.True(pop.Playing);
        Assert.False(pop.Stopped);
        Assert.False(pop.Disposed);
        Assert.True(h.Arbitration.WhisperBusy);
        Assert.Equal(1, h.Arbitration.ActiveSfxVoices);

        // And their channels still END normally afterwards — a scoped stop that quietly bumped
        // their generations would leave the busy flag and the pool slot stuck for the app's life.
        flash.RaiseEnded();
        pop.RaiseEnded();
        Assert.False(h.Arbitration.WhisperBusy);
        Assert.Equal(0, h.Arbitration.ActiveSfxVoices);
        Assert.True(flash.Disposed && pop.Disposed);
    }

    /// <summary>
    /// The panic path is still THE panic path: same two consumers, same instant, and
    /// <see cref="SoundArbitration.PanicReset"/> takes everything — which is why the fact above is
    /// about the CALL and not about a harness that cannot stop a whisper.
    ///
    /// <para>Mutations that red it: have <c>PanicReset</c> detach <c>[SoundChannel.Voice]</c>
    /// instead of <c>AllChannels</c>; drop the <c>ForceUnduck</c>; drop a channel from
    /// <see cref="SoundChannel"/>'s detach switch (the default arm then logs and the assert on that
    /// channel fails).</para>
    /// </summary>
    [Fact]
    public async Task PanicReset_StillTakesEveryChannel_IncludingTheOnesTheScopedStopSpares()
    {
        using var h = await Harness.NewAsync();

        Assert.IsType<BarkOutcome.Surfaced>(h.Pipeline.Raise("AttentionCheckFail"));
        h.Clock.Advance(TimeSpan.Zero);
        var voice = Assert.Single(h.Backend.Players);
        h.Arbitration.PlayWhisper("flash-clip.wav", 0.7f);
        var flash = h.Backend.Players[^1];
        h.Arbitration.PlaySfx("pop.mp3", 0.9f);
        var pop = h.Backend.Players[^1];
        Assert.True(voice.Playing && flash.Playing && pop.Playing);

        h.Arbitration.PanicReset();

        Assert.True(voice.Stopped && voice.Disposed);
        Assert.True(flash.Stopped && flash.Disposed);
        Assert.True(pop.Stopped && pop.Disposed);
        Assert.False(h.Arbitration.VoiceActive);
        Assert.False(h.Arbitration.WhisperBusy);
        Assert.Equal(0, h.Arbitration.ActiveSfxVoices);
        Assert.Equal(0, h.Arbitration.QueuedVoiceCount);
    }

    /// <summary>
    /// The window's own line, guarded LEXICALLY for the reason
    /// <see cref="AudioOwnershipGuardTests"/> already gives: <c>DtrhHostWindow</c> needs a real
    /// <c>ApplicationHost</c>, an <c>InitializeComponent()</c> and an <c>Opened</c> handler that
    /// boots a device, so no fact can drive its teardown — and this whole regression was a caller
    /// drifting from a mechanism that had moved underneath it. Comment lines are skipped on
    /// purpose: that file's teardown documentation NAMES the panic reset it used to call, and a
    /// guard that reds on the explanation would just delete the history.
    ///
    /// <para>Mutation: put <c>_barkArbitration?.PanicReset();</c> back into
    /// <c>TeardownBarkPipeline</c> — which is the tree exactly as it stood before this fix, and it
    /// reds. Deleting the stop entirely reds the second half.</para>
    /// </summary>
    [Fact]
    public void TheDtrhWindowsCloseCallsTheScopedStop_AndNeverThePanicPath()
    {
        var window = Path.Combine(
            FindRepoRoot(), "client", "src", "CcpClient.Desktop",
            "Features", "Dtrh", "DtrhHostWindow.axaml.cs");

        // Read it UNGUARDED: a missing or moved window must fail this fact loudly with the path in
        // the exception, never be skipped past by a File.Exists predicate the guard cannot tell
        // from a silenced assertion (VacuousShapeDetector's fs-predicate shape).
        var code = File.ReadLines(window)
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith('*'))
            .ToList();

        Assert.DoesNotContain(code, l => l.Contains("PanicReset(", StringComparison.Ordinal));
        Assert.Contains(code, l => l.Contains("DtrhBarkRouting.StopPipelineAudio(", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }

    // ---------- harness: the window's own composition over one app-wide arbitration ----------

    private sealed class Harness : IDisposable
    {
        private Harness(
            string dataDirectory, AudioParticipant audio, FakeBackend backend,
            ManualClock clock, PersistenceStore<CompanionStateDocument> store, BarkPipeline pipeline)
        {
            DataDirectory = dataDirectory;
            Audio = audio;
            Backend = backend;
            Clock = clock;
            Store = store;
            Pipeline = pipeline;
        }

        public string DataDirectory { get; }

        public AudioParticipant Audio { get; }

        public FakeBackend Backend { get; }

        public ManualClock Clock { get; }

        public PersistenceStore<CompanionStateDocument> Store { get; }

        public BarkPipeline Pipeline { get; }

        public SoundArbitration Arbitration => Audio.Arbitration;

        /// <summary>
        /// Exactly <c>DtrhHostWindow.InitBarkPipeline</c>'s order — the app-wide audio owner, its
        /// device, the companion store, the pipeline — over a recording backend and a MANUAL clock.
        /// The clock is manual because an ordinary bark QUEUES behind the 2 s pacing floor
        /// (<c>SoundArbitration.ScheduleNextVoiceLocked</c>), and a real clock would decide on a
        /// timer thread when (or whether) the voice player exists.
        /// </summary>
        public static async Task<Harness> NewAsync()
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(), "ccp-scoped-stop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);

            var backend = new FakeBackend();
            var clock = new ManualClock();
            var audio = new AudioParticipant(
                new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new SilentLogSink()),
                dataDirectory,
                backend: backend,
                clock: clock);
            Assert.IsType<SoundOutcome.Ready>(audio.EnsureDevice());

            var store = new PersistenceStore<CompanionStateDocument>(
                new OperationRegistry().OwnerFor("DtrhBarkCompanion"),
                new SilentLogSink(),
                Path.Combine(dataDirectory, "companion.json"),
                CompanionStateDocument.CurrentSchemaVersion);
            await store.StartAsync(TestContext.Current.CancellationToken);

            // The asset the built-in attention_check_fail rule resolves to. The folder name is a
            // LITERAL for the reason DtrhBarkCompositionTests states: writing it through
            // DtrhBarkRouting.CompanionAudioFolder would move both sides of the fact together.
            var folder = Path.Combine(dataDirectory, "companion_audio");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "attention_check_fail_1.mp3"), []);

            var pipeline = DtrhBarkRouting.CreatePipeline(
                audio.Arbitration, store, dataDirectory, _ => { }, masterVolume: audio.MasterVolume);
            return new Harness(dataDirectory, audio, backend, clock, store, pipeline);
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
        public List<FakePlayer> Players { get; } = [];

        public IReadOnlyList<string> EnumerateDevices() => ["Fake Endpoint"];

        public bool TryInit(string? deviceName, out string? error)
        {
            error = null;
            return true;
        }

        public IAudioPlayer CreatePlayer(string path, float volume)
        {
            var player = new FakePlayer(path, volume);
            Players.Add(player);
            return player;
        }

        public void Dispose() { }
    }

    private sealed class FakePlayer(string path, float volume) : IAudioPlayer
    {
        public string Path { get; } = path;

        public bool Playing { get; private set; }

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler? PlaybackEnded;

        public AudioPlayerState State => Playing ? AudioPlayerState.Playing : AudioPlayerState.Stopped;

        public double PositionSec => 0;

        public float Volume { get; set; } = volume;

        public void Play() => Playing = true;

        public void Pause() { }

        public void Stop()
        {
            Playing = false;
            Stopped = true;
        }

        public void Dispose() => Disposed = true;

        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A hand-advanced clock: nothing here waits on wall time.</summary>
    private sealed class ManualClock : ISoundClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;

            public required Action Fire;

            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            _timers.Add(entry);
            return new CancelHandle(entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                var next = _timers
                    .Where(t => !t.Cancelled && t.Due <= UtcNow)
                    .OrderBy(t => t.Due)
                    .FirstOrDefault();
                if (next is null)
                {
                    return;
                }

                _timers.Remove(next);
                next.Fire();
            }
        }

        private sealed class CancelHandle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
        }
    }
}
