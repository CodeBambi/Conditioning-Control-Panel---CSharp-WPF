using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-117 — the <b>Visuals</b> row: WPF's sixth EFFECTS entry
/// (<c>Views/Tabs/StudioTabView.xaml.cs:496</c>) and the only row in the rack that is not a module.
///
/// <para><b>What these facts are actually about.</b> Not "a new module runs" — there is no module.
/// They are about three numbers that were <c>const</c> inside <see cref="FlashSurfacePresenter"/>
/// from SP-100 to SP-117 becoming the user's, reaching the operating system's own placement request,
/// and doing it on upstream's schedule: once per flash, applied to every surface of that flash.</para>
///
/// <para><b>Nothing here claims a human saw a flash at any size or opacity.</b> Every measurement is
/// of the request this process handed to an overlay double, or of the document behind it. The
/// composited half is <c>presentation-verified</c> and no automated step on any platform discharges
/// it.</para>
/// </summary>
public class VisualsModuleTests
{
    private static readonly OverlayBounds Display = new(0, 0, 1920, 1080);

    // =====================================================================================
    //  The document: WPF's clamps, WPF's defaults, and the two members it must NOT have
    // =====================================================================================

    [Fact]
    public void TheDocumentShipsWpfsOwnDefaults()
    {
        var document = new VisualsPresetDocument();

        // CCP.Core/Models/AppSettings.cs:839, :853, :925 — the three backing-field initialisers.
        Assert.Equal(100, document.ImageScalePercent);
        Assert.Equal(100, document.FlashOpacityPercent);
        Assert.Equal(5, document.FlashDurationSeconds);
    }

    [Theory]
    // WPF's Math.Clamp(value, 50, 250) (AppSettings.cs:849), at and outside both ends.
    [InlineData(-1, 50)]
    [InlineData(0, 50)]
    [InlineData(49, 50)]
    [InlineData(50, 50)]
    [InlineData(137, 137)]
    [InlineData(250, 250)]
    [InlineData(251, 250)]
    [InlineData(int.MaxValue, 250)]
    public void TheScaleDialClampsExactlyAsWpfDoes(int written, int expected)
    {
        var document = new VisualsPresetDocument { ImageScalePercent = written };
        Assert.Equal(expected, document.ImageScalePercent);
    }

    [Theory]
    // WPF's Math.Clamp(value, 10, 100) (AppSettings.cs:859). The floor of TEN also keeps every
    // reachable value clear of the invisible-surface request the overlay refuses to construct
    // (Overlay/OverlaySurfaceRequest.cs:30-37), so the two rules agree without either being bent.
    [InlineData(int.MinValue, 10)]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void TheOpacityDialClampsExactlyAsWpfDoes(int written, int expected)
    {
        var document = new VisualsPresetDocument { FlashOpacityPercent = written };
        Assert.Equal(expected, document.FlashOpacityPercent);
    }

    [Theory]
    // WPF's Math.Clamp(value, 1, 30) (AppSettings.cs:929).
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(17, 17)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    public void TheDurationDialClampsExactlyAsWpfDoes(int written, int expected)
    {
        var document = new VisualsPresetDocument { FlashDurationSeconds = written };
        Assert.Equal(expected, document.FlashDurationSeconds);
    }

    [Fact]
    public void TheDocumentHasNOENABLEMEMBER_BecauseUpstreamsVisualsHasNoMasterToggle()
    {
        // The census finding, made mechanical. Every other module's document opens with an enable;
        // this one must not grow one by accident, because an enable here would be a switch upstream
        // does not have on a row upstream deliberately gives no dot
        // (Views/Tabs/StudioTabView.xaml.cs:494-496).
        var members = typeof(VisualsPresetDocument)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("Enabled", members);

        // And the two dials that are ABSENT rather than inert (D93): the fade slider upstream wires
        // to nothing, and the audio link a silent flash cannot honour.
        Assert.DoesNotContain("FadeDuration", members);
        Assert.DoesNotContain("FadePercent", members);
        Assert.DoesNotContain("FlashAudioEnabled", members);
        Assert.DoesNotContain("AudioLinked", members);
    }

    // =====================================================================================
    //  FlashDraw: the arithmetic, and the boundary that refuses rather than clamps
    // =====================================================================================

    [Fact]
    public void TheDefaultReadingIsWhatEveryFlashDrewWithBetweenSp100AndSp117()
    {
        Assert.Equal(FlashSurfacePresenter.ImageScalePercent, FlashDraw.Defaults.ScalePercent);
        Assert.Equal(FlashSurfacePresenter.OpacityPercent, FlashDraw.Defaults.OpacityPercent);
        Assert.Equal(FlashSurfacePresenter.FlashDurationSeconds, FlashDraw.Defaults.DurationSeconds);
        Assert.Equal(FlashSurfacePresenter.SurfaceLifetime, FlashDraw.Defaults.Lifetime);
    }

    [Theory]
    // WPF's lifetimeMs = (int)(duration * 1000) + 1000 (FlashService.cs:1073). The grace is not a
    // dial, so it is added at both ends of the duration's own range.
    [InlineData(1, 2000)]
    [InlineData(5, 6000)]
    [InlineData(12, 13000)]
    [InlineData(30, 31000)]
    public void TheLifetimeIsTheDurationPlusWpfsOneSecondGrace(int seconds, int expectedMilliseconds)
    {
        var draw = new FlashDraw(100, 100, seconds);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), draw.Lifetime);
    }

    [Theory]
    // WPF's settings.FlashOpacity / 100.0 (FlashService.cs:2072).
    [InlineData(10, 0.1)]
    [InlineData(40, 0.4)]
    [InlineData(100, 1.0)]
    public void TheOpacityReachesTheRequestAsWpfsOwnFraction(int percent, double expected)
    {
        Assert.Equal(expected, new FlashDraw(100, percent, 5).Opacity);
    }

    [Theory]
    [InlineData(49, 100, 5)]
    [InlineData(251, 100, 5)]
    [InlineData(100, 9, 5)]
    [InlineData(100, 101, 5)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 100, 31)]
    public void AnIllegalReadingTHROWSRatherThanBeingClamped(int scale, int opacity, int seconds)
    {
        // A second clamp here would silently absorb a document that had stopped clamping — the
        // defect it would hide is exactly the one TheScaleDialClampsExactlyAsWpfDoes and its two
        // siblings exist to catch. Same boundary rule the overlay states for opacity zero
        // (Overlay/OverlaySurfaceRequest.cs:30-37): a caller bug, so an exception.
        Assert.Throws<ArgumentOutOfRangeException>(() => new FlashDraw(scale, opacity, seconds));
    }

    [Fact]
    public void AReadingComesOffTheDocumentVerbatim()
    {
        var document = new VisualsPresetDocument
        {
            ImageScalePercent = 175,
            FlashOpacityPercent = 30,
            FlashDurationSeconds = 9,
        };

        var draw = FlashDraw.From(document);
        Assert.Equal(175, draw.ScalePercent);
        Assert.Equal(30, draw.OpacityPercent);
        Assert.Equal(9, draw.DurationSeconds);
    }

    // =====================================================================================
    //  The dials themselves: writes land, clamps hold, and Changed fires
    // =====================================================================================

    [Fact]
    public async Task TheDialsWriteThroughToTheDocumentAndRaiseChanged()
    {
        await using var rig = await DialRig.StartAsync();
        var changes = 0;
        rig.Dials.Changed += () => changes++;

        rig.Dials.SetImageScalePercent(180);
        rig.Dials.SetFlashOpacityPercent(45);
        rig.Dials.SetFlashDurationSeconds(20);

        Assert.Equal(180, rig.Store.Current.ImageScalePercent);
        Assert.Equal(45, rig.Store.Current.FlashOpacityPercent);
        Assert.Equal(20, rig.Store.Current.FlashDurationSeconds);
        Assert.Equal(3, changes);

        var draw = rig.Dials.Draw();
        Assert.Equal(180, draw.ScalePercent);
        Assert.Equal(0.45, draw.Opacity);
        Assert.Equal(TimeSpan.FromSeconds(21), draw.Lifetime);
    }

    [Fact]
    public async Task ADialWrittenOUTOFRANGEIsCorrectedByTheDocument_SoTheREADINGIsAlwaysLegal()
    {
        // The route a hand-edited file or a future caller takes. The document clamps, so Draw() —
        // which validates rather than clamps — can never throw on the product path.
        await using var rig = await DialRig.StartAsync();

        rig.Dials.SetImageScalePercent(int.MaxValue);
        rig.Dials.SetFlashOpacityPercent(0);
        rig.Dials.SetFlashDurationSeconds(-4);

        var draw = rig.Dials.Draw();
        Assert.Equal(VisualsPresetDocument.MaxImageScalePercent, draw.ScalePercent);
        Assert.Equal(VisualsPresetDocument.MinFlashOpacityPercent, draw.OpacityPercent);
        Assert.Equal(VisualsPresetDocument.MinFlashDurationSeconds, draw.DurationSeconds);
    }

    // =====================================================================================
    //  The presenter: the dials reach the OS's own placement request
    // =====================================================================================

    [Fact]
    public void TheDIALSNotTheConstantsDecideWhatTheOverlayIsASKEDFor()
    {
        var rig = new PresenterRig { Draw = new FlashDraw(200, 40, 12) };

        rig.Presenter.Show(["one.png"]);

        var presence = Assert.Single(rig.Presences);
        var request = Assert.Single(presence.Requests);

        // Opacity: 40 % reached the layered window's LWA_ALPHA byte, through the overlay's own
        // rounding (Overlay/OverlaySurfaceRequest.cs:61-68).
        Assert.Equal(0.4, request.Opacity);
        Assert.Equal((byte)102, request.Alpha);

        // Size: the frame source was asked for 800x600 scaled through FlashGeometry at 200 %, which
        // is upstream's own CalculateGeometry (FlashService.cs:2290-2315) and not this test's
        // arithmetic — the expectation is computed by the product's own pure function.
        var (width, height) = FlashGeometry.Size(800, 600, Display.Width, Display.Height, 200);
        Assert.Equal(width, request.Bounds.Width);
        Assert.Equal(height, request.Bounds.Height);

        // And the size really MOVED with the dial rather than happening to match the default.
        var (defaultWidth, _) = FlashGeometry.Size(800, 600, Display.Width, Display.Height, 100);
        Assert.NotEqual(defaultWidth, request.Bounds.Width);

        // Lifetime: the surface is still up one tick before the dialled lifetime and gone on it.
        rig.Clock.Advance(TimeSpan.FromSeconds(13) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, presence.WithdrawCalls);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, presence.WithdrawCalls);
    }

    [Fact]
    public void WithNoDialsAtAll_ThePresenterDrawsExactlyWhatItDrewBeforeSp117()
    {
        // The equivalence this packet owes every landed flash fact: a presenter built without a
        // draw supplier is byte-for-byte the old behaviour.
        var rig = new PresenterRig();

        rig.Presenter.Show(["one.png"]);

        var request = Assert.Single(Assert.Single(rig.Presences).Requests);
        var (width, height) = FlashGeometry.Size(
            800, 600, Display.Width, Display.Height, FlashSurfacePresenter.ImageScalePercent);
        Assert.Equal(width, request.Bounds.Width);
        Assert.Equal(height, request.Bounds.Height);
        Assert.Equal(FlashSurfacePresenter.OpacityPercent / 100.0, request.Opacity);

        rig.Clock.Advance(FlashSurfacePresenter.SurfaceLifetime - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, Assert.Single(rig.Presences).WithdrawCalls);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, Assert.Single(rig.Presences).WithdrawCalls);
    }

    [Fact]
    public void TheReadingIsTakenONCEPerFlash_SoOneFlashsPicturesCannotHaveTwoSizes()
    {
        // Upstream reads its duration once at the top of ShowImages (FlashService.cs:1028-1034) and
        // its scale once in LoadImagesUntilAsync (:655-656), then applies the same numbers to every
        // window of that flash. This presenter staggers a flash's surfaces by 300 ms each
        // (:1112), so a per-surface read would let a dial moved mid-stagger — by the user, or by
        // the ramp on its 2 s cadence — split one flash across two sizes.
        var rig = new PresenterRig { Draw = new FlashDraw(200, 40, 12) };

        rig.Presenter.Show(["a.png", "b.png", "c.png"]);

        // The dial moves while the second and third surfaces are still pending.
        rig.Draw = new FlashDraw(50, 100, 1);
        Assert.Equal(1, rig.DrawReads);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));
        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));

        Assert.Equal(3, rig.Presences.Count);
        Assert.Equal(1, rig.DrawReads);

        var sizes = rig.Presences.Select(p => p.Requests[0].Bounds.Width).Distinct().ToList();
        var opacities = rig.Presences.Select(p => p.Requests[0].Opacity).Distinct().ToList();
        Assert.Single(sizes);
        Assert.Single(opacities);
        Assert.Equal(0.4, opacities[0]);

        // And the NEXT flash does pick the new value up — read once per flash, not once per run.
        // The slot pool re-presents a surface the first flash used, so the reading is taken from
        // the last request ACROSS the pool rather than from the last presence created.
        rig.Presenter.HideAll();
        rig.Presenter.Show(["d.png"]);
        Assert.Equal(2, rig.DrawReads);
        Assert.Equal(4, rig.AllRequests.Count);
        Assert.Equal(1.0, rig.AllRequests[^1].Opacity);
    }

    [Fact]
    public void TheLastReadingIsReportable_SoAPanelCanSayWhatTheLastFlashUsed()
    {
        var rig = new PresenterRig { Draw = new FlashDraw(120, 60, 3) };
        Assert.Null(rig.Presenter.LastDraw);

        rig.Presenter.Show(["one.png"]);
        Assert.Equal(new FlashDraw(120, 60, 3), rig.Presenter.LastDraw);

        // An EMPTY draw is not a flash: nothing is placed, so nothing is read and the last reading
        // stays what it was rather than being overwritten by a firing that never happened.
        rig.Draw = new FlashDraw(50, 10, 1);
        rig.Presenter.Show([]);
        Assert.Equal(new FlashDraw(120, 60, 3), rig.Presenter.LastDraw);
    }

    // =====================================================================================
    //  The ramp's THIRD dial — D93's flash-opacity link, and what it does NOT do
    // =====================================================================================

    [Fact]
    public async Task TheFlashOpacityDialIsKeyedToTheModuleWhoseValueItBorrows_NotToThePanel()
    {
        await using var rig = await DialRig.StartAsync();
        var dial = new FlashOpacityDial(rig.Dials);

        // The ramp records custody by rack key, and the value belongs to Flash Images even though
        // the slider is drawn on the Visuals page. A dial keyed "visuals" would put the ramp's
        // custody line against a row that owns nothing.
        Assert.Equal(FlashImagesEffect.EffectId, dial.Id);
        Assert.Equal("Flash Images opacity", dial.Label);

        // WPF caps this link at 100 (MainWindow/MainWindow.StartStop.cs:508) and the document's own
        // clamp ceiling is the same number, so the ramp can never ask for a value the document
        // would silently correct.
        Assert.Equal(100, dial.Ceiling);
        Assert.Equal(VisualsPresetDocument.MaxFlashOpacityPercent, dial.Ceiling);
    }

    [Fact]
    public async Task TheRampAndThePanelsOwnSliderConvergeOnONEBehaviour()
    {
        await using var rig = await DialRig.StartAsync();
        var dial = new FlashOpacityDial(rig.Dials);

        Assert.Equal(VisualsPresetDocument.DefaultFlashOpacityPercent, dial.Read());

        dial.Write(35);
        Assert.Equal(35, dial.Read());
        Assert.Equal(35, rig.Store.Current.FlashOpacityPercent);
        Assert.Equal(35, rig.Dials.Draw().OpacityPercent);

        // Reapply is a NO-OP and the fact says so rather than leaving it to a reader. Upstream
        // re-tints live flash windows every composition frame (FlashService.cs:2072, applied
        // :2108-2117); here the alpha is set at placement and changing it would mean re-Presenting,
        // which clears click-through to run its differential hit test
        // (Overlay/Win32OverlayPresence.cs:558, :566, :574). D174.
        dial.Reapply();
        Assert.Equal(35, rig.Store.Current.FlashOpacityPercent);
    }

    [Fact]
    public async Task ARampedOpacityReachesTheNEXTFlash_WhichIsTheWholeOfWhatD174Claims()
    {
        await using var rig = await DialRig.StartAsync();
        var dial = new FlashOpacityDial(rig.Dials);
        var presenter = new PresenterRig { Draw = null, DrawSource = rig.Dials.Draw };

        presenter.Presenter.Show(["before.png"]);
        Assert.Equal(1.0, presenter.AllRequests[0].Opacity);

        // The ramp drives the dial while that flash is still on screen.
        dial.Write(50);
        dial.Reapply();

        // The flash already up is unchanged — no second Present was issued for it at all, which is
        // precisely the click-through gap that is NOT being opened (D174). If Reapply ever grew a
        // re-tint, this count would move.
        Assert.Single(presenter.AllRequests);
        Assert.Equal(1.0, presenter.AllRequests[0].Opacity);

        // The next one carries the ramped value.
        presenter.Presenter.HideAll();
        presenter.Presenter.Show(["after.png"]);
        Assert.Equal(2, presenter.AllRequests.Count);
        Assert.Equal(0.5, presenter.AllRequests[^1].Opacity);
    }

    // =====================================================================================
    //  The panel's sentences
    // =====================================================================================

    [Fact]
    public void TheOwnershipLineNamesFlashImagesAndFollowsItsEnable()
    {
        var on = VisualsPanelNotices.DescribeOwnership(flashEnabled: true);
        var off = VisualsPanelNotices.DescribeOwnership(flashEnabled: false);

        Assert.Contains("Flash Images", on, StringComparison.Ordinal);
        Assert.Contains("Flash Images", off, StringComparison.Ordinal);
        Assert.DoesNotContain("switched off", on, StringComparison.Ordinal);
        Assert.Contains("switched off", off, StringComparison.Ordinal);

        // Neither arm claims an on/off of its own: this row has none, and a sentence implying one
        // would be the enable the census says does not exist.
        Assert.DoesNotContain("Enable Visuals", on, StringComparison.Ordinal);
        Assert.DoesNotContain("Enable Visuals", off, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDialLineQuotesTheReadingAndSaysWHENAChangeLands()
    {
        var text = VisualsPanelNotices.DescribeDials(new FlashDraw(175, 30, 1));

        Assert.Contains("175%", text, StringComparison.Ordinal);
        Assert.Contains("30%", text, StringComparison.Ordinal);
        Assert.Contains("1 second", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1 seconds", text, StringComparison.Ordinal);
        Assert.Contains("NEXT flash", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAbsenceLineNamesBOTHMissingControlsAndTheReasonForEach()
    {
        var text = VisualsPanelNotices.DescribeAbsences();

        // The fade slider: dead upstream too, and the page says so rather than implying the port
        // dropped a working control.
        Assert.Contains("Fade", text, StringComparison.Ordinal);
        Assert.Contains("wired to nothing", text, StringComparison.Ordinal);

        // The audio link: absent because this port's flash is silent.
        Assert.Contains("Link to audio", text, StringComparison.Ordinal);
        Assert.Contains("silent", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSurfaceLineIsTheCapabilitysOwnAnswer_NeverAPlatformClaim()
    {
        Assert.Contains(
            "has not been asked",
            VisualsPanelNotices.DescribeSurface(null),
            StringComparison.Ordinal);

        Assert.Contains(
            "confirmed",
            VisualsPanelNotices.DescribeSurface(new CapabilityState.Available("placed")),
            StringComparison.Ordinal);

        // A refusal is quoted VERBATIM, reason detail and all, exactly as every other module panel
        // quotes its own surface — the rule SP-100 set after this page asserted a platform instead.
        var refused = VisualsPanelNotices.DescribeSurface(
            new CapabilityState.Unavailable(
                new CapabilityReason("overlay-unavailable", "no overlay backend on this build")));
        Assert.Contains("no overlay backend on this build", refused, StringComparison.Ordinal);

        var degraded = VisualsPanelNotices.DescribeSurface(
            new CapabilityState.Degraded(
                "the window is up", new CapabilityReason("overlay-paint-refused", "the blit was refused")));
        Assert.Contains("the window is up", degraded, StringComparison.Ordinal);
        Assert.Contains("the blit was refused", degraded, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  Persistence, INCLUDING the landed store this packet found unwired
    // =====================================================================================

    [Fact]
    public async Task TheVisualsDialsSurviveARestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-sp117-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using (var first = await ParticipantRig.StartAsync(directory))
            {
                first.Session.Visuals.SetImageScalePercent(210);
                first.Session.Visuals.SetFlashOpacityPercent(25);
                first.Session.Visuals.SetFlashDurationSeconds(18);
                await first.Session.FlushAsync(TimeSpan.FromSeconds(5));
            }

            await using var second = await ParticipantRig.StartAsync(directory);
            Assert.Equal(210, second.Session.VisualsPreset.Current.ImageScalePercent);
            Assert.Equal(25, second.Session.VisualsPreset.Current.FlashOpacityPercent);
            Assert.Equal(18, second.Session.VisualsPreset.Current.FlashDurationSeconds);
            Assert.Equal(210, second.Session.Visuals.Draw().ScalePercent);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public async Task THEBOUNCINGTEXTDIALSSurviveARestartToo_AndBeforeSp117TheyDidNot()
    {
        // NOT this packet's module, and that is why it is here. SP-115 built
        // _bouncingTextPreset and left it out of ALL FOUR of SessionParticipant's store lists —
        // StartAsync, LogIfDegraded, StopAsync and FlushAsync. PersistenceStore.Load runs only from
        // StartAsync, so the document was never read from disk and never written to it: every dial
        // the user set on that panel was silently gone at the next launch. Found while adding a
        // twelfth store to the same lists, fixed in the same commit, and pinned here so it cannot
        // regress quietly a second time.
        var directory = Path.Combine(Path.GetTempPath(), "ccp-sp117bt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using (var first = await ParticipantRig.StartAsync(directory))
            {
                first.Session.BouncingTextPreset.Mutate(p =>
                {
                    p.Enabled = true;
                    p.OpacityPercent = 37;
                });
                await first.Session.FlushAsync(TimeSpan.FromSeconds(5));
            }

            await using var second = await ParticipantRig.StartAsync(directory);
            Assert.True(second.Session.BouncingTextPreset.Current.Enabled);
            Assert.Equal(37, second.Session.BouncingTextPreset.Current.OpacityPercent);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // =====================================================================================
    //  rigs and doubles
    // =====================================================================================

    /// <summary>A store on a real temp file, started through the real operation owner.</summary>
    private sealed class DialRig : IAsyncDisposable
    {
        private readonly string _directory;

        private DialRig(string directory, PersistenceStore<VisualsPresetDocument> store)
        {
            _directory = directory;
            Store = store;
            Dials = new VisualsDials(store);
        }

        public PersistenceStore<VisualsPresetDocument> Store { get; }

        public VisualsDials Dials { get; }

        public static async Task<DialRig> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "ccp-sp117d-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var registry = new OperationRegistry();
            var store = new PersistenceStore<VisualsPresetDocument>(
                registry.OwnerFor("VisualsPreset"), new NullSink(),
                Path.Combine(directory, VisualsPresetDocument.FileName),
                VisualsPresetDocument.CurrentSchemaVersion);
            await store.StartAsync(TestContext.Current.CancellationToken);
            return new DialRig(directory, store);
        }

        public async ValueTask DisposeAsync()
        {
            await Store.StopAsync();
            TryDelete(_directory);
        }
    }

    /// <summary>The whole composition root over a real directory, for the persistence facts.</summary>
    private sealed class ParticipantRig : IAsyncDisposable
    {
        private ParticipantRig(ApplicationHost host, SessionParticipant session)
        {
            Host = host;
            Session = session;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public static async Task<ParticipantRig> StartAsync(string directory)
        {
            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var session = new SessionParticipant(
                infra, directory, new ManualSessionClock(), new StubPool(),
                new NullFlashSurface(), new NullSubliminalSurface(), static () => true);
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);
            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));
            return new ParticipantRig(host, session);
        }

        public async ValueTask DisposeAsync() => await Host.ShutdownAsync();
    }

    /// <summary>The presenter over recording overlay doubles and a manual clock.</summary>
    private sealed class PresenterRig
    {
        private readonly Lazy<FlashSurfacePresenter> _presenter;

        public PresenterRig()
        {
            _presenter = new Lazy<FlashSurfacePresenter>(() => new FlashSurfacePresenter(
                Clock,
                action => action(),
                () =>
                {
                    var presence = new RecordingPresence(AllRequests.Add);
                    Presences.Add(presence);
                    return presence;
                },
                new StubFrameSource(),
                () => Display,
                new Random(117),
                DrawSupplier()));
        }

        public ManualSessionClock Clock { get; } = new();

        public List<RecordingPresence> Presences { get; } = [];

        /// <summary>Every placement request this presenter has made, in order, ACROSS the pool.
        /// Surfaces are pooled and reused (<c>OverlaySurfaceSet</c>), so a second flash usually
        /// re-presents a slot the first one used rather than creating a new presence — indexing
        /// <see cref="Presences"/> would silently read the wrong flash's request.</summary>
        public List<OverlaySurfaceRequest> AllRequests { get; } = [];

        /// <summary>The reading this rig hands out, or null to leave the presenter on its
        /// defaults — the pre-SP-117 composition.</summary>
        public FlashDraw? Draw { get; set; }

        /// <summary>An explicit supplier, for the facts that drive a REAL VisualsDials.</summary>
        public Func<FlashDraw>? DrawSource { get; init; }

        public int DrawReads { get; private set; }

        public FlashSurfacePresenter Presenter => _presenter.Value;

        private Func<FlashDraw>? DrawSupplier()
        {
            if (DrawSource is not null)
            {
                return () =>
                {
                    DrawReads++;
                    return DrawSource();
                };
            }

            if (Draw is null)
            {
                return null;
            }

            return () =>
            {
                DrawReads++;
                return Draw!.Value;
            };
        }
    }

    private sealed class RecordingPresence(Action<OverlaySurfaceRequest> onPresent) : IOverlayPresence
    {
        private bool _live;

        public List<OverlaySurfaceRequest> Requests { get; } = [];

        public int WithdrawCalls { get; private set; }

        public bool IsPresenting => _live;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            Requests.Add(request);
            onPresent(request);
            _live = true;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame) =>
            new CapabilityState.Available("recording presence: painted");

        public void Reassert()
        {
        }

        public CapabilityState SetClickThrough(bool clickThrough) =>
            new CapabilityState.Available("recording presence: flipped");

        public CapabilityState Withdraw()
        {
            WithdrawCalls++;
            _live = false;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _live = false;
    }

    private sealed class StubFrameSource : IFlashFrameSource
    {
        public OverlayFrame? Render(string path, Func<int, int, (int Width, int Height)> targetSize)
        {
            var (width, height) = targetSize(800, 600);
            return OverlayFrame.Solid(width, height, 0x10, 0x20, 0x30);
        }
    }

    private sealed class StubPool : IFlashImagePool
    {
        public IReadOnlyList<string> Draw(int count) => [];

        public int Population => 0;
    }

    private sealed class NullFlashSurface : IFlashSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(IReadOnlyList<string> images)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class NullSubliminalSurface : ISubliminalSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(SubliminalCard card)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    /// <summary>The manual clock, SP-098's shape. Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

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

        private sealed class Handle(ManualSessionClock clock, Entry entry) : IDisposable
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
