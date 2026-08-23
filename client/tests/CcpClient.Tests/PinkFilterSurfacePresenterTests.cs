using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// How a CONTINUOUS effect reaches a surface, and how it stays there.
///
/// <para>The THIRD consumer of the overlay capability and of <see cref="OverlaySurfaceSet"/>, and
/// the first one whose surface has no end. Flash Images places up to ten rectangles for six
/// seconds; Subliminals places one card for a fifth of a second; this places one full-screen tint
/// and leaves it. Exactly one thing in the shared set had to change for that — a placement may have
/// no lifetime — and the facts below are what says so.</para>
///
/// <para>Everything runs on an injected clock: not one wall-clock wait anywhere in this file.</para>
/// </summary>
public class PinkFilterSurfacePresenterTests
{
    private static readonly OverlayBounds Display = new(0, 0, 1920, 1080);

    private static readonly PinkFilterTint Tint = new(255, 105, 180, 10);

    // ---------------------------------------------------------------------------------
    //  one full-screen tint, at the module's own opacity, click-through
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheTintCoversTheWholeDisplay_AtTheModulesOpacity_AndPassesClicksThrough()
    {
        var rig = new Rig();

        var outcome = rig.Presenter.Engage(Tint);

        Assert.IsType<CapabilityState.Available>(outcome);
        var presence = Assert.Single(rig.Presences);
        var request = Assert.Single(presence.Requests);

        // WPF sizes the tint window to the screen's own bounds (OverlayService.cs:1149-1157,
        // :1195-1199) and its opacity is LINEAR — its own comment at :1174 says so.
        Assert.Equal(Display, request.Bounds);
        Assert.Equal(0.10, request.Opacity, precision: 6);

        // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE, IsHitTestVisible=false (OverlayService.cs:1194,
        // :1210). A tint over the WHOLE screen for a WHOLE session that caught clicks would end
        // the user's ability to use their computer.
        Assert.True(request.ClickThrough);
        Assert.True(rig.Presenter.Showing);
        Assert.Equal(Tint, rig.Presenter.CurrentTint);
    }

    [Fact]
    public void TheTintIsPresentedBeforeItIsPainted_AndTheFrameIsTheColourTheModuleAskedFor()
    {
        var rig = new Rig();

        rig.Presenter.Engage(new PinkFilterTint(0x11, 0x22, 0x33, 25));

        var presence = Assert.Single(rig.Presences);
        Assert.Equal(["present", "paint"], presence.Calls);

        // The frame is the whole display in ONE colour — OverlayFrame.Solid is this module's entire
        // rasteriser, because the payload is a colour. ColourAt returns the OS's own COLORREF
        // (0x00BBGGRR), so this is the byte order the blit will really see.
        var frame = Assert.Single(presence.Frames);
        Assert.Equal(Display.Width, frame.Width);
        Assert.Equal(Display.Height, frame.Height);
        Assert.Equal(0x00332211u, frame.ColourAt(0, 0));
        Assert.Equal(0x00332211u, frame.ColourAt(Display.Width - 1, Display.Height - 1));
    }

    // ---------------------------------------------------------------------------------
    //  the one thing the shared surface set had to learn
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AContinuousSurfaceGetsNoLifetimeTimerAtAll_SoNoAmountOfClockTakesItDown()
    {
        var rig = new Rig();

        rig.Presenter.Engage(Tint);

        // TWO timers on the clock, and neither is a lifetime: the 5 s topmost cadence and the 500 ms
        // reconcile that watches for sustained band loss (OverlaySurfaceSet). A paced module's
        // placement arms a retirement here; this one must not, and a very large TimeSpan would not
        // be the same thing: it would be a timer that exists, that a stop has to cancel, and that
        // fires in some later session.
        Assert.Equal(2, rig.Clock.PendingCount);

        rig.Clock.Advance(TimeSpan.FromHours(4));

        Assert.True(rig.Presenter.Showing);
        Assert.Equal(0, Assert.Single(rig.Presences).WithdrawCalls);
    }

    [Fact]
    public void ReTinting_ReusesTheWindowThatIsAlreadyUp_RatherThanBuildingASecondOne()
    {
        var rig = new Rig();

        rig.Presenter.Engage(Tint);
        rig.Presenter.Engage(Tint with { OpacityPercent = 40 });

        // WPF's third reconcile arm updates the LIVE window's brush (OverlayService.cs:434-437 ->
        // :1252-1268); it does not create a second window. The presence pool is what makes that
        // true here, and the second engage must go through the same slot.
        var presence = Assert.Single(rig.Presences);
        Assert.Equal(["present", "paint", "present", "paint"], presence.Calls);
        Assert.Equal(0.10, presence.Requests[0].Opacity, precision: 6);
        Assert.Equal(0.40, presence.Requests[1].Opacity, precision: 6);
        Assert.Equal(40, rig.Presenter.CurrentTint!.Value.OpacityPercent);
    }

    // ---------------------------------------------------------------------------------
    //  the band, which only a long-lived surface has to fight for
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheTopmostBandIsReAssertedOnWpfsOwnCadence_ForAsLongAsTheTintIsUp()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);
        var presence = Assert.Single(rig.Presences);

        rig.Clock.Advance(PinkFilterSurfacePresenter.TopmostCadence);
        Assert.Equal(1, presence.ReassertCalls);

        rig.Clock.Advance(PinkFilterSurfacePresenter.TopmostCadence);
        Assert.Equal(2, presence.ReassertCalls);

        // WPF's periodic unconditional kick is ten ticks of its 500 ms reconcile loop
        // (OverlayService.cs:666-671). A layer that is up for a whole session loses the band to
        // anything that later claims it, which is why the other two modules can do without one.
        Assert.Equal(TimeSpan.FromSeconds(5), PinkFilterSurfacePresenter.TopmostCadence);
    }

    [Fact]
    public void Withdrawing_TakesItOffAtOnce_AndLeavesNoCadenceBehind()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);
        var presence = Assert.Single(rig.Presences);

        rig.Presenter.Withdraw();

        Assert.False(rig.Presenter.Showing);
        Assert.Null(rig.Presenter.CurrentTint);
        Assert.Equal(1, presence.WithdrawCalls);

        // The cadence re-arms only while something is up, so a stopped session leaves no timer
        // behind — the property every stop fact in this port is built on.
        rig.Clock.Advance(PinkFilterSurfacePresenter.TopmostCadence * 10);
        Assert.Equal(0, presence.ReassertCalls);
    }

    // ---------------------------------------------------------------------------------
    //  refusals, kept verbatim
    // ---------------------------------------------------------------------------------

    [Fact]
    public void WithNoDisplay_NothingIsPlaced_AndTheRefusalIsTypedRatherThanSilent()
    {
        var rig = new Rig { Display = () => null };

        var outcome = rig.Presenter.Engage(Tint);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(OverlayReasonCodes.OverlayNoDisplay, refusal.Reason.Code);
        Assert.False(rig.Presenter.Showing);
        Assert.Null(rig.Presenter.CurrentTint);
    }

    [Fact]
    public void ASurfaceThatCannotHoldTheFrame_IsTakenBackOff_AndTheOutcomeIsThePAINTSRefusal()
    {
        var rig = new Rig { PaintRefusal = OverlayReasonCodes.OverlayContentNotHeld };

        var outcome = rig.Presenter.Engage(Tint);

        // Present said Available and Paint did not. Reporting the present's Available here would
        // claim a tint that the surface set has already withdrawn, so the PAINT's refusal is the
        // outcome — which is the only one of the two that is still true.
        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(OverlayReasonCodes.OverlayContentNotHeld, refusal.Reason.Code);
        Assert.False(rig.Presenter.Showing);
        Assert.Null(rig.Presenter.CurrentTint);
        Assert.Equal(1, Assert.Single(rig.Presences).WithdrawCalls);
    }

    [Fact]
    public void ABackendThatRefusesToPresent_IsReportedWordForWord_AndNothingIsClaimed()
    {
        var rig = new Rig { PresentRefusal = OverlayReasonCodes.OverlayMechanismAbsent };

        var outcome = rig.Presenter.Engage(Tint);

        // On Linux this is the whole module: the backend refuses by design and names its own
        // manual gate. The presenter adds nothing to that sentence and subtracts nothing from it.
        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, refusal.Reason.Code);
        Assert.False(rig.Presenter.Showing);
        Assert.Equal(["present"], Assert.Single(rig.Presences).Calls);
    }

    [Fact]
    public void AfterDispose_NothingIsPlaced_AndTheRefusalNamesTheDisposal()
    {
        var rig = new Rig();
        rig.Presenter.Engage(Tint);

        rig.Presenter.Dispose();
        var outcome = rig.Presenter.Engage(Tint);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(OverlayReasonCodes.OverlayPresenceDisposed, refusal.Reason.Code);
        Assert.False(rig.Presenter.Showing);
    }

    // =====================================================================================

    private sealed class Rig
    {
        private readonly Lazy<PinkFilterSurfacePresenter> _presenter;

        public Rig()
        {
            Display = () => PinkFilterSurfacePresenterTests.Display;
            _presenter = new Lazy<PinkFilterSurfacePresenter>(() => new PinkFilterSurfacePresenter(
                Clock,
                action => action(),
                () =>
                {
                    var presence = new RecordingPresence
                    {
                        PaintRefusal = PaintRefusal,
                        PresentRefusal = PresentRefusal,
                    };
                    Presences.Add(presence);
                    return presence;
                },
                () => Display()));
        }

        public ManualClock Clock { get; } = new();

        public List<RecordingPresence> Presences { get; } = [];

        public string? PaintRefusal { get; init; }

        public string? PresentRefusal { get; init; }

        public Func<OverlayBounds?> Display { get; init; }

        public PinkFilterSurfacePresenter Presenter => _presenter.Value;
    }

    /// <summary>An overlay that records what it was asked to do, in order, and never touches a screen.</summary>
    private sealed class RecordingPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public List<string> Calls { get; } = [];

        public List<OverlaySurfaceRequest> Requests { get; } = [];

        public List<OverlayFrame> Frames { get; } = [];

        public int WithdrawCalls { get; private set; }

        public int ReassertCalls { get; private set; }

        public string? PaintRefusal { get; init; }

        public string? PresentRefusal { get; init; }

        public bool IsPresenting => _current is not null;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            Calls.Add("present");
            Requests.Add(request);
            if (PresentRefusal is not null)
            {
                return new CapabilityState.Unavailable(
                    new CapabilityReason(PresentRefusal, "recording presence: refused to present"));
            }

            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame)
        {
            Calls.Add("paint");
            Frames.Add(frame);
            return PaintRefusal is null
                ? new CapabilityState.Available("recording presence: painted")
                : new CapabilityState.Unavailable(new CapabilityReason(PaintRefusal, "recording presence: refused"));
        }

        public void Reassert()
        {
            Calls.Add("reassert");
            ReassertCalls++;
        }

        public CapabilityState SetClickThrough(bool clickThrough)
        {
            Calls.Add("set-click-through");
            return new CapabilityState.Available("recording presence: flipped");
        }

        public CapabilityState Withdraw()
        {
            Calls.Add("withdraw");
            WithdrawCalls++;
            _current = null;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _current = null;
    }

    /// <summary>The manual clock, in the shape every module test shares. Zero wall-clock.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

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
