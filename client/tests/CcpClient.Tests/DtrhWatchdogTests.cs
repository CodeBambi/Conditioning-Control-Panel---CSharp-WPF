using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-027 slice b5: the watchdog state machine (DtrhWatchdog) + the native signal's typed
/// capability outcomes (DtrhProcessFailed). Sizing case = W17: renderer kill → bridge beats
/// ~28s more → silence; detection must fire at last-beat + threshold, recovery must be
/// exactly-once with the episode latch (the latent WPF double-Recover bug, consult C1).
/// </summary>
public class DtrhWatchdogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    // ---------- guards: never fire on a not-live / exiting instance ----------

    [Fact]
    public void Tick_BeforeReady_NeverTrips()
    {
        var w = new DtrhWatchdog();
        // A still-loading page can't false-trip (WPF IsReady guard :830).
        Assert.Null(w.Tick(T0 + TimeSpan.FromMinutes(10), runActive: true));
        Assert.Null(w.Tick(T0 + TimeSpan.FromMinutes(10), runActive: false));
    }

    [Fact]
    public void Signals_DuringExit_AreDropped()
    {
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        w.BeginExit();
        // A process dying mid-wind-down is expected, not a recovery case.
        Assert.Null(w.OnProcessFailed(1));
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(60), runActive: true));
    }

    // ---------- silence thresholds (10s run / 20s hub, WPF :833-836) ----------

    [Fact]
    public void Tick_MidRun_TripsAtTenSeconds_NotBefore()
    {
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(9.9), runActive: true));
        var outcome = w.Tick(T0 + TimeSpan.FromSeconds(10.1), runActive: true);
        var relaunch = Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(outcome);
        Assert.Contains("heartbeat-silent", relaunch.Reason);
        Assert.Contains("mid-run", relaunch.Reason);
    }

    [Fact]
    public void Tick_Hub_TripsAtTwentySeconds_NotAtTen()
    {
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        // 15s silent would trip mid-run but NOT in the idling Warren (WPF :834).
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(15), runActive: false));
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(19.9), runActive: false));
        var outcome = w.Tick(T0 + TimeSpan.FromSeconds(20.1), runActive: false);
        Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(outcome);
    }

    [Fact]
    public void Tick_W17SizingCase_BlackButBeatingWindowThenThreshold()
    {
        // W17: kill at t=0; beats continue ~28s (last beat t=28); silence from ~t=38.
        // Mid-run detection = last-beat + 10s = ~t=38.1 — never claimed fast (the native
        // ProcessFailed signal is the fast path; this net catches JS-main-thread wedges).
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        w.Heartbeat(T0 + TimeSpan.FromSeconds(28)); // the last zombie beat
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(37.9), runActive: true));
        Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(
            w.Tick(T0 + TimeSpan.FromSeconds(38.1), runActive: true));
    }

    [Fact]
    public void Heartbeat_ResumeResetsSilence()
    {
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(9), runActive: true));
        w.Heartbeat(T0 + TimeSpan.FromSeconds(9)); // a beat lands — the clock restarts
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(18), runActive: true)); // 9s silent
        Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(
            w.Tick(T0 + TimeSpan.FromSeconds(19.5), runActive: true)); // 10.5s silent
    }

    [Fact]
    public void Tick_LiveSessionWithRegularBeats_NeverFires()
    {
        // b4 run-lifecycle regression guard: a healthy live session (beats every ~2s)
        // must NEVER trip the watchdog, run or hub.
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        var now = T0;
        for (var i = 0; i < 600; i++)
        {
            now += TimeSpan.FromSeconds(2);
            w.Heartbeat(now);
            Assert.Null(w.Tick(now + TimeSpan.FromSeconds(4), runActive: i % 2 == 0));
        }
    }

    // ---------- relaunch-once + the recovery-episode latch ----------

    [Fact]
    public void ProcessFailedBurst_ExactlyOneRelaunch_RestDropped()
    {
        // W17 kill burst: 7 msedgewebview2 processes die → N ProcessFailed events.
        // Without the latch the second event would consume the relaunch AND tear down the
        // replacement (the latent WPF bug, consult CORRECTION 1).
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        var outcomes = Enumerable.Range(0, 7).Select(_ => w.OnProcessFailed(1)).ToList();
        var relaunch = Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(outcomes[0]);
        Assert.Equal(1, relaunch.Generation);
        Assert.All(outcomes.Skip(1), Assert.Null);
        Assert.True(w.RelaunchSpent);
        Assert.Equal(1, w.PendingGeneration);
        // And the watch is parked while the successor boots (no phantom trips).
        Assert.Null(w.Tick(T0 + TimeSpan.FromMinutes(5), runActive: true));
    }

    [Fact]
    public void Relaunch_ThenFailureAfterLive_TypedExhaustion_NeverARestartLoop()
    {
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(w.OnProcessFailed(1));

        // The relaunched instance reports ready — the episode latch clears.
        w.MarkLive(T0 + TimeSpan.FromSeconds(8));
        Assert.Equal(0, w.PendingGeneration);

        // A second kill AFTER the relaunched instance is live: typed exhaustion → the
        // caller closes honestly. WPF 'giving up' :864 parity.
        var exhausted = Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Exhausted>(w.OnProcessFailed(0));
        Assert.Contains("process-failed:BrowserProcessExited", exhausted.Reason);
        // And nothing fires again afterwards — never a restart loop.
        Assert.Null(w.OnProcessFailed(1));
        Assert.Null(w.Tick(T0 + TimeSpan.FromMinutes(10), runActive: true));
    }

    [Fact]
    public void MixedSignals_SameEpisode_LatchedAcrossBothKinds()
    {
        // ProcessFailed fires AND the heartbeats stop — the SAME episode must not consume
        // two recoveries via two signal paths.
        var w = new DtrhWatchdog();
        w.MarkLive(T0);
        Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(w.OnProcessFailed(1));
        Assert.Null(w.Tick(T0 + TimeSpan.FromSeconds(60), runActive: true));
    }

    [Fact]
    public void KindNames_MatchWebView2Idl_UnknownStaysNumeric()
    {
        Assert.Equal("BrowserProcessExited", DtrhWatchdog.KindName(0));
        Assert.Equal("RenderProcessExited", DtrhWatchdog.KindName(1));
        Assert.Equal("RenderProcessUnresponsive", DtrhWatchdog.KindName(2));
        Assert.Equal("GpuProcessExited", DtrhWatchdog.KindName(6));
        Assert.Equal("UnknownProcessExited", DtrhWatchdog.KindName(9));
        Assert.Equal("kind-42", DtrhWatchdog.KindName(42)); // forward tolerance
    }

    // ---------- native signal capability honesty (never faked) ----------

    [Fact]
    public void ProcessFailedAttach_TypedUnavailable_NeverThrows_NeverFakes()
    {
        var outcome = DtrhProcessFailed.TryAttach(IntPtr.Zero, _ => { });
        var unavailable = Assert.IsType<DtrhProcessFailed.AttachOutcome.Unavailable>(outcome);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("invalid-handle", unavailable.Code);
        }
        else
        {
            // Linux: the platform check fires first — the honest named limit.
            Assert.Equal("unsupported-platform", unavailable.Code);
        }

        Assert.False(string.IsNullOrWhiteSpace(unavailable.Detail));
    }
}
