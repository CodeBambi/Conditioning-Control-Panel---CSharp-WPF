using System.Collections.Concurrent;
using CcpClient.Desktop.Audio;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Arbitration core — channel ownership (voice stop-replace + generation,
/// whisper real-event busy, SFX pool 8 drop-on-overflow), queue ordering + freshness,
/// ducking refcount symmetry (overlapping + watchdog + panic release-all), device re-probe,
/// off-sync-context construction (a regression guard), panic cleanup. All against RECORDING
/// FAKES + a manual clock — never the real SoundFlow backend (backend-event evidence = Step 3
/// console harness). WPF parity cites per test (record.md Step 1 archaeology).
/// </summary>
public sealed class SoundArbitrationTests
{
    private readonly List<string> _log = [];

    /// <param name="log">An optional per-test sink. The default keeps every existing
    /// fact on the shared <see cref="_log"/> list; the repeated-close facts pass a concurrent sink
    /// because their release phase genuinely has two product threads logging at once (the
    /// unwedged probe and the teardown thread), and <see cref="_log"/> is an unsynchronised
    /// <see cref="List{T}"/>.</param>
    private (SoundArbitration arb, FakeBackend backend, FakeDuckSink duck, ManualClock clock) Make(
        int maxSfx = 8, string[]? devices = null, TimeSpan? teardownBudget = null, Action<string>? log = null)
    {
        var backend = new FakeBackend { Devices = devices ?? ["RDP Sink"] };
        var duck = new FakeDuckSink();
        var clock = new ManualClock();
        var arb = new SoundArbitration(backend, duck, clock, new SoundArbitrationOptions
        {
            MaxSfxVoices = maxSfx,
            DuckWatchdog = TimeSpan.FromMinutes(5),
            VoicePacingDelay = TimeSpan.FromSeconds(2),
            // Budgets whose elapsing is NOT the subject get the shared 60s injection;
            // the give-up facts pass their own short literal (elapsing IS the subject).
            TeardownBudget = teardownBudget ?? TestWait.InjectedBudget,
        }, log ?? _log.Add);
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

    // ---------- The session-disable EXPIRES (WPF d33b5d8d, #778/#779) ----------

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

    // ---------- Teardown off the UI thread (host close must not wait on a wedged probe) ----------

    // The give-up facts inject a 200ms budget whose ELAPSING is the subject (TestWait
    // population 2). Every rendezvous is a deterministic signal: the probe is PROVEN parked
    // (TryInitInFlight) before Dispose is called, so the budget always expires with the
    // native call still in flight — the outcome never depends on scheduler timing.
    private static readonly TimeSpan GiveUpBudget = TimeSpan.FromMilliseconds(200); // wallclock-allow: the budget's elapsing IS the subject — the probe is PROVEN parked before Dispose, so the give-up always fires with the native call in flight

    /// <summary>
    /// Drive the REAL probe path (suppressed → kick → the scheduled one-shot fires on a
    /// background thread) into a backend whose TryInit parks until released. Returns with
    /// the probe PROVEN parked inside the native call, holding `_initLock` — exactly the
    /// dead-endpoint window the DTRH host close can hit.
    /// </summary>
    private (SoundArbitration arb, FakeBackend backend, Thread probe, ManualResetEventSlim probeDone) ParkedProbe(
        Action<string>? log = null)
    {
        var (arb, backend, _, clock) = Make(devices: [], teardownBudget: GiveUpBudget, log: log);
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
        => $"initInFlight={backend.TryInitInFlight} enumerateInFlight={backend.EnumerateInFlight} "
           + $"disposeCalls={backend.DisposeCallCount} disposedBy={backend.DisposingThreadName ?? "(none)"} "
           + $"events=[{string.Join(",", backend.NativeEvents)}]";

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
        // the teardown moved off the UI thread (panic line as before, channels stopped)
        // and NO give-up/completion lines.
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        arb.PlayVoice("v.mp3", 1f);
        var player = backend.Players[^1];

        arb.Dispose();

        Assert.Equal(1, backend.DisposeCallCount);
        Assert.True(player.Stopped && player.Disposed); // PanicReset still ran on the caller
        Assert.DoesNotContain(_log, l => l.Contains("teardown exceeds") || l.Contains("backgrounded backend teardown"));
        Assert.Contains(_log, l => l.Contains("panic-reset")); // the original observable line
    }

    // ---------- The give-up residue is bounded across REPEATED host closes ----------

    // The off-UI-thread teardown left ONE thread per close parked on _initLock until the wedged native call
    // returned, and framed that residue as "bounded by user close actions". The census
    // (record.md §1) found the count is governed by host OPENS, not closes: every
    // DtrhHostWindow builds its own arbitration (DtrhHostWindow.axaml.cs:214) and disposes it
    // at Closing (:258), five of the close paths are automatic, and the WPF product's own
    // doors re-launch the host on every press (MainWindow.Lab.cs:237-253, :318-322) with only
    // RELAUNCHES latched (DtrhHostService.cs:39 `_relaunchedOnce`). So the residue is driven
    // in a LOOP below: a single cycle cannot distinguish "bounded" from "bounded by one".
    private const string TeardownThreadName = "SoundArbitrationTeardown";

    [Fact]
    public void Teardown_RepeatedWedgedCycles_TeardownThreadsDoNotAccumulate()
    {
        const int cycles = 5;
        var sink = new ConcurrentQueue<string>();
        var open = new List<(SoundArbitration arb, FakeBackend backend, Thread probe, ManualResetEventSlim probeDone)>();
        try
        {
            for (var i = 0; i < cycles; i++)
            {
                // One host open/close cycle against a permanently wedged endpoint. Each cycle
                // is sequenced to completion before the next starts, so the log sink and the
                // rendezvous stay ordered by signals rather than by assumption.
                var cycle = ParkedProbe(sink.Enqueue);
                open.Add(cycle);
                var disposeReturned = new ManualResetEventSlim();
                RunDispose(cycle.arb, disposeReturned).Start();
                TestWait.UntilSync(
                    () => disposeReturned.IsSet,
                    $"close {i + 1} of {cycles} gives up bounded while its own probe is parked",
                    () => TeardownState(cycle.backend));
            }

            // THE BOUND: N wedged closes leave ZERO teardown threads holding an OS thread.
            // Before the bound this count is N — one parked on _initLock per close, for the life of
            // the process if the endpoint never unwedges.
            TestWait.UntilSync(
                () => open.All(c => !c.arb.TeardownThreadOutstanding),
                $"all {cycles} wedged closes released their teardown thread (residue does not accumulate)",
                () => $"outstanding={open.Count(c => c.arb.TeardownThreadOutstanding)}/{cycles}");
            Assert.Equal(cycles, sink.Count(l => l.Contains("teardown exceeds"))); // one give-up line per close
            Assert.All(open, c => Assert.Equal(0, c.backend.DisposeCallCount));    // give-up never touches the backend

            // ...and the handoff did not SKIP the disposal (the forbidden overflow): released
            // one wedge at a time, every backend is disposed EXACTLY once, by a teardown thread.
            for (var i = 0; i < cycles; i++)
            {
                var cycle = open[i];
                cycle.backend.TryInitRelease!.Set();
                TestWait.UntilSync(
                    () => cycle.probeDone.IsSet && cycle.backend.DisposeCallCount == 1,
                    $"cycle {i + 1} disposed its backend once after its wedge cleared",
                    () => TeardownState(cycle.backend));
                cycle.probe.Join();
                Assert.Equal(TeardownThreadName, cycle.backend.DisposingThreadName);
            }

            TestWait.UntilSync(
                () => sink.Count(l => l.Contains("backgrounded backend teardown completed")) == cycles,
                $"each of the {cycles} handed-off teardowns logged its completion pair",
                () => $"completions={sink.Count(l => l.Contains("backgrounded backend teardown completed"))}");
            Assert.All(open, c => Assert.Equal(1, c.backend.DisposeCallCount)); // never zero (leak), never two
        }
        finally
        {
            foreach (var cycle in open)
            {
                cycle.backend.TryInitRelease!.Set(); // unwedge the fixture whatever the verdict
            }
        }
    }

    [Fact]
    public void Teardown_GiveUp_NoThreadOutstandingInsideTheWedgedInit_DisposedOnceAfterRelease()
    {
        // The inside-the-wedged-operation read (the disposeCountAtTeardownEnd shape, not
        // an end-state observation): the residue is read ON the wedged thread, INSIDE the
        // still-in-flight native init. That is the only window where "no teardown thread is
        // outstanding" means anything — after the release the drain spawns one to perform the
        // disposal, so a read taken afterwards would be reading a different question.
        var sink = new ConcurrentQueue<string>();
        var (arb, backend, probe, probeDone) = ParkedProbe(sink.Enqueue);
        var outstandingInsideWedge = true;
        var disposeCountInsideWedge = -1;
        backend.InsideWedgedInit = () =>
        {
            outstandingInsideWedge = arb.TeardownThreadOutstanding;
            disposeCountInsideWedge = backend.DisposeCallCount;
        };
        try
        {
            var disposeReturned = new ManualResetEventSlim();
            RunDispose(arb, disposeReturned).Start();
            TestWait.UntilSync(() => disposeReturned.IsSet, "the caller gives up bounded", () => TeardownState(backend));
            // Establishes the state the inside read then confirms, so a starved scheduler can
            // never be the verdict. The load-bearing assertion is still the inside one.
            TestWait.UntilSync(
                () => !arb.TeardownThreadOutstanding,
                "the teardown thread handed the disposal off and exited instead of parking on the wedge",
                () => TeardownState(backend));
        }
        finally
        {
            backend.TryInitRelease!.Set();
        }

        TestWait.UntilSync(
            () => probeDone.IsSet && backend.DisposeCallCount == 1,
            "the post-release drain performed the handed-off disposal",
            () => TeardownState(backend));
        probe.Join(); // publishes the values written on the probe thread

        Assert.False(outstandingInsideWedge);      // THE FACT: no thread held while the wedge was
        Assert.Equal(0, disposeCountInsideWedge);  // and the backend was untouched there
        Assert.Equal(1, backend.DisposeCallCount); // handed off, never skipped
        Assert.Equal(TeardownThreadName, backend.DisposingThreadName); // never the wedged probe thread
        Assert.True(backend.DisposingThreadIsBackground);
        Assert.Equal(1, sink.Count(l => l.Contains("teardown exceeds")));
        TestWait.UntilSync(
            () => sink.Count(l => l.Contains("backgrounded backend teardown completed")) == 1,
            "the give-up/completion pair still lands exactly once",
            () => TeardownState(backend));
    }

    [Fact]
    public void Teardown_GiveUp_DuringParkedEnumerate_DrainsAfterTheEnumerateReleases()
    {
        // The SECOND _initLock scope. EnumerateDevices holds the same lock as the recovery
        // probe, so a close during a wedged enumeration hands off identically — and without a
        // post-release drain on THIS scope the handoff would have nobody to run it and the
        // native device would leak.
        var sink = new ConcurrentQueue<string>();
        var (arb, backend, _, _) = Make(teardownBudget: GiveUpBudget, log: sink.Enqueue);
        Initialized(arb);
        backend.EnumerateRelease = new ManualResetEventSlim();
        var outstandingInsideWedge = true;
        var disposeCountInsideWedge = -1;
        backend.InsideWedgedEnumerate = () =>
        {
            outstandingInsideWedge = arb.TeardownThreadOutstanding;
            disposeCountInsideWedge = backend.DisposeCallCount;
        };
        var enumerateDone = new ManualResetEventSlim();
        var enumerate = new Thread(() => { arb.EnumerateDevices(); enumerateDone.Set(); })
            { IsBackground = true, Name = "test-enumerate" };
        enumerate.Start();
        TestWait.UntilSync(
            () => backend.EnumerateInFlight,
            "the enumeration parked inside the native call (fixture never reached the mechanism)",
            () => TeardownState(backend));
        try
        {
            var disposeReturned = new ManualResetEventSlim();
            RunDispose(arb, disposeReturned).Start();
            TestWait.UntilSync(
                () => disposeReturned.IsSet,
                "the caller gives up bounded against a wedged enumeration",
                () => TeardownState(backend));
            TestWait.UntilSync(
                () => !arb.TeardownThreadOutstanding,
                "the teardown thread handed off and exited",
                () => TeardownState(backend));
            Assert.Equal(0, backend.DisposeCallCount);
            Assert.Equal(1, sink.Count(l => l.Contains("teardown exceeds")));
        }
        finally
        {
            backend.EnumerateRelease!.Set();
        }

        TestWait.UntilSync(
            () => enumerateDone.IsSet && backend.DisposeCallCount == 1,
            "the enumerate scope's post-release drain performed the handed-off disposal",
            () => TeardownState(backend));
        enumerate.Join();

        Assert.False(outstandingInsideWedge);
        Assert.Equal(0, disposeCountInsideWedge);
        Assert.Equal(1, backend.DisposeCallCount);
        Assert.Equal(TeardownThreadName, backend.DisposingThreadName);
    }

    [Fact]
    public void Teardown_NoContention_DisposalNeverRunsOnTheCallerThread()
    {
        // A REGRESSION GUARD for the off-UI-thread teardown, not a bounded-residue fact: code from
        // before the bound passes it too, so it is deliberately NOT counted in the packet's revert
        // matrix. It exists because the bound's uncontended branch sits one edit away from "we hold
        // the lock, just dispose here", which is the off-UI-thread teardown reverted — the native
        // device teardown back on the UI close handler.
        var (arb, backend, _, _) = Make();
        Initialized(arb);
        var callerThreadId = Environment.CurrentManagedThreadId;

        arb.Dispose();

        Assert.Equal(1, backend.DisposeCallCount);
        Assert.NotEqual(callerThreadId, backend.DisposingThreadId);
        Assert.Equal(TeardownThreadName, backend.DisposingThreadName);
        Assert.True(backend.DisposingThreadIsBackground); // a wedged teardown never blocks process exit
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

    // ---------- off-sync-context construction (regression) ----------

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
                // the work there is no context and no deadlock (the AssetDataProvider class).
                Assert.Null(SynchronizationContext.Current);
                workThread = Environment.CurrentManagedThreadId;
                return 42;
            });
            Assert.Equal(42, result);
        });
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(10)), "deadlocked on a sync-context thread — the off-sync-context wedge class");
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

    // ---------- An abandoned player construction never reaches the mixer ----------

    // The budget whose ELAPSING is the subject (TestWait population 2 — same pinned-literal
    // discipline as the GiveUpBudget). Every rendezvous below is a deterministic signal
    // (gates + the recorded event stream), never timing.
    //
    // THE CLASS-WIDE RULE, and the separation three facts here were caught by (P2, 2026-08-24).
    // OrphanSafePlayerFactory.Create QUEUES the construction on the thread pool and then waits
    // this wall-clock budget for it (AudioSeams.cs: Task.Run, then task.Wait(_budget)), so the
    // budget covers POOL DISPATCH LATENCY + the native call, not the native call alone. On a
    // starved pool the dispatch alone can exceed it. Two consequences, and both are properties of
    // the FACTS rather than of the product — the arbitration is correct in every starved run
    // measured (see the fixed facts for the numbers):
    //
    //   1. "The construction is parked" is NOT implied by "the caller abandoned it": the work item
    //      may still be QUEUED. So every fact that READS or CLAIMS construction state holds first
    //      on the fake's own entry record (ConstructCount) via the shared bounded helper. Waiting
    //      for the entry can never mask a product defect: the caller's abandonment already
    //      happened, and a construction that is never entered fails the wait LOUDLY.
    //   2. A fact that needs the caller's task.Wait to RETURN TRUE cannot be fixed by waiting at
    //      all — the caller has already decided. Such a fact (there is exactly one:
    //      Construction_LockUnavailableAtCompletion_...) must not put its construction budget on
    //      this literal; it takes the shared 60 s injection and moves the short literal onto the
    //      LIFECYCLE-LOCK budget, whose elapsing is what that fact is actually about.
    //
    // Hence this literal has two roles, both class-3: the construction give-up (facts whose
    // construction is gated shut) and the lifecycle-lock give-up (the fact above).
    private static readonly TimeSpan ConstructionBudget = TimeSpan.FromMilliseconds(200); // wallclock-allow: the budget's elapsing IS the subject — either the wedged construction's give-up (the gate is shut, so abandonment always fires with the construction still wedged) or the lifecycle-lock give-up (the lock is held for the whole fact)

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
        public int ConstructCount; // ConstructStarted is a one-shot latch and cannot count N
        public readonly OrphanSafePlayerFactory<OrphanPlayer> Factory;

        // Cap instrumentation, in the InsideWedgedInit/InsideWedgedEnumerate shape
        // (FakeBackend below): InsideWedgedConstruct runs ON the construction thread, INSIDE the
        // native stand-in, after any gate is released and BEFORE construct returns — i.e. before
        // the product's settling finally can run. That is the only window in which "how many
        // constructions does the factory consider outstanding RIGHT NOW" can be read from the
        // parked side. With no gate armed it still runs, while the caller sits in task.Wait.
        public volatile Action? InsideWedgedConstruct;
        public readonly ConcurrentQueue<int> OutstandingInsideWedge = new();
        // Parks the orphan disposer INSIDE the dispose delegate, which the product runs while
        // holding _lifecycle — the window that separates "the pool thread was released" from
        // "the player was disposed".
        public volatile ManualResetEventSlim? DisposeGate;
        // Parks the CALLER inside the attach delegate, which the product also runs while holding
        // _lifecycle. That is the only way to hold that lock with the factory still LIVE: Teardown
        // sets _tornDown before running its delegate, so a lock held that way refuses every later
        // Create at the top of the method, and an orphan disposer can only be manufactured by an
        // abandonment (which is what the holder is needed to cause in the first place).
        public volatile ManualResetEventSlim? AttachGate;
        // The faulted-construction leg: a native call that returns by THROWING still returns.
        public volatile Exception? ConstructThrows;

        public OrphanHarness(
            TimeSpan budget,
            Action<string>? logHook = null,
            int? maxOutstandingAbandoned = null,
            TimeSpan? lifecycleLockBudget = null)
        {
            Factory = new OrphanSafePlayerFactory<OrphanPlayer>(
                construct: (path, volume) =>
                {
                    Interlocked.Increment(ref ConstructCount);
                    ConstructStarted?.Set();
                    ConstructGate?.Wait(); // wallclock-allow: the wedge IS the subject — a native construction still inside the call, released by the test, never by timing
                    InsideWedgedConstruct?.Invoke(); // still INSIDE the native call, before it returns
                    if (ConstructThrows is { } failure)
                    {
                        throw failure;
                    }

                    Events.Enqueue("construct-returned");
                    var p = new OrphanPlayer(path, volume);
                    LastPlayer = p;
                    return p;
                },
                attach: p =>
                {
                    Interlocked.Increment(ref AttachCount);
                    Events.Enqueue("attached"); // enqueued BEFORE parking: the signal that the lock is held
                    if (AttachGate is { } gate)
                    {
                        // The approved bounded helper rather than a raw park: this one runs on the
                        // CALLER's thread, so an unreleased gate would hang the run instead of failing it.
                        TestWait.UntilSync(() => gate.IsSet, "the test released the parked attach", State);
                    }
                },
                dispose: p =>
                {
                    Events.Enqueue("dispose-entered"); // inside the product's _lifecycle lock
                    DisposeGate?.Wait(); // wallclock-allow: the wedge IS the subject — parks the orphan disposer inside the product's lifecycle lock, released by the test
                    p.Dispose();
                    Interlocked.Increment(ref DisposeCount);
                    Events.Enqueue("orphan-disposed");
                },
                log: line => { lock (Log) Log.Add(line); logHook?.Invoke(line); },
                tag: "test",
                budget: budget,
                maxOutstandingAbandoned: maxOutstandingAbandoned,
                lifecycleLockBudget: lifecycleLockBudget);
        }

        public int AbandonmentLines
        {
            get { lock (Log) return Log.Count(l => l.Contains("construction abandoned")); }
        }

        /// <summary>Cap refusals — disjoint from <see cref="AbandonmentLines"/> by wording.</summary>
        public int CapRefusalLines
        {
            get { lock (Log) return Log.Count(l => l.Contains("abandoned construction(s) still parked")); }
        }

        /// <summary>Every log line, so the healthy path can assert ZERO new lines rather than zero of one kind.</summary>
        public int LogLines
        {
            get { lock (Log) return Log.Count; }
        }

        public int Outstanding => Factory.OutstandingAbandonedConstructions;

        public string State() =>
            $"attach={AttachCount} dispose={DisposeCount} abandonmentLines={AbandonmentLines} " +
            $"capRefusalLines={CapRefusalLines} outstanding={Outstanding} constructs={Volatile.Read(ref ConstructCount)} " +
            $"events=[{string.Join(",", Events)}]";
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
        // ARMING DEPENDENCY: this rendezvous relies on the product logging the abandonment
        // line BEFORE its completed-check (marked LOAD-BEARING ORDER in
        // OrphanSafePlayerFactory.Create). If that order ever moves, this pin degenerates to
        // a single-disposer scenario and passes WITHOUT exercising the latch.
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
        // (that teardown was made concurrent). The teardown delegate parks INSIDE the
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
            teardownMayFinish.Wait(); // wallclock-allow: the wedge IS the subject — the wedged native teardown holds the lifecycle lock until the test releases it
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

    // ---------- The outstanding ABANDONED constructions are bounded ----------

    [Fact]
    public void Construction_AtTheOutstandingCap_FurtherCreatesRefusedWithoutStartingAConstruction()
    {
        // THE BOUND. With the cap's worth of abandoned constructions still parked — one pool
        // thread each, none of them interruptible — the next create is refused typed BEFORE any
        // Task.Run: the refusal must never take the very resource it exists to bound. Driven ONE
        // PAST the cap (3 creates against cap 2), because a loop that stops at the cap cannot
        // distinguish "bounded" from "bounded by one".
        var h = new OrphanHarness(ConstructionBudget, maxOutstandingAbandoned: 2)
        {
            ConstructGate = new ManualResetEventSlim(false),
        };

        var thrown = new List<Exception?>();
        for (var i = 0; i < 3; i++)
        {
            // SEQUENTIAL BY CONSTRUCTION: each caller's typed outcome is observed before the next
            // Create begins. Started concurrently, the third could reach the cap check a whole
            // budget before the first two abandoned and all three would construct — a flake, not
            // a bound.
            var (thread, done, threadThrown) = StartCreate(h.Factory);
            thread.Start();

            // THE DETERMINISTIC SIGNAL, and the reason this fact used to flake under load with
            // "Expected 2, Actual 1" on the ConstructCount assertion below. Create queues its
            // construction on the THREAD POOL and then waits a WALL-CLOCK budget for it
            // (AudioSeams.cs, OrphanSafePlayerFactory.Create: Task.Run, then task.Wait(_budget)).
            // On a loaded machine the pool can take longer than the budget to dispatch that work
            // item, so the caller abandons a construction the fake has never been entered for and
            // ConstructCount reads short — measured, not theorised: with the pool deliberately
            // starved this fact reported constructs=1 (and constructs=0 with it starved harder)
            // while the ARBITRATION was simultaneously perfect at abandonmentLines=2,
            // outstanding=2, capRefusalLines=1 and the third caller's typed cap refusal in hand.
            // The flake was therefore the fact measuring the thread pool's dispatch latency, never
            // the bound. The first two callers are held here until their construction has
            // DEMONSTRABLY entered the fake; the third is not, because a refused create queues
            // nothing at all — which is the very claim being made about it.
            if (i < 2)
            {
                TestWait.UntilSync(() => Volatile.Read(ref h.ConstructCount) == i + 1,
                    $"caller {i}'s construction entered the fake", () => h.State());
            }

            TestWait.UntilSync(() => done.IsSet, $"caller {i} reached its typed outcome", () => h.State());
            thread.Join();
            thrown.Add(threadThrown());
        }

        // The third create STARTED NO CONSTRUCTION — the fake's own record, never the absence of
        // an exception.
        Assert.Equal(2, Volatile.Read(ref h.ConstructCount));
        Assert.Equal(2, h.Outstanding);      // both still parked, both still counted
        Assert.Equal(2, h.AbandonmentLines); // the first two abandoned
        Assert.Equal(1, h.CapRefusalLines);  // the third was refused, exactly once
        Assert.Equal(0, h.AttachCount);      // nothing ever reached the mixer
        Assert.IsType<PlayerConstructionTimeoutException>(thrown[0]); // budget expiry
        Assert.IsType<PlayerConstructionTimeoutException>(thrown[2]); // the SAME typed vocabulary at the cap
        Assert.Contains("cap", thrown[2]!.Message);                   // distinguishable by message, not by type

        h.ConstructGate!.Set(); // retire the parked stand-ins rather than leaking them past the test
        TestWait.UntilSync(() => h.Outstanding == 0, "the parked constructions returned", () => h.State());
    }

    [Fact]
    public void Construction_ParkedConstructionsReturn_OutstandingDropsToZero_AndConstructionIsAdmittedAgain()
    {
        // THE DECREMENT, which a cap-only test cannot see: a cap that only counts up is a
        // refuse-forever bug — permanent silence on a live user path after one transient slow
        // patch. Plus the count observed from INSIDE the still-parked constructions.
        var h = new OrphanHarness(ConstructionBudget, maxOutstandingAbandoned: 2)
        {
            ConstructGate = new ManualResetEventSlim(false),
        };
        h.InsideWedgedConstruct = () => h.OutstandingInsideWedge.Enqueue(h.Outstanding);

        for (var i = 0; i < 2; i++)
        {
            var (thread, done, _) = StartCreate(h.Factory); // sequential, as above
            thread.Start();
            // Rule 1 (see ConstructionBudget): "parked" below is a claim about the POOL, and only
            // the fake's own entry record can make it true — an abandoned construction may still
            // be QUEUED on a starved pool.
            TestWait.UntilSync(() => Volatile.Read(ref h.ConstructCount) == i + 1,
                $"caller {i}'s construction entered the fake", () => h.State());
            TestWait.UntilSync(() => done.IsSet, $"caller {i} abandoned its construction", () => h.State());
            thread.Join();
        }

        Assert.Equal(2, h.Outstanding); // AT the cap, with both constructions demonstrably parked

        h.ConstructGate!.Set(); // the wedged endpoint finally returns
        TestWait.UntilSync(() => h.Outstanding == 0, "the parked pool threads were released", () => h.State());
        var insideReadings = h.OutstandingInsideWedge.ToArray(); // snapshot BEFORE the readmission

        var (readmit, readmitDone, _) = StartCreate(h.Factory);
        readmit.Start();
        TestWait.UntilSync(() => Volatile.Read(ref h.ConstructCount) == 3, "the post-recovery create was ADMITTED", () => h.State());
        TestWait.UntilSync(() => readmitDone.IsSet, "the post-recovery caller completed", () => h.State());
        readmit.Join();

        Assert.Equal(3, Volatile.Read(ref h.ConstructCount)); // admitted — C1 did not refuse it
        Assert.Equal(0, h.CapRefusalLines);
        Assert.Equal(0, h.Outstanding);
        // Read on the construction thread, inside the native stand-in, before it returned: the
        // first hook to run globally precedes every settling finally, so it must see the full
        // cap. The count is real WHILE parked, not an artifact of reading it after the unwind.
        Assert.Equal(2, insideReadings.Length);
        Assert.Contains(2, insideReadings);
    }

    [Fact]
    public void Construction_Ordinary_NeverTouchesTheOutstandingCount_NoCapLine_NoLogLine()
    {
        // NEGATIVE CONTROL, and the one that guards the live user-visible path: an ordinary
        // in-flight construction must never consume the bound. If it did, a burst of concurrent
        // HEALTHY cues could reach the cap and refuse a live audio cue — silence on a working
        // device. The reading is taken from INSIDE the construction, while the caller is still in
        // its task.Wait: counting every in-flight construction rather than only abandoned ones
        // reads 1 here where the bound reads 0.
        var h = new OrphanHarness(TestWait.InjectedBudget);
        h.InsideWedgedConstruct = () => h.OutstandingInsideWedge.Enqueue(h.Outstanding);
        Assert.Equal(0, h.Outstanding);

        var player = h.Factory.Create("ok.mp3", 0.7f);

        Assert.Equal(0, Assert.Single(h.OutstandingInsideWedge)); // DURING — the discriminator
        Assert.Equal(0, h.Outstanding);                           // and after
        Assert.Same(h.LastPlayer, player);
        Assert.Equal(1, h.AttachCount);
        Assert.Equal(1, Volatile.Read(ref h.ConstructCount));
        Assert.Equal(0, h.CapRefusalLines);
        Assert.Equal(0, h.LogLines); // invariant clause 5's "zero new log lines", asserted not assumed
    }

    [Fact]
    public void Construction_AbandonedConstructionReturns_CountDropsAtTheNativeReturn_NotAtOrphanDisposal()
    {
        // WHERE the release lives. The count tracks the parked THREAD, not the orphan OBJECT, so
        // it must fall when the native call returns — not when the orphan is finally disposed,
        // which happens later and behind the lifecycle lock. Observed in the one window that
        // separates the two: the disposer is parked INSIDE the dispose delegate, which the
        // product runs while holding _lifecycle.
        var h = new OrphanHarness(ConstructionBudget, maxOutstandingAbandoned: 2)
        {
            ConstructGate = new ManualResetEventSlim(false),
            DisposeGate = new ManualResetEventSlim(false),
        };
        var (thread, done, thrown) = StartCreate(h.Factory);
        thread.Start();
        // Rule 1 (see ConstructionBudget): this fact's whole subject is WHERE the count is
        // released — at the parked thread's native return — so "parked" has to be true, not
        // assumed. A queued-but-undispatched construction is counted too (correctly: it will take
        // a thread), and the release below would then be observed at the wrong event.
        TestWait.UntilSync(() => Volatile.Read(ref h.ConstructCount) == 1,
            "the construction entered the fake", () => h.State());
        TestWait.UntilSync(() => done.IsSet, "the caller abandoned its construction", () => h.State());
        thread.Join();

        Assert.IsType<PlayerConstructionTimeoutException>(thrown());
        Assert.Equal(1, h.Outstanding);  // parked and counted
        Assert.Equal(0, h.DisposeCount); // and not yet disposed

        h.ConstructGate!.Set(); // the native call returns — the pool thread is free HERE
        TestWait.UntilSync(() => h.Events.Contains("dispose-entered"), "the orphan disposer holds the lifecycle lock", () => h.State());

        // THE assertion, in one line: released at the native return, with the orphan
        // demonstrably NOT yet disposed. Deterministic — the settling finally strictly precedes
        // task completion, which strictly precedes the continuation now parked inside dispose.
        Assert.Equal(0, h.Outstanding);
        Assert.Equal(0, h.DisposeCount);

        h.DisposeGate!.Set();
        TestWait.UntilSync(() => h.DisposeCount == 1, "the orphan is disposed exactly once", () => h.State());
        Assert.Equal(0, h.AttachCount);
        Assert.Equal(0, h.LastPlayer!.PlayCount);
        Assert.Equal(0, h.Outstanding); // disposal did not double-release it either
    }

    [Fact]
    public void Construction_AbandonedThenFaults_CountStillDrops_CapNeverRefusesForever()
    {
        // THE FINALLY, specifically. A native call that returns by THROWING has still returned
        // its thread. If the count fell only on the success path, one transient bad file would
        // refuse every later cue on this factory for the rest of the session.
        var h = new OrphanHarness(ConstructionBudget, maxOutstandingAbandoned: 1)
        {
            ConstructGate = new ManualResetEventSlim(false),
        };
        var (thread, done, thrown) = StartCreate(h.Factory);
        thread.Start();
        // Rule 1 (see ConstructionBudget). Without this hold the ConstructCount assertion below
        // measures thread-pool DISPATCH latency against the caller's 200 ms budget: with the pool
        // deliberately starved (24 parked workers on 16 cores) this fact failed 20/20 with exactly
        // the reported "Expected: 1, Actual: 0" — the construction had not entered the fake yet —
        // while the arbitration in the same run was perfect (outstanding=1, the typed cap refusal
        // in hand, capRefusalLines=1). The construction is held here until it has DEMONSTRABLY
        // entered the fake and parked on the shut gate, which is what "parked" below then means.
        TestWait.UntilSync(() => Volatile.Read(ref h.ConstructCount) == 1,
            "the construction entered the fake", () => h.State());
        TestWait.UntilSync(() => done.IsSet, "the caller abandoned its construction", () => h.State());
        thread.Join();
        Assert.IsType<PlayerConstructionTimeoutException>(thrown());
        Assert.Equal(1, h.Outstanding); // the cap of 1 is now full

        var refused = Assert.Throws<PlayerConstructionTimeoutException>(() => h.Factory.Create("second.mp3", 1f));
        Assert.Contains("cap", refused.Message);
        Assert.Equal(1, h.CapRefusalLines);
        Assert.Equal(1, Volatile.Read(ref h.ConstructCount)); // the refusal started no pool thread

        h.ConstructThrows = new InvalidOperationException("native decoder threw on the way out");
        h.ConstructGate!.Set(); // the parked construction returns — by FAULTING
        TestWait.UntilSync(() => h.Outstanding == 0, "a FAULTED construction released its count", () => h.State());

        var (readmit, readmitDone, _) = StartCreate(h.Factory);
        readmit.Start();
        TestWait.UntilSync(() => Volatile.Read(ref h.ConstructCount) == 2, "the post-fault create was ADMITTED", () => h.State());
        TestWait.UntilSync(() => readmitDone.IsSet, "the post-fault caller completed", () => h.State());
        readmit.Join();

        Assert.Equal(2, Volatile.Read(ref h.ConstructCount)); // admitted — the cap did not refuse forever
        Assert.Equal(1, h.CapRefusalLines);                   // still exactly the one refusal
        Assert.Equal(0, h.AttachCount);
    }

    [Fact]
    public void Construction_LockUnavailableAtCompletion_AbandonsWithoutCounting_NothingWasParked()
    {
        // THE SECOND ROUTE TO ABANDONMENT, and the only one on which the accounting CAS does any
        // work. Create reaches `slot.Abandoned = true; CountAbandoned(slot);` two ways: (a) the
        // caller's task.Wait expired, so the construction IS parked — the route every other fact
        // here takes; or (b) task.Wait returned TRUE and Monitor.TryEnter(_lifecycle, …) timed out
        // because something holds the lifecycle lock (the give-up class that bounded TryEnter
        // exists for). On route (b) the construction has already RETURNED, so no pool thread is
        // parked and the count must NOT rise. If it did, this slot's settling finally has already
        // run and can never run again, so the increment would leak permanently: after `cap` such
        // events the factory refuses every later cue for the rest of its life — the refuse-forever
        // failure the count exists to prevent.
        //
        // WHY THIS FACT NOW HAS TWO FACTORIES — the P2 separation, 2026-08-24. The two legs need
        // OPPOSITE things from the construction budget: leg (a) needs it to ELAPSE, leg (b) needs
        // it never to decide anything (task.Wait must return TRUE). One factory carrying one
        // 200 ms budget for both made leg (b) a race against thread-pool DISPATCH latency, and it
        // lost: with the pool deliberately starved (24 parked workers on 16 cores) this fact
        // failed 20/20 with "Expected: 0, Actual: 1" — the construction had not been dispatched
        // inside 200 ms, so the caller took route (a) and counted 1, WHICH IS CORRECT. The product
        // was never wrong here; the fact silently stopped reaching its own subject. No added wait
        // can fix that, because the caller has already decided by the time anything is observable.
        // So the budgets are separated instead: leg (b) puts the CONSTRUCTION on the shared 60 s
        // injection (it can no longer decide anything) and keeps the short literal on the
        // LIFECYCLE-LOCK give-up, whose elapsing is what leg (b) is actually about.
        var outstandingAtAbandonment = new ConcurrentQueue<int>();

        // ---- LEG (a): parked, therefore counted.
        OrphanHarness? parked = null;
        parked = new OrphanHarness(
            ConstructionBudget,
            logHook: line => { if (line.Contains("construction abandoned")) { outstandingAtAbandonment.Enqueue(parked!.Outstanding); } })
        {
            ConstructGate = new ManualResetEventSlim(false),
        };
        var (first, firstDone, firstThrew) = StartCreate(parked.Factory);
        first.Start();
        TestWait.UntilSync(() => Volatile.Read(ref parked.ConstructCount) == 1,
            "the first construction entered the fake", () => parked.State()); // rule 1: "parked" is proved
        TestWait.UntilSync(() => firstDone.IsSet, "the first caller abandoned its construction", () => parked.State());
        first.Join();
        Assert.IsType<PlayerConstructionTimeoutException>(firstThrew());
        Assert.Equal(1, parked.Outstanding); // genuinely parked, so genuinely counted

        // ---- LEG (b): completed, therefore NOT counted. The lifecycle lock is held by a parked
        // ATTACH — the product runs that delegate while holding it — which is the only way to hold
        // it with the factory still LIVE: Teardown sets _tornDown before running its delegate, so
        // a lock held that way refuses every later Create at the top of the method.
        OrphanHarness? h = null;
        h = new OrphanHarness(
            TestWait.InjectedBudget,
            logHook: line => { if (line.Contains("construction abandoned")) { outstandingAtAbandonment.Enqueue(h!.Outstanding); } },
            maxOutstandingAbandoned: 1,
            lifecycleLockBudget: ConstructionBudget)
        {
            AttachGate = new ManualResetEventSlim(false),
        };
        var (holder, holderDone, holderThrew) = StartCreate(h.Factory);
        holder.Start();
        TestWait.UntilSync(() => h.Events.Contains("attached"), "the holder parked inside the lifecycle lock", () => h.State());
        Assert.Equal(0, h.Outstanding); // an ordinary in-flight construction never consumes the bound

        // The construct gate is unarmed, so this construction completes and task.Wait returns TRUE
        // — deterministically, because its budget is the 60 s injection and no dispatch is that
        // slow. TryEnter then cannot take _lifecycle inside the 200 ms lock budget, and the
        // completed player is abandoned. (The product's log line quotes the CONSTRUCTION budget,
        // so it reads "after 60000ms" here; the caller in fact spent 200 ms, on the lock.)
        var abandoned = Assert.Throws<PlayerConstructionTimeoutException>(() => h.Factory.Create("second.mp3", 1f));

        Assert.Contains("abandoned", abandoned.Message);
        Assert.DoesNotContain("cap", abandoned.Message); // the abandonment path, not the C1 refusal
        Assert.Equal(0, h.Outstanding); // THE FACT: abandoned, and nothing counted — nothing is parked
        // Read on the CALLER thread at the abandonment decision itself (the product logs
        // immediately after CountAbandoned), with the lock holder still parked: the SAME call site
        // counts on route (a) and must not on route (b). Two factories, one call site — and the
        // contrast is what keeps either half from passing vacuously.
        Assert.Equal([1, 0], outstandingAtAbandonment.ToArray());
        // THE ROUTE IS PROVED, NOT ASSUMED: both constructions RETURNED, so the abandoned one was
        // abandoned by the LOCK and not by its own budget. Read from the fake's own record.
        Assert.Equal(2, h.Events.Count(e => e == "construct-returned"));
        Assert.Equal(2, Volatile.Read(ref h.ConstructCount));
        Assert.Equal(1, h.AbandonmentLines);
        Assert.Equal(0, h.CapRefusalLines); // never refused: with cap 1, the count read 0, correctly
        Assert.Equal(1, h.AttachCount);  // only the holder — a player the waiter could not attach never reaches the mixer
        Assert.Equal(0, h.DisposeCount); // its disposer is blocked on the lock the holder still owns

        h.AttachGate!.Set(); // the holder's attach returns and _lifecycle frees
        TestWait.UntilSync(() => holderDone.IsSet, "the holder's create returned", () => h.State());
        holder.Join();
        Assert.Null(holderThrew()); // the holder attached and returned normally, as an ordinary create does
        TestWait.UntilSync(() => h.DisposeCount == 1, "the orphan is disposed once the lifecycle lock is free", () => h.State());
        Assert.Equal(0, h.Outstanding); // disposal never releases a count it never took
        Assert.Equal(1, h.AttachCount); // and the abandoned player still never reached the mixer

        // Retire leg (a)'s parked stand-in rather than leaking it past the test.
        parked.ConstructGate!.Set();
        TestWait.UntilSync(() => parked.Outstanding == 0, "the parked construction returned", () => parked.State());
    }

    [Fact]
    public void PlaySfx_ConstructionTimeout_TypedFailed_NeverInPool()
    {
        // The bound's caller vocabulary: the typed construction expiry rides the
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
        public override void Post(SendOrPostCallback d, object? state) { /* never pumped — the off-sync-context wedge condition */ }
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
        // Inject a typed construction failure (the bound's expiry) at the seam.
        public Exception? ThrowOnCreatePlayer { get; set; }

        // Cross-thread teardown instrumentation. Both gates null = today's synchronous
        // behavior, so the landed facts are byte-identical. TryInitRelease parked = the wedged
        // native call; the fake RECORDS the moment TryInit returns and the moment Dispose is
        // called on it so the ordering fact asserts the order, not merely that nothing threw.
        public ManualResetEventSlim? TryInitRelease { get; set; }
        public volatile bool TryInitInFlight;
        public volatile bool DisposedWhileInitInFlight;
        public ConcurrentQueue<string> NativeEvents { get; } = new();
        private int _disposeCallCount;
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        // Repeated-close instrumentation. InsideWedgedInit/InsideWedgedEnumerate run ON the wedged
        // thread, INSIDE the native call, after the test releases it and BEFORE it returns —
        // the only window in which "no teardown thread is outstanding while the wedge is
        // still held" can be observed at all (afterwards the drain spawns one). The disposing
        // identity is recorded because "who performs the native teardown" is the invariant:
        // never the caller, never the wedged thread, always a teardown thread.
        public ManualResetEventSlim? EnumerateRelease { get; set; }
        public volatile bool EnumerateInFlight;
        public Action? InsideWedgedInit { get; set; }
        public Action? InsideWedgedEnumerate { get; set; }
        public string? DisposingThreadName { get; private set; }
        public int DisposingThreadId { get; private set; }
        public bool DisposingThreadIsBackground { get; private set; }

        public IReadOnlyList<string> EnumerateDevices()
        {
            EnumerateCallCount++;
            if (EnumerateRelease is { } release)
            {
                EnumerateInFlight = true;
                release.Wait(); // wallclock-allow: the wedge IS the subject — the wedged native enumeration, released by the test
                InsideWedgedEnumerate?.Invoke();
                EnumerateInFlight = false;
                NativeEvents.Enqueue("enumerate-returned");
            }

            return Devices;
        }

        public bool TryInit(string? deviceName, out string? error)
        {
            InitCallCount++;
            RequestedDeviceName = deviceName;
            if (TryInitRelease is { } release)
            {
                TryInitInFlight = true;
                release.Wait(); // wallclock-allow: the wedge IS the subject — the wedged native call, released by the test, never by timing
                InsideWedgedInit?.Invoke(); // still INSIDE the native call, still holding _initLock
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
            DisposingThreadName = Thread.CurrentThread.Name;
            DisposingThreadId = Environment.CurrentManagedThreadId;
            DisposingThreadIsBackground = Thread.CurrentThread.IsBackground;
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
