// SP-071 pre-fix RED probe (NOT a committed test): a backend parked inside TryInit while
// another thread calls SoundArbitration.Dispose. Pre-fix expectation: Dispose does NOT
// return while the native call is parked (it blocks on _initLock, SoundArbitration.cs
// :1087-1091) — the unbounded UI-thread block this packet removes.
using CcpClient.Desktop.Audio;

var initEntered = new ManualResetEventSlim(false);
var releaseInit = new ManualResetEventSlim(false);

var backend = new ParkingBackend(initEntered, releaseInit);
var arb = new SoundArbitration(
    backend,
    new NullDuckSink(),
    new SystemSoundClock(),
    new SoundArbitrationOptions(),
    Console.WriteLine);

// Bring the device layer into "probe in flight": a second Initialize (the recovery probe's
// own path, RunRecoveryProbe -> Initialize -> _initLock) parks inside the native call.
var probe = new Thread(() => arb.Initialize(null)) { IsBackground = true, Name = "probe-sim" };
probe.Start();
if (!initEntered.Wait(TimeSpan.FromSeconds(10)))
{
    Console.WriteLine("PROBE-ERROR: fake never entered TryInit");
    return 2;
}

// The UI-thread caller (DtrhHostWindow close handler -> TeardownBarkPipeline -> Dispose).
var disposeReturned = new ManualResetEventSlim(false);
var ui = new Thread(() => { arb.Dispose(); disposeReturned.Set(); })
    { IsBackground = true, Name = "ui-sim" };
ui.Start();

var returned = disposeReturned.Wait(TimeSpan.FromSeconds(3));
Console.WriteLine(returned
    ? "GREEN: Dispose returned while the native call was parked"
    : "RED: Dispose did NOT return within 3s while TryInit was parked — the UI thread is wedged on _initLock (SoundArbitration.Dispose)");
releaseInit.Set();
ui.Join(TimeSpan.FromSeconds(10));
return returned ? 0 : 1;

sealed class ParkingBackend(ManualResetEventSlim entered, ManualResetEventSlim release) : IAudioBackend
{
    public IReadOnlyList<string> EnumerateDevices() => ["Fake Endpoint"];

    public bool TryInit(string? deviceName, out string? error)
    {
        entered.Set();
        release.Wait(); // the wedged native call — parks until released
        error = null;
        return true;
    }

    public IAudioPlayer CreatePlayer(string path, float volume) => throw new NotSupportedException();
    public void Dispose() { }
}

sealed class NullDuckSink : IAudioDuckSink
{
    public bool TryApply(float strength, out string? error) { error = null; return true; }
    public void Restore() { }
}
