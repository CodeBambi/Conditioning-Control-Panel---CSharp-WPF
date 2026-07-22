// SP-029 Step-3 backend-event evidence harness (console, no pixels).
// Drives the REAL SoundArbitration on the REAL SoundFlow backend (SoundFlow 1.4.1,
// SP-017 selection). Every completion/interruption claim is a BACKEND EVENT
// (PlaybackEnded / state transitions), never call-return; volume is mechanism-only
// (no audibility claims). Transcript lines are the evidence.

using System.Diagnostics;
using CcpClient.Desktop.Audio;

var failures = new List<string>();
var passes = new List<string>();

void Pass(string what) { passes.Add(what); Console.WriteLine($"PASS: {what}"); }
void Fail(string what) { failures.Add(what); Console.WriteLine($"FAIL: {what}"); }
void Check(bool ok, string what) { if (ok) Pass(what); else Fail(what); }

// ---------- tones (spike ToneGen shape, self-contained copy — client/spikes is read-only) ----------
const int SampleRate = 48000;
static void WriteWav(string path, int durationMs, double freqHz, int fadeMs)
{
    var frames = SampleRate * durationMs / 1000;
    var fadeFrames = SampleRate * fadeMs / 1000;
    var pcm = new short[frames];
    for (var i = 0; i < frames; i++)
    {
        var t = (double)i / SampleRate;
        var env = 1.0;
        if (i < fadeFrames) env = (double)i / fadeFrames;
        else if (i > frames - fadeFrames) env = (double)(frames - i) / fadeFrames;
        pcm[i] = (short)(Math.Sin(2 * Math.PI * freqHz * t) * 0.6 * env * short.MaxValue);
    }
    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var bw = new BinaryWriter(fs);
    var dataLen = frames * 2;
    bw.Write("RIFF"u8); bw.Write(36 + dataLen); bw.Write("WAVE"u8);
    bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
    bw.Write(SampleRate); bw.Write(SampleRate * 2); bw.Write((short)2); bw.Write((short)16);
    bw.Write("data"u8); bw.Write(dataLen);
    foreach (var s in pcm) bw.Write(s);
}

var toneDir = Path.Combine(AppContext.BaseDirectory, "tones");
Directory.CreateDirectory(toneDir);
var voice = Path.Combine(toneDir, "voice-2500ms.wav");
var whisper = Path.Combine(toneDir, "whisper-1500ms.wav");
var sfx = Path.Combine(toneDir, "sfx-300ms.wav");
if (!File.Exists(voice)) WriteWav(voice, 2500, 440.0, 20);
if (!File.Exists(whisper)) WriteWav(whisper, 1500, 220.0, 50);
if (!File.Exists(sfx)) WriteWav(sfx, 300, 2000.0, 5);
Console.WriteLine($"tones: {toneDir}");

// ---------- recording probe duck sink (q1 named limit: cross-app sink not admitted — machinery evidence only) ----------
var probeApplies = new List<float>();
var probeRestores = 0;
var probeSink = new ProbeSink(probeApplies, () => probeRestores++);

var arb = new SoundArbitration(
    new SoundFlowAudioBackend(Console.WriteLine),
    probeSink,
    new SystemSoundClock(),
    new SoundArbitrationOptions { MaxSfxVoices = 8, DuckWatchdog = TimeSpan.FromSeconds(3), VoicePacingDelay = TimeSpan.FromSeconds(2) },
    m => Console.WriteLine($"arb: {m}"));

var voiceCompletions = new List<long>();
var whisperBusyEvents = new List<bool>();
arb.VoiceCompleted += g => { lock (voiceCompletions) voiceCompletions.Add(g); Console.WriteLine($"event: VoiceCompleted(gen={g})"); };
arb.WhisperBusyChanged += b => { lock (whisperBusyEvents) whisperBusyEvents.Add(b); Console.WriteLine($"event: WhisperBusyChanged({b})"); };

static bool WaitFor(Func<bool> cond, TimeSpan timeout, string what)
{
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
        if (cond()) return true;
        Thread.Sleep(25);
    }
    Console.WriteLine($"timeout waiting: {what}");
    return false;
}

// ---------- 1. device re-probe discipline (real backend) ----------
Console.WriteLine("=== 1. device layer (real backend, F1 re-probe) ===");
var devices = arb.EnumerateDevices();
Console.WriteLine($"session facts: {devices.Count} render endpoint(s): {string.Join(" | ", devices)}");
Check(devices.Count > 0, "render endpoints enumerated (session facts)");

var staleInit = arb.Initialize("SP029 Bogus Device (never exists)");
Check(staleInit is SoundOutcome.Ready, "stale NAME -> typed fallback to default (WPF AudioService.cs:292-293)");
Console.WriteLine($"active endpoint: {arb.ActiveDeviceName}");

// ---------- 2. voice natural completion (backend event + generation) ----------
Console.WriteLine("=== 2. voice natural completion ===");
var v1 = arb.PlayVoice(voice, 0.8f);
var gen1 = v1 is SoundOutcome.Started s1 ? s1.Generation : -1;
Check(v1 is SoundOutcome.Started, $"voice started (gen={gen1})");
Check(WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Contains(gen1); }, TimeSpan.FromSeconds(8), "VoiceCompleted gen1"),
    $"voice natural completion surfaced as VoiceCompleted(gen={gen1}) — backend PlaybackEnded, generation-filtered");

// ---------- 3. voice interruption: stop-replace, stale generation never completes ----------
Console.WriteLine("=== 3. voice stop-replace / generation ===");
lock (voiceCompletions) voiceCompletions.Clear();
var va = arb.PlayVoice(voice, 0.8f) as SoundOutcome.Started;
Thread.Sleep(400); // mid-play
var vb = arb.PlayVoice(voice, 0.8f) as SoundOutcome.Started;
Check(va is not null && vb is not null && vb.Generation > va.Generation, $"stop-replace newest-wins (gen {va?.Generation} -> {vb?.Generation})");
Check(WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Contains(vb!.Generation); }, TimeSpan.FromSeconds(8), "VoiceCompleted genB"),
    "replacement line completes naturally");
Check(WaitFor(() => true, TimeSpan.FromMilliseconds(300), "settle") && !(voiceCompletions.ToArray().Contains(va!.Generation)),
    $"interrupted gen {va?.Generation} NEVER surfaced as completion (F2 generation filter; SP-017 A2)");

// ---------- 4. whisper busy: real-event set/clear ----------
Console.WriteLine("=== 4. whisper busy (real event, replaces WPF duration estimate) ===");
lock (whisperBusyEvents) whisperBusyEvents.Clear();
var w = arb.PlayWhisper(whisper, 0.5f);
Check(w is SoundOutcome.Started && arb.WhisperBusy, "whisper playing, busy set at play");
Check(WaitFor(() => !arb.WhisperBusy, TimeSpan.FromSeconds(6), "whisper busy cleared"),
    "whisper busy cleared ONLY by the real completion event (spike A5 shape)");
lock (whisperBusyEvents) Check(whisperBusyEvents.SequenceEqual(new[] { true, false }), "WhisperBusyChanged exactly [true, false]");

// ---------- 5. SFX pool: 8/8 overlap + drop-on-overflow at 9th (typed + logged) ----------
Console.WriteLine("=== 5. SFX pool 8 + overflow ===");
var sfxOutcomes = Enumerable.Range(0, 9).Select(_ => arb.PlaySfx(sfx, 0.6f)).ToArray();
Check(sfxOutcomes.Take(8).All(o => o is SoundOutcome.Started), "8/8 SFX overlap started (bounded pool 8, SP-025 decree)");
Check(sfxOutcomes[8] is SoundOutcome.Dropped { Reason: SoundDropReason.PoolOverflow },
    "9th cue DROPPED typed PoolOverflow, never queued (ChaosSfx.cs:91-107 parity)");
Console.WriteLine($"pool after burst: {arb.ActiveSfxVoices}/8");
Check(WaitFor(() => arb.ActiveSfxVoices == 0, TimeSpan.FromSeconds(5), "sfx pool drain"),
    "pool slots reclaimed by real PlaybackEnded events (8 -> 0)");

// ---------- 6. off-sync-context construction on the REAL backend (SP-025 wedge class) ----------
Console.WriteLine("=== 6. off-sync-context construction (real SoundFlow, never-pumping context) ===");
lock (voiceCompletions) voiceCompletions.Clear();
long genOsc = -1;
var oscThread = new Thread(() =>
{
    SynchronizationContext.SetSynchronizationContext(new NeverPumping());
    var o = arb.PlayVoice(voice, 0.8f); // SoundFlow AssetDataProvider ctor = sync-over-async; on this thread it deadlocks WITHOUT the marshal
    if (o is SoundOutcome.Started so) genOsc = so.Generation;
});
oscThread.Start();
Check(oscThread.Join(TimeSpan.FromSeconds(15)), "PlayVoice from a never-pumping-sync-context thread completed — no deadlock (SP-025 regression, real backend)");
Check(genOsc > 0 && WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Contains(genOsc); }, TimeSpan.FromSeconds(8), "VoiceCompleted osc"),
    $"off-context-constructed player plays and completes (gen={genOsc})");

// ---------- 7. ducking machinery under real playback (probe sink; cross-app sink = named limit) ----------
Console.WriteLine("=== 7. ducking (machinery on probe sink, under real playback) ===");
arb.PlayVoice(voice, 0.8f); // real playback underneath
var d1 = arb.AcquireDuck(0.8f);
var d2 = arb.AcquireDuck(0.5f);
Check(d1.Held && d2.Held && arb.DuckCount == 2, "refcount 0->1->2 under real voice playback");
Check(probeApplies.SequenceEqual(new[] { 0.8f }), "sink applied once at first holder's strength (WPF :774-778)");
d1.Handle!.Dispose();
Check(arb.DuckCount == 1 && probeRestores == 0, "overlapping release: count 2->1, NO restore (WPF :900-906)");
d2.Handle!.Dispose();
Check(arb.DuckCount == 0 && probeRestores == 1, "last release restores exactly once — Apply/Restore symmetric");
WaitFor(() => arb.ActiveSfxVoices == 0, TimeSpan.FromSeconds(3), "settle");

// watchdog on real timer (harness-shortened 3 s; WPF DuckWatchdogMs 300_000)
var dw = arb.AcquireDuck(0.8f);
Check(dw.Held && WaitFor(() => arb.DuckCount == 0 && probeRestores == 2, TimeSpan.FromSeconds(8), "watchdog"),
    "watchdog force-unduck on real clock (mechanism; WPF 5-min policy)");
dw.Handle!.Dispose(); // stale-generation release ignored
Check(probeRestores == 2, "stale handle release ignored after watchdog (WPF :892-898)");

// panic release-all
var p1 = arb.AcquireDuck(0.8f); var p2 = arb.AcquireDuck(0.8f); var p3 = arb.AcquireDuck(0.8f);
arb.ForceUnduck();
Check(arb.DuckCount == 0 && probeRestores == 3, "ForceUnduck panic release-all: 3 overlapping holders, exactly ONE restore (WPF :1024-1033)");
p1.Handle!.Dispose(); p2.Handle!.Dispose(); p3.Handle!.Dispose();
Check(probeRestores == 3, "post-panic handle releases are stale no-ops");

// ---------- 8. error mid-play -> typed outcome + panic cleanup, no wedged players ----------
Console.WriteLine("=== 8. error -> typed outcome + panic cleanup ===");
lock (voiceCompletions) voiceCompletions.Clear();
arb.PlayVoice(voice, 0.8f);
arb.PlayWhisper(whisper, 0.5f);
for (var i = 0; i < 8; i++) arb.PlaySfx(sfx, 0.6f);
arb.QueueVoice(voice, 0.8f);
arb.QueueVoice(voice, 0.8f);
arb.AcquireDuck(0.8f);
var missing = arb.PlayVoice(Path.Combine(toneDir, "does-not-exist.wav"), 0.8f);
Check(missing is SoundOutcome.Failed, $"missing file -> typed Failed, never silent ({(missing as SoundOutcome.Failed)?.Error.Split(':')[0]})");
Console.WriteLine($"pre-panic: sfx={arb.ActiveSfxVoices} queued={arb.QueuedVoiceCount} ducks={arb.DuckCount} whisperBusy={arb.WhisperBusy}");

var proc = Process.GetCurrentProcess();
var handlesBefore = proc.HandleCount;
var threadsBefore = proc.Threads.Count;

arb.PanicReset();
Check(arb.ActiveSfxVoices == 0 && arb.QueuedVoiceCount == 0 && arb.DuckCount == 0 && !arb.WhisperBusy,
    "panic cleanup: every channel released, queue cleared, ducks force-released, busy cleared — no wedged state");
Thread.Sleep(700); // any late backend events land here
Check(!voiceCompletions.ToArray().Any(), "late backend events after panic are stale no-ops (generation bump; callback-race safe)");

// recovery after panic
var rec = arb.PlayVoice(voice, 0.8f) as SoundOutcome.Started;
Check(rec is not null && WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Contains(rec.Generation); }, TimeSpan.FromSeconds(8), "recovery completion"),
    "channels recover after panic (voice plays + completes)");

arb.PanicReset();
arb.Dispose();

var handlesAfter = proc.HandleCount;
var threadsAfter = proc.Threads.Count;
Console.WriteLine($"teardown: handles {handlesBefore} -> {handlesAfter} (delta {handlesAfter - handlesBefore}), threads {threadsBefore} -> {threadsAfter} (delta {threadsAfter - threadsBefore})");
Check(Math.Abs(handlesAfter - handlesBefore) < 64 && threadsAfter - threadsBefore < 16, "teardown leak counts bounded (SP-017 discipline)");

// ---------- summary ----------
Console.WriteLine("=== SUMMARY ===");
Console.WriteLine($"PASS {passes.Count} / FAIL {failures.Count}");
foreach (var f in failures) Console.WriteLine($"  FAILED: {f}");
Environment.Exit(failures.Count == 0 ? 0 : 1);

sealed class NeverPumping : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) { /* never pumped — the SP-025 wedge condition */ }
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
}

sealed class ProbeSink(List<float> applies, Action onRestore) : IAudioDuckSink
{
    public bool TryApply(float strength, out string? error) { applies.Add(strength); error = null; return true; }
    public void Restore() => onRestore();
}
