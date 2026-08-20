using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-126 — <b>the five port trigger points</b>: that each of them tells the limb the right moment,
/// with the right argument, at the right place in its own sequence, and that the ones the census
/// calls absent stay absent.
///
/// <para>The limb here is a RECORDING double rather than the real one. The separation is deliberate:
/// <see cref="HapticLimbTests"/> owns the arithmetic against a recording SINK, and this file owns the
/// wiring — which module calls which verb, how often, and on which branch. A single fact trying to
/// do both would pass for the wrong reason as soon as either half moved.</para>
/// </summary>
public class HapticLimbSiteTests
{
    // =====================================================================================
    //  Census sites 1-3 — Effects/FlashSurfacePresenter.cs, one ladder per PLACED IMAGE
    // =====================================================================================

    [Fact]
    public void AFlashTellsTheLimbONCEPERPLACEDIMAGE_NotOncePerFlash()
    {
        // Upstream fires FlashDecayVibeAsync() from all THREE of SpawnFlashWindow's spawn arms
        // (FlashService.cs:1453, :1480, :1516), which is once per WINDOW. A flash of three images
        // spawns three windows, staggered 300 ms apart (:1112), so it reaches the haptic service
        // three times and each call replaces the ladder before it (HapticService.cs:774-776).
        var rig = new FlashRig();

        rig.Presenter.Show(["a.png", "b.png", "c.png"]);
        Assert.Equal(1, rig.Limb.FlashPlacements);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));
        Assert.Equal(2, rig.Limb.FlashPlacements);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));
        Assert.Equal(3, rig.Limb.FlashPlacements);
        Assert.Equal(3, rig.Presenter.SurfacesShown);
    }

    [Fact]
    public void AnImageThatProducedNoPixelsTellsTheLimbNOTHING()
    {
        // Upstream's arms sit inside SpawnFlashWindow, past the decode. An undecodable image is
        // upstream's own normal case, and a haptic for a picture nobody saw would be a vibration the
        // user cannot account for.
        var rig = new FlashRig();
        rig.Frames.Undecodable.Add("broken.png");

        rig.Presenter.Show(["broken.png"]);

        Assert.Equal(0, rig.Limb.FlashPlacements);
        Assert.Equal(1, rig.Presenter.UndecodableImages);
    }

    [Fact]
    public void AFlashWithNowhereToGoTellsTheLimbNOTHING()
    {
        var rig = new FlashRig();
        rig.Display = () => null;

        rig.Presenter.Show(["a.png"]);

        Assert.Equal(0, rig.Limb.FlashPlacements);
        Assert.Equal(0, rig.Presenter.SurfacesShown);
    }

    // =====================================================================================
    //  Census sites 6, 7 and 12 — Effects/MandatoryVideoEffect.cs
    // =====================================================================================

    [Fact]
    public void AClipTHATREALLYPLAYEDRaisesTheLayer_AndOneThatWasOnlyASKEDForDoesNot()
    {
        // Upstream raises the layer at VideoService.cs:2580, immediately under its own "Playback is
        // REAL from here: a window (and, on the LibVLC path, a registered media player) exists"
        // (:2567-2576). So the Available arm, and only the Available arm.
        using var refused = new VideoRig();
        refused.Surface.BeginResult = new CapabilityState.Unavailable(
            new CapabilityReason("test-refusal", "the double refused it"));
        refused.Pool.Clips.Add("clip.mp4");
        refused.Enable();
        refused.Effect.Arm();
        refused.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(["clip.mp4"], refused.Surface.Begun);
        Assert.Equal(0, refused.Limb.VideoStarts);

        using var played = new VideoRig();
        played.Pool.Clips.Add("clip.mp4");
        played.Enable();
        played.Effect.Arm();
        played.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, played.Limb.VideoStarts);
    }

    [Fact]
    public void THESTOPRUNSONTHENATURALEND()
    {
        // Census site 12's first path: the clip finished, was capped, or the surface stopped holding
        // it. Upstream's own only path (VideoService.cs:6580, inside Cleanup()).
        using var rig = new VideoRig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, rig.Limb.VideoStarts);
        Assert.Equal(0, rig.Limb.VideoStops);

        rig.Surface.RaiseEnded();

        Assert.Equal(1, rig.Limb.VideoStops);
    }

    [Fact]
    public void THESTOPALSORUNSONDISARM_WHICHISTHEPATHUPSTREAMMISSES()
    {
        // D203, and the whole point of the divergence. Upstream's StopVideoBackgroundVibeAsync has
        // exactly ONE caller in the whole tree and it is inside Cleanup(); ForceCleanup() is the
        // panic-key path and carries no haptic reference at all, while a comment three lines under
        // the stop claims it "Runs on every teardown path (natural end, skip, panic, attention
        // retry)". So panic-keying a clip upstream leaves the layer LATCHED.
        //
        // Disarm is this port's teardown funnel — OwnedSessionEffect.Disarm reaches OnDisarmed,
        // SessionEngine.Stop disarms every module, and the session participant's stop calls that —
        // and it does NOT reach OnClipEnded, because VideoSurfacePresenter clears the ended callback
        // rather than invoking it. A stop on the ended callback alone would repeat the defect.
        using var rig = new VideoRig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, rig.Limb.VideoStarts);
        Assert.Equal(0, rig.Limb.VideoStops);

        // Mid-clip teardown. The surface is torn down WITHOUT its ended callback ever running, which
        // is exactly what the product's presenter does.
        rig.Effect.Disarm();

        Assert.Equal(1, rig.Limb.VideoStops);
        Assert.Equal(1, rig.Surface.Ends);
        Assert.Equal(0, rig.Surface.EndedRaises);
    }

    [Fact]
    public void BOTHTeardownPathsAreWiredSeparately_SoNeitherCanBeDeletedSilently()
    {
        // A claim about what a guard cannot do needs the same evidence as a claim about what it can:
        // if the two stops shared one call site, deleting either would leave this file green.
        using var rig = new VideoRig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        rig.Surface.RaiseEnded();
        Assert.Equal(1, rig.Limb.VideoStops);

        rig.Effect.Disarm();
        Assert.Equal(2, rig.Limb.VideoStops);
    }

    // =====================================================================================
    //  Census sites 14-15 — Effects/SubliminalsEffect.cs
    // =====================================================================================

    [Fact]
    public void ASubliminalCardTellsTheLimbITSOWNWORDS_BecauseTheDurationIsKeyedOffThem()
    {
        // Upstream keys the pulse's duration off the trigger's wording (HapticService.cs:899-909),
        // so the PHRASE has to travel rather than a number somebody upstream of the limb computed.
        using var rig = new SubliminalRig("you will DROP for me");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(["you will DROP for me"], rig.Limb.SubliminalPhrases);
        Assert.Equal(["you will DROP for me"], rig.Surface.Shown);
        // And that phrase really does select the long duration, so the wiring and the arithmetic
        // meet.
        Assert.Equal(250, HapticEnvelopes.SubliminalDurationMs(rig.Limb.SubliminalPhrases[0]));
    }

    [Fact]
    public void AFiringWithNoActivePhraseTellsTheLimbNOTHING()
    {
        // Upstream returns before it counts or shows anything when the pool is empty
        // (SubliminalService.cs:207-212), and the timer is re-armed regardless.
        using var rig = new SubliminalRig();
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Empty(rig.Limb.SubliminalPhrases);
        Assert.Empty(rig.Surface.Shown);
    }

    // =====================================================================================
    //  Census site 18 — Effects/BouncingTextField.cs
    // =====================================================================================

    [Fact]
    public void EVERYWALLBOUNCETellsTheLimbONCE_AndTheStepsBetweenTellItNothing()
    {
        // Upstream fires BouncingTextBounceAsync() at BouncingTextService.cs:516, between the bounce
        // bookkeeping and the 10 % re-roll at :519 — in a module this port shipped at SP-115.
        var limb = new RecordingLimb();
        var field = new BouncingTextField(
            new BouncingTextPresentation(
                Enabled: true,
                SpeedSetting: 10,
                SizePercent: 100,
                OpacityPercent: 100,
                ColourMode: BouncingTextColourMode.Random,
                FixedColour: null,
                Outline: false,
                EnabledPhrases: ["GOOD GIRL"]),
            (0, 0, 400, 300),
            static _ => (100, 40),
            new Random(7),
            limb);

        field.Advance(0.016);   // the first call establishes the baseline and moves nothing
        var bounces = 0;
        for (var i = 0; i < 400; i++)
        {
            field.Advance(0.05);
            Assert.Equal(field.Bounces, limb.Bounces);
            bounces = field.Bounces;
        }

        // The trajectory really did hit walls, so the equality above is a fact rather than 0 == 0.
        Assert.True(bounces > 5, $"expected the logo to hit a wall repeatedly; it bounced {bounces} times");
    }

    // =====================================================================================
    //  The composition — the limb REACHES the modules in the real product wiring
    // =====================================================================================

    [Fact]
    public void THECOMPOSITIONROOTGivesTheSessionTheHapticParticipantsOWNLimb()
    {
        // The wiring this packet exists to complete, asserted rather than assumed: one limb, over
        // one sink, reaching the session's five statements. A root that quietly stopped passing it
        // would leave every module silent and look identical from outside — which is D179 all over
        // again, one layer up.
        using var scratch = new Scratch();
        var root = new CompositionRoot
        {
            LogSinkFactory = () => new NullLog(),
            SettingsPathFactory = () => Path.Combine(scratch.Path, "settings.json"),
        };

        Assert.True(root.Validate(out var failure));
        Assert.Null(failure);
        var host = root.Build(new StartupTrace());

        var session = host.Participants.OfType<SessionParticipant>().Single();
        var haptics = host.Participants.OfType<HapticParticipant>().Single();

        Assert.NotNull(session.Haptics);
        Assert.Same(haptics.Limb, session.Haptics);

        // And the haptic participant is still registered LAST, which is what decides phase-3 start
        // order and reverse-order teardown.
        Assert.Same(haptics, host.Participants[^1]);
    }

    [Fact]
    public void ASESSIONWITHNOLIMBIsABSENTRatherThanSilent()
    {
        // There is deliberately no no-op IHapticLimb. A module holds a nullable one and calls through
        // ?., so "this build wires no limb" and "the limb decided nothing" stay different facts — a
        // no-op double is exactly the shape that lets a wiring regression pass every test.
        using var scratch = new Scratch();
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new NullLog());
        var session = new SessionParticipant(infra, scratch.Path);

        Assert.Null(session.Haptics);
    }

    // =====================================================================================
    //  The rack dot — Live is STILL unreachable, and a limb does not change that
    // =====================================================================================

    [Fact]
    public async Task ABUSYLIMBDoesNotLightTheRackDot_BecauseLiveWouldMeanSomethingIsBeingSENT()
    {
        // SP-119 gave this row three dot values and Live is unreachable BY CONSTRUCTION. The reason
        // has moved — the modules are no longer silent — but the dot has not, and must not: both of
        // its conjuncts are about what a SERVER said, and a limb touches neither. Views/** is closed
        // to this packet and nothing here asks it to change.
        using var scratch = new Scratch();
        var participant = new HapticParticipant(
            new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new NullLog()),
            scratch.Path,
            resolveEntitlement: _ => Task.FromResult<EntitlementOutcome>(
                new EntitlementOutcome.Entitled(EntitlementTier.Supporter, "confirmed")));

        await participant.StartAsync(TestContext.Current.CancellationToken);
        participant.RequestEnable(true);
        Assert.True(participant.Enabled);
        Assert.True(participant.OutputAllowed);

        // Drive the limb as hard as a session can.
        for (var i = 0; i < 20; i++)
        {
            participant.Limb.FlashPlaced();
            participant.Limb.SubliminalShown("drop");
            participant.Limb.BounceHit();
        }

        participant.Limb.VideoStarted();

        // Enabled AND entitled AND commanded, and the dot is still Off, because the second conjunct
        // asks the sink what a server last said and nothing was ever asked of one.
        Assert.Null(participant.LastObservation);
        Assert.Equal(EffectDotState.Off, participant.Dot);
        Assert.NotEqual(EffectDotState.Live, participant.Dot);

        // And nothing was sent, because there is no device key to address.
        Assert.Equal(0, participant.Limb.Sends);

        await participant.StopAsync();
    }

    [Fact]
    public async Task THEALLSTOPClearsTheLimbBEFOREItStopsTheDevices_AndOnlyStopsThemOnce()
    {
        // Upstream's gate-close arm: drop everything and zero every layer (HapticMixer.cs:264,
        // ClearAll at :1044-1049), THEN stop the toys ONCE (:265). A limb that all-stopped for itself
        // would spend the stop budget twice on the teardown path.
        using var scratch = new Scratch();
        var sink = new RecordingHapticSink();
        var participant = new HapticParticipant(
            new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new NullLog()),
            scratch.Path,
            sink,
            _ => Task.FromResult<EntitlementOutcome>(
                new EntitlementOutcome.Entitled(EntitlementTier.Supporter, "confirmed")));

        await participant.StartAsync(TestContext.Current.CancellationToken);
        participant.RequestEnable(true);
        participant.Limb.VideoStarted();
        Assert.True(participant.Limb.Layer > 0);

        await participant.ShutdownStopAsync();

        Assert.Equal(0, participant.Limb.Layer);
        Assert.Equal(0, participant.Limb.ActivePulses);
        Assert.Equal(1, participant.AllStops);
        Assert.Equal(1, sink.StopAlls);

        // The latch: a second all-stop on the teardown path costs nothing.
        await participant.StopAsync();
        Assert.Equal(1, participant.AllStops);
        Assert.Equal(1, sink.StopAlls);
    }

    // =====================================================================================
    //  Rigs and doubles
    // =====================================================================================

    /// <summary>A limb that records the MOMENTS it was told about and nothing else. It computes no
    /// level, so a wiring fact can never accidentally assert an envelope.</summary>
    private sealed class RecordingLimb : IHapticLimb
    {
        public int FlashPlacements { get; private set; }

        public int VideoStarts { get; private set; }

        public int VideoStops { get; private set; }

        public int Bounces { get; private set; }

        public List<string> SubliminalPhrases { get; } = [];

        public void FlashPlaced() => FlashPlacements++;

        public void VideoStarted() => VideoStarts++;

        public void VideoStopped() => VideoStops++;

        public void SubliminalShown(string phrase) => SubliminalPhrases.Add(phrase);

        public void BounceHit() => Bounces++;
    }

    private sealed class FlashRig
    {
        private readonly Lazy<FlashSurfacePresenter> _presenter;

        public FlashRig()
        {
            Display = () => new OverlayBounds(0, 0, 1920, 1080);
            _presenter = new Lazy<FlashSurfacePresenter>(() => new FlashSurfacePresenter(
                Clock, action => action(), () => new SilentPresence(), Frames, () => Display(),
                new Random(4242), null, Limb));
        }

        public ManualClock Clock { get; } = new();

        public RecordingLimb Limb { get; } = new();

        public StubFrames Frames { get; } = new();

        public Func<OverlayBounds?> Display { get; set; }

        public FlashSurfacePresenter Presenter => _presenter.Value;
    }

    private sealed class SilentPresence : IOverlayPresence
    {
        private bool _presenting;

        public bool IsPresenting => _presenting;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            _presenting = true;
            return new CapabilityState.Available("the double placed it");
        }

        public CapabilityState SetClickThrough(bool clickThrough) =>
            new CapabilityState.Available("the double flipped it");

        public CapabilityState Paint(OverlayFrame frame) => new CapabilityState.Available("the double painted it");

        public void Reassert()
        {
        }

        public CapabilityState Withdraw()
        {
            _presenting = false;
            return new CapabilityState.Available("the double withdrew it");
        }

        public void Dispose() => _presenting = false;
    }

    private sealed class StubFrames : IFlashFrameSource
    {
        public HashSet<string> Undecodable { get; } = new(StringComparer.Ordinal);

        public OverlayFrame? Render(string path, Func<int, int, (int Width, int Height)> targetSize)
        {
            if (Undecodable.Contains(path))
            {
                return null;
            }

            var (width, height) = targetSize(800, 600);
            return OverlayFrame.Solid(width, height, 0x10, 0x20, 0x30);
        }
    }

    private sealed class VideoRig : IDisposable
    {
        private readonly OperationRegistry _registry = new();
        private readonly Scratch _scratch = new();

        public VideoRig()
        {
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            Preset = new PersistenceStore<MandatoryVideoPresetDocument>(
                _registry.OwnerFor("VideoPreset"), new NullLog(),
                Path.Combine(_scratch.Path, MandatoryVideoPresetDocument.FileName),
                MandatoryVideoPresetDocument.CurrentSchemaVersion);

            Effect = new MandatoryVideoEffect(
                _registry.OwnerFor("MandatoryVideo"),
                new EffectSignal(boundary, static () => true),
                Clock,
                Preset,
                Pool,
                Surface,
                new Random(11),
                Limb);
        }

        public ManualClock Clock { get; } = new();

        public RecordingLimb Limb { get; } = new();

        public EndedVideoSurface Surface { get; } = new();

        public StubClips Pool { get; } = new();

        public PersistenceStore<MandatoryVideoPresetDocument> Preset { get; }

        public MandatoryVideoEffect Effect { get; }

        public void Enable() => Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.PerHour = MandatoryVideoSchedule.DefaultPerHour;
        });

        public void Dispose()
        {
            Effect.Disarm();
            _scratch.Dispose();
        }
    }

    /// <summary>
    /// A video surface that mirrors the product where the product's own transitions matter: Begin
    /// marks it showing only when it succeeded, End takes it down, and the ended callback is raised
    /// ONLY when a fact asks for it — because the real presenter clears that callback on teardown
    /// rather than invoking it, which is the whole reason the stop needs two homes.
    /// </summary>
    private sealed class EndedVideoSurface : IVideoSurface
    {
        private Action? _onEnded;

        public List<string> Begun { get; } = [];

        public int Ends { get; private set; }

        public int EndedRaises { get; private set; }

        public CapabilityState BeginResult { get; set; } = new CapabilityState.Available("the double allowed it");

        public bool Showing { get; private set; }

        public bool Running => true;

        public bool Engaged => Showing;

        public bool CanReachADisplay => true;

        public int FramesDecoded => 0;

        public int FramesHeld => 0;

        public int FramesAdvanced => 0;

        public CapabilityState? LastPlacement { get; private set; }

        public string? PlayingClip => Showing ? Begun[^1] : null;

        public VideoSurfaceObservation LastObservation => VideoSurfaceObservation.NotAsked;

        public CapabilityState Begin(
            string clipPath, TimeSpan maxLength, Action onEnded, IVideoFramePainter? painter = null)
        {
            Begun.Add(clipPath);
            _onEnded = onEnded;
            LastPlacement = BeginResult;
            if (BeginResult is CapabilityState.Available)
            {
                Showing = true;
            }

            return BeginResult;
        }

        public void End()
        {
            Ends++;
            Showing = false;
            // The product's presenter clears the callback here rather than invoking it, so a teardown
            // never reaches OnClipEnded.
            _onEnded = null;
        }

        public void RaiseEnded()
        {
            var ended = _onEnded;
            EndedRaises++;
            End();
            ended?.Invoke();
        }
    }

    private sealed class StubClips : IVideoClipPool
    {
        public List<string> Clips { get; } = [];

        public int ActiveCount => Clips.Count;

        public string Folder => "clips";

        public string? Draw() => Clips.Count == 0 ? null : Clips[0];
    }

    private sealed class SubliminalRig : IDisposable
    {
        private readonly OperationRegistry _registry = new();
        private readonly Scratch _scratch = new();

        public SubliminalRig(params string[] phrases)
        {
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            Preset = new PersistenceStore<SubliminalPresetDocument>(
                _registry.OwnerFor("SubliminalPreset"), new NullLog(),
                Path.Combine(_scratch.Path, SubliminalPresetDocument.FileName),
                SubliminalPresetDocument.CurrentSchemaVersion);

            Effect = new SubliminalsEffect(
                _registry.OwnerFor("Subliminals"),
                new EffectSignal(boundary, static () => true),
                Clock,
                new StubPhrases(phrases),
                Preset,
                new Random(3),
                Surface,
                Limb);
        }

        public ManualClock Clock { get; } = new();

        public RecordingLimb Limb { get; } = new();

        public RecordingCards Surface { get; } = new();

        public PersistenceStore<SubliminalPresetDocument> Preset { get; }

        public SubliminalsEffect Effect { get; }

        public void Enable() => Preset.Mutate(p => p.Enabled = true);

        public void Dispose()
        {
            Effect.Disarm();
            _scratch.Dispose();
        }
    }

    private sealed class StubPhrases(string[] phrases) : ISubliminalPhrasePool
    {
        public int ActiveCount => phrases.Length;

        public string? Draw() => phrases.Length == 0 ? null : phrases[0];
    }

    private sealed class RecordingCards : ISubliminalSurface
    {
        public List<string> Shown { get; } = [];

        public CapabilityState? LastPlacement => null;

        public void Show(SubliminalCard card) => Shown.Add(card.Text);

        public void HideAll()
        {
        }
    }

    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ccp-sp126-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class NullLog : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

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
