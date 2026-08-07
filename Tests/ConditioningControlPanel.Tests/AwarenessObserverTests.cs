using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The observer's pipeline: the dwell gate, the transition detector, do-not-disturb, and the two
/// orderings that are privacy invariants rather than implementation details (doc 02 §2.1, §4).
///
/// <para>Every Win32/WinRT/audio edge is behind one of the probe interfaces and every clock is
/// injected, so this drives twenty simulated seconds instead of waiting twenty real ones and never
/// touches a desktop, an audio stack or %LOCALAPPDATA%.</para>
///
/// <para>Two assertions here are the load-bearing ones. <b>Dropped windows never reach the
/// ledger</b> — asserted by counting the ledger's keys after minutes of an incognito or deny-listed
/// window in the foreground, because a check that runs after the write is not a check. And <b>DND
/// suppresses the LINE, not the RECORD</b> — the fullscreen gate has to leave the visit counted, or
/// the "so how many hours was that?" beat when the game closes has nothing to stand on.</para>
/// </summary>
public class AwarenessObserverTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly List<IDisposable> _disposables = new();

    /// <summary>Monday 2026-08-03, 09:00 local — a weekday morning, well away from any boundary.</summary>
    private static readonly DateTime Start = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Local);

    private DateTime _now = Start;

    public AwarenessObserverTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-aware-obs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "awareness_ledger.json");
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { }
        }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ===================== fakes =====================

    private sealed class FakeForegroundProbe : IForegroundProbe
    {
        public ForegroundSample? Sample;
        public ForegroundSample? Read() => Sample;
    }

    private sealed class FakeInputProbe : IInputProbe
    {
        public int Idle;
        public bool Burst;
        public int IdleSeconds => Idle;
        public bool IsTypingBurst => Burst;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeMicrophoneProbe : IMicrophoneProbe
    {
        public bool InUse;
        public bool IsInUse(DateTime at) => InUse;
    }

    private sealed class FakeMediaWatcher : IMediaWatcher
    {
        public MediaSample? Sample;
        public MediaSample? Current => Sample;
        public bool IsAvailable => true;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeAppStateProbe : IAppStateProbe
    {
        public AppStateSample State = AppStateSample.Empty;
        public AppStateSample Read(DateTime at) => State;
    }

    /// <summary>A silent arbiter: the observer's own gates are what these tests are about.</summary>
    private sealed class SilentArbiter : IReactionArbiter
    {
        public Task<ArbiterDecision> SubmitAsync(ContextFrame frame, CancellationToken cancellationToken = default)
            => Task.FromResult(ArbiterDecision.Silent("test"));

        public void RecordExternalLine(ReactionSource source, string? appId = null) { }
        public bool CanSpeak(ReactionSource source, string? appId = null) => true;
    }

    // ===================== rig =====================

    private sealed class Rig
    {
        public required AwarenessObserver Observer { get; init; }
        public required ActivityLedger Ledger { get; init; }
        public required FakeForegroundProbe Foreground { get; init; }
        public required FakeInputProbe Input { get; init; }
        public required FakeMicrophoneProbe Microphone { get; init; }
        public required FakeMediaWatcher Media { get; init; }
        public required FakeAppStateProbe AppState { get; init; }
        public required List<ContextFrame> Frames { get; init; }
    }

    private Rig NewRig(
        IEnumerable<string>? deny = null,
        IEnumerable<string>? titles = null,
        bool adultReactions = true,
        bool adultRecording = true,
        AwarenessIntensity intensity = AwarenessIntensity.Unhinged)
    {
        var ledger = new ActivityLedger(_path, () => _now, () => ActivityLedger.DefaultRetentionDays);
        ledger.Start();
        _disposables.Add(ledger);

        var policy = new AwarenessPolicySettings(
            AwarenessText.SanitizeRuleList(deny),
            AwarenessText.SanitizeRuleList(titles),
            adultReactions,
            adultRecording);

        var foreground = new FakeForegroundProbe();
        var input = new FakeInputProbe();
        var microphone = new FakeMicrophoneProbe();
        var media = new FakeMediaWatcher();
        var appState = new FakeAppStateProbe();
        var frames = new List<ContextFrame>();

        var observer = new AwarenessObserver(
            ledger,
            new WorthinessScorer(() => intensity),
            new SilentArbiter(),
            new StubCompanionMemory(),
            () => _now,
            foreground, input, microphone, media, appState,
            () => policy);

        observer.FrameCut += (_, frame) => frames.Add(frame);
        _disposables.Add(observer);

        return new Rig
        {
            Observer = observer,
            Ledger = ledger,
            Foreground = foreground,
            Input = input,
            Microphone = microphone,
            Media = media,
            AppState = appState,
            Frames = frames
        };
    }

    private static ForegroundSample Window(string title, string process, bool fullscreen = false)
        => new(new IntPtr(1), title, process, fullscreen);

    /// <summary>Advances the simulated clock and runs one pipeline pass.</summary>
    private async Task Tick(Rig rig, int advanceSeconds = 0)
    {
        _now = _now.AddSeconds(advanceSeconds);
        await rig.Observer.TickAsync(_now);
    }

    // ===================== dwell gate =====================

    [Fact]
    public async Task DwellGate_CutsNothingBeforeTwentySeconds_AndCutsOnceAtIt()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("#general | some server", "discord");

        await Tick(rig);                                        // candidate opens here
        await Tick(rig, AwarenessObserver.DwellGateSeconds - 1);
        Assert.Empty(rig.Frames);

        await Tick(rig, 1);                                     // exactly at the gate
        Assert.Single(rig.Frames);
        Assert.Equal("discord", rig.Frames[0].AppId);
        Assert.Equal(TransitionKind.NewApp, rig.Frames[0].Transition);

        // Sitting there does not keep producing frames.
        await Tick(rig, 30);
        await Tick(rig, 30);
        Assert.Single(rig.Frames);
    }

    [Fact]
    public async Task PassThroughWindows_ProduceNothingAtAll()
    {
        var rig = NewRig();

        foreach (var app in new[] { "notepad", "calc", "explorer" })
        {
            rig.Foreground.Sample = Window(app, app);
            await Tick(rig, 5);
        }

        Assert.Empty(rig.Frames);
    }

    [Fact]
    public async Task SustainedAltTabbing_CollapsesIntoExactlyOneRapidCyclingFrame()
    {
        var rig = NewRig();

        for (int i = 0; i < 12; i++)
        {
            rig.Foreground.Sample = Window($"window {i}", $"app{i}");
            await Tick(rig, 3);
        }

        var cycling = rig.Frames.Where(f => f.Transition == TransitionKind.RapidCycling).ToList();
        Assert.Single(cycling);

        // And nothing else got through the gate either — none of those windows was held for 20s.
        Assert.Single(rig.Frames);
    }

    // ===================== privacy ordering =====================

    [Fact]
    public async Task IncognitoWindow_NeverReachesTheLedger_NoMatterHowLongItIsHeld()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("Reddit - Google Chrome (Incognito)", "chrome");

        for (int i = 0; i < 20; i++) await Tick(rig, 60);   // twenty minutes of it

        Assert.Empty(rig.Frames);
        Assert.Equal(0, rig.Ledger.AppCount);
        Assert.Null(rig.Observer.CurrentAppId);
    }

    [Fact]
    public async Task DenyListedWindow_NeverReachesTheLedger()
    {
        var rig = NewRig(deny: new[] { "1password" });
        rig.Foreground.Sample = Window("All Vaults", "1password");

        for (int i = 0; i < 20; i++) await Tick(rig, 60);

        Assert.Empty(rig.Frames);
        Assert.Equal(0, rig.Ledger.AppCount);
    }

    [Fact]
    public async Task DroppingHappensBeforeTheWrite_SoTimeOnADeniedWindowIsNotCreditedToTheAppBeforeIt()
    {
        var rig = NewRig(deny: new[] { "1password" });

        rig.Foreground.Sample = Window("#general | some server", "discord");
        for (int i = 0; i < 5; i++) await Tick(rig, 60);      // five minutes on Discord
        Assert.Equal(1, rig.Ledger.AppCount);

        var before = rig.Ledger.Snapshot("discord", _now).MinutesToday;

        rig.Foreground.Sample = Window("All Vaults", "1password");
        for (int i = 0; i < 10; i++) await Tick(rig, 60);     // ten minutes in the password manager

        // Still one app, and Discord did not silently inherit those ten minutes. The one minute of
        // slack is the poll interval the switch happened inside — that minute genuinely was Discord.
        Assert.Equal(1, rig.Ledger.AppCount);
        Assert.InRange(rig.Ledger.Snapshot("discord", _now).MinutesToday, before, before + 1);
    }

    [Fact]
    public async Task AdultRecordingOff_LeavesNoLedgerEntry()
    {
        var rig = NewRig(adultRecording: false);
        rig.Foreground.Sample = Window("something - pornhub - Google Chrome", "chrome");

        for (int i = 0; i < 10; i++) await Tick(rig, 60);

        Assert.Empty(rig.Frames);
        Assert.Equal(0, rig.Ledger.AppCount);
    }

    [Fact]
    public async Task FailClosed_NoPolicyMeansNoObservationAtAll()
    {
        var ledger = new ActivityLedger(_path, () => _now, () => ActivityLedger.DefaultRetentionDays);
        ledger.Start();
        _disposables.Add(ledger);

        var foreground = new FakeForegroundProbe { Sample = Window("#general", "discord") };
        var frames = new List<ContextFrame>();

        var observer = new AwarenessObserver(
            ledger, new WorthinessScorer(() => AwarenessIntensity.Unhinged), new SilentArbiter(),
            new StubCompanionMemory(), () => _now,
            foreground, new FakeInputProbe(), new FakeMicrophoneProbe(),
            new FakeMediaWatcher(), new FakeAppStateProbe(),
            () => null);                                       // settings unreadable
        observer.FrameCut += (_, f) => frames.Add(f);
        _disposables.Add(observer);

        for (int i = 0; i < 10; i++)
        {
            _now = _now.AddSeconds(30);
            await observer.TickAsync(_now);
        }

        Assert.Empty(frames);
        Assert.Equal(0, ledger.AppCount);
    }

    // ===================== do not disturb =====================

    [Fact]
    public async Task Fullscreen_SuppressesTheLine_ButTheVisitIsStillCounted()
    {
        var rig = NewRig();
        rig.Input.Idle = 2;                                    // hands on the controller
        rig.Foreground.Sample = Window("Elden Ring", "eldenring", fullscreen: true);

        for (int i = 0; i < 10; i++) await Tick(rig, 60);

        Assert.Empty(rig.Frames);                              // she said nothing
        Assert.Equal(1, rig.Ledger.AppCount);                  // but she was counting
        Assert.True(rig.Ledger.Snapshot("eldenring", _now).MinutesToday >= 8);
    }

    [Fact]
    public async Task LeavingFullscreen_IsTheCallbackFrameThatMakesTheSuppressionWorthIt()
    {
        var rig = NewRig();
        rig.Input.Idle = 2;
        rig.Foreground.Sample = Window("Elden Ring", "eldenring", fullscreen: true);
        for (int i = 0; i < 10; i++) await Tick(rig, 60);
        Assert.Empty(rig.Frames);

        rig.Foreground.Sample = Window("Elden Ring", "eldenring", fullscreen: false);
        await Tick(rig, 2);

        var frame = Assert.Single(rig.Frames);
        Assert.Equal(TransitionKind.ExitFullscreen, frame.Transition);
        Assert.False(frame.IsFullscreen);
        Assert.True(frame.DwellSeconds >= 500);                // the stint she is about to joke about
    }

    [Fact]
    public async Task MeetingDnd_NeedsBothTheAppAndTheMicrophone()
    {
        var rig = NewRig();
        rig.Microphone.InUse = true;
        rig.Foreground.Sample = Window("Meeting with the team | Microsoft Teams", "teams");

        for (int i = 0; i < 5; i++) await Tick(rig, 30);
        Assert.Empty(rig.Frames);

        // Same window, mic released: she is allowed to notice it again on the next transition.
        rig.Microphone.InUse = false;
        rig.Foreground.Sample = Window("#general | some server", "discord");
        await Tick(rig, 5);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);

        Assert.Single(rig.Frames);
        Assert.Equal("discord", rig.Frames[0].AppId);
    }

    [Fact]
    public async Task TypingBurst_SuppressesTheLine()
    {
        var rig = NewRig();
        rig.Input.Burst = true;
        rig.Foreground.Sample = Window("draft - Notepad", "notepad");

        await Tick(rig);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);

        Assert.Empty(rig.Frames);
    }

    [Fact]
    public async Task CcpsOwnSurfaces_SuppressTheLine()
    {
        var rig = NewRig();
        rig.AppState.State = AppStateSample.Empty with { BlockingSurfaceActive = true };
        rig.Foreground.Sample = Window("#general | some server", "discord");

        await Tick(rig);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);

        Assert.Empty(rig.Frames);
    }

    [Fact]
    public async Task ASuppressedFrameDoesNotBurnItsTrend_SoTheCallbackStillLands()
    {
        // Three visits under a typing burst: the ReturnVisit(3) trend must survive to be told later.
        var rig = NewRig();
        rig.Input.Burst = true;

        for (int visit = 0; visit < 3; visit++)
        {
            rig.Foreground.Sample = Window("Amazon.com - Google Chrome", "chrome");
            await Tick(rig, 60);
            await Tick(rig, 60);
            rig.Foreground.Sample = Window("#general | some server", "discord");
            await Tick(rig, 60);
            await Tick(rig, 60);
        }

        Assert.Empty(rig.Frames);

        rig.Input.Burst = false;
        rig.Foreground.Sample = Window("Amazon.com - Google Chrome", "chrome");
        await Tick(rig, 60);
        await Tick(rig, 60);

        var frame = Assert.Single(rig.Frames);
        Assert.Contains(frame.Trends, t => t.Kind == TrendKind.ReturnVisit);
    }

    // ===================== idle, wake, staleness =====================

    [Fact]
    public async Task RealIdle_SuspendsAccrual_AndComingBackIsAWakeFrame()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("#general | some server", "discord");
        await Tick(rig, 60);
        await Tick(rig, 60);
        rig.Frames.Clear();

        var minutesBefore = rig.Ledger.Snapshot("discord", _now).MinutesToday;

        // Four hours away from the keyboard, not fullscreen and nothing playing.
        rig.Input.Idle = 4 * 3600;
        for (int i = 0; i < 8; i++) await Tick(rig, 1800);

        Assert.Equal(minutesBefore, rig.Ledger.Snapshot("discord", _now).MinutesToday);

        rig.Input.Idle = 0;
        await Tick(rig, 2);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);

        var frame = Assert.Single(rig.Frames);
        Assert.Equal(TransitionKind.WakeFromIdle, frame.Transition);
        Assert.Contains(frame.Trends, t => t.Kind == TrendKind.GhostTown);
    }

    [Fact]
    public async Task WatchingAVideoIsNotIdle_EvenWithNobodyTouchingTheKeyboard()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("some film - Netflix - Google Chrome", "chrome", fullscreen: true);
        rig.Input.Idle = 30 * 60;                              // half an hour without a keypress

        for (int i = 0; i < 10; i++) await Tick(rig, 60);

        // Fullscreen with the user idle is "watching": it accrues, and the fullscreen DND gate does
        // NOT apply once the input is old, so this is exactly the case the legacy code got wrong.
        Assert.Equal(1, rig.Ledger.AppCount);
        Assert.True(rig.Ledger.Snapshot("netflix", _now).MinutesToday >= 8);
    }

    [Fact]
    public async Task FrameCarriesItsCutTime_AndCurrentAppIdMovesOnForTheStalenessCheck()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("#general | some server", "discord");
        await Tick(rig);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);

        var frame = Assert.Single(rig.Frames);
        Assert.Equal(_now, frame.CutAt);
        Assert.Equal(frame.AppId, rig.Observer.CurrentAppId);

        // The user moves on while the LLM is still thinking. The frame's timestamp does not move, and
        // the live app id no longer matches it — which is the whole staleness test (doc 02 §4.3).
        rig.Foreground.Sample = Window("Amazon.com - Google Chrome", "chrome");
        await Tick(rig, 5);

        Assert.Equal(_now.AddSeconds(-5), frame.CutAt);
        Assert.NotEqual(frame.AppId, rig.Observer.CurrentAppId);
    }

    // ===================== milestones and media =====================

    [Fact]
    public async Task CumulativeDwell_ProducesAMilestoneFrameWithItsLongHaulTrend()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("Elden Ring", "eldenring");

        for (int i = 0; i < 40; i++) await Tick(rig, 60);       // forty minutes, hands on

        var milestone = rig.Frames.FirstOrDefault(f => f.Transition == TransitionKind.Milestone);
        Assert.NotNull(milestone);
        Assert.Contains(milestone!.Trends, t => t.Kind == TrendKind.LongHaul);
        Assert.True(milestone.DwellSeconds >= 30 * 60);
    }

    [Fact]
    public async Task ATrackOnLoop_IsCountedWithoutItsTitleEverBeingWrittenAnywhere()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("Spotify Premium", "spotify");
        rig.Media.Sample = new MediaSample("Bad Habit", "Steve Lacy", "Playing", TimeSpan.FromSeconds(200));

        await Tick(rig);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);
        rig.Frames.Clear();

        // Three restarts: the position rewinds while the title stands still.
        for (int i = 0; i < 3; i++)
        {
            rig.Media.Sample = new MediaSample("Bad Habit", "Steve Lacy", "Playing", TimeSpan.FromSeconds(2));
            await Tick(rig, 5);
            rig.Media.Sample = new MediaSample("Bad Habit", "Steve Lacy", "Playing", TimeSpan.FromSeconds(200));
            await Tick(rig, 200);
        }

        rig.Media.Sample = new MediaSample("Another Song", "Steve Lacy", "Playing", TimeSpan.FromSeconds(1));
        await Tick(rig, 5);

        Assert.Contains(rig.Frames, f => f.NowPlaying is { } playing &&
                                         playing.RepeatCount >= ActivityLedger.MediaLoopMinimum);

        // The privacy half: nothing about the track is on disk. The ledger has no parameter that could
        // carry one, and this asserts the serialised file agrees.
        rig.Ledger.SaveNow();
        var json = File.ReadAllText(_path);
        Assert.DoesNotContain("Bad Habit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Steve Lacy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Another Song", json, StringComparison.OrdinalIgnoreCase);
    }

    // ===================== erasure =====================

    [Fact]
    public async Task ResetTransientState_LeavesNothingInRamThatCouldStillProduceALine()
    {
        var rig = NewRig();
        rig.Foreground.Sample = Window("#general | some server", "discord");
        await Tick(rig);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);
        Assert.Single(rig.Frames);
        Assert.NotNull(rig.Observer.LastFrame);

        rig.Observer.ResetTransientState();
        rig.Ledger.Wipe();

        Assert.Null(rig.Observer.LastFrame);
        Assert.Null(rig.Observer.CurrentAppId);
        Assert.False(File.Exists(rig.Ledger.LedgerPath));
        Assert.False(File.Exists(rig.Ledger.LedgerTempPath));

        // And the gate starts from scratch rather than resuming mid-visit.
        rig.Frames.Clear();
        await Tick(rig, 1);
        Assert.Empty(rig.Frames);
        await Tick(rig, AwarenessObserver.DwellGateSeconds);
        Assert.Single(rig.Frames);
    }

    [Fact]
    public void ConstructorRefusesToBuildWithoutItsCollaborators()
    {
        var ledger = new ActivityLedger(_path, () => _now);
        _disposables.Add(ledger);
        var scorer = new WorthinessScorer(() => AwarenessIntensity.Chatty);
        var arbiter = new SilentArbiter();
        var memory = new StubCompanionMemory();

        Assert.Throws<ArgumentNullException>(() => new AwarenessObserver(null!, scorer, arbiter, memory));
        Assert.Throws<ArgumentNullException>(() => new AwarenessObserver(ledger, null!, arbiter, memory));
        Assert.Throws<ArgumentNullException>(() => new AwarenessObserver(ledger, scorer, null!, memory));
        Assert.Throws<ArgumentNullException>(() => new AwarenessObserver(ledger, scorer, arbiter, null!));
    }
}
