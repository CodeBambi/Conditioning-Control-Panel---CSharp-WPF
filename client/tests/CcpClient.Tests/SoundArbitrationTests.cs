using System.Collections.Concurrent;
using CcpClient.Desktop.Audio;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-029 slice q1: arbitration core — channel ownership (voice stop-replace + generation,
/// whisper real-event busy, SFX pool 8 drop-on-overflow), queue ordering + freshness,
/// ducking refcount symmetry (overlapping + watchdog + panic release-all), device re-probe,
/// off-sync-context construction (SP-025 regression), panic cleanup. All against RECORDING
/// FAKES + a manual clock — never the real SoundFlow backend (backend-event evidence = Step 3
/// console harness). WPF parity cites per test (SP-029 record.md Step 1 archaeology).
/// </summary>
public sealed class SoundArbitrationTests
{
    private readonly List<string> _log = [];

    private (SoundArbitration arb, FakeBackend backend, FakeDuckSink duck, ManualClock clock) Make(
        int maxSfx = 8, string[]? devices = null, TimeSpan? teardownBudget = null)
    {
        var backend = new FakeBackend { Devices = devices ?? ["RDP Sink"] };
        var duck = new FakeDuckSink();
        var clock = new ManualClock();
        var arb = new SoundArbitration(backend, duck, clock, new SoundArbitrationOptions
        {
            MaxSfxVoices = maxSfx,
            DuckWatchdog = TimeSpan.FromMinutes(5),
            VoicePacingDelay = TimeSpan.FromSeconds(2),
            // Budgets whose elapsing is NOT the subject get the shared 60s injection (SP-063);
            // the SP-071 give-up facts pass their own short literal (elapsing IS the subject).
            TeardownBudget = teardownBudget ?? TestWait.InjectedBudget,
        }, _log.Add);
        return (arb, backend, duck, clock);
    }

    private static SoundArbitration Initialized(SoundArbitration arb)
    {
        var outcome = arb.Initialize(null);
        Assert.IsType<SoundOutcome.Ready>(outcome);
        return arb;
    }

    // ---------- voice ownership: stop-replace + generation ----------

    [Fact]
    public void Voice_StopReplace_StaleGenerationDiscarded()
    {
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        var completed = new List<long>();
        arb.VoiceCompleted += completed.Add;

        var a = arb.PlayVoice("a.mp3", 1f);
        var genA = Assert.IsType<SoundOutcome.Started>(a).Generation;
        var playerA = backend.Players[^1];

        var b = arb.PlayVoice("b.mp3", 1f);
        var genB = Assert.IsType<SoundOutcome.Started>(b).Generation;
        var playerB = backend.Players[^1];

        Assert.True(playerA.Stopped && playerA.Disposed); // stop-replace newest-wins (Speech.cs:473,:1594)
        Assert.True(playerB.Playing);
        Assert.NotEqual(genA, genB);

        playerA.RaiseEnded(); // stale generation: never surfaces as completion (F2 class, :1623-1632)
        Assert.Empty(completed);

        playerB.RaiseEnded(); // current generation natural end
        Assert.Equal([genB], completed);
        Assert.True(playerB.Disposed);
    }

    [Fact]
    public void Voice_PriorityPreempt_ClearsQueue_PlaysNow()
    {
        var (arb, backend, _, _) = Make();
        Initialized(arb);

        arb.PlayVoice("current.mp3", 1f);
        arb.QueueVoice("q1.mp3", 1f);
        arb.QueueVoice("q2.mp3", 1f);
        Assert.Equal(2, arb.QueuedVoiceCount);

        var outcome = arb.PlayVoicePriority("pri.mp3", 1f);
        Assert.IsType<SoundOutcome.Started>(outcome);
        Assert.Equal(0, arb.QueuedVoiceCount); // clear-all + play-now (Speech.cs:319-360)
        Assert.Contains(_log, l => l.Contains("cleared (2)") && l.Contains("preempt"));
        Assert.True(backend.Players[^1].Path == "pri.mp3" && backend.Players[^1].Playing);
    }

    // ---------- whisper: real-event busy ----------

    [Fact]
    public void Whisper_BusySetCleared_ByRealEventOnly()
    {
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        var busy = new List<bool>();
        arb.WhisperBusyChanged += busy.Add;

        arb.PlayWhisper("w1.mp3", 0.5f);
        Assert.True(arb.WhisperBusy);
        Assert.Equal([true], busy);
        var w1 = backend.Players[^1];

        arb.PlayWhisper("w2.mp3", 0.5f); // stop-replace; busy stays set, no duplicate event
        Assert.True(arb.WhisperBusy);
        Assert.Equal([true], busy);
        var w2 = backend.Players[^1];
        Assert.True(w1.Stopped && w1.Disposed);

        w1.RaiseEnded(); // stale generation cannot clear busy
        Assert.True(arb.WhisperBusy);

        w2.RaiseEnded(); // the REAL completion event clears busy (replaces WPF duration estimate, AudioService.cs:750-758)
        Assert.False(arb.WhisperBusy);
        Assert.Equal([true, false], busy);
    }

    // ---------- SFX pool: bounded, drop-on-overflow typed ----------

    [Fact]
    public void Sfx_PoolBounded_DropOnOverflowTyped()
    {
        var (arb, backend, _, _) = Make(maxSfx: 8);
        Initialized(arb);

        for (var i = 0; i < 8; i++)
        {
            Assert.IsType<SoundOutcome.Started>(arb.PlaySfx($"s{i}.wav", 1f));
        }
        Assert.Equal(8, arb.ActiveSfxVoices);

        var ninth = arb.PlaySfx("s9.wav", 1f);
        var dropped = Assert.IsType<SoundOutcome.Dropped>(ninth); // drop, never queue (ChaosSfx.cs:91-107)
        Assert.Equal(SoundDropReason.PoolOverflow, dropped.Reason);
        Assert.Contains(_log, l => l.Contains("pool full (8)") && l.Contains("dropping"));
        Assert.Equal(8, backend.Players.Count); // no 9th player constructed
    }

    [Fact]
    public void Sfx_PoolReclaims_OnRealPlaybackEnded()
    {
        var (arb, backend, _, _) = Make(maxSfx: 1);
        Initialized(arb);

        arb.PlaySfx("a.wav", 1f);
        Assert.IsType<SoundOutcome.Dropped>(arb.PlaySfx("b.wav", 1f));

        backend.Players[0].RaiseEnded(); // backend completion event reclaims the slot
        Assert.Equal(0, arb.ActiveSfxVoices);
        Assert.True(backend.Players[0].Disposed);

        Assert.IsType<SoundOutcome.Started>(arb.PlaySfx("c.wav", 1f));
    }

    // ---------- queue: ordering, pacing, freshness ----------

    [Fact]
    public void Voice_QueueFifo_PacingBetweenLines()
    {
        var (arb, backend, _, clock) = Make();
        Initialized(arb);

        arb.PlayVoice("first.mp3", 1f);
        var q1 = arb.QueueVoice("q1.mp3", 1f);
        var q2 = arb.QueueVoice("q2.mp3", 1f);
        Assert.Equal(1, Assert.IsType<SoundOutcome.Queued>(q1).Depth);
        Assert.Equal(2, Assert.IsType<SoundOutcome.Queued>(q2).Depth);

        backend.Players[^1].RaiseEnded(); // first ends; pacing debt = 2 s (Speech.cs:112-119) — nothing starts yet
        Assert.DoesNotContain(backend.Players, p => p.Path == "q1.mp3");

        clock.Advance(TimeSpan.FromSeconds(2)); // pacing elapsed → q1 starts
        Assert.Equal("q1.mp3", backend.Players[^1].Path);
        Assert.True(backend.Players[^1].Playing);

        backend.Players[^1].RaiseEnded();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("q2.mp3", backend.Players[^1].Path); // FIFO order preserved
    }

    [Fact]
    public void Voice_QueueFreshness_StaleDroppedTyped()
    {
        var (arb, backend, _, clock) = Make();
        Initialized(arb);

        arb.PlayVoice("first.mp3", 1f);
        arb.QueueVoice("stale.mp3", 1f, freshness: TimeSpan.FromSeconds(1)); // caller-supplied window (mechanism; WPF has no ms-age expiry — policy = q2)
        arb.QueueVoice("fresh.mp3", 1f);

        backend.Players[^1].RaiseEnded();
        clock.Advance(TimeSpan.FromSeconds(5)); // both pacing elapsed AND stale.mp3 expired

        Assert.Equal("fresh.mp3", backend.Players[^1].Path); // stale skipped, fresh started
        Assert.Contains(_log, l => l.Contains("stale"));
    }

    // ---------- ducking: refcount symmetry, watchdog, panic ----------

    [Fact]
    public void Duck_RefCount_OverlappingHolders_Symmetric()
    {
        var (arb, _, duck, _) = Make();
        Initialized(arb);

        var h1 = arb.AcquireDuck(0.8f);
        var h2 = arb.AcquireDuck(0.5f);
        Assert.True(h1.Held && h2.Held);
        Assert.Equal(2, arb.DuckCount);
        Assert.Equal([0.8f], duck.Applies); // first holder's strength wins (AudioService.cs:778); overlapping holders bump the count (:774-776)

        h1.Handle!.Dispose();
        Assert.Equal(1, arb.DuckCount);
        Assert.Empty(duck.Restores); // restore only when the last holder releases (:900-906)

        h2.Handle!.Dispose();
        Assert.Equal(0, arb.DuckCount);
        Assert.Single(duck.Restores);
    }

    [Fact]
    public void Duck_ApplyFailure_NotHeld_Symmetric()
    {
        var (arb, _, duck, _) = Make();
        duck.ApplyOk = false;

        var attempt = arb.AcquireDuck(0.8f);
        Assert.False(attempt.Held); // duck failure decrements back (AudioService.cs:869-873)
        Assert.NotNull(attempt.Error);
        Assert.Equal(0, arb.DuckCount);

        duck.ApplyOk = true;
        Assert.True(arb.AcquireDuck(0.8f).Held); // recoverable
    }

    [Fact]
    public void Duck_Watchdog_ForceUnduck_StaleHandleIgnored()
    {
        var (arb, _, duck, clock) = Make();
        Initialized(arb);

        var h = arb.AcquireDuck(0.8f);
        Assert.True(h.Held);

        clock.Advance(TimeSpan.FromMinutes(5)); // DuckWatchdogMs 300_000 (AudioService.cs:39)
        Assert.Equal(0, arb.DuckCount);
        Assert.Single(duck.Restores);
        Assert.Contains(_log, l => l.Contains("watchdog"));

        h.Handle!.Dispose(); // stale-generation release ignored (:892-898) — no double restore
        Assert.Single(duck.Restores);
    }

    [Fact]
    public void Duck_ForceUnduck_PanicReleaseAll_ExactlyOneRestore()
    {
        var (arb, _, duck, _) = Make();
        Initialized(arb);

        var h1 = arb.AcquireDuck(0.8f);
        var h2 = arb.AcquireDuck(0.8f);
        var h3 = arb.AcquireDuck(0.8f);

        arb.ForceUnduck(); // WPF ForceUnduck :1024-1033 — panic key / app exit
        Assert.Equal(0, arb.DuckCount);
        Assert.Single(duck.Restores);

        h1.Handle!.Dispose(); h2.Handle!.Dispose(); h3.Handle!.Dispose(); // all stale — ignored
        Assert.Single(duck.Restores);

        arb.ForceUnduck(); // idempotent
        Assert.Single(duck.Restores);
    }

    [Fact]
    public void Duck_RestoreFailure_StaysRecoverable_NeverRatchet()
    {
        var (arb, _, duck, clock) = Make();
        Initialized(arb);

        var h = arb.AcquireDuck(0.8f);
        duck.RestoreThrows = true;
        h.Handle!.Dispose(); // restore throws → state preserved, recoverable (AudioService.cs:1003-1016)
        Assert.True(arb.DuckCount > 0);
        Assert.Single(duck.Restores); // attempted

        duck.RestoreThrows = false;
        clock.Advance(TimeSpan.FromMinutes(5)); // re-armed watchdog retries the restore
        Assert.Equal(2, duck.Restores.Count);
        Assert.Equal(0, arb.DuckCount);
    }

    // ---------- device re-probe ----------

    [Fact]
    public void Device_NameMatched_PassedToBackend()
    {
        var (arb, backend, _, _) = Make(devices: ["RDP Sink", "Speakers (Realtek)"]);
        var outcome = arb.Initialize("Speakers (Realtek)");
        Assert.Equal("Speakers (Realtek)", Assert.IsType<SoundOutcome.Ready>(outcome).DeviceName);
        Assert.Equal("Speakers (Realtek)", backend.RequestedDeviceName); // NAME, never an Id (F1)
    }

    [Fact]
    public void Device_StaleName_TypedFallbackToDefault()
    {
        var (arb, backend, _, _) = Make(devices: ["RDP Sink"]);
        var outcome = arb.Initialize("Unplugged USB Headset");
        Assert.IsType<SoundOutcome.Ready>(outcome); // missing → default (AudioService.cs:292-293)
        Assert.Null(backend.RequestedDeviceName);
        Assert.Contains(_log, l => l.Contains("not in fresh enumeration") && l.Contains("falling back to default"));
    }

    [Fact]
    public void Device_NoEndpoints_AudioDisabledForSession()
    {
        var (arb, backend, _, _) = Make(devices: []);
        var outcome = arb.Initialize(null);
        Assert.IsType<SoundOutcome.Unavailable>(outcome); // WPF :129-131
        Assert.True(arb.AudioDisabledForSession);

        var play = arb.PlayVoice("a.mp3", 1f); // typed, no player constructed
        Assert.IsType<SoundOutcome.Unavailable>(play);
        Assert.Empty(backend.Players);
    }

    [Fact]
    public void Device_SetPreferred_StopsChannels_ReInits()
    {
        var (arb, backend, _, _) = Make(devices: ["RDP Sink", "Speakers (Realtek)"]);
        Initialized(arb);
        arb.PlayVoice("a.mp3", 1f);
        arb.PlayWhisper("w.mp3", 1f);

        var outcome = arb.SetPreferredDevice("Speakers (Realtek)");
        Assert.IsType<SoundOutcome.Ready>(outcome);
        Assert.All(backend.Players, p => Assert.True(p.Stopped && p.Disposed)); // mid-play switch = named limit 10; channels stopped typed
        Assert.False(arb.WhisperBusy);
        Assert.Equal("Speakers (Realtek)", backend.RequestedDeviceName);
    }

    // ---------- SP-070: the session-disable EXPIRES (WPF d33b5d8d, #778/#779) ----------

    [Fact]
    public void Recovery_EndpointReturns_AfterCooldownNextPlay_PlaysAgain()
    {
        // The user story as a fact — no real device, no real time: zero endpoints at init →
        // play refused → endpoint appears → after the cooldown the play-attempt path recovers.
        var (arb, backend, _, clock) = Make(devices: []);
        Assert.IsType<SoundOutcome.Unavailable>(arb.Initialize(null));
        Assert.True(arb.AudioDisabledForSession);
        Assert.Equal(1, backend.EnumerateCallCount); // one enumeration per Initialize call
        Assert.Equal(0, backend.InitCallCount); // zero endpoints — TryInit never reached

        var refused = Assert.IsType<SoundOutcome.Unavailable>(arb.PlayVoice("a.mp3", 1f));
        Assert.Contains("re-probe after cooldown", refused.Reason); // cooldown unexpired — honest state
        Assert.Empty(backend.Players);

        backend.Devices = ["RDP Sink"]; // the endpoint comes back
        clock.Advance(TimeSpan.FromSeconds(31)); // cooldown expires; nothing fires without a play attempt
        Assert.True(arb.AudioDisabledForSession);
        Assert.Equal(1, backend.EnumerateCallCount);

        var discovering = Assert.IsType<SoundOutcome.Unavailable>(arb.PlayVoice("a.mp3", 1f));
        Assert.Contains("re-probe in flight", discovering.Reason); // discovering caller refused typed, never blocked
        Assert.Equal(1, backend.EnumerateCallCount); // probe scheduled, not yet run (single-flight one-shot)

        clock.Advance(TimeSpan.Zero); // ManualClock fires the scheduled probe
        Assert.False(arb.AudioDisabledForSession);
        Assert.Equal(2, backend.EnumerateCallCount);
        Assert.Equal(1, backend.InitCallCount);
        Assert.Null(backend.RequestedDeviceName); // remembered preferred NAME (null = default), never an Id
        Assert.Contains(_log, l => l.Contains("output recovered — playback resumed"));

        Assert.IsType<SoundOutcome.Started>(arb.PlayVoice("a.mp3", 1f)); // audio plays again
        Assert.True(backend.Players[^1].Playing);
    }

    [Fact]
    public void Recovery_FailureCounting_EscalatesAtThreshold_SuccessResets()
    {
        var (arb, backend, _, clock) = Make(devices: []);
        arb.Initialize(null); // streak 1 (healthy→suppressed trip line)
        Assert.Equal(1, _log.Count(l => l.Contains("audio suppressed")));

        FailProbes(arb, clock, 3); // streak 2..4 — no escalation yet
        Assert.Equal(0, _log.Count(l => l.Contains("still down after")));
        Assert.Equal(4, backend.EnumerateCallCount);

        FailProbes(arb, clock, 1); // streak 5 == threshold — ONE escalation line
        Assert.Equal(1, _log.Count(l => l.Contains("still down after 5 consecutive")));
        Assert.Equal(5, backend.EnumerateCallCount);

        FailProbes(arb, clock, 1); // streak 6 — escalation does NOT repeat (transition-only)
        Assert.Equal(1, _log.Count(l => l.Contains("still down after")));

        backend.Devices = ["RDP Sink"];
        clock.Advance(TimeSpan.FromSeconds(31));
        arb.PlayVoice("a.mp3", 1f); // kick
        clock.Advance(TimeSpan.Zero); // probe succeeds — success RESETS the streak
        Assert.False(arb.AudioDisabledForSession);

        backend.Devices = [];
        arb.Initialize(null); // one failure after the reset: streak restarts at 1...
        Assert.True(arb.AudioDisabledForSession);
        Assert.Equal(2, _log.Count(l => l.Contains("audio suppressed"))); // ...a fresh trip transition...
        Assert.Equal(1, _log.Count(l => l.Contains("still down after"))); // ...and NO new escalation (would need 5 more)
    }

    [Fact]
    public void Recovery_CooldownEnforcedBeforeAttempt_InitCountDoesNotMove()
    {
        var (arb, backend, _, clock) = Make(devices: []);
        arb.Initialize(null);
        Assert.Equal(1, backend.EnumerateCallCount);

        for (var i = 0; i < 20; i++) // hammer the play seam across channels, cooldown unexpired
        {
            Assert.IsType<SoundOutcome.Unavailable>(arb.PlayVoice($"v{i}.mp3", 1f));
            Assert.IsType<SoundOutcome.Unavailable>(arb.PlayWhisper($"w{i}.mp3", 1f));
            Assert.IsType<SoundOutcome.Unavailable>(arb.PlaySfx($"s{i}.mp3", 1f));
            Assert.IsType<SoundOutcome.Unavailable>(arb.QueueVoice($"q{i}.mp3", 1f));
        }
        Assert.Equal(1, backend.EnumerateCallCount); // never a retry per play call
        Assert.Empty(backend.Players);

        clock.Advance(TimeSpan.FromSeconds(15)); // still inside the 30s window
        Assert.IsType<SoundOutcome.Unavailable>(arb.PlayVoice("v.mp3", 1f));
        Assert.Equal(1, backend.EnumerateCallCount);
    }

    [Fact]
    public void Recovery_SingleFlight_ConcurrentAttempts_OneInitCall()
    {
        var (arb, backend, _, clock) = Make(devices: []);
        arb.Initialize(null);
        clock.Advance(TimeSpan.FromSeconds(31)); // cooldown expired

        // Concurrent play attempts across every channel — all refused, exactly ONE schedule wins.
        // (Refused plays never reach CreatePlayer, so the recording lists stay single-threaded;
        // the kick itself is serialized under the arbitration gate.)
        Parallel.For(0, 32, i =>
        {
            switch (i % 4)
            {
                case 0: arb.PlayVoice($"v{i}.mp3", 1f); break;
                case 1: arb.PlayWhisper($"w{i}.mp3", 1f); break;
                case 2: arb.PlaySfx($"s{i}.mp3", 1f); break;
                default: arb.QueueVoice($"q{i}.mp3", 1f); break;
            }
        });
        Assert.Equal(1, backend.EnumerateCallCount); // 32 concurrent attempts, at most one schedule

        clock.Advance(TimeSpan.Zero); // the scheduled probe(s) fire with the endpoint STILL DOWN —
        Assert.Equal(2, backend.EnumerateCallCount); // N concurrent attempts → exactly one backend init
        Assert.True(arb.AudioDisabledForSession);

        // ...and the single-flight probe still recovers when the endpoint returns.
        backend.Devices = ["RDP Sink"];
        clock.Advance(TimeSpan.FromSeconds(31));
        arb.PlayVoice("kick.mp3", 1f);
        clock.Advance(TimeSpan.Zero);
        Assert.False(arb.AudioDisabledForSession);
        Assert.Equal(3, backend.EnumerateCallCount);
    }

    [Fact]
    public void Recovery_RepeatedFailure_ExactlyOneAttemptPerCooldownWindow()
    {
        var (arb, backend, _, clock) = Make(devices: []);
        arb.Initialize(null);

        for (var window = 0; window < 3; window++)
        {
            for (var i = 0; i < 10; i++) // window armed but UNEXPIRED: in-window hammering adds NOTHING
            {
                arb.PlayVoice($"v{i}.mp3", 1f);
                arb.PlaySfx($"s{i}.mp3", 1f);
            }
            Assert.Equal(1 + window, backend.EnumerateCallCount);

            clock.Advance(TimeSpan.FromSeconds(31)); // the window expires
            arb.PlayVoice("kick.mp3", 1f); // the one trigger this window
            Assert.Equal(1 + window, backend.EnumerateCallCount); // probe scheduled, not fired
            clock.Advance(TimeSpan.FromSeconds(1)); // probe fires, fails, re-arms the window
            Assert.Equal(2 + window, backend.EnumerateCallCount); // exactly one attempt per window
        }
        Assert.True(arb.AudioDisabledForSession);
        Assert.Equal(4, backend.EnumerateCallCount); // startup + 3 windows — no busy loop
    }

    [Fact]
    public void Recovery_Panic_NothingResurrected_ExplicitStopUntouched()
    {
        var (arb, backend, _, clock) = Make(devices: []);
        arb.Initialize(null);
        clock.Advance(TimeSpan.FromSeconds(31));
        backend.Devices = ["RDP Sink"];
        arb.PlayVoice("before.mp3", 1f); // kick
        clock.Advance(TimeSpan.Zero); // probe succeeds
        Assert.False(arb.AudioDisabledForSession);

        arb.PlayVoice("line.mp3", 1f);
        var playing = backend.Players[^1];
        Assert.True(playing.Playing);

        arb.PanicReset(); // panic owns the players; recovery must never touch what it cleared
        Assert.True(playing.Stopped && playing.Disposed);

        // Force suppression again and recover: nothing panic cleared comes back.
        backend.Devices = [];
        arb.Initialize(null);
        Assert.True(arb.AudioDisabledForSession);
        clock.Advance(TimeSpan.FromSeconds(31));
        backend.Devices = ["RDP Sink"];
        arb.PlayVoice("kick2.mp3", 1f);
        clock.Advance(TimeSpan.Zero);
        Assert.False(arb.AudioDisabledForSession);

        Assert.True(playing.Stopped && playing.Disposed); // the panicked player was NOT resurrected
        Assert.Equal(0, arb.QueuedVoiceCount);
        var restarted = Assert.IsType<SoundOutcome.Started>(arb.PlayVoice("new.mp3", 1f));
        var newPlayer = backend.Players[^1];
        Assert.NotSame(playing, newPlayer); // a post-recovery play constructs a NEW player through the normal seam
        Assert.True(newPlayer.Playing);
        _ = restarted;
    }

    [Fact]
    public void Recovery_Teardown_NoProbeAfterDispose_Ever()
    {
        var (arb, backend, _, clock) = Make(devices: []);
        arb.Initialize(null);
        clock.Advance(TimeSpan.FromSeconds(31));
        arb.PlayVoice("kick.mp3", 1f); // schedule a probe

        arb.Dispose(); // teardown cancels the pending probe
        backend.Devices = ["RDP Sink"];
        clock.Advance(TimeSpan.FromSeconds(60)); // the cancelled probe never fires
        Assert.Equal(1, backend.EnumerateCallCount);

        var after = Assert.IsType<SoundOutcome.Unavailable>(arb.PlayVoice("x.mp3", 1f));
        Assert.Contains("torn down", after.Reason); // honest state, and...
        Assert.Equal(1, backend.EnumerateCallCount); // ...no re-probe after teardown, ever
    }

    [Fact]
    public void Recovery_HealthySession_NoExtraDeviceCalls_NoNewLogLines()
    {
        // Negative control: nothing fails → byte-for-byte unaffected. One init, one
        // enumeration, zero recovery state transitions or log lines.
        var (arb, backend, _, clock) = Make();
        Initialized(arb);
        Assert.Equal(1, backend.InitCallCount);
        Assert.Equal(1, backend.EnumerateCallCount);

        arb.PlayVoice("a.mp3", 1f);
        arb.PlayWhisper("w.mp3", 1f);
        arb.PlaySfx("s.mp3", 1f);
        arb.QueueVoice("q.mp3", 1f);
        clock.Advance(TimeSpan.FromMinutes(10)); // pacing fires; clock far past any window
        arb.PanicReset();

        Assert.Equal(1, backend.InitCallCount); // no probe, ever
        Assert.Equal(1, backend.EnumerateCallCount); // no extra device call
        Assert.False(arb.AudioDisabledForSession);
        Assert.DoesNotContain(_log, l => l.Contains("re-probe") || l.Contains("recovered") || l.Contains("suppressed") || l.Contains("still down"));
    }

    [Fact]
    public void Recovery_ProbeThrows_DegradesTyped_FlagClears_NoEscape()
    {
        // Exception-proofing pin (pre-approach consult finding): a backend that THROWS from
        // TryInit on the timer thread must degrade to a typed failed probe — the single-flight
        // flag always clears and the window re-arms, never a stuck-true permanence.
        var (arb, backend, _, clock) = Make(devices: []);
        arb.Initialize(null);

        backend.Devices = ["RDP Sink"];
        backend.ThrowOnTryInit = true;
        clock.Advance(TimeSpan.FromSeconds(31));
        arb.PlayVoice("kick.mp3", 1f);
        clock.Advance(TimeSpan.Zero); // probe throws internally — nothing escapes the scheduled callback
        Assert.True(arb.AudioDisabledForSession); // still suppressed, window re-armed (streak 2)
        Assert.Equal(2, backend.EnumerateCallCount);

        clock.Advance(TimeSpan.FromSeconds(31));
        arb.PlayVoice("kick2.mp3", 1f); // flag cleared → a later window can probe again
        backend.ThrowOnTryInit = false;
        clock.Advance(TimeSpan.Zero);
        Assert.False(arb.AudioDisabledForSession); // and recovery still lands
        Assert.Equal(3, backend.EnumerateCallCount);
    }

    // ---------- SP-071: teardown off the UI thread (host close must not wait on a wedged probe) ----------

    // The give-up facts inject a 200ms budget whose ELAPSING is the subject (TestWait
    // population 2). Every rendezvous is a deterministic signal: the probe is PROVEN parked
    // (TryInitInFlight) before Dispose is called, so the budget always expires with the
    // native call still in flight — the outcome never depends on scheduler timing.
    private static readonly TimeSpan GiveUpBudget = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Drive the REAL probe path (suppressed → kick → the scheduled one-shot fires on a
    /// background thread) into a backend whose TryInit parks until released. Returns with
    /// the probe PROVEN parked inside the native call, holding `_initLock` — exactly the
    /// dead-endpoint window the DTRH host close can hit.
    /// </summary>
    private (SoundArbitration arb, FakeBackend backend, Thread probe, ManualResetEventSlim probeDone) ParkedProbe()
    {
        var (arb, backend, _, clock) = Make(devices: [], teardownBudget: GiveUpBudget);
        Assert.IsType<SoundOutcome.Unavailable>(arb.Initialize(null)); // endpoint down — suppressed
        backend.Devices = ["RDP Sink"];
        backend.TryInitRelease = new ManualResetEventSlim();
        clock.Advance(TimeSpan.FromSeconds(31)); // cooldown expires
        arb.PlayVoice("kick.mp3", 1f); // schedule the single-flight re-probe

        var probeDone = new ManualResetEventSlim();
        var probe = new Thread(() => { clock.Advance(TimeSpan.Zero); probeDone.Set(); })
            { IsBackground = true, Name = "test-probe" };
        probe.Start();
        TestWait.UntilSync(
            () => backend.TryInitInFlight,
            "the recovery probe parked inside the native init (fixture never reached the mechanism)",
            () => $"initCalls={backend.InitCallCount} enumerateCalls={backend.EnumerateCallCount}");
        return (arb, backend, probe, probeDone);
    }

    private static Thread RunDispose(SoundArbitration arb, ManualResetEventSlim returned)
        => new(() => { arb.Dispose(); returned.Set(); }) { IsBackground = true, Name = "test-dispose" };

    private static string TeardownState(FakeBackend backend)
        => $"inFlight={backend.TryInitInFlight} disposeCalls={backend.DisposeCallCount} events=[{string.Join(",", backend.NativeEvents)}]";

    [Fact]
    public void Teardown_ProbeParked_DisposeReturnsBounded_GiveUpLogged_BackendUntouched()
    {
        // The defect pin (pre-fix RED captured in evidence/pre-fix-red.txt): with a native
        // init parked, Dispose must return within the budget, log ONE typed give-up line,
        // and the give-up path must NEVER touch the backend.
        var (arb, backend, probe, probeDone) = ParkedProbe();
        try
        {
            var disposeReturned = new ManualResetEventSlim();
            RunDispose(arb, disposeReturned).Start();
            TestWait.UntilSync(
                () => disposeReturned.IsSet,
                "Dispose returns bounded while the native call is parked",
                () => TeardownState(backend));
            Assert.True(backend.TryInitInFlight); // the native call is STILL parked...
            Assert.Equal(0, backend.DisposeCallCount); // ...and the give-up never touched the backend
            Assert.Equal(1, _log.Count(l => l.Contains("teardown exceeds"))); // ONE typed give-up line
        }
        finally
        {
            backend.TryInitRelease!.Set(); // unwedge the fixture whatever the verdict
        }
        TestWait.UntilSync(() => probeDone.IsSet, "probe thread drains after release", () => TeardownState(backend));
        probe.Join();
    }

    [Fact]
    public void Teardown_ProbeParked_BackendDisposedOnlyAfterNativeCallReturns()
    {
        // The safety fact: the backend is NEVER disposed while a native call is in flight.
        // Asserted as ORDERING (the fake records when its TryInit returns and when Dispose
        // is called on it), not merely that nothing threw.
        var (arb, backend, probe, probeDone) = ParkedProbe();
        var disposeReturned = new ManualResetEventSlim();
        try
        {
            RunDispose(arb, disposeReturned).Start();
            // No assertion on HOW the caller returned (bounded give-up = the defect pin
            // above); this pin's subject is the ORDER the fake records.
            TestWait.UntilSync(() => disposeReturned.IsSet, "Dispose returned", () => TeardownState(backend));
        }
        finally
        {
            backend.TryInitRelease!.Set();
        }
        TestWait.UntilSync(() => probeDone.IsSet && backend.DisposeCallCount == 1, "teardown completes after the native call returns", () => TeardownState(backend));
        probe.Join();
        Assert.False(backend.DisposedWhileInitInFlight); // never concurrent with the native call
        var events = backend.NativeEvents.ToArray();
        Assert.True(
            Array.IndexOf(events, "init-returned") < Array.IndexOf(events, "backend-disposed"),
            $"the native init returned BEFORE the backend was disposed — events: {string.Join(",", events)}");
    }

    [Fact]
    public void Teardown_GiveUp_BackgroundCompletes_ExactlyOneDispose_CompletionLogged()
    {
        // The completion fact: after the caller gives up, the backgrounded teardown still
        // runs and disposes the backend EXACTLY ONCE when the native call finally returns —
        // the host can be closed and reopened, so the give-up must not leak.
        var (arb, backend, probe, probeDone) = ParkedProbe();
        var disposeReturned = new ManualResetEventSlim();
        try
        {
            RunDispose(arb, disposeReturned).Start();
            TestWait.UntilSync(() => disposeReturned.IsSet, "caller gives up bounded", () => TeardownState(backend));
            Assert.Equal(1, _log.Count(l => l.Contains("teardown exceeds")));
        }
        finally
        {
            backend.TryInitRelease!.Set();
        }
        TestWait.UntilSync(() => probeDone.IsSet && backend.DisposeCallCount == 1, "backgrounded teardown disposes exactly once", () => TeardownState(backend));
        probe.Join();
        Assert.Equal(1, backend.DisposeCallCount); // exactly once — never zero (leak), never two
        Assert.Equal(1, _log.Count(l => l.Contains("backgrounded backend teardown completed"))); // the transition pair
    }

    [Fact]
    public void Dispose_TwiceWhileTeardownParked_OneBackendDispose_OneTeardown()
    {
        // Idempotence: two Dispose calls → ONE backend dispose, ONE teardown; the second
        // returns promptly (it never waits on the parked native call).
        var (arb, backend, probe, probeDone) = ParkedProbe();
        var firstReturned = new ManualResetEventSlim();
        var secondReturned = new ManualResetEventSlim();
        try
        {
            RunDispose(arb, firstReturned).Start();
            TestWait.UntilSync(() => firstReturned.IsSet, "first Dispose gives up bounded", () => TeardownState(backend));
            RunDispose(arb, secondReturned).Start();
            TestWait.UntilSync(() => secondReturned.IsSet, "second Dispose returns promptly", () => TeardownState(backend));
            Assert.Equal(0, backend.DisposeCallCount);
            Assert.Equal(1, _log.Count(l => l.Contains("teardown exceeds"))); // the second call logged nothing
        }
        finally
        {
            backend.TryInitRelease!.Set();
        }
        TestWait.UntilSync(() => probeDone.IsSet && backend.DisposeCallCount == 1, "single teardown disposes once", () => TeardownState(backend));
        probe.Join();
        Assert.Equal(1, backend.DisposeCallCount); // one dispose across TWO Dispose calls
        Assert.Equal(1, _log.Count(l => l.Contains("backgrounded backend teardown completed")));
    }

    [Fact]
    public void Dispose_NoProbeInFlight_DisposesOnce_NoGiveUpLines()
    {
        // Negative control — ordinary teardown is unchanged: no probe in flight → the
        // backend is disposed exactly once with the same observable outcome as before
        // SP-071 (panic line as before, channels stopped) and NO give-up/completion lines.
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        arb.PlayVoice("v.mp3", 1f);
        var player = backend.Players[^1];

        arb.Dispose();

        Assert.Equal(1, backend.DisposeCallCount);
        Assert.True(player.Stopped && player.Disposed); // PanicReset still ran on the caller
        Assert.DoesNotContain(_log, l => l.Contains("teardown exceeds") || l.Contains("backgrounded backend teardown"));
        Assert.Contains(_log, l => l.Contains("panic-reset")); // the pre-SP-071 observable line
    }

    /// <summary>Run <paramref name="count"/> failed re-probes (each: expire window → play kicks → advance fires the failed probe).</summary>
    private static void FailProbes(SoundArbitration arb, ManualClock clock, int count)
    {
        for (var i = 0; i < count; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            arb.PlayVoice("kick.mp3", 1f); // kick the single-flight probe
            clock.Advance(TimeSpan.FromSeconds(1)); // probe fires and fails (devices still [])
        }
    }

    // ---------- off-sync-context construction (SP-025 regression) ----------

    [Fact]
    public void OffSyncContext_SyncContextThread_DoesNotDeadlock_RunsOffContext()
    {
        // The wedge condition must be installed on a DEDICATED thread — never on an xUnit
        // runner thread (a never-pumping context on a shared runner thread swallows the
        // runner's own posted continuations and wedges the whole test process).
        int? workThread = null;
        var t = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverPumpingSyncContext());
            var result = OffSyncContext.Run(() =>
            {
                // OffSyncContext must marshal AWAY from the never-pumping context; inside
                // the work there is no context and no deadlock (SP-025 AssetDataProvider class).
                Assert.Null(SynchronizationContext.Current);
                workThread = Environment.CurrentManagedThreadId;
                return 42;
            });
            Assert.Equal(42, result);
        });
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(10)), "deadlocked on a sync-context thread — the SP-025 wedge class");
        Assert.True(workThread is not null && workThread != t.ManagedThreadId); // marshaled to a context-free thread
    }

    [Fact]
    public void OffSyncContext_NoContext_RunsInline()
    {
        var thread = Environment.CurrentManagedThreadId;
        var ran = OffSyncContext.Run(() => Environment.CurrentManagedThreadId);
        Assert.Equal(thread, ran);
    }

    // ---------- panic cleanup ----------

    [Fact]
    public void PanicReset_ReleasesEverything_NoWedgedPlayers_Idempotent()
    {
        var (arb, backend, duck, clock) = Make();
        Initialized(arb);
        var busy = new List<bool>();
        arb.WhisperBusyChanged += busy.Add;

        arb.PlayVoice("v.mp3", 1f);
        arb.PlayWhisper("w.mp3", 0.5f);
        arb.PlaySfx("s1.wav", 1f);
        arb.PlaySfx("s2.wav", 1f);
        arb.QueueVoice("q.mp3", 1f);
        arb.AcquireDuck(0.8f);
        arb.AcquireDuck(0.8f);
        var playerCount = backend.Players.Count;

        arb.PanicReset();

        Assert.All(backend.Players, p => Assert.True(p.Stopped && p.Disposed)); // no wedged players
        Assert.Equal(0, arb.ActiveSfxVoices);
        Assert.Equal(0, arb.QueuedVoiceCount);
        Assert.Equal(0, arb.DuckCount);
        Assert.Single(duck.Restores);
        Assert.False(arb.WhisperBusy);
        Assert.Equal([true, false], busy);
        Assert.Contains(_log, l => l.Contains("panic-reset") && l.Contains(playerCount.ToString()));

        // Callback-race safety: late backend events after panic are stale no-ops.
        var survivors = backend.Players.ToArray();
        foreach (var p in survivors)
        {
            p.RaiseEnded();
        }
        Assert.Equal(0, arb.ActiveSfxVoices);
        Assert.False(arb.WhisperBusy);

        arb.PanicReset(); // idempotent
        Assert.Single(duck.Restores);

        // Channels recover after panic (voice still usable; queued line plays after pacing).
        var again = arb.PlayVoice("v2.mp3", 1f);
        Assert.IsType<SoundOutcome.Started>(again);
    }

    // ---------- panic/start race guard (pre-completion consult finding 2026-07-22) ----------

    [Fact]
    public void Play_ThrowsOnDisposedPlayer_TypedFailed_NeverUnhandled()
    {
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        backend.PlayerHook = p => p.ThrowOnPlay = true; // simulates Play() on a player panic already disposed

        var voice = arb.PlayVoice("v.mp3", 1f);
        Assert.IsType<SoundOutcome.Failed>(voice); // typed, logged — never an unhandled throw
        Assert.Contains(_log, l => l.Contains("start failed"));

        var sfx = arb.PlaySfx("s.wav", 1f);
        Assert.IsType<SoundOutcome.Failed>(sfx);
        Assert.Equal(0, arb.ActiveSfxVoices); // pool membership rolled back — no phantom voice
    }

    [Fact]
    public void Play_PanicRacesStart_NoWedgedPlayer_ChannelsClean()
    {
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        // Compressed race: PanicReset lands DURING Play() (the real window is install→Play).
        backend.PlayerHook = p => p.OnPlay = () => arb.PanicReset();

        arb.PlayVoice("v.mp3", 1f);
        Assert.All(backend.Players, p => Assert.True(p.Stopped && p.Disposed)); // ownership re-check caught the panic-taken player

        arb.PlaySfx("s.wav", 1f);
        Assert.All(backend.Players, p => Assert.True(p.Stopped && p.Disposed));
        Assert.Equal(0, arb.ActiveSfxVoices);
    }

    // ---------- SP-072: an abandoned player construction never reaches the mixer ----------

    // The budget whose ELAPSING is the subject (TestWait population 2 — same pinned-literal
    // discipline as SP-071's GiveUpBudget). Every rendezvous below is a deterministic signal
    // (gates + the recorded event stream), never timing.
    private static readonly TimeSpan ConstructionBudget = TimeSpan.FromMilliseconds(200);

    private sealed class OrphanPlayer(string path, float volume)
    {
        public string Path { get; } = path;
        public float Volume { get; } = volume;
        public int PlayCount;
        public int DisposeCount;
        public void Play() => Interlocked.Increment(ref PlayCount);
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    /// <summary>
    /// Drives <see cref="OrphanSafePlayerFactory{TPlayer}"/> with recording delegates — the
    /// mechanism the real backends cannot exercise headless. The fake's own record is the
    /// assertion surface (never the absence of an exception). ConstructGate parked = the
    /// wedged AssetDataProvider stand-in, released by the test, never by timing.
    /// </summary>
    private sealed class OrphanHarness
    {
        public readonly List<string> Log = [];
        public readonly ConcurrentQueue<string> Events = new();
        public volatile ManualResetEventSlim? ConstructGate;
        public volatile ManualResetEventSlim? ConstructStarted;
        public volatile OrphanPlayer? LastPlayer;
        public int AttachCount;
        public int DisposeCount;
        public readonly OrphanSafePlayerFactory<OrphanPlayer> Factory;

        public OrphanHarness(TimeSpan budget, Action<string>? logHook = null)
        {
            Factory = new OrphanSafePlayerFactory<OrphanPlayer>(
                construct: (path, volume) =>
                {
                    ConstructStarted?.Set();
                    ConstructGate?.Wait(); // the wedge — released by the test, never by timing
                    Events.Enqueue("construct-returned");
                    var p = new OrphanPlayer(path, volume);
                    LastPlayer = p;
                    return p;
                },
                attach: p => { Interlocked.Increment(ref AttachCount); Events.Enqueue("attached"); },
                dispose: p => { p.Dispose(); Interlocked.Increment(ref DisposeCount); Events.Enqueue("orphan-disposed"); },
                log: line => { lock (Log) Log.Add(line); logHook?.Invoke(line); },
                tag: "test",
                budget: budget);
        }

        public int AbandonmentLines
        {
            get { lock (Log) return Log.Count(l => l.Contains("construction abandoned")); }
        }

        public string State() =>
            $"attach={AttachCount} dispose={DisposeCount} abandonmentLines={AbandonmentLines} events=[{string.Join(",", Events)}]";
    }

    private static (Thread Thread, ManualResetEventSlim Done, Func<Exception?> Thrown) StartCreate(
        OrphanSafePlayerFactory<OrphanPlayer> factory)
    {
        Exception? thrown = null;
        var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try { factory.Create("orphan.mp3", 0.5f); }
            catch (Exception ex) { thrown = ex; }
            finally { done.Set(); }
        })
        { IsBackground = true, Name = "test-create" };
        return (thread, done, () => thrown);
    }

    [Fact]
    public void Construction_Abandoned_NeverAttached_NeverPlayed_DisposedOnce()
    {
        // The orphan fact: a construction that completes AFTER its caller stopped waiting is
        // never attached (never reaches the mixer), never plays, and is disposed exactly once.
        var h = new OrphanHarness(ConstructionBudget) { ConstructGate = new ManualResetEventSlim(false) };
        var (thread, done, thrown) = StartCreate(h.Factory);
        thread.Start();

        TestWait.UntilSync(() => done.IsSet, "the caller's wait expired — typed abandonment", () => h.State());
        Assert.IsType<PlayerConstructionTimeoutException>(thrown()); // the typed no-player outcome
        Assert.Equal(0, h.AttachCount); // abandoned BEFORE it could reach the mixer
        Assert.Equal(1, h.AbandonmentLines); // ONE transition line, never per-call

        h.ConstructGate!.Set(); // the wedge clears LATE — the moment has passed
        TestWait.UntilSync(() => h.DisposeCount == 1, "the late completion disposes the orphan", () => h.State());
        thread.Join();

        var player = h.LastPlayer!;
        Assert.Equal(0, player.PlayCount); // never plays
        // Exactly once: the completion continuation is the ONLY armed disposer in this
        // scenario, and the observed dispose is its last act — the count can never change.
        Assert.Equal(1, player.DisposeCount);
        Assert.Equal(1, h.DisposeCount); // never zero (leak), never two
        Assert.Equal(0, h.AttachCount); // still never attached after the late completion
    }

    [Fact]
    public void Construction_CompletionRacesAbandonment_DisposedExactlyOnce()
    {
        // The exactly-once fact, at the race the latch exists for: BOTH orphan disposers are
        // armed — P4 (the completion continuation) AND P3 (the waiter's completed-check
        // disposer). Forced deterministically: the abandonment log runs ON the waiter thread;
        // the hook opens the wedge and holds the waiter until P4 has claimed the latch and
        // disposed — after which the task is PROVABLY complete, so the waiter's check
        // provably spawns P3. The latch must admit exactly one disposal.
        var constructGate = new ManualResetEventSlim(false);
        OrphanHarness? h = null;
        h = new OrphanHarness(ConstructionBudget, logHook: _ =>
        {
            constructGate.Set();
            TestWait.UntilSync(() => h!.DisposeCount == 1, "P4 claimed the latch and disposed the orphan", () => h!.State());
        })
        { ConstructGate = constructGate };
        var (thread, done, thrown) = StartCreate(h.Factory);
        thread.Start();

        TestWait.UntilSync(() => done.IsSet, "the caller's wait expired — typed abandonment", () => h.State());
        Assert.IsType<PlayerConstructionTimeoutException>(thrown());
        thread.Join();

        // Never zero: the UntilSync inside the hook already failed loudly if P4 never fired.
        // Never two: P3 was provably spawned (the waiter proceeded only after the task
        // completed); the latch is the only thing that can keep the count at 1 — and it is
        // timing-independent (CAS), so this green never depends on scheduling.
        Assert.Equal(1, h.DisposeCount);
        Assert.Equal(1, h.LastPlayer!.DisposeCount);
        Assert.Equal(0, h.AttachCount);
        Assert.Equal(0, h.LastPlayer.PlayCount);
        Assert.Equal(1, h.AbandonmentLines);
    }

    [Fact]
    public void Construction_OrphanDisposal_OrderedAgainstDeviceTeardown()
    {
        // The ordering fact: abandoned-player disposal NEVER overlaps device teardown
        // (SP-071 made that teardown concurrent). The teardown delegate parks INSIDE the
        // lifecycle lock (the wedged native device teardown); the late construction
        // completes during it; disposal must land strictly after teardown releases the lock.
        var h = new OrphanHarness(ConstructionBudget) { ConstructGate = new ManualResetEventSlim(false) };
        var (thread, done, thrown) = StartCreate(h.Factory);
        thread.Start();
        TestWait.UntilSync(() => done.IsSet, "the caller abandoned the construction", () => h.State());
        Assert.IsType<PlayerConstructionTimeoutException>(thrown());
        thread.Join();

        var teardownStarted = new ManualResetEventSlim();
        var teardownMayFinish = new ManualResetEventSlim();
        var disposeCountAtTeardownEnd = -1;
        var teardown = new Thread(() => h.Factory.Teardown(() =>
        {
            h.Events.Enqueue("teardown-start");
            teardownStarted.Set();
            teardownMayFinish.Wait(); // the wedged native teardown — holds the lifecycle lock
            disposeCountAtTeardownEnd = h.DisposeCount; // the ordering observation
            h.Events.Enqueue("teardown-end");
        }))
        { IsBackground = true, Name = "test-teardown" };
        teardown.Start();
        TestWait.UntilSync(() => teardownStarted.IsSet, "teardown holds the lifecycle lock", () => h.State());

        h.ConstructGate!.Set(); // the late completion lands DURING the wedged teardown
        TestWait.UntilSync(() => h.Events.Contains("construct-returned"), "the construction completed during teardown", () => h.State());
        // The orphan disposer claimed the latch and is blocked on the lifecycle lock — it
        // CANNOT have disposed (deterministic: disposal happens only under that lock).
        Assert.Equal(0, h.DisposeCount);

        teardownMayFinish.Set();
        TestWait.UntilSync(() => h.DisposeCount == 1, "the orphan is disposed after teardown released the lock", () => h.State());
        teardown.Join();

        // THE ordering assertion — absence of the event is a FAILURE (the UntilSync above),
        // never a vacuous pass; and the observation from inside the teardown proves
        // non-overlap directly.
        Assert.Equal(0, disposeCountAtTeardownEnd);
        Assert.Contains(h.Events, e => e == "teardown-start");
        Assert.Contains(h.Events, e => e == "construct-returned");
        Assert.Contains(h.Events, e => e == "teardown-end");
        Assert.Contains(h.Events, e => e == "orphan-disposed");
        Assert.Equal(0, h.AttachCount);
        Assert.Equal(1, h.AbandonmentLines);
    }

    [Fact]
    public void Construction_Ordinary_AttachedOnce_NeverDisposed_NoAbandonmentLine()
    {
        // Negative control: the non-abandoned path is observably unchanged — same object
        // returned, same path/volume passthrough, attached exactly once, never disposed, no
        // abandonment line. Budget elapsing is NOT the subject (the shared 60s injection).
        var h = new OrphanHarness(TestWait.InjectedBudget);
        var player = h.Factory.Create("ok.mp3", 0.7f);

        Assert.Same(h.LastPlayer, player);
        Assert.Equal("ok.mp3", player.Path);
        Assert.Equal(0.7f, player.Volume);
        Assert.Equal(1, h.AttachCount);
        Assert.Equal(0, h.DisposeCount);
        Assert.Equal(0, h.AbandonmentLines);
        Assert.Equal(["construct-returned", "attached"], h.Events.ToArray());
    }

    [Fact]
    public void Construction_TornDownDuringWait_DisposedOnce_TypedRefusal_NeverAttached()
    {
        // Teardown lands while a construction is in flight: the completed player is disposed
        // exactly once (the waiter path, under the lock), never attached, and the caller gets
        // the typed refusal. A Create AFTER teardown is refused before constructing.
        var h = new OrphanHarness(TestWait.InjectedBudget)
        {
            ConstructGate = new ManualResetEventSlim(false),
            ConstructStarted = new ManualResetEventSlim(false),
        };
        var (thread, done, thrown) = StartCreate(h.Factory);
        thread.Start();

        // The construction is PROVABLY parked inside the native call before teardown lands —
        // the in-flight window, never a pre-construction refusal.
        TestWait.UntilSync(() => h.ConstructStarted!.IsSet, "the construction is in flight", () => h.State());
        h.Factory.Teardown(() => h.Events.Enqueue("teardown-end"));
        h.ConstructGate!.Set();

        TestWait.UntilSync(() => done.IsSet, "the in-flight construction completes into a torn-down backend", () => h.State());
        thread.Join();
        Assert.IsType<InvalidOperationException>(thrown());
        Assert.Equal(1, h.DisposeCount); // the within-budget waiter-dispose path
        Assert.Equal(0, h.AttachCount);
        Assert.Equal(0, h.LastPlayer!.PlayCount);

        var refusal = Assert.Throws<InvalidOperationException>(() => h.Factory.Create("late.mp3", 1f));
        Assert.Contains("torn down", refusal.Message);
        Assert.Equal(1, h.DisposeCount); // the refused construction never ran
    }

    [Fact]
    public void PlaySfx_ConstructionTimeout_TypedFailed_NeverInPool()
    {
        // The bound's caller vocabulary (SP-072): the typed construction expiry rides the
        // EXISTING catch → SoundOutcome.Failed — no new refusal semantic, no silent player.
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        backend.ThrowOnCreatePlayer = new PlayerConstructionTimeoutException("test: wedged construction");

        var outcome = arb.PlaySfx("s.mp3", 1f);

        Assert.IsType<SoundOutcome.Failed>(outcome);
        Assert.Empty(backend.Players);
        Assert.Equal(0, arb.ActiveSfxVoices);
        Assert.Contains(_log, l => l.Contains("sfx player construction failed"));
    }

    [Fact]
    public void PlayVoice_ConstructionTimeout_TypedFailed_ChannelStaysIdle()
    {
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        backend.ThrowOnCreatePlayer = new PlayerConstructionTimeoutException("test: wedged construction");
        var completed = new List<long>();
        arb.VoiceCompleted += completed.Add;

        var outcome = arb.PlayVoice("v.mp3", 1f);

        Assert.IsType<SoundOutcome.Failed>(outcome);
        Assert.Empty(backend.Players);
        Assert.Empty(completed); // no phantom completion on a channel that never started
        Assert.Contains(_log, l => l.Contains("Voice player construction failed"));
    }

    // ---------- fakes ----------

    private sealed class NeverPumpingSyncContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { /* never pumped — the SP-025 wedge condition */ }
        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
    }

    private sealed class FakeBackend : IAudioBackend
    {
        public List<FakePlayer> Players { get; } = [];
        public string[] Devices { get; set; } = ["RDP Sink"];
        public string? RequestedDeviceName { get; private set; }
        public int InitCallCount { get; private set; }
        public int EnumerateCallCount { get; private set; }
        public string? TryInitError { get; set; }
        public bool ThrowOnTryInit { get; set; }
        public Action<FakePlayer>? PlayerHook { get; set; }
        // SP-072: inject a typed construction failure (the bound's expiry) at the seam.
        public Exception? ThrowOnCreatePlayer { get; set; }

        // SP-071 cross-thread teardown instrumentation. Both gates null = today's synchronous
        // behavior, so the landed facts are byte-identical. TryInitRelease parked = the wedged
        // native call; the fake RECORDS the moment TryInit returns and the moment Dispose is
        // called on it so the ordering fact asserts the order, not merely that nothing threw.
        public ManualResetEventSlim? TryInitRelease { get; set; }
        public volatile bool TryInitInFlight;
        public volatile bool DisposedWhileInitInFlight;
        public ConcurrentQueue<string> NativeEvents { get; } = new();
        private int _disposeCallCount;
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public IReadOnlyList<string> EnumerateDevices()
        {
            EnumerateCallCount++;
            return Devices;
        }

        public bool TryInit(string? deviceName, out string? error)
        {
            InitCallCount++;
            RequestedDeviceName = deviceName;
            if (TryInitRelease is { } release)
            {
                TryInitInFlight = true;
                release.Wait(); // the wedged native call — released by the test, never by timing
                TryInitInFlight = false;
                NativeEvents.Enqueue("init-returned");
            }
            if (ThrowOnTryInit)
            {
                throw new InvalidOperationException("backend wedged in the audio stack");
            }

            if (TryInitError is { } failure)
            {
                error = failure;
                return false;
            }

            error = null;
            return true;
        }

        public IAudioPlayer CreatePlayer(string path, float volume)
        {
            if (ThrowOnCreatePlayer is { } constructionFailure)
            {
                throw constructionFailure;
            }

            var p = new FakePlayer(path, volume);
            PlayerHook?.Invoke(p);
            Players.Add(p);
            return p;
        }

        public void Dispose()
        {
            if (TryInitInFlight)
            {
                DisposedWhileInitInFlight = true; // the concurrent-native-call class, observed
            }
            NativeEvents.Enqueue("backend-disposed");
            Interlocked.Increment(ref _disposeCallCount);
        }
    }

    private sealed class FakePlayer(string path, float volume) : IAudioPlayer
    {
        public string Path { get; } = path;
        public bool Playing { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public bool ThrowOnPlay { get; set; }
        public Action? OnPlay { get; set; }

        public event EventHandler? PlaybackEnded;
        public AudioPlayerState State => Playing ? AudioPlayerState.Playing : AudioPlayerState.Stopped;
        public double PositionSec => 0;
        public float Volume { get; set; } = volume;

        public void Play()
        {
            OnPlay?.Invoke();
            if (ThrowOnPlay)
            {
                throw new ObjectDisposedException("player");
            }
            Playing = true;
        }
        public void Pause() { }
        public void Stop() { Playing = false; Stopped = true; }
        public void Dispose() => Disposed = true;
        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeDuckSink : IAudioDuckSink
    {
        public bool ApplyOk { get; set; } = true;
        public bool RestoreThrows { get; set; }
        public List<float> Applies { get; } = [];
        public List<int> Restores { get; } = [];

        public bool TryApply(float strength, out string? error)
        {
            if (!ApplyOk)
            {
                error = "typed unavailable";
                return false;
            }

            error = null;
            Applies.Add(strength);
            return true;
        }

        public void Restore()
        {
            Restores.Add(1);
            if (RestoreThrows)
            {
                throw new InvalidOperationException("restore failed");
            }
        }
    }

    private sealed class ManualClock : ISoundClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            _timers.Add(entry);
            return new CancelHandle(entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            // Fire due timers in due order; timers scheduled by callbacks fire in the same pass.
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
