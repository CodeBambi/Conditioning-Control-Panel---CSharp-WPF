using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Pointer;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The sound three silent surfaces make</b> — the flash's clip, the ambient bubble field's pop,
/// and the counting clip's pop — and the two doors on the app-wide arbitration they go through.
///
/// <para><b>What these facts are for.</b> The arbitration has had a busy-signalling whisper channel
/// and a bounded overlapping SFX pool since it landed, and until this row nothing outside the DTRH
/// host window played through either. "Which door a surface uses" is not a matter of taste here: the
/// whisper channel is stop-replace and raises <c>WhisperBusy</c>, which
/// <c>Companion/BarkPipeline.cs:416-419</c> already reads to keep the companion from talking over a
/// clip; the SFX pool overlaps and drops at capacity. Put a burst of pops on the whisper channel and
/// each one cuts the last off — a defect the user hears at once — so the door is asserted rather
/// than described.</para>
///
/// <para><b>What they do NOT prove, stated because audio is a real device.</b> The backend below is
/// a recording fake. Nothing here shows that an operating system opened a render endpoint, that a
/// sample reached a mixer, or that a human heard anything: these facts are about the wiring above
/// the device, exactly as <see cref="AudioParticipantTests"/> says of its own. The device-level
/// gates are <c>Audio/IAudioPresence.cs</c>'s read-back and
/// <c>AudioPresenceFactory.LinuxManualGate</c>, and no headless run discharges either.</para>
///
/// <para><b>And the clips themselves do not ship.</b> Upstream reads 118 voice lines from
/// <c>Resources/sounds/flashes_audio</c> and three pops from <c>Resources/sounds/bubbles</c>; those
/// are legacy-tree bytes and this port forks none of them, so BOTH pools are empty on a fresh
/// install. That is a first-class outcome here, not an edge case — see
/// <see cref="AnEmptyClipFolderIsRefusedByNAME_AndNeverBringsADeviceUp"/>.</para>
/// </summary>
public sealed class EffectSoundsTests
{
    // =====================================================================================
    //  THE TWO DOORS
    // =====================================================================================

    [Fact]
    public void AFlashTakesTheWHISPERDoor_AndRaisesTheBusySignalTheCompanionRespects()
    {
        using var lab = new Lab();

        // Upstream's flash sound is stop-replace by construction: PlaySound opens with
        // StopCurrentSound() (Services/Flash/FlashService.cs:3516-3518) and keeps exactly one
        // _currentSound field (:3539).
        var first = Assert.IsType<SoundOutcome.Started>(lab.Sounds.Flash());
        Assert.Equal(SoundChannel.Whisper, first.Channel);

        // And its caller then tells the bark system a clip is audible so the companion will not talk
        // over it — App.Audio?.MarkWhisperAudio(duration) (:1044). In this port that half is not a
        // comment: BarkPipeline.cs:416-419 gates a bark with the reason "whisper-active" while this
        // flag holds, which BarkPipelineTests already pins from the other side.
        Assert.True(lab.Audio.Arbitration.WhisperBusy);

        // A second flash REPLACES the first, which is what "one _currentSound" means to a user: the
        // older clip stops mid-word rather than the two overlapping.
        Assert.IsType<SoundOutcome.Started>(lab.Sounds.Flash());
        Assert.Equal(2, lab.Backend.Plays.Count);
        Assert.True(lab.Backend.Plays[0].Stopped);
        Assert.False(lab.Backend.Plays[1].Stopped);
    }

    [Fact]
    public void PopsTakeTheSFXPoolAndOVERLAP_BecauseABurstMustNotCutItselfOff()
    {
        using var lab = new Lab();

        // Upstream's pops are App.Audio.PlayOneShot (Services/BubbleService.cs:2027,
        // Windows/BubbleCountWindow.xaml.cs:1343), which overlaps concurrent clips rather than
        // replacing them (Services/Audio/AudioService.Playback.cs:212-218 only refuses PAST the cap).
        for (var i = 0; i < 3; i++)
        {
            var started = Assert.IsType<SoundOutcome.Started>(lab.Sounds.PopNow());
            Assert.Equal(SoundChannel.Sfx, started.Channel);
        }

        // THE DOOR, ASSERTED FROM THE OUTCOME A USER HEARS: three pops in flight, none of them
        // stopped by the next. Routing a pop to the whisper channel would leave one player alive and
        // two stopped, and would also silence the companion for the length of a bubble pop.
        Assert.Equal(3, lab.Backend.Plays.Count);
        Assert.DoesNotContain(lab.Backend.Plays, p => p.Stopped);
        Assert.False(lab.Audio.Arbitration.WhisperBusy);
    }

    // =====================================================================================
    //  THE TWO VOLUME LAWS — different in a way the user hears, so they are separate
    // =====================================================================================

    [Fact]
    public void TheFlashClipKeepsUpstreamsFIVEPERCENTFloor_AndItsCurveAboveIt()
    {
        using var lab = new Lab();

        // Math.Max(0.05f, (float)Math.Pow(volume, 1.5)) — Services/Flash/FlashService.cs:3529-3530,
        // whose own comment reads "Apply volume curve (gentler, minimum 5%)". At master 0 the curve
        // gives nothing and the floor is the whole answer.
        lab.SetMasterVolume(0);
        lab.Sounds.Flash();
        Assert.Equal(0.05, lab.LastGain, 5);

        // Still floored at 10, where the curve alone would give 0.0316: the floor really bites over a
        // RANGE rather than at one point.
        lab.SetMasterVolume(10);
        lab.Sounds.Flash();
        Assert.Equal(0.05, lab.LastGain, 5);

        // And above the floor it is the curve and nothing else: 0.64^1.5 = 0.512. A linear law would
        // answer 0.64 here and 0.10 above.
        lab.SetMasterVolume(64);
        lab.Sounds.Flash();
        Assert.Equal(0.512, lab.LastGain, 5);
    }

    [Fact]
    public void APopCarriesTheMissingBUBBLESDial_SoItIsNotThreeTimesTheShippingAppsLoudness()
    {
        using var lab = new Lab();

        // Math.Pow(masterVolume * bubblesVolume, 1.5) — Services/BubbleService.cs:2002-2006, and the
        // counting window computes the same product at Windows/BubbleCountWindow.xaml.cs:1342. This
        // port has no BubblesVolume dial, so the LAW is ported with the factor pinned at upstream's
        // own default of 50 (Models/AppSettings.cs:2790). Dropping the factor instead would answer
        // 1.0 here — roughly three times as loud as the shipping app at the same master.
        lab.SetMasterVolume(100);
        lab.Sounds.PopNow();
        Assert.Equal(0.353553, lab.LastGain, 5);

        // (0.40 x 0.50)^1.5 = 0.089443. Without the dial: 0.252982. Without the curve: 0.2.
        lab.SetMasterVolume(40);
        lab.Sounds.PopNow();
        Assert.Equal(0.089443, lab.LastGain, 5);
    }

    [Fact]
    public void AtZeroMasterAPopIsRefusedBEFORETheDevice_WhileAFlashStillSoundsAtFivePercent()
    {
        using var lab = new Lab();
        lab.SetMasterVolume(0);

        // Upstream refuses a muted one-shot at the top of PlayOneShot — "Muted — don't touch the
        // audio stack at all" (Services/Audio/AudioService.Playback.cs:183-187) — and the refusal is
        // BEFORE the device, so a user at zero master never brings an endpoint up by clicking
        // bubbles.
        var refused = Assert.IsType<SoundOutcome.Unavailable>(lab.Sounds.PopNow());
        Assert.Contains("master volume is zero", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(0, lab.Audio.DeviceInitAttempts);
        Assert.Empty(lab.Backend.Plays);

        // THE ASYMMETRY IS UPSTREAM'S AND IS PORTED RATHER THAN CORRECTED. The flash gate at
        // FlashService.cs:1037 tests FlashAudioEnabled and never the volume, and PlaySound's own
        // floor is 5%, so a user with master at zero still hears the flash clip. Same document, same
        // moment, opposite outcome.
        Assert.IsType<SoundOutcome.Started>(lab.Sounds.Flash());
        Assert.Equal(0.05, lab.LastGain, 5);
        Assert.Equal(1, lab.Audio.DeviceInitAttempts);
    }

    // =====================================================================================
    //  THE CLIPS THAT DO NOT SHIP
    // =====================================================================================

    [Fact]
    public void AnEmptyClipFolderIsRefusedByNAME_AndNeverBringsADeviceUp()
    {
        using var lab = new Lab(flashClips: [], popClips: []);

        // Upstream's own folders ship full and this port forks none of their bytes, so a fresh
        // install has nothing to play. That is answered TYPED and by NAME — never as silence with no
        // reason, and never by inventing a clip.
        var flash = Assert.IsType<SoundOutcome.Unavailable>(lab.Sounds.Flash());
        Assert.Contains(lab.Sounds.FlashClipFolder, flash.Reason, StringComparison.Ordinal);

        var pop = Assert.IsType<SoundOutcome.Unavailable>(lab.Sounds.PopNow());
        Assert.Contains(lab.Sounds.PopClipFolder, pop.Reason, StringComparison.Ordinal);

        // The reason names a FOLDER, so the user can act on it, and it says the shipping app's own
        // clips are legacy bytes rather than implying the build is broken.
        Assert.Contains("this build ships none", flash.Reason, StringComparison.Ordinal);

        // AND THE DEVICE IS NEVER ASKED FOR. A folder with nothing in it must not seize a render
        // endpoint, which is the whole reason AudioParticipant.EnsureDevice is a first-need call: the
        // draw comes first and the device only follows a clip that really exists.
        Assert.Equal(0, lab.Audio.DeviceInitAttempts);
        Assert.Empty(lab.Backend.Plays);
    }

    // =====================================================================================
    //  THE DRAW POLICIES — the two modules deal differently and a user hears it
    // =====================================================================================

    [Fact]
    public void TheFlashDealsEVERYClipBeforeRepeatingOne_WhichIsNotHowTheOtherPoolsDraw()
    {
        using var folder = new TempAssets();
        var names = Enumerable.Range(0, 8).Select(i => folder.WriteClip("flashes_audio", $"line{i}.wav")).ToArray();

        // Upstream deals its flash clips out of a shuffled queue and only reshuffles when it empties
        // (Services/Flash/FlashService.cs:3315-3329), so every clip in the folder is heard once
        // before any is heard twice. The seed is fixed, so this is a deterministic statement about
        // the DRAW rather than a probabilistic one: a uniform pick over eight files would hit eight
        // distinct draws about once in four hundred runs, and never with this seed.
        var bag = new AudioCuePool(folder.Root, "flashes_audio", new Random(20260824), withoutReplacement: true);
        var firstBag = Enumerable.Range(0, 8).Select(_ => bag.Draw()).ToArray();
        Assert.Equal(8, firstBag.Distinct().Count());
        Assert.All(firstBag, path => Assert.Contains(path!, names));

        // The ninth draw starts a fresh bag, which is what "refilled when it empties" means: the
        // property holds for every run of eight, not only for the first.
        var secondBag = Enumerable.Range(0, 8).Select(_ => bag.Draw()).ToArray();
        Assert.Equal(8, secondBag.Distinct().Count());

        // The pops draw the OTHER way, and that is upstream's too: one uniform pick over the three
        // shipped files on every pop (Services/BubbleService.cs:1996-1997), so the same pop can sound
        // twice running. A bag here would make three pops in a row always sound different, which is
        // audibly not the shipping app.
        folder.WriteClip("bubbles", "Pop.mp3");
        folder.WriteClip("bubbles", "Pop2.mp3");
        var uniform = new AudioCuePool(folder.Root, "bubbles", new Random(20260824));
        var twenty = Enumerable.Range(0, 20).Select(_ => uniform.Draw()).ToArray();
        Assert.Equal(2, twenty.Distinct().Count());
        Assert.Contains(twenty.Zip(twenty.Skip(1)), pair => pair.First == pair.Second);
    }

    // =====================================================================================
    //  THE THREAD A POP LEAVES ON
    // =====================================================================================

    [Fact]
    public void APopLEAVESTheCallersThread_AndAFlashDeliberatelyDoesNot()
    {
        var posted = new List<Action>();
        using var lab = new Lab(post: posted.Add);

        // Upstream's pop path is asynchronous on purpose and says why: "Pop sounds fire in bursts,
        // which is exactly the pattern that used to park two thread-pool threads per bubble"
        // (Services/BubbleService.cs:2022-2026), and the counting window says it again in a line of
        // its own — "Run everything off UI thread to avoid blocking LibVLC rendering" (:1331). It
        // matters here for a concrete reason: a pop's two callers are a pointer message pump and a
        // video frame painter, and this port's player construction BLOCKS its caller for up to two
        // seconds against a wedged endpoint (Audio/AudioSeams.cs:244-245).
        lab.Sounds.Pop();
        Assert.Single(posted);
        Assert.Empty(lab.Backend.Plays);

        posted[0]();
        Assert.Single(lab.Backend.Plays);

        // THE FLASH DOES NOT USE IT, and the reason is ordering rather than taste: the whisper
        // channel is stop-replace, so handing two flashes to a pool would put no order on which
        // reaches the channel first and could leave the OLDER clip playing — upstream's
        // StopCurrentSound inverted. Upstream plays its flash clip inline too
        // (Services/Flash/FlashService.cs:1042, reached from :634-637).
        lab.Sounds.Flash();
        Assert.Single(posted);
        Assert.Equal(2, lab.Backend.Plays.Count);
    }

    [Fact]
    public void AFaultOnThePopThreadIsCONTAINEDAndNAMED_BecauseAnEscapeThereEndsTheProcess()
    {
        var logged = new List<string>();
        using var lab = new Lab(popPool: new ThrowingCuePool(), post: work => work(), log: logged.Add);

        // The default hand-off is a thread-pool work item, where an unhandled exception ends the
        // process. The arbitration answers typed rather than throwing, so reaching the catch is a
        // defect somewhere below — which is exactly why it must be named rather than swallowed.
        Assert.Null(Record.Exception(lab.Sounds.Pop));

        var failed = Assert.IsType<SoundOutcome.Failed>(lab.Sounds.LastPop);
        Assert.Contains("the pool is broken", failed.Error, StringComparison.Ordinal);
        Assert.Contains(logged, line => line.Contains("a pop faulted and was contained", StringComparison.Ordinal));
    }

    // =====================================================================================
    //  THE COUNTING GAME'S POP
    // =====================================================================================

    [Fact]
    public void EachCountedBubbleSoundsExactlyONCE_AtTheMomentItStartsPopping()
    {
        var pops = 0;
        var run = new BubbleCountRun(BubbleCountDifficulty.Easy, new SequenceRandom(0.5), () => pops++);
        var frame = VideoFrame.Solid(320, 240, 0, 0, 0);

        // The lead-in tick spawns one bubble and nothing has popped yet.
        run.Paint(frame, BubbleCountArithmetic.SpawnLeadIn);
        Assert.Equal(1, run.BubblesShown);
        Assert.Equal(0, pops);

        var bubble = run.Bubbles[0];
        run.Paint(frame, bubble.PopsAt - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, pops);

        // Upstream sounds from StartPopping, whose first line is `if (_isPopping || _isDisposed)
        // return;` (Windows/BubbleCountWindow.xaml.cs:1795-1797), so the clip is tied to the
        // TRANSITION into popping — one per bubble, however many animation ticks the pop takes.
        run.Paint(frame, bubble.PopsAt);
        Assert.Equal(1, pops);

        run.Paint(frame, bubble.PopsAt + TimeSpan.FromMilliseconds(50));
        run.Paint(frame, bubble.GoneAt + TimeSpan.FromMilliseconds(50));
        Assert.Equal(1, pops);

        // The NEXT bubble is its own sound, so a run does not go quiet after the first one.
        run.Paint(frame, BubbleCountArithmetic.SpawnLeadIn + run.SpawnInterval);
        Assert.Equal(2, run.BubblesShown);
        run.Paint(frame, run.Bubbles[1].PopsAt);
        Assert.Equal(2, pops);
    }

    [Fact]
    public void ABubbleWhosePopFellBetweenTwoPicturesStillSounds_BecauseUpstreamsTimerDoesNotWaitForAFrame()
    {
        var pops = 0;
        var run = new BubbleCountRun(BubbleCountDifficulty.Easy, new SequenceRandom(0.5), () => pops++);
        var frame = VideoFrame.Solid(320, 240, 0, 0, 0);

        run.Paint(frame, BubbleCountArithmetic.SpawnLeadIn);
        var bubble = run.Bubbles[0];

        // Upstream's bubble animation rides its own 30 ms DispatcherTimer
        // (Windows/BubbleCountWindow.xaml.cs:1742-1793), not the video's frames, so a stall in the
        // clip does not make a bubble pop silently. Jumping the whole pop window here — past GoneAt,
        // where the bubble is no longer drawn at all — must still sound exactly once.
        run.Paint(frame, bubble.GoneAt + TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, pops);
    }

    // =====================================================================================
    //  THE AMBIENT GAME'S POP
    // =====================================================================================

    [Fact]
    public void AClickThatREALLYPopsSoundsOnce_AndASecondClickOnTheSameBubbleIsSilent()
    {
        var pops = 0;
        var surface = new RecordingPointerSurface();
        var clock = new ManualClock();
        using var presenter = new BubblePopSurfacePresenter(
            clock, static action => action(), () => surface,
            () => new PointerBounds(0, 0, 1920, 1080), () => new Random(101), () => pops++);

        presenter.Engage(new BubblePopSettings(1, 100, 0));
        var target = surface.Opened[0].Handle;

        // Upstream's sound sits BEHIND the double-pop guard: Pop() returns at
        // Services/BubbleService.cs:3994 when the bubble is already popping, and the pop reward that
        // reaches PlayPopSound (:961) is invoked from inside Pop() at :4064 — so a second click on a
        // popping bubble makes no noise. (Its other click-time callback, _onClickPop at :3984,
        // carries the E-Stim charge and not the sound.)
        surface.DeliverPress(target, PointerPressKind.Down);
        clock.Advance(BubblePopField.StepInterval);
        Assert.Equal(1, pops);

        surface.DeliverPress(target, PointerPressKind.Down);
        clock.Advance(BubblePopField.StepInterval);
        Assert.Equal(1, pops);

        // A press the OS routed to a handle this field does not own pops nothing and sounds nothing.
        surface.DeliverPress(9999, PointerPressKind.Down);
        clock.Advance(BubblePopField.StepInterval);
        Assert.Equal(1, pops);

        // And once the pop animation has run out, the field agrees: one pop scored, one sound made.
        // A sound that outran the field — or a field that scored a pop nobody heard — would be the
        // two halves of a click disagreeing.
        for (var i = 0; i < 20; i++)
        {
            clock.Advance(BubblePopField.StepInterval);
        }

        Assert.Equal(1, presenter.Popped);
        Assert.Equal(1, pops);
    }

    // =====================================================================================
    //  THE COMPOSITION
    // =====================================================================================

    [Fact]
    public void TheSessionHandsTheONEArbitrationToTheFlash_AndNamesBothFoldersUnderTheUserMediaRoot()
    {
        using var dir = new TempAssets();
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new NullLog());
        var audio = new AudioParticipant(infra, dir.Root, new RecordingBackend());
        using var arbitration = audio.Arbitration;

        var session = new SessionParticipant(infra, dir.Root, appAudio: audio);

        // Both pools live under the ONE user-media root every other module draws from, in upstream's
        // own folder names (Services/Companion/CompanionPhraseService.cs:34-35 and
        // Services/BubbleService.cs:1998).
        Assert.NotNull(session.Sounds);
        Assert.Equal(
            Path.Combine(SessionParticipant.AssetsRootFor(dir.Root), "sounds", "flashes_audio"),
            session.Sounds!.FlashClipFolder);
        Assert.Equal(
            Path.Combine(SessionParticipant.AssetsRootFor(dir.Root), "sounds", "bubbles"),
            session.Sounds.PopClipFolder);

        // The flash module got THE SAME instance rather than a second one of its own — one pool, one
        // bag, one arbitration.
        Assert.Equal(session.Sounds.FlashClipFolder, session.Flash.ClipFolder);

        // Construction opens NO device and reads no folder: a session that never flashes and never
        // pops never seizes a render endpoint, which is the property AudioParticipant keeps honest.
        Assert.Equal(0, audio.DeviceInitAttempts);
    }

    [Fact]
    public void AHostBuiltWithNoAudioOwnerHasNoClipPathAtAll_RatherThanASilentOne()
    {
        using var dir = new TempAssets();
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new NullLog());

        var session = new SessionParticipant(infra, dir.Root);

        // Null is "there is no app audio here", which is a different state from "the folder is
        // empty" and must not be collapsed into it: an owner-less host reports no clip folder at all.
        Assert.Null(session.Sounds);
        Assert.Null(session.Flash.ClipFolder);
        Assert.Null(session.Flash.LastSound);
    }

    // =====================================================================================
    //  harness
    // =====================================================================================

    /// <summary>The two pools, the app-wide audio owner and a recording backend, wired the way the
    /// product wires them but over doubles that touch neither a disk nor a device.</summary>
    private sealed class Lab : IDisposable
    {
        private readonly TempAssets _dir = new();

        public Lab(
            string[]? flashClips = null,
            string[]? popClips = null,
            IAudioCuePool? popPool = null,
            Action<Action>? post = null,
            Action<string>? log = null)
        {
            var infra = new ParticipantInfrastructure(
                new OperationRegistry(), new UiDispatchBoundary(), new NullLog());
            Audio = new AudioParticipant(infra, _dir.Root, Backend);
            Sounds = new EffectSounds(
                Audio,
                new StubCuePool(Path.Combine(_dir.Root, "sounds", "flashes_audio"), flashClips ?? ["line.wav"]),
                popPool ?? new StubCuePool(Path.Combine(_dir.Root, "sounds", "bubbles"), popClips ?? ["Pop.mp3"]),
                post,
                log);
        }

        public RecordingBackend Backend { get; } = new();

        public AudioParticipant Audio { get; }

        public EffectSounds Sounds { get; }

        /// <summary>The gain the arbitration handed the backend for the most recent player. Read
        /// from the CONSTRUCTION request, which is the only part of the ask a test can honestly
        /// observe.</summary>
        public double LastGain => Backend.Plays[^1].Gain;

        public void SetMasterVolume(int percent) => Audio.Settings.Mutate(d => d.MasterVolume = percent);

        public void Dispose()
        {
            Audio.Arbitration.Dispose();
            _dir.Dispose();
        }
    }

    /// <summary>A temp directory that stands in for the data directory and the user-media root under
    /// it. Content-free: every clip written here is an empty file with a real extension, because
    /// nothing in these facts decodes one.</summary>
    private sealed class TempAssets : IDisposable
    {
        public TempAssets() => Directory.CreateDirectory(Root);

        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "ccp-sfx-" + Guid.NewGuid().ToString("N"));

        public string WriteClip(string moduleFolder, string fileName)
        {
            var folder = Path.Combine(Root, AudioCuePool.SoundsFolderName, moduleFolder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp reaper owns the residue.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class StubCuePool(string folder, string[] clips) : IAudioCuePool
    {
        private int _index;

        public int ActiveCount => clips.Length;

        public string Folder => folder;

        public string? Draw() => clips.Length == 0 ? null : clips[_index++ % clips.Length];

        public void Invalidate() => _index = 0;
    }

    /// <summary>A pool that faults where a real one cannot, so the containment on the pop thread has
    /// something to contain.</summary>
    private sealed class ThrowingCuePool : IAudioCuePool
    {
        public int ActiveCount => throw new InvalidOperationException("the pool is broken");

        public string Folder => "(broken)";

        public string? Draw() => throw new InvalidOperationException("the pool is broken");

        public void Invalidate()
        {
        }
    }

    /// <summary>The real backend is SoundFlow over a hardware device. This records what the
    /// arbitration ASKED the platform for — the path and the gain — which is the only part of the
    /// request a test can honestly observe.</summary>
    private sealed class RecordingBackend : IAudioBackend
    {
        public List<RecordingPlayer> Plays { get; } = [];

        public IReadOnlyList<string> EnumerateDevices() => ["Fake Endpoint"];

        public bool TryInit(string? deviceName, out string? error)
        {
            error = null;
            return true;
        }

        public IAudioPlayer CreatePlayer(string path, float volume)
        {
            var player = new RecordingPlayer(path, volume);
            Plays.Add(player);
            return player;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingPlayer(string path, float gain) : IAudioPlayer
    {
        /// <summary>Never raised: nothing here reaches natural completion, so no fact above depends
        /// on an end event this fake would have to invent a schedule for.</summary>
        public event EventHandler? PlaybackEnded { add { } remove { } }

        public string Path { get; } = path;

        public float Gain { get; } = gain;

        public bool Stopped { get; private set; }

        public AudioPlayerState State { get; private set; } = AudioPlayerState.Stopped;

        public double PositionSec => 0;

        public float Volume { get; set; } = gain;

        public void Play() => State = AudioPlayerState.Playing;

        public void Pause() => State = AudioPlayerState.Paused;

        public void Stop()
        {
            Stopped = true;
            State = AudioPlayerState.Stopped;
        }

        public void Dispose() => State = AudioPlayerState.Stopped;
    }

    /// <summary>A <see cref="Random"/> whose draws are a constant, so a fact pins a placement and a
    /// lifetime without pinning a seed.</summary>
    private sealed class SequenceRandom(double value) : Random
    {
        public override double NextDouble() => value;

        public override int Next(int maxValue) => (int)(value * maxValue) % Math.Max(1, maxValue);
    }

    /// <summary>A pointer surface that hands presses over in a QUEUE, the way the OS does: nothing in
    /// the presenter runs until its own step pumps them.</summary>
    private sealed class RecordingPointerSurface : IPointerSurface
    {
        private readonly Queue<PointerPress> _queued = new();

        private int _next = 1;

        public List<(int Handle, PointerBounds Bounds)> Opened { get; } = [];

        public List<int> Closed { get; } = [];

        public bool CanReachAPointer => true;

        public int OpenTargets => Opened.Count - Closed.Count;

        public CapabilityState? LastPlacement { get; private set; }

        public int MouseActivateQueries { get; private set; }

        public int MouseActivateRefusals { get; private set; }

        public int PressesSeen { get; private set; }

        public PointerStationObservation ObserveStation() => new(true, true, 1, true);

        public CapabilityState Open(PointerTargetRequest request, out int target)
        {
            target = _next++;
            Opened.Add((target, request.Bounds));
            return LastPlacement = new CapabilityState.Available("placed");
        }

        public CapabilityState Move(int target, PointerBounds bounds) =>
            LastPlacement = new CapabilityState.Available("placed");

        public CapabilityState Close(int target)
        {
            Closed.Add(target);
            return new CapabilityState.Available("closed");
        }

        public PointerTargetObservation Observe(int target) => PointerTargetObservation.NotAsked;

        public Action<PointerPress>? OnPress { get; set; }

        public int Pump(int max)
        {
            var dispatched = 0;
            while (dispatched < max && _queued.TryDequeue(out var press))
            {
                OnPress?.Invoke(press);
                dispatched++;
            }

            return dispatched;
        }

        public void DeliverPress(int target, PointerPressKind kind)
        {
            PressesSeen++;
            MouseActivateQueries++;
            MouseActivateRefusals++;
            _queued.Enqueue(new PointerPress(target, kind, 4, 4));
        }

        public void Dispose()
        {
        }
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

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(entry);
        }

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
}
