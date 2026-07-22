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
        int maxSfx = 8, string[]? devices = null)
    {
        var backend = new FakeBackend { Devices = devices ?? ["RDP Sink"] };
        var duck = new FakeDuckSink();
        var clock = new ManualClock();
        var arb = new SoundArbitration(backend, duck, clock, new SoundArbitrationOptions
        {
            MaxSfxVoices = maxSfx,
            DuckWatchdog = TimeSpan.FromMinutes(5),
            VoicePacingDelay = TimeSpan.FromSeconds(2),
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
        public Action<FakePlayer>? PlayerHook { get; set; }

        public IReadOnlyList<string> EnumerateDevices() => Devices;

        public bool TryInit(string? deviceName, out string? error)
        {
            RequestedDeviceName = deviceName;
            error = null;
            return true;
        }

        public IAudioPlayer CreatePlayer(string path, float volume)
        {
            var p = new FakePlayer(path, volume);
            PlayerHook?.Invoke(p);
            Players.Add(p);
            return p;
        }

        public void Dispose() { }
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
