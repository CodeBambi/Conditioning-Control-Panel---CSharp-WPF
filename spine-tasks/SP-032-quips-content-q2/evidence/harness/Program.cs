// SP-032 Step-3 backend-event evidence harness (console, no pixels).
// Drives the REAL BarkPipeline on the REAL SoundArbitration over the REAL SoundFlow
// backend (SoundFlow 1.4.1, SP-017 selection). Every completion/interruption claim is a
// BACKEND EVENT (PlaybackEnded / generation-filtered VoiceCompleted), never call-return;
// volume is mechanism-only (no audibility claims); Linux = mechanism facts, NO timing
// claims (WSLg jitter). Transcript lines are the evidence.

using System.Diagnostics;
using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Companion;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

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
var bark1 = Path.Combine(toneDir, "bark1.wav");
var bark2 = Path.Combine(toneDir, "bark2.wav");
var voice = Path.Combine(toneDir, "voice-2500ms.wav");
var whisper = Path.Combine(toneDir, "whisper-1500ms.wav");
var sfx = Path.Combine(toneDir, "sfx-300ms.wav");
if (!File.Exists(bark1)) WriteWav(bark1, 1200, 330.0, 20);
if (!File.Exists(bark2)) WriteWav(bark2, 1000, 550.0, 20);
if (!File.Exists(voice)) WriteWav(voice, 2500, 440.0, 20);
if (!File.Exists(whisper)) WriteWav(whisper, 1500, 220.0, 50);
if (!File.Exists(sfx)) WriteWav(sfx, 300, 2000.0, 5);
Console.WriteLine($"tones: {toneDir}");

var proc = Process.GetCurrentProcess();
var handlesBefore = proc.HandleCount;
var threadsBefore = proc.Threads.Count;
Console.WriteLine($"baseline: handles={handlesBefore} threads={threadsBefore}");

// ---------- real pipeline on the real arbitration + real backend ----------
var storeDir = Path.Combine(AppContext.BaseDirectory, "state");
Directory.CreateDirectory(storeDir);
var storePath = Path.Combine(storeDir, "companion.json");
if (File.Exists(storePath)) File.Delete(storePath);

var backend = new SoundFlowAudioBackend(m => Console.WriteLine($"backend: {m}"));
var arb = new SoundArbitration(
    backend, new UnavailableDuckSink(), new SystemSoundClock(), new SoundArbitrationOptions(),
    m => Console.WriteLine($"arb: {m}"));
var voiceCompletions = new List<long>();
arb.VoiceCompleted += g => { lock (voiceCompletions) voiceCompletions.Add(g); Console.WriteLine($"event: VoiceCompleted(gen={g})"); };
arb.WhisperBusyChanged += b => Console.WriteLine($"event: WhisperBusyChanged({b})");

var store = new PersistenceStore<CompanionStateDocument>(
    new OperationRegistry().OwnerFor("HarnessCompanion"),
    new HarnessLogSink(),
    storePath,
    CompanionStateDocument.CurrentSchemaVersion);
store.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

var rules = BarkRuleLoader.Parse("""
    [
      {
        "id": "harness_ordinary", "trigger": "Ordinary", "priority": 0, "cooldown_ms": 0,
        "mood": "calm", "class": "normal",
        "variant_pool": [
          { "text": "ordinary line one for {who}", "audio": "bark1.wav" },
          { "text": "ordinary text-only line" }
        ]
      },
      {
        "id": "harness_priority", "trigger": "PriorityBark", "priority": 150, "cooldown_ms": 0,
        "mood": "sharp", "class": "normal",
        "variant_pool": [ { "text": "priority line", "audio": "bark2.wav" } ]
      },
      {
        "id": "harness_safety", "trigger": "Panic", "priority": 1000, "cooldown_ms": 0,
        "class": "safety",
        "variant_pool": [ { "text": "safety line", "audio": "bark2.wav" } ]
      }
    ]
    """, m => Console.WriteLine($"rules: {m}"));

// Harness-fast gate windows via the options seam (WPF policy values unchanged in product defaults).
var pipelineOptions = new BarkPipelineOptions
{
    GlobalMinGap = TimeSpan.FromMilliseconds(200),
    SafetyHold = TimeSpan.FromMilliseconds(500),
    MinSpeechDelaySeconds = 0.3,
    Rng = static () => 0.0,
};
var pipeline = new BarkPipeline(
    arb, store, new MapResolver(new Dictionary<string, string> { ["bark1.wav"] = bark1, ["bark2.wav"] = bark2 }),
    rules, pipelineOptions, m => Console.WriteLine($"pipeline: {m}"));
var surfacedPayloads = new List<BarkPayload>();
pipeline.BarkSurfaced += p => { lock (surfacedPayloads) surfacedPayloads.Add(p); };

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

// ---------- 1. device layer: typed stale-NAME fallback, then Ready ----------
var stale = arb.Initialize("SP032 Bogus Device (never exists)");
Check(stale is SoundOutcome.Ready, "stale device NAME → typed fallback to default → Ready (AudioService.cs:292-293 parity)");
var endpoints = arb.EnumerateDevices();
Console.WriteLine($"session facts: {endpoints.Count} render endpoint(s): {string.Join(" | ", endpoints)}");

// ---------- 2. ordinary bark: payload integrity + backend-event completion ----------
var o1 = pipeline.Raise("Ordinary", new Dictionary<string, object?> { ["who"] = "harness" });
Check(o1 is BarkOutcome.Surfaced, "ordinary bark → Surfaced (voice handed to arbitration)");
BarkPayload? p1;
lock (surfacedPayloads) p1 = surfacedPayloads.LastOrDefault();
Check(p1 is not null
    && p1.Text == "ordinary line one for harness"
    && p1.AudioPath == bark1
    && p1.EmotionLineId == "bark1"
    && p1.Mood == "calm"
    && p1.LineId == "Bark:harness_ordinary:bark1",
    "payload integrity: text/audio/emotion/mood/line-id as ONE unit (never torn)");
Check(WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Count >= 1; }, TimeSpan.FromSeconds(10), "bark voice completion"),
    "bark voice completed via BACKEND EVENT (never call-return)");

// ---------- 3. pacing: the next AUDIO variant hands to the voice channel behind the TEXT-derived debt ----------
Thread.Sleep(250); // let the harness-short min-gap (200ms) elapse
// Rotation-aware: the 2-variant pool alternates audio/text-only (deterministic Rng). A
// text-only pick is the typed NoAudioAsset degradation (an evidence point, never silent)
// — raise again; the recycle reseeds to the audio variant for the pacing check.
var o2 = pipeline.Raise("Ordinary", guaranteed: true);
if (o2 is BarkOutcome.SurfacedTextOnly { Reason: BarkSuppression.NoAudioAsset })
{
    Pass("text-only variant picked by rotation → typed SurfacedTextOnly(NoAudioAsset) (never silent)");
    o2 = pipeline.Raise("Ordinary", guaranteed: true);
}
Check(o2 is BarkOutcome.Surfaced { QueueDepth: 1 } or BarkOutcome.Surfaced { QueueDepth: 0 },
    "second audio bark → handed (queued or immediate per the pacing debt)");
Check(WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Count >= 2; }, TimeSpan.FromSeconds(10), "second bark completion"),
    "second bark completed after the pacing debt (ordering fact, no timing claim)");

// ---------- 4. priority preempt during voice: interrupted gen NEVER completes ----------
Thread.Sleep(250);
var beforePreempt = pipeline.Raise("Ordinary", guaranteed: true); // starts a line (gen N)
if (beforePreempt is BarkOutcome.SurfacedTextOnly { Reason: BarkSuppression.NoAudioAsset })
{
    beforePreempt = pipeline.Raise("Ordinary", guaranteed: true); // rotation alternate → audio variant
}
Check(beforePreempt is BarkOutcome.Surfaced, "ordinary line started for the preempt setup");
Thread.Sleep(150); // let it start playing
var completionsBeforePreempt = voiceCompletions.Count;
var pr = pipeline.Raise("PriorityBark", guaranteed: true);
Check(pr is BarkOutcome.Surfaced { Priority: true }, "priority bark (>= 100) → preempt (clear queue + play now, Speech.cs:319-360)");
Check(WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Count == completionsBeforePreempt + 1; }, TimeSpan.FromSeconds(10), "priority completion"),
    "priority line completed; the interrupted ordinary NEVER surfaced as completion (F2 generation filter)");

// ---------- 5. whisper gate (real-event busy) ----------
Check(arb.PlayWhisper(whisper, 0.5f) is SoundOutcome.Started, "whisper started");
Check(arb.WhisperBusy, "WhisperBusy set at play");
var gated = pipeline.Raise("Ordinary", guaranteed: false);
Check(gated is BarkOutcome.Gated { Reason: "whisper-active" }, "bark gated while whisper busy (typed whisper-active; guaranteed=false path)");
Check(WaitFor(() => !arb.WhisperBusy, TimeSpan.FromSeconds(10), "whisper completion"),
    "WhisperBusy cleared ONLY by the real completion event (spike A5 shape)");

// ---------- 6. mute text-only (typed, surfaced, never silent) ----------
Thread.Sleep(250);
pipeline.Muted = true;
var muted = pipeline.Raise("Ordinary", guaranteed: true);
Check(muted is BarkOutcome.SurfacedTextOnly { Reason: BarkSuppression.Muted }, "muted → SurfacedTextOnly(Muted) — typed");
BarkPayload? mp;
lock (surfacedPayloads) mp = surfacedPayloads.LastOrDefault();
Check(mp is not null && mp.Text.StartsWith("ordinary"), "muted bark STILL surfaced the payload (never silent — the text-only mode is surfaced)");
Check(!arb.VoiceActive, "muted bark made NO voice call");
pipeline.Muted = false;

// ---------- 7. rapid SFX burst under voice + whisper (coexistence, A11 class) ----------
Check(arb.PlayVoice(voice, 0.8f) is SoundOutcome.Started, "coexistence: voice started (2500ms)");
Check(arb.PlayWhisper(whisper, 0.5f) is SoundOutcome.Started, "coexistence: whisper started (1500ms)");
var sfxOutcomes = new List<SoundOutcome>();
for (var i = 0; i < 12; i++)
{
    sfxOutcomes.Add(arb.PlaySfx(sfx, 0.9f)); // the rapid-click-cue class: bursts through the pool
}

var started = sfxOutcomes.Count(o => o is SoundOutcome.Started);
var overflow = sfxOutcomes.Count(o => o is SoundOutcome.Dropped { Reason: SoundDropReason.PoolOverflow });
Check(started == 8 && overflow == 4,
    $"rapid cue burst under voice+whisper: 8 started / 4 typed PoolOverflow (actual {started}/{overflow}) — never silent");
var completionsBeforeCoexist = voiceCompletions.Count;
Check(WaitFor(() => !arb.WhisperBusy, TimeSpan.FromSeconds(10), "whisper completion under burst"),
    "coexistence: whisper completed naturally under the SFX burst (no starvation)");
Check(WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Count == completionsBeforeCoexist + 1; }, TimeSpan.FromSeconds(10), "voice completion under burst"),
    "coexistence: voice completed naturally under the SFX burst (no starvation)");
Check(WaitFor(() => arb.ActiveSfxVoices == 0, TimeSpan.FromSeconds(10), "sfx pool drain"),
    "coexistence: SFX pool drained to 0 on real backend events");

// ---------- 8. disabled-phrase round-trip on the REAL store ----------
var ordRule = rules.Single(r => r.Id == "harness_ordinary");
foreach (var v in ordRule.VariantPool)
{
    pipeline.DisablePhrase(BarkText.LineId(ordRule.Id, v));
}

Check(pipeline.Raise("Ordinary", guaranteed: true) is BarkOutcome.Gated { Reason: "empty-pool" },
    "all variants disabled → typed empty-pool gate (WPF :1172-1180)");
pipeline.FlushAsync().GetAwaiter().GetResult();
var store2 = new PersistenceStore<CompanionStateDocument>(
    new OperationRegistry().OwnerFor("HarnessCompanion2"),
    new HarnessLogSink(),
    storePath,
    CompanionStateDocument.CurrentSchemaVersion);
store2.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
Check(store2.Current.DisabledPhraseIds.Count == 2, "disabled ids round-tripped through the SP-005 store (schema-versioned, atomic)");

// ---------- 9. panic cleanup + recovery ----------
arb.PlayVoice(voice, 0.8f);
arb.PlayWhisper(whisper, 0.5f);
arb.PlaySfx(sfx, 0.9f);
Thread.Sleep(150);
arb.PanicReset();
Check(!arb.VoiceActive && !arb.WhisperBusy && arb.ActiveSfxVoices == 0,
    "panic reset: every channel stopped, busy cleared, pool empty (typed, logged)");
var completionsBeforeRecovery = voiceCompletions.Count;
var recovery = arb.PlayVoice(bark2, 0.5f);
Check(recovery is SoundOutcome.Started, "channels recover after panic (voice plays again)");
Check(WaitFor(() => { lock (voiceCompletions) return voiceCompletions.Count == completionsBeforeRecovery + 1; }, TimeSpan.FromSeconds(10), "post-panic completion"),
    "post-panic voice completed via backend event");

// ---------- teardown leak counts (SP-017 discipline) ----------
pipeline.FlushAsync().GetAwaiter().GetResult();
arb.Dispose();
GC.Collect();
GC.WaitForPendingFinalizers();
Thread.Sleep(500);
var handlesAfter = proc.HandleCount;
var threadsAfter = proc.Threads.Count;
Console.WriteLine($"teardown: handles {handlesBefore}→{handlesAfter} (delta {handlesAfter - handlesBefore}), threads {threadsBefore}→{threadsAfter} (delta {threadsAfter - threadsBefore})");
Check(Math.Abs(handlesAfter - handlesBefore) <= 5 && Math.Abs(threadsAfter - threadsBefore) <= 5,
    "teardown bounded (handles/threads delta ≤ 5)");

Console.WriteLine();
Console.WriteLine($"RESULT: {passes.Count} PASS, {failures.Count} FAIL");
return failures.Count == 0 ? 0 : 1;

internal sealed class MapResolver(Dictionary<string, string> map) : IBarkAudioResolver
{
    public string? Resolve(string audioFileName) => map.TryGetValue(audioFileName, out var path) ? path : null;
}

internal sealed class HarnessLogSink : ILogSink
{
    public void Log(string message) => Console.WriteLine($"store: {message}");
}
