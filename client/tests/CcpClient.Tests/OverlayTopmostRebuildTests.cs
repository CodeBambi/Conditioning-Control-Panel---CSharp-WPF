using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>What happens when something else wins the always-on-top band.</b>
///
/// <para>Before this, nothing did: a tint or a spiral that lost the band stayed gone for the rest of
/// the run, and the port said so itself (<c>PinkFilterSurfacePresenter</c>'s divergence D79). WPF
/// does not leave it — its reconcile loop counts six consecutive losing ticks of a 500 ms timer,
/// three seconds, and then recreates the overlay windows
/// (<c>Services/Notifications/OverlayService.cs:678-708</c>).</para>
///
/// <para><b>The trap these facts exist to hold shut.</b> The loss is detected by READING BACK the
/// window's extended style, never by believing a call. <c>SetWindowPos(HWND_TOPMOST)</c> returns
/// TRUE and silently declines to apply <c>WS_EX_TOPMOST</c> when the process holds no
/// <c>SetForegroundWindow</c> permission — measured in this repository, and the reason
/// <see cref="RealDesktopWindowFloor"/> keeps a hidden window alive on every real-desktop test
/// thread. A rebuild loop that could not tell a REFUSED band from a LOST one would tear its window
/// down every three seconds for the whole session, so the escalation is capped at
/// <see cref="OverlaySurfaceSet.MaxRebuildAttempts"/> and then goes on re-asserting cheaply.</para>
///
/// <para>Every tick here is an injected clock tick. Not one wall-clock wait anywhere in this file.</para>
/// </summary>
public class OverlayTopmostRebuildTests
{
    private static readonly OverlayBounds Display = new(0, 0, 1920, 1080);

    private static readonly PinkFilterTint Tint = new(255, 105, 180, 10);

    private static readonly TimeSpan Tick = OverlaySurfaceSet.ReconcileCadence;

    // ---------------------------------------------------------------------------------
    //  three seconds of CONTINUOUS loss, and not a moment sooner
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheRuleIsThreeSeconds_SixTicksOfWpfsFiveHundredMillisecondLoop_CappedAtThreeRebuilds()
    {
        // The numbers themselves, not a re-derivation of them: OverlayService.cs:682 comments its
        // own arithmetic ("6 x 500ms = 3 seconds of continuous loss") and :212 sets
        // MaxRecreateAttempts = 3. Every other fact in this file counts in terms of these constants,
        // so this is the one that notices if a constant moves.
        Assert.Equal(TimeSpan.FromMilliseconds(500), OverlaySurfaceSet.ReconcileCadence);
        Assert.Equal(6, OverlaySurfaceSet.LossTicksBeforeRebuild);
        Assert.Equal(3, OverlaySurfaceSet.MaxRebuildAttempts);
        Assert.Equal(
            TimeSpan.FromSeconds(3),
            OverlaySurfaceSet.ReconcileCadence * OverlaySurfaceSet.LossTicksBeforeRebuild);
    }

    [Fact]
    public void SixConsecutiveLosingTicks_RebuildTheTint_AndFiveDoNot()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);
        var presence = Assert.Single(rig.Presences);
        Assert.Equal(1, presence.PresentCalls);

        rig.TopmostHeld = false;

        // Five ticks is 2.5 seconds. WPF's rule is SIX (OverlayService.cs:681-682, "6 x 500ms =
        // 3 seconds of continuous loss"), so nothing has been rebuilt yet.
        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild - 1; i++)
        {
            rig.Clock.Advance(Tick);
        }

        Assert.Equal(0, rig.Presenter.Rebuilds);
        Assert.Equal(1, presence.PresentCalls);

        rig.Clock.Advance(Tick);

        Assert.Equal(1, rig.Presenter.Rebuilds);

        // The rebuild is a re-PRESENT — the one call that re-reads the extended style, the z-order
        // and the hit test from the OS — on the SAME pooled window, which is what makes it a
        // recovery rather than a second tint (OverlaySurfaceSet.Place).
        Assert.Equal(2, presence.PresentCalls);
        Assert.Single(rig.Presences);
        Assert.True(rig.Presenter.Showing);
        Assert.Equal(Tint, rig.Presenter.CurrentTint);
    }

    [Fact]
    public void OneTickThatRegainsTheBand_ResetsTheStreak_SoAFlickerNeverRebuilds()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);

        rig.TopmostHeld = false;
        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild - 1; i++)
        {
            rig.Clock.Advance(Tick);
        }

        Assert.Equal(OverlaySurfaceSet.LossTicksBeforeRebuild - 1, rig.Presenter.TopmostLossTicks);

        // One tick with the band back: "topmost regained — allow recreation to help again on a
        // future loss" (OverlayService.cs:706-708).
        rig.TopmostHeld = true;
        rig.Clock.Advance(Tick);
        Assert.Equal(0, rig.Presenter.TopmostLossTicks);

        // And now five more losses. The streak restarted, so the run is 5 and not 10.
        rig.TopmostHeld = false;
        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild - 1; i++)
        {
            rig.Clock.Advance(Tick);
        }

        Assert.Equal(0, rig.Presenter.Rebuilds);
    }

    [Fact]
    public void ABandThatIsHeld_IsNeverRebuilt_HoweverLongTheSessionRuns()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);
        rig.TopmostHeld = true;

        // Two minutes of ticks.
        for (var i = 0; i < 240; i++)
        {
            rig.Clock.Advance(Tick);
        }

        Assert.Equal(0, rig.Presenter.Rebuilds);
        Assert.Equal(0, rig.Presenter.RebuildsBackedOff);
        Assert.Equal(1, Assert.Single(rig.Presences).PresentCalls);
    }

    // ---------------------------------------------------------------------------------
    //  the trap: a refused band must not be fought forever
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ABandTheOsKeepsRefusing_StopsBeingRebuiltAfterThreeAttempts_AndIsReassertedInstead()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);
        var presence = Assert.Single(rig.Presences);

        // The refusal shape, exactly: every re-assertion "succeeds" — the presence records the call
        // and returns nothing, as SetWindowPos does — and the style read-back keeps saying the bit
        // is not there. This is what a process without foreground permission measures
        // (Win32OverlayPresence.cs:504-517).
        rig.TopmostHeld = false;

        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild * 20; i++)
        {
            rig.Clock.Advance(Tick);
        }

        // Twenty escalations came due; only WPF's three spent a rebuild (OverlayService.cs:212).
        Assert.Equal(OverlaySurfaceSet.MaxRebuildAttempts, rig.Presenter.Rebuilds);
        Assert.Equal(20 - OverlaySurfaceSet.MaxRebuildAttempts, rig.Presenter.RebuildsBackedOff);

        // Backed off is not given up: the cheap re-assertion goes on every single losing tick, which
        // is upstream's own fallback (:698-704) and is what recovers the band if the refusal lifts.
        Assert.True(presence.ReassertCalls >= OverlaySurfaceSet.LossTicksBeforeRebuild * 20);

        // And the surface is still up. A refusal is lived with, never answered by taking the user's
        // tint away.
        Assert.True(rig.Presenter.Showing);
    }

    [Fact]
    public void RegainingTheBand_RestoresTheRebuildBudget_SoALaterLossCanStillBeRepaired()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);

        rig.TopmostHeld = false;
        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild * OverlaySurfaceSet.MaxRebuildAttempts; i++)
        {
            rig.Clock.Advance(Tick);
        }

        Assert.Equal(OverlaySurfaceSet.MaxRebuildAttempts, rig.Presenter.Rebuilds);

        rig.TopmostHeld = true;
        rig.Clock.Advance(Tick);

        rig.TopmostHeld = false;
        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild; i++)
        {
            rig.Clock.Advance(Tick);
        }

        Assert.Equal(OverlaySurfaceSet.MaxRebuildAttempts + 1, rig.Presenter.Rebuilds);
        Assert.Equal(0, rig.Presenter.RebuildsBackedOff);
    }

    // ---------------------------------------------------------------------------------
    //  the detection is a read-back, and "nothing to read" is not a loss
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AReadBackThatCannotAnswer_NeverCountsAsALoss_AndNeverSpendsARebuild()
    {
        var rig = new Rig { TopmostHeld = null };
        rig.Presenter.Engage(Tint);

        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild * 4; i++)
        {
            rig.Clock.Advance(Tick);
        }

        Assert.Equal(0, rig.Presenter.Rebuilds);
        Assert.Equal(0, rig.Presenter.RebuildsBackedOff);

        // Not one tick was counted as a loss either, which is the part that matters: the streak is
        // what a rebuild is spent from. (The surface is still re-asserted by the UNCONDITIONAL 5 s
        // kick — a different rule, WPF's :713-716, and one that asks the OS nothing.)
        Assert.Equal(0, rig.Presenter.TopmostLossTicks);
    }

    [Fact]
    public void TheProductReadBack_AnswersNullForAnyPresenceWithNoWindowToAsk()
    {
        // The Linux backend, and every test double. Null is "nothing here can be asked" — never
        // false, because false is what costs a rebuild.
        using var unsupported = OverlayPresenceFactory.CreateFor(OverlayHostPlatform.Linux);
        Assert.Null(OverlaySurfaceSet.TopmostHeldByOs(unsupported));

        using var recording = new RecordingPresence();
        Assert.Null(OverlaySurfaceSet.TopmostHeldByOs(recording));
    }

    [Fact]
    public void EveryLosingTickReAssertsTheLostSurfaceFirst_BeforeAnyEscalation()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);
        var presence = Assert.Single(rig.Presences);

        rig.TopmostHeld = false;
        rig.Clock.Advance(Tick);

        // WPF's non-forced pass re-pins exactly the windows whose read-back lost the bit
        // (OverlayService.cs:2864-2880). One tick, one re-assertion, no rebuild.
        Assert.Equal(1, presence.ReassertCalls);
        Assert.Equal(0, rig.Presenter.Rebuilds);
    }

    // ---------------------------------------------------------------------------------
    //  no timer outlives what it was watching
    // ---------------------------------------------------------------------------------

    [Fact]
    public void WithdrawingTheTint_LeavesNoReconcileTimerBehind()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);
        rig.TopmostHeld = false;
        rig.Clock.Advance(Tick);

        rig.Presenter.Withdraw();
        var pending = rig.Clock.PendingCount;

        // Nothing re-arms, and a session that stopped hours ago is not still asking the OS about a
        // window it took down — the property every stop fact in this port is built on.
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, pending);
        Assert.Equal(0, rig.Clock.PendingCount);
        Assert.Equal(0, rig.Presenter.Rebuilds);
    }

    [Fact]
    public void ASurfaceSetWithNoRebuild_RunsNoReconcileLoopAtAll()
    {
        // Flash Images and Subliminals. Upstream rebuilds only its PERSISTENT overlays
        // (OverlayService.cs:692-695); a flash window lives six seconds and has its own re-raise
        // (FlashService.cs:206-243), so a 2 Hz style probe for it would be cost with no outcome.
        var clock = new ManualClock();
        var presence = new RecordingPresence();
        using var set = new OverlaySurfaceSet(
            clock, action => action(), () => presence, maxSurfaces: 1, topmostCadence: null);

        var slot = set.Acquire();
        Assert.NotNull(slot);
        Assert.True(set.Place(slot, new OverlaySurfaceRequest(Display, 0.5, ClickThrough: true),
            OverlayFrame.Solid(4, 4, 0, 0, 0), lifetime: null));

        Assert.Equal(0, clock.PendingCount);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, set.RebuildsRequested);
    }

    // ---------------------------------------------------------------------------------
    //  the spiral rebuilds too, and does not restart its spin
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheSpiralIsRebuiltOnTheSameRule_AndKeepsTheFrameItWasOn()
    {
        var clock = new ManualClock();
        var presence = new RecordingPresence();
        var frames = new StubSpiralFrames(frameCount: 8, delay: TimeSpan.FromMilliseconds(100));
        bool? held = true;
        using var presenter = new SpiralSurfacePresenter(
            clock, action => action(), () => presence, frames, () => Display, _ => held);

        presenter.Engage("spiral.gif", new SpiralPresentation(50));
        Assert.True(presenter.Showing);

        // Three frame advances at 100 ms — the clip is on frame 3 when the band goes.
        for (var i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(3, presenter.FrameIndex);

        held = false;
        for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild; i++)
        {
            clock.Advance(Tick);
        }

        Assert.Equal(1, presenter.Rebuilds);
        Assert.True(presenter.Showing);

        // ONE decoder open for the whole run: a rebuild that re-decoded the file would restart the
        // spin from frame zero, which the user would see.
        Assert.Equal(1, frames.Opens);
        Assert.True(presenter.FrameIndex > 0);
    }

    // ---------------------------------------------------------------------------------
    //  rig
    // ---------------------------------------------------------------------------------

    private sealed class Rig
    {
        private readonly Lazy<PinkFilterSurfacePresenter> _presenter;

        public Rig()
        {
            _presenter = new Lazy<PinkFilterSurfacePresenter>(() => new PinkFilterSurfacePresenter(
                Clock,
                action => action(),
                () =>
                {
                    var presence = new RecordingPresence();
                    Presences.Add(presence);
                    return presence;
                },
                () => Display,
                _ => TopmostHeld));
        }

        public ManualClock Clock { get; } = new();

        public List<RecordingPresence> Presences { get; } = [];

        /// <summary>What the OS says about the band right now. True held, false lost, null
        /// unaskable — the three answers the product read-back has.</summary>
        public bool? TopmostHeld { get; set; } = true;

        public PinkFilterSurfacePresenter Presenter => _presenter.Value;
    }

    /// <summary>An overlay that records what it was asked to do and never touches a screen. Its
    /// <c>Reassert</c> always "succeeds", which is the whole point: the band's fate is decided by the
    /// rig's read-back, not by this call returning.</summary>
    private sealed class RecordingPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public int PresentCalls { get; private set; }

        public int ReassertCalls { get; private set; }

        public int WithdrawCalls { get; private set; }

        public bool IsPresenting => _current is not null;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            PresentCalls++;
            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame) =>
            new CapabilityState.Available("recording presence: painted");

        public void Reassert() => ReassertCalls++;

        public CapabilityState SetClickThrough(bool clickThrough) =>
            new CapabilityState.Available("recording presence: flipped");

        public CapabilityState Withdraw()
        {
            WithdrawCalls++;
            _current = null;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _current = null;
    }

    /// <summary>A clip with a known frame count, and a count of how many times it was OPENED.</summary>
    private sealed class StubSpiralFrames(int frameCount, TimeSpan delay) : ISpiralFrameSource
    {
        public int Opens { get; private set; }

        public ISpiralAnimation? Open(string path, int width, int height)
        {
            Opens++;
            return new StubAnimation(frameCount, delay, width, height);
        }

        private sealed class StubAnimation(int frames, TimeSpan delay, int width, int height) : ISpiralAnimation
        {
            public int FrameCount => frames;

            public TimeSpan FrameDelay => delay;

            public OverlayFrame? Render(int index) => OverlayFrame.Solid(width, height, 0, 0, 0);

            public void Dispose()
            {
            }
        }
    }

    /// <summary>The manual clock, in the shape every module test shares. Zero wall-clock.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

        public int PendingCount
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count;
                }
            }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(ManualClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
