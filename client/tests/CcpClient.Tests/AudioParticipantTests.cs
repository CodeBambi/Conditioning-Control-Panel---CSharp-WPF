using System.Text.Json.Nodes;
using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The APP-WIDE audio owner: the one arbitration, the one settings document, and the route from an
/// <see cref="ApplicationHost"/> to both.
///
/// <para><b>What these facts are for.</b> The arbitration has called itself "the APP-WIDE sound
/// arbitration core" since it landed (<c>Audio/SoundArbitration.cs:88-90</c>) while being
/// constructed only inside the DTRH host window, so the seams on it — device enumeration, the
/// preferred device, the overlapping SFX pool — were unreachable from anywhere else in the
/// application, and no fact could say so because "reachable" is a property of the WIRING rather
/// than of the class. Every failure available here is silent in production: an audio settings
/// document that never loads leaves the user's endpoint choice inert with no error anywhere, and a
/// launch that grabs a render device for a user who plays nothing is invisible from inside the
/// process.</para>
///
/// <para><b>What they do NOT prove, stated because audio is a real device.</b> The backend below is
/// a recording fake. Nothing here shows that Windows opened an endpoint, that a sample reached a
/// mixer, or that a human heard anything — those are the read-back and manual gates
/// <c>Audio/IAudioPresence.cs</c> and <c>AudioPresenceFactory.LinuxManualGate</c> describe, and no
/// headless run discharges them. These facts are about the wiring above the device.</para>
/// </summary>
public sealed class AudioParticipantTests
{
    // ---------- the document ----------

    [Fact]
    public void TheDocument_ShipsUpstreamsDefaults_AndClampsToUpstreamsRange()
    {
        // Literals, never the product's own constants: a fact that reads MasterVolume back through
        // AudioSettingsDocument.MinVolume would move with any edit to it and pin nothing. 32 and 50
        // are WPF's own (Models/AppSettings.cs:1127, :1134); the clamp is Math.Clamp(value, 0, 100)
        // at :1131 and :1138.
        var document = new AudioSettingsDocument();

        Assert.Equal(32, document.MasterVolume);
        Assert.Equal(50, document.VideoVolume);
        Assert.Equal("", document.OutputDeviceName);

        document.MasterVolume = 101;
        document.VideoVolume = 1000;
        Assert.Equal(100, document.MasterVolume);
        Assert.Equal(100, document.VideoVolume);

        document.MasterVolume = -1;
        document.VideoVolume = int.MinValue;
        Assert.Equal(0, document.MasterVolume);
        Assert.Equal(0, document.VideoVolume);

        // Zero is a REAL value and survives a round trip through the setter — a document that
        // treated it as "unset" would un-mute a user who muted the app (AudioService.cs:535-536).
        document.MasterVolume = 0;
        Assert.Equal(0, document.MasterVolume);

        document.OutputDeviceName = null!;
        Assert.Equal("", document.OutputDeviceName);
    }

    [Fact]
    public async Task TheSettings_ReachDisk_UnderTheNameAndKeysTheProductWrites()
    {
        // The file name is a LITERAL here on purpose: it is a compatibility surface (a user's own
        // <dataDir>), and a fact that spelled it through AudioSettingsDocument.FileName would stay
        // green while the product renamed the file under everybody's settings.
        using var dir = new TempDir();
        var audio = NewParticipant(dir, new RecordingBackend());
        await audio.StartAsync(TestContext.Current.CancellationToken);

        audio.Settings.Mutate(d =>
        {
            d.MasterVolume = 71;
            d.VideoVolume = 12;
            d.OutputDeviceName = "Studio Cans";
        });
        await audio.FlushAsync(TestWait.InjectedBudget);

        var document = JsonNode.Parse(File.ReadAllText(dir.Path("audio.json")))!.AsObject();
        Assert.Equal(71, document["masterVolume"]!.GetValue<int>());
        Assert.Equal(12, document["videoVolume"]!.GetValue<int>());
        Assert.Equal("Studio Cans", document["outputDeviceName"]!.GetValue<string>());
        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());

        // And a second owner over the same directory reads them back — which is what "persisted"
        // means to the user: the volume they set is the volume the app has at the next launch.
        var relaunched = NewParticipant(dir, new RecordingBackend());
        await relaunched.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(71, relaunched.MasterVolume);
        Assert.Equal(12, relaunched.VideoVolume);
        Assert.Equal("Studio Cans", relaunched.OutputDeviceName);
        await relaunched.StopAsync();
        await audio.StopAsync();
    }

    // ---------- phase 3 ----------

    [Fact]
    public async Task StartingTheApp_LoadsTheSettings_AndTAKES_NO_RENDER_DEVICE()
    {
        // Both halves matter and neither is enough alone. A start that opened a device would seize
        // a render endpoint on every launch for a user who plays nothing (the class upstream
        // refuses for its own auto-connect, App.xaml.cs:2173-2176); a start that opened nothing
        // because it also loaded nothing would pass the device half vacuously, so the loaded value
        // is asserted from a file written before the start.
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("audio.json"),
            """{ "schemaVersion": 1, "masterVolume": 7, "videoVolume": 9, "outputDeviceName": "Studio Cans" }""");
        var backend = new RecordingBackend();
        var audio = NewParticipant(dir, backend);

        await audio.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, audio.MasterVolume);
        Assert.Equal(9, audio.VideoVolume);
        Assert.Equal("Studio Cans", audio.OutputDeviceName);
        Assert.True(audio.Running);

        Assert.Equal(0, audio.DeviceInitAttempts);
        Assert.Null(audio.DeviceOutcome);
        Assert.Equal(0, backend.InitCalls);
        Assert.Equal(0, backend.EnumerateCalls);
        await audio.StopAsync();
    }

    // ---------- the device, on the user's own choice ----------

    [Fact]
    public async Task EnsureDevice_BringsTheEndpointUpOnThePersistedChoice_AndOnlyOnce()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("audio.json"),
            """{ "schemaVersion": 1, "outputDeviceName": "Studio Cans" }""");
        var backend = new RecordingBackend { Devices = ["Speakers", "Studio Cans"] };
        var audio = NewParticipant(dir, backend);
        await audio.StartAsync(TestContext.Current.CancellationToken);

        var first = audio.EnsureDevice();

        // The endpoint the user chose, not the default the backend would otherwise pick.
        Assert.Equal("Studio Cans", Assert.IsType<SoundOutcome.Ready>(first).DeviceName);
        Assert.Equal("Studio Cans", backend.RequestedDeviceName);
        Assert.Equal(1, backend.InitCalls);

        // Idempotent: the second consumer to want sound gets the first one's answer rather than a
        // device re-init underneath whatever is already playing.
        var second = audio.EnsureDevice();
        Assert.Same(first, second);
        Assert.Equal(1, backend.InitCalls);
        Assert.Equal(1, audio.DeviceInitAttempts);
        await audio.StopAsync();
    }

    [Fact]
    public async Task SelectOutputDevice_ReRoutesTheLiveArbitration_AndTheChoiceSURVIVESARELAUNCH()
    {
        using var dir = new TempDir();
        var backend = new RecordingBackend { Devices = ["Speakers", "Studio Cans"] };
        var audio = NewParticipant(dir, backend);
        await audio.StartAsync(TestContext.Current.CancellationToken);
        audio.EnsureDevice();
        Assert.Null(backend.RequestedDeviceName); // no choice yet: the system default

        var outcome = audio.SelectOutputDevice("Studio Cans");

        Assert.Equal("Studio Cans", Assert.IsType<SoundOutcome.Ready>(outcome).DeviceName);
        Assert.Equal("Studio Cans", backend.RequestedDeviceName);
        Assert.Equal(2, backend.InitCalls);
        await audio.FlushAsync(TestWait.InjectedBudget);

        // The choice is the user's, so it outlives the process that made it: a fresh owner over the
        // same directory routes there without being told.
        var relaunchBackend = new RecordingBackend { Devices = ["Speakers", "Studio Cans"] };
        var relaunched = NewParticipant(dir, relaunchBackend);
        await relaunched.StartAsync(TestContext.Current.CancellationToken);
        relaunched.EnsureDevice();
        Assert.Equal("Studio Cans", relaunchBackend.RequestedDeviceName);

        // And clearing it goes back to the system default rather than to the last name.
        relaunched.SelectOutputDevice(null);
        Assert.Null(relaunchBackend.RequestedDeviceName);
        Assert.Null(relaunched.OutputDeviceName);
        await relaunched.StopAsync();
        await audio.StopAsync();
    }

    // ---------- the reachability this row exists for ----------

    [Fact]
    public async Task TheAudioSeams_AreReachableFromTheHOST_WithNoDtrhWindowAnywhere()
    {
        // THE FACT THE ROW TURNS ON. Before the lift the only SoundArbitration in the product was
        // built inside DtrhHostWindow, so the sentence "the app has an audio seam" was false: a
        // settings page, a session module or a diagnostic had no route to it. This builds the REAL
        // composition root — the product participant list, not a hand-made graph — and reaches the
        // owner the way any surface holding an ApplicationHost will. No window is involved and none
        // can be: this project carries no Avalonia application at all.
        using var dir = new TempDir();
        var root = new CompositionRoot { SettingsPathFactory = () => dir.Path("settings.json") };
        Assert.True(root.Validate(out _));
        var host = root.Build(new StartupTrace());

        var audio = AudioParticipant.Of(host);

        Assert.NotNull(audio);
        Assert.Single(host.Participants.OfType<AudioParticipant>()); // one owner, so one device
        Assert.Equal(32, audio!.MasterVolume);

        // Building the application takes no render endpoint, on the product path and not only on
        // the injected-backend one.
        Assert.Equal(0, audio.DeviceInitAttempts);
        Assert.Null(audio.DeviceOutcome);

        // And the seam is not read-only from out here: a dial moved through the host's owner is on
        // disk after the app's own teardown, through the root's reserved pre-drain flush slot.
        await host.StartParticipantsAsync(TestContext.Current.CancellationToken);

        // Phase 3 has now run over the REAL backend (SoundFlow/miniaudio, not the fake the other
        // facts inject) and still no endpoint was taken. This is the launch-time claim on the
        // product path: starting the application does not seize a render device.
        Assert.Equal(0, audio.DeviceInitAttempts);
        Assert.Null(audio.DeviceOutcome);

        audio.Settings.Mutate(d => d.MasterVolume = 63);
        await host.ShutdownAsync();

        var document = JsonNode.Parse(File.ReadAllText(dir.Path("audio.json")))!.AsObject();
        Assert.Equal(63, document["masterVolume"]!.GetValue<int>());
    }

    // ---------- the overlapping one-shot path (the finding census #96 inherits) ----------

    [Fact]
    public async Task TheAppWideSfxPath_OVERLAPS_RatherThanReplacing_AndDropsPastItsPool()
    {
        // Upstream's one-shots OVERLAP — PlayOneShot admits up to MaxConcurrentOneShots and drops
        // the excess rather than queueing it (Services/Audio/AudioService.Playback.cs:111, enforced
        // at :212) — because "overlapping short clips are normal (pop + bark + whisper)". A burst
        // of bubble pops through a path that REPLACED per slot would cut each other off, which is
        // the shape IAudioPresence.Cue has (IAudioPresence.cs:45-48) and the reason this path
        // exists beside it. Pool of three so the drop is reachable in four calls.
        using var dir = new TempDir();
        var backend = new RecordingBackend();
        var audio = NewParticipant(dir, backend, new SoundArbitrationOptions { MaxSfxVoices = 3 });
        await audio.StartAsync(TestContext.Current.CancellationToken);
        audio.EnsureDevice();

        Assert.IsType<SoundOutcome.Started>(audio.Arbitration.PlaySfx("pop-a.wav", 1f));
        Assert.IsType<SoundOutcome.Started>(audio.Arbitration.PlaySfx("pop-b.wav", 1f));
        Assert.IsType<SoundOutcome.Started>(audio.Arbitration.PlaySfx("pop-c.wav", 1f));

        // Three DISTINCT players, all still sounding: the second pop did not stop the first.
        Assert.Equal(3, backend.Players.Count);
        Assert.Equal(3, audio.Arbitration.ActiveSfxVoices);
        Assert.DoesNotContain(backend.Players, p => p.Stopped);
        Assert.Equal(["pop-a.wav", "pop-b.wav", "pop-c.wav"], backend.Players.Select(p => p.Path));

        // Past the pool the cue is DROPPED and typed, never queued (ChaosSfx.cs:91-107 parity) —
        // and the three already sounding are left alone.
        var dropped = Assert.IsType<SoundOutcome.Dropped>(audio.Arbitration.PlaySfx("pop-d.wav", 1f));
        Assert.Equal(SoundDropReason.PoolOverflow, dropped.Reason);
        Assert.Equal(3, backend.Players.Count);
        Assert.DoesNotContain(backend.Players, p => p.Stopped);
        await audio.StopAsync();
    }

    // ---------- harness ----------

    private static AudioParticipant NewParticipant(
        TempDir dir, IAudioBackend backend, SoundArbitrationOptions? options = null) =>
        new(
            new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new SilentLogSink()),
            dir.Root,
            backend,
            new SystemSoundClock(),
            options);

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Directory.CreateDirectory(Root);

        public string Root { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-audio-" + Guid.NewGuid().ToString("N"));

        public string Path(string fileName) => System.IO.Path.Combine(Root, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp reaper owns the residue.
            }
        }
    }

    private sealed class SilentLogSink : ILogSink
    {
        public void Log(string message) { }
    }

    /// <summary>The real backend is SoundFlow over a hardware device. This records what the
    /// arbitration ASKED the platform for, which is the only part of the request a test can
    /// honestly observe.</summary>
    private sealed class RecordingBackend : IAudioBackend
    {
        public List<RecordingPlayer> Players { get; } = [];

        public string[] Devices { get; set; } = ["Speakers"];

        public string? RequestedDeviceName { get; private set; }

        public int InitCalls { get; private set; }

        public int EnumerateCalls { get; private set; }

        public IReadOnlyList<string> EnumerateDevices()
        {
            EnumerateCalls++;
            return Devices;
        }

        public bool TryInit(string? deviceName, out string? error)
        {
            InitCalls++;
            RequestedDeviceName = deviceName;
            error = null;
            return true;
        }

        public IAudioPlayer CreatePlayer(string path, float volume)
        {
            var player = new RecordingPlayer(path);
            Players.Add(player);
            return player;
        }

        public void Dispose() { }
    }

    private sealed class RecordingPlayer(string path) : IAudioPlayer
    {
        /// <summary>Never raised: nothing here reaches natural completion, so no fact above can
        /// depend on an end event this fake would have to invent a schedule for.</summary>
        public event EventHandler? PlaybackEnded { add { } remove { } }

        public string Path { get; } = path;

        public bool Stopped { get; private set; }

        public AudioPlayerState State { get; private set; } = AudioPlayerState.Stopped;

        public double PositionSec => 0;

        public float Volume { get; set; }

        public void Play() => State = AudioPlayerState.Playing;

        public void Pause() => State = AudioPlayerState.Paused;

        public void Stop()
        {
            Stopped = true;
            State = AudioPlayerState.Stopped;
        }

        public void Dispose() => State = AudioPlayerState.Stopped;
    }
}
