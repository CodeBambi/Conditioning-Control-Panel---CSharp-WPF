using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-111 — the Mandatory Video module: its pacing law, its clip pool, its three arm outcomes, and
/// the dot's SEVENTH meaning.
///
/// <para>Nothing here touches a desktop, a decoder or a file the OS has to parse: the surface is a
/// double and the pool is a stub, which is exactly what lets these facts pin the module's own
/// decisions rather than the operating system's. The OS-level half lives in
/// <see cref="VideoCapabilityTests"/>.</para>
/// </summary>
public class MandatoryVideoModuleTests
{
    // ---------------------------------------------------------------------------------------
    //  THE PACING LAW
    // ---------------------------------------------------------------------------------------

    [Theory]
    // 3600/perHour * (0.8 + roll*0.4), floored at 60s (VideoService.cs:2225-2229).
    [InlineData(2, 0.0, 1440.0)]
    [InlineData(2, 0.5, 1800.0)]
    [InlineData(2, 1.0, 2160.0)]
    [InlineData(20, 0.0, 144.0)]
    [InlineData(20, 1.0, 216.0)]
    public void TheIntervalIsUpstreamsOwnArithmetic(int perHour, double roll, double expectedSeconds)
    {
        Assert.Equal(
            expectedSeconds,
            MandatoryVideoSchedule.Interval(perHour, roll).TotalSeconds,
            precision: 6);
    }

    [Fact]
    public void TheSIXTYSECONDFloorBinds_AndItIsNotTheSameThingAsTheDialsClamp()
    {
        // A dial beyond the clamp cannot reach the arithmetic through the document, but the schedule
        // floors the DIAL too (upstream's own Math.Max(1, …) at :2225), so an out-of-range value
        // arriving another way still produces a sane interval rather than a divide-by-zero.
        Assert.Equal(
            MandatoryVideoSchedule.Interval(MandatoryVideoSchedule.MinPerHour, 0.0),
            MandatoryVideoSchedule.Interval(0, 0.0));
        Assert.Equal(
            MandatoryVideoSchedule.Interval(MandatoryVideoSchedule.MinPerHour, 0.0),
            MandatoryVideoSchedule.Interval(-5, 0.0));

        // And the floor itself: at 3600 an hour the base interval is one second, and the schedule
        // still refuses to come round faster than a minute. A video FIRING occupies the screen for
        // the length of a clip, so without this the module would be permanently busy and drop every
        // other firing.
        Assert.Equal(MandatoryVideoSchedule.MinimumInterval, MandatoryVideoSchedule.Interval(3600, 0.0));
        Assert.Equal(MandatoryVideoSchedule.MinimumInterval, MandatoryVideoSchedule.Interval(3600, 1.0));
    }

    [Fact]
    public void TheDialsClampsAreUpstreamsSliderBounds()
    {
        var document = new MandatoryVideoPresetDocument();
        Assert.Equal(MandatoryVideoSchedule.DefaultPerHour, document.PerHour);
        Assert.False(document.Enabled);
        Assert.Equal(0, document.MaxSeconds);
        Assert.Equal(TimeSpan.Zero, document.MaxLength);

        document.PerHour = 0;
        Assert.Equal(MandatoryVideoSchedule.MinPerHour, document.PerHour);
        document.PerHour = 9999;
        Assert.Equal(MandatoryVideoSchedule.MaxPerHour, document.PerHour);

        document.MaxSeconds = -30;
        Assert.Equal(0, document.MaxSeconds);
        document.MaxSeconds = 999999;
        Assert.Equal(MandatoryVideoPresetDocument.MaxMaxSeconds, document.MaxSeconds);
        document.MaxSeconds = 45;
        Assert.Equal(TimeSpan.FromSeconds(45), document.MaxLength);
    }

    // ---------------------------------------------------------------------------------------
    //  THE CLIP POOL
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ThePoolWalksSUBFOLDERS_BecauseUpstreamsVideoWalkDoes()
    {
        using var scratch = new Scratch();
        var root = Path.Combine(scratch.Path, VideoClipPool.VideosFolderName);
        Directory.CreateDirectory(Path.Combine(root, "category"));
        File.WriteAllText(Path.Combine(root, "top.mp4"), "x");
        File.WriteAllText(Path.Combine(root, "category", "nested.mkv"), "x");

        var pool = new VideoClipPool(scratch.Path, new Random(1));
        Assert.Equal(2, pool.ActiveCount);

        // AND this is the OPPOSITE of the audio pool, deliberately: upstream's video walk is
        // SearchOption.AllDirectories (VideoService.cs:6944, "Scan subfolders to support
        // user-organized categories") while its audio pools take the top-level-only overload.
        var drawn = new[] { pool.Draw(), pool.Draw() };
        Assert.Contains(drawn, p => p is not null && p.EndsWith("nested.mkv", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePoolFiltersByUpstreamsOwnExtensionSet()
    {
        using var scratch = new Scratch();
        var root = Path.Combine(scratch.Path, VideoClipPool.VideosFolderName);
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "a.mp4", "b.mov", "c.avi", "d.wmv", "e.mkv", "f.webm" })
        {
            File.WriteAllText(Path.Combine(root, name), "x");
        }

        foreach (var name in new[] { "g.mp3", "h.txt", "i.png", "j" })
        {
            File.WriteAllText(Path.Combine(root, name), "x");
        }

        Assert.Equal(6, new VideoClipPool(scratch.Path, new Random(1)).ActiveCount);
    }

    [Fact]
    public void ThePoolIsASHUFFLEDBAG_SoEveryClipPlaysOnceBeforeAnyPlaysTwice()
    {
        using var scratch = new Scratch();
        var root = Path.Combine(scratch.Path, VideoClipPool.VideosFolderName);
        Directory.CreateDirectory(root);
        for (var i = 0; i < 6; i++)
        {
            File.WriteAllText(Path.Combine(root, $"clip{i}.mp4"), "x");
        }

        var pool = new VideoClipPool(scratch.Path, new Random(7));
        var firstPass = Enumerable.Range(0, 6).Select(_ => pool.Draw()).ToList();

        // This is upstream's draw policy and it is NOT the audio pool's uniform-with-replacement:
        // upstream Fisher-Yates-shuffles the whole enumeration into a Queue and dequeues one per
        // firing (VideoService.cs:7042-7044, :7073-7079, :6684), refilling only when it runs dry
        // (:6611-6614). A user sees the difference.
        Assert.Equal(6, firstPass.Distinct().Count());
        Assert.DoesNotContain(null, firstPass);

        // And the bag refills: the seventh draw is a clip again, not null.
        Assert.NotNull(pool.Draw());

        // ActiveCount reports what the FOLDER holds, not what is left in the bag — a user with six
        // clips who has seen five must not be told the pool has one.
        Assert.Equal(6, pool.ActiveCount);

        // AND THE BAG IS REALLY SHUFFLED. Distinctness alone does not say that: an unshuffled queue
        // over the same enumeration is distinct too, and it would hand every session the same clip
        // first, for ever. Two pools with different seeds must not deal the same order.
        var seedA = Enumerable.Range(0, 6).Select(_ => new VideoClipPool(scratch.Path, new Random(3)).Draw()).ToList();
        var byThree = new VideoClipPool(scratch.Path, new Random(3));
        var byNine = new VideoClipPool(scratch.Path, new Random(9));
        var orderA = Enumerable.Range(0, 6).Select(_ => byThree.Draw()).ToList();
        var orderB = Enumerable.Range(0, 6).Select(_ => byNine.Draw()).ToList();
        Assert.NotEqual(orderA, orderB);

        // Same seed, same order — so the difference above is the SHUFFLE and not the filesystem.
        Assert.Equal(orderA[0], seedA[0]);
    }

    [Fact]
    public void AnEmptyOrMissingFolderDrawsNOTHING_AndNamesItselfRatherThanBeingCreated()
    {
        using var scratch = new Scratch();
        var pool = new VideoClipPool(scratch.Path, new Random(1));
        Assert.Equal(0, pool.ActiveCount);
        Assert.Null(pool.Draw());
        Assert.EndsWith(VideoClipPool.VideosFolderName, pool.Folder, StringComparison.Ordinal);

        // Upstream CREATES its videos folder (VideoService.cs:1213); the port does not. A folder the
        // user never asked for is a folder they cannot find, and the panel names the path instead —
        // so nothing at all appears under the assets root just because a pool was built and drawn from.
        Assert.Empty(Directory.GetFileSystemEntries(scratch.Path));
    }

    // ---------------------------------------------------------------------------------------
    //  THE MODULE'S THREE ARM OUTCOMES
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void WithNoDisplayTheModuleIsUNAVAILABLE_AndCarriesTheCAPABILITYSOwnReason()
    {
        using var rig = new Rig(canReachADisplay: false);
        rig.Surface.LastPlacement = new CapabilityState.Unavailable(new CapabilityReason(
            VideoReasonCodes.VideoMechanismAbsent, "the capability's own words, carried verbatim"));
        rig.Enable();

        var state = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.Arm());
        Assert.Equal(EffectReasonCodes.VideoSurfaceUnavailable, state.Reason.Code);
        Assert.Contains("the capability's own words, carried verbatim", state.Reason.Detail, StringComparison.Ordinal);

        // The whole CHANNEL is gone, so the dot is Armed rather than Live — the Pink Filter answer.
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
    }

    [Fact]
    public void AHealthyRunIsSTILLDegraded_BecauseTheClipsSoundIsNotPortedAndThatIsAPropertyOfTheBUILD()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();

        // EVERY other module in the rack reports a clean Available when everything works. This one
        // must not: the absence is a property of the BUILD, not of the run, and a row that only
        // admitted to being half a row when something else broke would be the silently-missing half
        // the reason code exists to prevent. SP-109's Brain Drain set this precedent.
        var state = Assert.IsType<CapabilityState.Degraded>(rig.Effect.Arm());
        Assert.Equal(EffectReasonCodes.VideoSilentHalfAbsent, state.Reason.Code);
        Assert.Equal(MandatoryVideoEffect.VideoPanelNoticeText, state.Reason.Detail);
        Assert.Contains("SILENTLY", state.SurvivingSemantics, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPoolDegradesToo_AndBOTHCausesTravel_TheRunLevelOneFIRST()
    {
        using var rig = new Rig();
        rig.Enable();

        var state = Assert.IsType<CapabilityState.Degraded>(rig.Effect.Arm());

        // The code is the RUN-level one, because that is the one the user can fix.
        Assert.Equal(EffectReasonCodes.VideoNoClip, state.Reason.Code);
        Assert.StartsWith("there is no video in ", state.Reason.Detail, StringComparison.Ordinal);

        // And the BUILD-level one still travels, in the same detail. SP-109 shipped this defect once
        // — Brain Drain's Ready replaced the run-level cause with the build-level one and the user
        // lost "there is no clip in <folder>" entirely.
        Assert.Contains(MandatoryVideoEffect.VideoPanelNoticeText, state.Reason.Detail, StringComparison.Ordinal);

        // A pool is CONTENT, not a channel: the dot stays Live and dropping a clip in is picked up
        // at the next firing with no re-arm. The Subliminals answer.
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
    }

    [Fact]
    public void ADisabledModuleSaysSoInTypeRatherThanArmingSilently()
    {
        using var rig = new Rig();
        var state = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.Arm());
        Assert.Equal(EffectReasonCodes.EffectDialOff, state.Reason.Code);
        Assert.Equal(EffectDotState.Off, rig.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------------
    //  THE DOT'S SEVENTH MEANING — MOTION
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ALLTHREEClausesOfTheSeventhMeaningAreLoadBearing()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();
        rig.Effect.Arm();

        // Clause 1 — a firing on the clock. Present here by construction.
        Assert.True(rig.Effect.ScheduleArmed);

        // With nothing playing, the module is Live: a session spends almost all its time between
        // clips, and a bare "the picture is moving" conjunct would darken the dot for 95 % of it.
        Assert.False(rig.Surface.Showing);
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);

        // Clause 2 — the OS says a picture could reach somebody.
        rig.Surface.CanReachADisplay = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
        rig.Surface.CanReachADisplay = true;

        // Clause 3 — WHILE A CLIP IS UP, the OS's own copy of the surface must have advanced. A
        // frozen picture is upstream's own worst failure and every call still succeeds
        // (VideoService.cs:2677-2678).
        rig.Surface.Showing = true;
        rig.Surface.Running = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);

        rig.Surface.Running = true;
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);

        // AND the third clause is a DISJUNCTION, which is its own claim: with NOTHING playing the dot
        // stays Live even though the surface is not advancing, because a session spends almost all of
        // its time between clips. Requiring motion unconditionally would darken the dot for 95 % of a
        // session, which is the opposite lie from the one the clause prevents.
        rig.Surface.Showing = false;
        rig.Surface.Running = false;
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
    }

    [Fact]
    public void TheROWSTITLEIsLoadBearing_BecauseTheDotIsOnlyHonestBecauseTheRowNamesItsScope()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();
        rig.Effect.Arm();

        Assert.Equal("Mandatory Video (silent half)", MandatoryVideoEffect.DisplayTitle);
        Assert.Equal(MandatoryVideoEffect.DisplayTitle, rig.Effect.Title);
        Assert.Equal("video", MandatoryVideoEffect.EffectId);

        // Asserted in the SAME fact as the dot it justifies: a later tidy of the label back to plain
        // "Mandatory Video" turns a truthful dot into a false one AND reds here.
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------------
    //  THE FIRING
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AFiringPlaysADrawnClipWithTheDialsMaxLength_AndTheEVENTCarriesNoPath()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable(maxSeconds: 42);
        rig.Effect.Arm();

        MandatoryVideoEvent? fired = null;
        rig.Effect.Fired += e => fired = e;
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(["only.mp4"], rig.Surface.Begun);
        Assert.Equal(TimeSpan.FromSeconds(42), rig.Surface.LastMaxLength);

        // SP-112 added an optional PAINTER to this seam for a second module. THIS module passes
        // none, and the double records that so the claim is asserted rather than promised: the
        // capability's second consumer must not have changed what the first one does.
        Assert.Null(rig.Surface.LastPainter);
        Assert.Equal(1, rig.Effect.PlayedCount);
        Assert.NotNull(fired);
        Assert.Equal(1, fired!.Value.Ordinal);

        // The event carries no path and no file name: the clips are the user's own media and this
        // record reaches subscribers and, one day, a log. SP-098's rule, kept.
        Assert.DoesNotContain("only.mp4", fired.Value.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("already showing")]
    [InlineData("no display")]
    [InlineData("empty pool")]
    public void AFiringThatCanShowNothing_CountsNOTHING_AndKeepsTheScheduleRunning(string situation)
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();

        switch (situation)
        {
            case "already showing":
                rig.Surface.Showing = true;
                break;
            case "no display":
                rig.Surface.CanReachADisplay = false;
                break;
            case "empty pool":
                rig.Pool.Clips.Clear();
                break;
        }

        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Empty(rig.Surface.Begun);
        Assert.Equal(0, rig.Effect.PlayedCount);
        Assert.True(
            rig.Effect.ScheduleArmed,
            "a firing that showed nothing must still re-arm: upstream's scheduler keeps running through "
            + "every one of these (VideoService.cs:2237, :6647-6650)");
    }

    [Fact]
    public void ARefusedPlaybackIsREMEMBERED_BecauseItIsTheOnlyPlaceAUserLearnsTheSurfaceNeverHeldIt()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Surface.BeginResult = new CapabilityState.Unavailable(new CapabilityReason(
            VideoReasonCodes.VideoFrameNotHeld, "the operating system's copy of the surface does not carry it"));

        // Exactly ONE firing: a refused playback leaves the surface not showing, so the schedule's
        // next firing is not dropped by the already-playing guard and a bigger advance would count
        // two. Advancing to the due moment is what makes the count mean what it says.
        rig.Clock.AdvanceToNextDue();

        var remembered = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.LastPlayback);
        Assert.Equal(VideoReasonCodes.VideoFrameNotHeld, remembered.Reason.Code);

        // Upstream counts the firing that got as far as calling the player, and so does this: what
        // the SURFACE did with it is LastPlayback's question, not the counter's.
        Assert.Equal(1, rig.Effect.PlayedCount);
    }

    [Fact]
    public void WhenTheClipENDSTheScheduleIsREPACEDFromTheEND_WhichIsUpstreamsOwnOrder()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable(perHour: 1);
        rig.Effect.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Single(rig.Surface.Begun);

        var dueAfterStart = rig.Clock.NextDue;
        rig.Clock.Advance(TimeSpan.FromMinutes(20));
        rig.Surface.RaiseEnded();

        // Upstream re-schedules when a video ENDS rather than when it starts ("Cleanup() will call
        // ScheduleNext() when video ends", VideoService.cs:2264). Without the re-pace, the next clip
        // would come due a full interval after the previous one STARTED — so a long clip would be
        // followed almost immediately by the next.
        Assert.True(
            rig.Clock.NextDue > dueAfterStart,
            $"the clip ending must push the next firing out; it was due {dueAfterStart} and is now "
            + $"due {rig.Clock.NextDue}");
    }

    [Fact]
    public void DisarmEndsTheClipUNCONDITIONALLY()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Single(rig.Surface.Begun);

        rig.Effect.Disarm();
        Assert.Equal(1, rig.Surface.Ends);
        Assert.False(rig.Effect.ScheduleArmed);
        // Enabled is still true, so the dot is ARMED rather than Off: the dial did not move, the
        // schedule did. Off is reserved for a module the user switched off.
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------------
    //  fixtures
    // ---------------------------------------------------------------------------------------

    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-sp111-pool-" + Guid.NewGuid().ToString("N"));
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

    private sealed class Rig : IDisposable
    {
        private readonly OperationRegistry _registry = new();
        private readonly string _directory;

        public Rig(bool canReachADisplay = true)
        {
            _directory = Path.Combine(Path.GetTempPath(), "ccp-sp111-mod-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            Clock = new ManualClock();
            Surface = new RecordingVideoSurface { CanReachADisplay = canReachADisplay };
            Pool = new StubVideoPool(Path.Combine(_directory, "videos"));
            Preset = new PersistenceStore<MandatoryVideoPresetDocument>(
                _registry.OwnerFor("VideoPreset"), new NullLog(),
                Path.Combine(_directory, MandatoryVideoPresetDocument.FileName),
                MandatoryVideoPresetDocument.CurrentSchemaVersion);

            Effect = new MandatoryVideoEffect(
                _registry.OwnerFor("MandatoryVideo"),
                new EffectSignal(boundary, static () => true),
                Clock,
                Preset,
                Pool,
                Surface,
                // A fixed roll: the SPACING is what these facts make deterministic, and the
                // arithmetic it is fed into stays the module's own.
                new Random(11));
        }

        public ManualClock Clock { get; }

        public RecordingVideoSurface Surface { get; }

        public StubVideoPool Pool { get; }

        public PersistenceStore<MandatoryVideoPresetDocument> Preset { get; }

        public MandatoryVideoEffect Effect { get; }

        public void Enable(int perHour = MandatoryVideoSchedule.DefaultPerHour, int maxSeconds = 0) =>
            Preset.Mutate(p =>
            {
                p.Enabled = true;
                p.PerHour = perHour;
                p.MaxSeconds = maxSeconds;
            });

        public void Dispose()
        {
            Effect.Disarm();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A video surface that records what it was asked to do and never touches a window or a decoder.
    ///
    /// <para><b>It decides nothing the product should decide.</b> It reports whatever state the fact
    /// set and hands back whatever outcome the fact chose, and it MIRRORS the product where the
    /// product's own state transitions matter — <c>Begin</c> marks the surface showing only when it
    /// succeeded, exactly as <see cref="VideoSurfacePresenter"/> does, because SP-110 shipped a
    /// double that diverged from the product in precisely the state a defect lived in.</para>
    /// </summary>
    private sealed class RecordingVideoSurface : IVideoSurface
    {
        private Action? _onEnded;

        public List<string> Begun { get; } = [];

        public int Ends { get; private set; }

        public TimeSpan LastMaxLength { get; private set; }

        public CapabilityState BeginResult { get; set; } = new CapabilityState.Available("the double allowed it");

        public bool Showing { get; set; }

        public bool Running { get; set; } = true;

        public bool Engaged => Showing;

        public bool CanReachADisplay { get; set; } = true;

        public int FramesDecoded { get; set; }

        public int FramesHeld { get; set; }

        public int FramesAdvanced { get; set; }

        public string? PlayingClip { get; private set; }

        public CapabilityState? LastPlacement { get; set; }

        public VideoSurfaceObservation LastObservation { get; set; } = VideoSurfaceObservation.NotAsked;

        /// <summary>The painter the last Begin was handed, or null. SP-112 added the seam; this
        /// module never uses it, and the double records it so a fact can assert that.</summary>
        public IVideoFramePainter? LastPainter { get; private set; }

        public CapabilityState Begin(
            string clipPath, TimeSpan maxLength, Action onEnded, IVideoFramePainter? painter = null)
        {
            Begun.Add(clipPath);
            LastMaxLength = maxLength;
            _onEnded = onEnded;
            LastPainter = painter;
            LastPlacement = BeginResult;
            if (BeginResult is CapabilityState.Available)
            {
                Showing = true;
                PlayingClip = clipPath;
            }

            return BeginResult;
        }

        public void End()
        {
            Ends++;
            Showing = false;
            PlayingClip = null;
        }

        /// <summary>What the presenter does when the clip finishes: end, then tell the caller.</summary>
        public void RaiseEnded()
        {
            var ended = _onEnded;
            End();
            ended?.Invoke();
        }
    }

    private sealed class StubVideoPool(string folder) : IVideoClipPool
    {
        public List<string> Clips { get; } = [];

        public int ActiveCount => Clips.Count;

        public string Folder => folder;

        public string? Draw() => Clips.Count == 0 ? null : Clips[0];
    }

    private sealed class ManualClock : ISessionClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset? NextDue
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Where(t => !t.Cancelled).Select(t => (DateTimeOffset?)t.Due).Min();
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

            return new Handle(entry);
        }

        /// <summary>Move forward to the soonest live timer and run exactly it.</summary>
        public void AdvanceToNextDue()
        {
            if (NextDue is { } due)
            {
                Advance(due - UtcNow);
            }
        }

        /// <summary>Move time forward and run everything that comes due, in order.</summary>
        public void Advance(TimeSpan by)
        {
            var target = UtcNow + by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => !t.Cancelled && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is not null)
                    {
                        _timers.Remove(next);
                    }
                }

                if (next is null)
                {
                    UtcNow = target;
                    return;
                }

                UtcNow = next.Due;
                next.Fire();
            }
        }

        private sealed class Handle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
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
        public void Post(Action action) => action();
    }
}
