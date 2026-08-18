using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-101 — how a subliminal reaches a surface, and what happens when there is no surface to reach.
///
/// <para>The SECOND consumer of the overlay capability, and therefore the first evidence that
/// <see cref="OverlaySurfaceSet"/> is really shared rather than merely extracted: every fact below
/// runs through the same pooled slots, the same present-then-paint sequence and the same verbatim
/// outcome bookkeeping that <see cref="FlashSurfacePresenterTests"/> drives, and it asserts a
/// completely different geometry, cadence and lifetime on top of them.</para>
///
/// <para>Everything runs on an injected clock: not one wall-clock wait anywhere in this file.</para>
/// </summary>
public class SubliminalSurfacePresenterTests
{
    private static readonly OverlayBounds Display = new(0, 0, 1920, 1080);

    private static readonly SubliminalCard Card =
        new("GOOD GIRL", 80, SubliminalsEffect.CardLifetime(2));

    // ---------------------------------------------------------------------------------
    //  one full-screen card, at the module's own opacity, click-through
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ACardCoversTheWholeDisplay_AtTheModulesOpacity_AndPassesClicksThrough()
    {
        var rig = new Rig();

        rig.Presenter.Show(Card);

        var presence = Assert.Single(rig.Presences);
        var request = Assert.Single(presence.Requests);

        // WPF sizes the card window to the screen's own physical bounds with SetWindowPos
        // (SubliminalService.cs:1044-1046) — not a 40 %-of-monitor box like a flash. Sharing the
        // slot pool with the flash presenter must not have brought its geometry along.
        Assert.Equal(Display, request.Bounds);
        Assert.Equal(0.80, request.Opacity, precision: 6);

        // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE (SubliminalService.cs:1030-1034). A full-screen card
        // that caught clicks would swallow the user's input over everything for the whole envelope.
        Assert.True(request.ClickThrough);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, rig.Presenter.SurfacesShown);
    }

    [Fact]
    public void TheCardIsPresentedBeforeItIsPainted_AndTheOrderIsTheProductsNotTheTests()
    {
        var rig = new Rig();

        rig.Presenter.Show(Card);

        // SP-100's measurement, inherited: painting a HIDDEN layered window is discarded by the OS,
        // so show-then-paint is not a style choice. It is in the shared set, which is why this fact
        // and the flash's assert the same ordering about different content.
        var presence = Assert.Single(rig.Presences);
        Assert.Equal(["present", "paint"], presence.Calls);
    }

    [Fact]
    public void ACardWhosePaintFails_IsTakenBackOffTheScreen_RatherThanLeftAsARectangleOfNothing()
    {
        var rig = new Rig { PaintRefusal = OverlayReasonCodes.OverlayNotComposited };

        rig.Presenter.Show(Card);

        var presence = Assert.Single(rig.Presences);
        Assert.Equal(["present", "paint", "withdraw"], presence.Calls);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Presenter.SurfacesShown);

        var paint = Assert.IsType<CapabilityState.Unavailable>(rig.Presenter.LastPaint);
        Assert.Equal(OverlayReasonCodes.OverlayNotComposited, paint.Reason.Code);
    }

    // ---------------------------------------------------------------------------------
    //  WPF's envelope, exactly as long — and the divergence inside it
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheCardLeavesTheScreenWhenWpfsEnvelopeExpires_AndNotBefore()
    {
        var rig = new Rig();
        rig.Presenter.Show(Card);

        // 50 ms fade-in + 100 ms hold (the floor, at the shipped 2-frame dial) + 50 ms fade-out
        // (SubliminalService.cs:615-617, :1253-1255). Two hundred milliseconds, and a flash's six
        // seconds is thirty times longer — a shared lifetime constant would be visibly wrong.
        Assert.Equal(TimeSpan.FromMilliseconds(200), Card.Lifetime);

        rig.Clock.Advance(Card.Lifetime - TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Presences[0].WithdrawCalls);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, rig.Presences[0].WithdrawCalls);
    }

    [Fact]
    public void ALongerDurationDialReallyHoldsTheCardLonger()
    {
        var rig = new Rig();
        var longCard = new SubliminalCard("JUST OBEY", 80, SubliminalsEffect.CardLifetime(10));

        rig.Presenter.Show(longCard);
        rig.Clock.Advance(Card.Lifetime);

        // The default card would be gone by now; this one is not, because the dial is read per show.
        Assert.Equal(TimeSpan.FromMilliseconds(270), longCard.Lifetime);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);

        rig.Clock.Advance(longCard.Lifetime - Card.Lifetime);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
    }

    // ---------------------------------------------------------------------------------
    //  one card at a time, replaced rather than stacked
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ASecondCardArrivingEarly_ReplacesTheFirst_OnTheSameRecycledSurface()
    {
        var rig = new Rig();

        rig.Presenter.Show(Card);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
        rig.Presenter.Show(Card with { Text = "BIMBO DOLL" });

        // WPF keeps ONE keep-alive window and swaps its content, and its show-generation guard
        // exists precisely so the old envelope cannot blank the new phrase (SubliminalService.cs:
        // :29-32, :1285-1292). One presence, two cards, never two windows.
        var presence = Assert.Single(rig.Presences);
        Assert.Equal(2, presence.Requests.Count);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);
        Assert.Equal(2, rig.Presenter.SurfacesShown);
        Assert.Equal(["present", "paint", "withdraw", "present", "paint"], presence.Calls);
    }

    [Fact]
    public void TheReplacementCardGetsAWholeFreshEnvelope_NotTheRemainderOfTheOldOne()
    {
        var rig = new Rig();

        rig.Presenter.Show(Card);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(150));
        rig.Presenter.Show(Card with { Text = "BIMBO DOLL" });

        // The old card's lifetime would have expired 50 ms from here. It must not take the new one
        // down with it — that is the defect WPF's own generation guard was added for.
        rig.Clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Equal(1, rig.Presenter.LiveSurfaces);

        rig.Clock.Advance(Card.Lifetime);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
    }

    [Fact]
    public void SurfacesAreRecycledAcrossCards_RatherThanCreatedPerShowForever()
    {
        var rig = new Rig();

        for (var i = 0; i < 5; i++)
        {
            rig.Presenter.Show(Card);
            rig.Clock.Advance(Card.Lifetime);
        }

        // Each presence carries a registered window class and a top-level window. Five cards, one
        // window — the reason the pool is in the shared set and not in either presenter.
        Assert.Single(rig.Presences);
        Assert.Equal(5, rig.Presenter.SurfacesShown);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
    }

    // ---------------------------------------------------------------------------------
    //  no cadence: the difference from the flash presenter, asserted
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheCardLeavesNoTopmostCadenceTimerBehindIt_UnlikeAFlash()
    {
        var rig = new Rig();

        rig.Presenter.Show(Card);
        rig.Clock.Advance(FlashSurfacePresenter.TopmostCadence);

        // WPF re-asserts a flash window's band about once a second (FlashService.cs:206-243) and
        // does no such thing for a subliminal — it applies the topmost band once, per show
        // (SubliminalService.cs:1042-1046), which is what Present already does. A card is on screen
        // for a fifth of a second; a cadence timer would outlive the card it was armed for.
        Assert.Equal(0, rig.Presences[0].ReassertCalls);

        rig.Clock.Advance(Card.Lifetime);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  stop, and nowhere to draw
    // ---------------------------------------------------------------------------------

    [Fact]
    public void HideAll_TakesTheCardOffAtOnce_AndLeavesNoTimerOnTheClock()
    {
        var rig = new Rig();
        rig.Presenter.Show(Card);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);

        rig.Presenter.HideAll();

        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, rig.Presences[0].WithdrawCalls);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    [Fact]
    public void WithNoDisplayEnumerated_NothingIsAttempted_AndTheRefusalIsTyped()
    {
        var rig = new Rig { Display = () => null };

        rig.Presenter.Show(Card);

        // WPF enumerates monitors and places per monitor (SubliminalService.cs:629-631); with none
        // enumerated there is nowhere a card could legally go. Silence would be the wrong answer,
        // and so would an exception on a timer thread.
        var refusal = Assert.IsType<CapabilityState.Unavailable>(rig.Presenter.LastPlacement);
        Assert.Equal(OverlayReasonCodes.OverlayNoDisplay, refusal.Reason.Code);
        Assert.Equal(0, rig.Presenter.SurfacesShown);
    }

    [Fact]
    public void OnAPlatformWhoseBackendRefuses_TheBackendsOwnReasonIsWhatTheCallerGets()
    {
        // The shared set asks the presence itself rather than inventing a reason, so a Linux build's
        // refusal — which names the route and the manual gate — reaches the module panel verbatim
        // instead of being flattened into "no display".
        var rig = new Rig
        {
            Display = () => null,
            // The REAL Linux refusal the factory builds (OverlayPresenceFactory.CreateFor), not a
            // double of it: the point of the fact is that its own words reach the caller.
            PresenceFactory = () => OverlayPresenceFactory.CreateFor(OverlayHostPlatform.Linux),
        };

        rig.Presenter.Show(Card);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(rig.Presenter.LastPlacement);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, refusal.Reason.Code);
    }

    [Fact]
    public void APhraseThatCannotBeRasterised_ContributesNoSurface_AndIsCounted()
    {
        var rig = new Rig { Frames = new RefusingFrameSource() };

        rig.Presenter.Show(Card);

        // Never an exception: on a build with no rasteriser this is the ordinary outcome, and the
        // schedule behind the card keeps running whatever the screen can do.
        Assert.Equal(1, rig.Presenter.UnrasterisedPhrases);
        Assert.Equal(0, rig.Presenter.SurfacesShown);
        Assert.Empty(rig.Presences);
    }

    [Fact]
    public void ADisposedPresenter_ShowsNothingMore()
    {
        var rig = new Rig();
        rig.Presenter.Show(Card);
        rig.Presenter.Dispose();

        rig.Presenter.Show(Card);

        Assert.Equal(1, rig.Presenter.SurfacesShown);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
    }

    // ---------------------------------------------------------------------------------
    //  the pixels themselves (Windows-only mechanism, asserted on every platform)
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheCardRastersExactlyWhereGdiPlusExists_AndNowhereElse_WithoutEverThrowing()
    {
        var run = SubliminalCardObservations.Run;

        // The machine property IS the expectation. No skip, no platform branch, and both branches
        // assert something real: on Windows a card comes back, on Linux nothing does and nothing
        // throws — which is the same shape SP-100 used for the flash's decoder.
        Assert.Equal(run.RasteriserAvailable, run.Rendered);
        Assert.True(run.EmptySizeRefused,
            "a zero-sized card must come back null rather than throwing or allocating a buffer the surface would blit");
    }

    [Fact]
    public void TheRasterisedCard_IsTheSizeAskedFor_OpaqueToItsCorners()
    {
        var run = SubliminalCardObservations.Run;

        Assert.Equal(run.RasteriserAvailable ? SubliminalCardObservations.CardWidth : 0, run.Width);
        Assert.Equal(run.RasteriserAvailable ? SubliminalCardObservations.CardHeight : 0, run.Height);

        // SubBackgroundTransparent ships FALSE (AppSettings.cs:1333), so upstream's default card is
        // opaque edge to edge. On a surface with one uniform alpha and no per-pixel alpha (SP-100
        // D57) that is the only way the card is not a hole onto the desktop.
        Assert.Equal(run.RasteriserAvailable, run.CornersAreTheBackgroundColour);
    }

    [Fact]
    public void TheRasterisedCard_CarriesWpfsMagentaTextAndItsWhiteOutline()
    {
        var run = SubliminalCardObservations.Run;

        // AppSettings.cs:1340 (#FF00FF) and :1354 (#FFFFFF). Both must be present: the outline is
        // eight offset copies drawn UNDER the phrase (SubliminalService.cs:996-1008), so a card
        // with text and no outline means the outline loop was dropped, and one with outline and no
        // text means the phrase was drawn first and buried.
        Assert.Equal(run.RasteriserAvailable, run.CarriesTheTextColour);
        Assert.Equal(run.RasteriserAvailable, run.CarriesTheOutlineColour);
        Assert.Equal(run.RasteriserAvailable, run.SurvivedAPhraseLongerThanTheCard);
    }

    // ---------------------------------------------------------------------------------
    //  rig
    // ---------------------------------------------------------------------------------

    private sealed class Rig
    {
        private readonly Lazy<SubliminalSurfacePresenter> _presenter;

        public Rig()
        {
            PresenceFactory = () =>
            {
                var presence = new RecordingPresence { PaintRefusal = PaintRefusal };
                Presences.Add(presence);
                return presence;
            };

            Display = () => SubliminalSurfacePresenterTests.Display;
            Frames = new StubFrameSource();
            _presenter = new Lazy<SubliminalSurfacePresenter>(() => new SubliminalSurfacePresenter(
                Clock, action => action(), () => PresenceFactory(), Frames, () => Display()));
        }

        public ManualClock Clock { get; } = new();

        public List<RecordingPresence> Presences { get; } = [];

        public ISubliminalFrameSource Frames { get; init; }

        public string? PaintRefusal { get; init; }

        public Func<IOverlayPresence> PresenceFactory { get; set; }

        public Func<OverlayBounds?> Display { get; set; }

        public SubliminalSurfacePresenter Presenter => _presenter.Value;
    }

    /// <summary>An overlay that records what it was asked to do, in order, and never touches a screen.</summary>
    private sealed class RecordingPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public List<string> Calls { get; } = [];

        public List<OverlaySurfaceRequest> Requests { get; } = [];

        public int WithdrawCalls { get; private set; }

        public int ReassertCalls { get; private set; }

        public string? PaintRefusal { get; init; }

        public bool IsPresenting => _current is not null;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            Calls.Add("present");
            Requests.Add(request);
            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame)
        {
            Calls.Add("paint");
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

    /// <summary>A rasteriser with no GDI+: it answers the size the caller asks for.</summary>
    private sealed class StubFrameSource : ISubliminalFrameSource
    {
        public OverlayFrame? Render(string text, int width, int height) =>
            OverlayFrame.Solid(width, height, 0xFF, 0x00, 0xFF);
    }

    /// <summary>A rasteriser that can produce nothing — a build with no text stack.</summary>
    private sealed class RefusingFrameSource : ISubliminalFrameSource
    {
        public OverlayFrame? Render(string text, int width, int height) => null;
    }

    /// <summary>The manual clock, SP-098's shape. Zero wall-clock.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

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
