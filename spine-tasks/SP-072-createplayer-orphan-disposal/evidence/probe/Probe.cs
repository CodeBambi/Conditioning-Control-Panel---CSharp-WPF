using CcpClient.Desktop.Audio;

// SP-072 pre-fix probe (NOT a committed test — a bounded observation, run once, output
// captured in ../pre-fix-observation.txt). Drives the seam a headless run can reach:
// SoundArbitration.PlaySfx with a FakeBackend whose CreatePlayer parks (the wedged
// AssetDataProvider stand-in). The real backends cannot be constructed headless, so the
// real MasterMixer.AddComponent line (SoundFlowAudioBackend.cs:118,
// SoundFlowDtrhAudio.cs:112) is verified by reading only — the difference is named in
// record.md. What this probe captures about TODAY's code:
//   (1) the caller CANNOT stop waiting — CreatePlayer has no bound, the calling thread
//       blocks inside the construction for as long as the native call is wedged;
//   (2) when the wedge clears, the construction proceeds to attachment and playback even
//       though its moment has passed — nothing anywhere disposes or refuses it;
//   (3) read-only half of the observation: both CreatePlayerCore bodies call
//       _device.MasterMixer.AddComponent(player) UNCONDITIONALLY before returning, so a
//       hypothetical caller that had stopped waiting would get a ghost play + a leak whose
//       disposal races SP-071's backgrounded device teardown.

var log = new List<string>();
var backend = new ParkingBackend();
var arb = new SoundArbitration(
    backend, new UnavailableDuckSink(), new SystemSoundClock(),
    new SoundArbitrationOptions(), log.Add);

var init = arb.Initialize(null);
Console.WriteLine($"init: {init.GetType().Name}");

backend.Park = new ManualResetEventSlim(false); // the wedged native construction

SoundOutcome? outcome = null;
var caller = new Thread(() =>
{
    var started = Environment.TickCount64;
    outcome = arb.PlaySfx("probe.mp3", 0.5f);
    Console.WriteLine($"caller: PlaySfx returned {outcome.GetType().Name} after {Environment.TickCount64 - started}ms (only because the probe released the wedge)");
})
{ IsBackground = true, Name = "probe-caller" };
caller.Start();

// Bounded sample of an UNBOUNDED block: the caller is still parked 2s in (a real wedged
// endpoint parks it forever — SP-071's dead-endpoint window).
Thread.Sleep(2000);
Console.WriteLine($"observation @2s: caller thread alive={caller.IsAlive} (blocked inside CreatePlayer — no bound exists), players constructed={backend.Players.Count}");

Console.WriteLine("probe: releasing the wedge NOW — the moment for this sound passed 2s ago");
backend.Park.Set();
caller.Join();

var player = backend.Players.Single();
Console.WriteLine($"after release: outcome={outcome!.GetType().Name}, player.Playing={player.Playing}, player.Stopped={player.Stopped}, player.Disposed={player.Disposed}");
Console.WriteLine("VERDICT: a construction that completes after its moment passes is attached and played TODAY;");
Console.WriteLine("a caller that had stopped waiting would leave it attached, playing, and never disposed (orphan).");

arb.Dispose();

internal sealed class ParkingBackend : IAudioBackend
{
    public ManualResetEventSlim? Park { get; set; }
    public List<ProbePlayer> Players { get; } = [];

    public IReadOnlyList<string> EnumerateDevices() => ["Probe Sink"];

    public bool TryInit(string? deviceName, out string? error)
    {
        error = null;
        return true;
    }

    public IAudioPlayer CreatePlayer(string path, float volume)
    {
        Park?.Wait(); // the wedged AssetDataProvider ctor stand-in
        var p = new ProbePlayer();
        lock (Players) Players.Add(p);
        return p;
    }

    public void Dispose() { }
}

internal sealed class ProbePlayer : IAudioPlayer
{
    public event EventHandler? PlaybackEnded { add { } remove { } }
    public AudioPlayerState State => Playing ? AudioPlayerState.Playing : AudioPlayerState.Stopped;
    public double PositionSec => 0;
    public float Volume { get; set; }
    public bool Playing { get; private set; }
    public bool Stopped { get; private set; }
    public bool Disposed { get; private set; }
    public void Play() => Playing = true;
    public void Pause() { }
    public void Stop() { Playing = false; Stopped = true; }
    public void Dispose() => Disposed = true;
}
