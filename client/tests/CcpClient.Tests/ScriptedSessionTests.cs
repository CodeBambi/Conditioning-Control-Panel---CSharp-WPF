using System.Text.Json;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The SCRIPTED session — upstream's <c>Services/Session/SessionEngine.cs:22</c>, the timed session
/// with named phases that runs on top of the ordinary engine. Slice 1: the persisted model, phases
/// on a clock, START/STOP with the settings snapshot, and the clock-jump guard.
///
/// <para><b>Not one wall-clock wait, and not one real clock.</b> Both clocks and the tick timer
/// come
/// from an injected <see cref="IScriptedClock"/> driven by hand, which is the only way the guard's
/// subject — a wall clock that jumps while a monotonic one does not — can exist at all.</para>
///
/// <para><b>No double stands in for the engine or the stores.</b> A real
/// <see cref="SessionEngine"/> over real <see cref="PersistenceStore{TModel}"/> documents in a temp
/// directory, for the reason <c>SchedulerModuleTests</c> gives: a double diverges from the product
/// exactly where the defect lives. The settings round-trip is asserted on the FILES, byte for
/// byte.</para>
/// </summary>
public class ScriptedSessionTests
{
    // =====================================================================================
    //  THE MODEL, as the four shipped files really are
    // =====================================================================================

    [Fact]
    public void TheFourShippedSessions_Load_AndEveryKeyInThemBindsToAMember()
    {
        var sessions = ScriptedSession.ReadBuiltIns();

        Assert.Equal(
            ["distant_doll", "gamer_girl", "good_girls_dont_cum", "morning_drift"],
            sessions.Select(s => s.Id));

        foreach (var session in sessions)
        {
            // The whole point: a key that landed in extension data is a key the model does not
            // know, and this is what makes "reads them faithfully" checkable rather than claimed.
            Assert.True(
                session.ExtensionData is null or { Count: 0 },
                $"{session.Id}: unmodelled top-level keys {Keys(session.ExtensionData)}");
            Assert.True(
                session.Settings.ExtensionData is null or { Count: 0 },
                $"{session.Id}: unmodelled settings keys {Keys(session.Settings.ExtensionData)}");
            foreach (var phase in session.Phases)
            {
                Assert.True(
                    phase.ExtensionData is null or { Count: 0 },
                    $"{session.Id}/{phase.Name}: unmodelled phase keys {Keys(phase.ExtensionData)}");
            }
        }

        static string Keys(Dictionary<string, JsonElement>? data) =>
            data is null ? "" : string.Join(", ", data.Keys);
    }

    [Fact]
    public void MorningDrift_ReadsItsTimelineAndItsDialsVerbatim()
    {
        var session = Built("morning_drift");

        Assert.Equal("Morning Drift", session.Name);
        Assert.Equal(30, session.DurationMinutes);
        Assert.Equal(ScriptedSessionDifficulty.Easy, session.Difficulty);
        Assert.Equal(400, session.BonusXP);
        Assert.True(session.IsAvailable);
        Assert.False(session.HasCornerGifOption);
        Assert.Equal("Gentle subliminals & bouncing text with gradual pink filter", session.VibeSummary);

        Assert.Equal(
            [(0, "Settling In"), (10, "Pink Awakening"), (15, "Drifting"), (25, "Deep Pink"), (30, "Complete")],
            session.Phases.Select(p => (p.StartMinute, p.Name)));

        var s = session.Settings;
        Assert.True(s.FlashEnabled);
        Assert.Equal(12, s.FlashPerHour);
        Assert.Equal(2, s.FlashImages);
        Assert.Equal(30, s.FlashOpacity);
        Assert.Equal(2, s.SubliminalPerMin);
        Assert.Equal(3, s.SubliminalFrames);
        Assert.Equal(
            ["GOOD GIRL", "BAMBI SLEEP", "BIMBO DOLL", "PRIMPED AND PAMPERED", "GIGGLETIME"],
            s.SubliminalPhrases);
        Assert.True(s.PinkFilterEnabled);
        Assert.Equal(10, s.PinkFilterStartMinute);
        Assert.Equal(0, s.PinkFilterStartOpacity);
        Assert.Equal(15, s.PinkFilterEndOpacity);
        Assert.Equal(5, s.BubblesStartMinute);
        Assert.Equal(40, s.MindWipeVolume);
        Assert.Equal(ScriptedCornerPosition.BottomLeft, s.CornerGifPosition);
    }

    [Fact]
    public void TheFourShippedSessions_CarryTheirOwnDurationsDifficultiesAndPhaseCounts()
    {
        var byId = ScriptedSession.ReadBuiltIns().ToDictionary(s => s.Id, StringComparer.Ordinal);

        Assert.Equal((30, ScriptedSessionDifficulty.Easy, 400, 5),
            Shape(byId["morning_drift"]));
        Assert.Equal((45, ScriptedSessionDifficulty.Medium, 800, 5),
            Shape(byId["gamer_girl"]));
        Assert.Equal((45, ScriptedSessionDifficulty.Easy, 400, 6),
            Shape(byId["distant_doll"]));
        Assert.Equal((60, ScriptedSessionDifficulty.Hard, 1200, 7),
            Shape(byId["good_girls_dont_cum"]));

        // Gamer Girl is the only shipped session that offers the corner GIF
        // (Models/Session.cs:236-237); its window is not in this slice, but the flag is what a rack
        // reads to decide whether to show the opt-in at all.
        Assert.Equal(["gamer_girl"], byId.Values.Where(s => s.HasCornerGifOption).Select(s => s.Id));

        static (int, ScriptedSessionDifficulty, int, int) Shape(ScriptedSession s) =>
            (s.DurationMinutes, s.Difficulty, s.BonusXP, s.Phases.Count);
    }

    [Fact]
    public void AnUnreadableSessionFile_IsARowThatDoesNotAppear_NeverAThrow()
    {
        // Upstream's own answer: catch (JsonException) { return null; }
        // (Services/Session/SessionFileService.cs:105-108).
        Assert.Null(ScriptedSession.Parse("{ this is not json"));

        var dir = Path.Combine(Path.GetTempPath(), "ccp-scripted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.session.json"), "{ oops");
            File.WriteAllText(
                Path.Combine(dir, "good.session.json"),
                """{ "id": "good", "name": "Good", "durationMinutes": 12 }""");

            var sessions = ScriptedSession.ReadFolder(dir);

            Assert.Equal(["good"], sessions.Select(s => s.Id));
            Assert.Equal(12, sessions[0].DurationMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // =====================================================================================
    //  THE CLOCK-JUMP GUARD (upstream Services/Session/SessionEngine.cs:96-115, issue #369)
    // =====================================================================================

    [Fact]
    public void WhenTheTwoClocksAgree_TheWallClockIsTheAnswer()
    {
        var reading = ScriptedSessionRun.Reconcile(
            TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(0.4));

        Assert.False(reading.UsedMonotonic);
        Assert.Equal(TimeSpan.FromMinutes(9), reading.Elapsed);
    }

    [Fact]
    public void AtExactlyThirtySecondsOfDivergence_TheWallClockIsStillTrusted()
    {
        // Upstream's comparison is strictly greater (:104): 30 s is inside the tolerance in both
        // directions. This is the fact a ">= 30" mutation reds.
        var ahead = ScriptedSessionRun.Reconcile(
            TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
        var behind = ScriptedSessionRun.Reconcile(
            TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));

        Assert.False(ahead.UsedMonotonic);
        Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30), ahead.Elapsed);
        Assert.False(behind.UsedMonotonic);
        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(30), behind.Elapsed);
    }

    [Fact]
    public void PastThirtySeconds_TheMonotonicClockWins_InBothDirections()
    {
        // Just over the line, so this is the fact a widened threshold (say "> 60") reds while the
        // big-jump facts below still pass.
        var ahead = ScriptedSessionRun.Reconcile(
            TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(31), TimeSpan.FromMinutes(5));
        var behind = ScriptedSessionRun.Reconcile(
            TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(31), TimeSpan.FromMinutes(5));

        Assert.True(ahead.UsedMonotonic);
        Assert.Equal(TimeSpan.FromMinutes(5), ahead.Elapsed);
        Assert.True(behind.UsedMonotonic);
        Assert.Equal(TimeSpan.FromMinutes(5), behind.Elapsed);
    }

    [Fact]
    public void ASmallBackwardStep_NeverReportsNegativeElapsedTime()
    {
        // Inside the tolerance the wall clock still wins, and upstream floors it at zero (:114)
        // rather than reporting a session that has not started yet.
        var reading = ScriptedSessionRun.Reconcile(TimeSpan.FromSeconds(-10), TimeSpan.FromSeconds(2));

        Assert.False(reading.UsedMonotonic);
        Assert.Equal(TimeSpan.Zero, reading.Elapsed);
    }

    [Fact]
    public async Task AWallClockThatJUMPSBACKWARD_DoesNotBalloonTheTimeRemaining()
    {
        // Upstream's own worked example of the defect: "149 minutes left" on a 30-minute session
        // (Services/Session/SessionEngine.cs:101-102).
        await using var rig = await Rig.StartAsync();
        rig.Run.Start(Built("morning_drift"));
        rig.Clock.Advance(TimeSpan.FromMinutes(12));

        rig.Clock.JumpWallClock(TimeSpan.FromMinutes(-120));

        var reading = rig.Run.ReadElapsed();
        Assert.True(reading.UsedMonotonic);
        Assert.Equal(TimeSpan.FromMinutes(12), rig.Run.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(18), rig.Run.Remaining);
        Assert.Equal(40, rig.Run.ProgressPercent, 6);
    }

    [Fact]
    public async Task AWallClockThatJUMPSFORWARD_DoesNotEndTheSessionEarly()
    {
        // The other half of upstream's comment (:100): a positive divergence is the speed-hack, and
        // without the guard the very next tick would see 132 minutes of a 30-minute session and
        // complete it.
        await using var rig = await Rig.StartAsync();
        rig.Run.Start(Built("morning_drift"));
        rig.Clock.Advance(TimeSpan.FromMinutes(12));

        rig.Clock.JumpWallClock(TimeSpan.FromHours(2));
        rig.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(rig.Run.Running);
        Assert.Null(rig.Outcome);
        Assert.True(rig.Run.ReadElapsed().UsedMonotonic);
        Assert.Equal(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(1), rig.Run.Elapsed);
        // ...and phase 2 ("Drifting", 15 min) has NOT been announced.
        Assert.Equal(1, rig.Run.CurrentPhaseIndex);
    }

    // =====================================================================================
    //  PHASES ON A CLOCK
    // =====================================================================================

    [Fact]
    public async Task PhasesAdvanceOnTheClock_InOrder_AndTheReadoutFollowsThem()
    {
        await using var rig = await Rig.StartAsync();
        var session = Built("morning_drift");

        Assert.True(rig.Run.Start(session));

        // Upstream announces phase 0 at START, before any tick
        // (Services/Session/SessionEngine.cs:264-267).
        Assert.Equal([(0, "Settling In")], rig.Phases);
        Assert.Equal("Settling In", rig.Run.CurrentPhase!.Name);
        Assert.Equal(TimeSpan.FromMinutes(30), rig.Run.Remaining);

        rig.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(0, rig.Run.CurrentPhaseIndex);
        Assert.Equal(TimeSpan.FromMinutes(21), rig.Run.Remaining);
        Assert.Equal(30, rig.Run.ProgressPercent, 6);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, rig.Run.CurrentPhaseIndex);

        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        rig.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(
            [(0, "Settling In"), (10, "Pink Awakening"), (15, "Drifting"), (25, "Deep Pink")],
            rig.Phases);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Progress[^1].Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(5), rig.Progress[^1].Remaining);
    }

    [Fact]
    public async Task APhaseMovesBACK_WhenASmallDriftIsCorrected()
    {
        // Upstream compares the new index with "!=" and not ">"
        // (Services/Session/SessionEngine.cs:556), so a phase
        // is what the CLOCK says and not a ratchet. The path that reaches it is narrow and real: a
        // wall clock 29 s ahead is INSIDE the guard's tolerance, so it is trusted, and a correction
        // that pulls those 29 s back moves the session across a phase boundary in reverse.
        await using var rig = await Rig.StartAsync();
        rig.Run.Start(Built("morning_drift"));

        rig.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(20));
        rig.Clock.JumpWallClock(TimeSpan.FromSeconds(29));
        rig.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, rig.Run.CurrentPhaseIndex);
        Assert.Equal([(0, "Settling In"), (10, "Pink Awakening")], rig.Phases);

        rig.Clock.JumpWallClock(TimeSpan.FromSeconds(-29));
        rig.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(0, rig.Run.CurrentPhaseIndex);
        Assert.Equal(
            [(0, "Settling In"), (10, "Pink Awakening"), (0, "Settling In")],
            rig.Phases);
    }

    [Fact]
    public async Task AtItsDuration_TheSessionEndsItself_AndSaysItCompleted()
    {
        await using var rig = await Rig.StartAsync();
        rig.Run.Start(Built("morning_drift"));

        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.False(rig.Run.Running);
        Assert.NotNull(rig.Outcome);
        Assert.True(rig.Outcome!.Completed);
        Assert.Equal("morning_drift", rig.Outcome.Session.Id);
        Assert.Equal(TimeSpan.FromMinutes(30), rig.Outcome.Elapsed);
        // Upstream returns from the tick the moment it stops the session (:513-517): no progress
        // event is published past the end.
        Assert.DoesNotContain(rig.Progress, p => p.Elapsed >= TimeSpan.FromMinutes(30));
        // And nothing is left on the clock.
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    [Fact]
    public async Task AStoppedSessionReportsNothing_AndAStopWithNoSessionIsRefused()
    {
        await using var rig = await Rig.StartAsync();
        Assert.False(rig.Run.Stop());

        rig.Run.Start(Built("morning_drift"));
        rig.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.True(rig.Run.Stop());

        Assert.False(rig.Run.Stop());
        Assert.Single(rig.Outcomes);
        Assert.False(rig.Outcome!.Completed);
        Assert.Equal(TimeSpan.FromMinutes(4), rig.Outcome.Elapsed);

        // Upstream's ElapsedTime returns Zero when nothing runs (:95), and RemainingTime with no
        // session is Zero too (:121).
        Assert.Equal(TimeSpan.Zero, rig.Run.Elapsed);
        Assert.Equal(TimeSpan.Zero, rig.Run.Remaining);
        Assert.Equal(0, rig.Run.ProgressPercent);
        Assert.Null(rig.Run.Current);
        Assert.Null(rig.Run.CurrentPhase);
    }

    // =====================================================================================
    //  THE SETTINGS SNAPSHOT — the promise the confirm dialog makes
    // =====================================================================================

    [Fact]
    public async Task WhileASessionRuns_TheDialsAreTheSESSIONS()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();

        rig.Run.Start(Built("morning_drift"));

        Assert.True(rig.Flash.Current.FlashEnabled);
        Assert.Equal(12, rig.Flash.Current.FlashesPerHour);
        Assert.Equal(2, rig.Flash.Current.ImagesPerFlash);
        Assert.Equal(30, rig.Visuals.Current.FlashOpacityPercent);

        Assert.True(rig.Subliminal.Current.Enabled);
        Assert.Equal(2, rig.Subliminal.Current.PerMinute);
        Assert.Equal(3, rig.Subliminal.Current.DurationFrames);
        Assert.Equal(45, rig.Subliminal.Current.OpacityPercent);
        Assert.Equal(
            ["GOOD GIRL", "BAMBI SLEEP", "BIMBO DOLL", "PRIMPED AND PAMPERED", "GIGGLETIME"],
            rig.Subliminal.Current.ActivePhrases());
        // The user's own phrase is still in the pool, switched off — upstream disables rather than
        // deletes (Services/Session/SessionEngine.cs:1191-1194), which is what makes the restore
        // give a POOL back.
        Assert.False(rig.Subliminal.Current.Phrases["MINE ONLY"]);

        Assert.True(rig.BouncingText.Current.Enabled);
        Assert.Equal(2, rig.BouncingText.Current.Speed);
        Assert.Equal(50, rig.BouncingText.Current.SizePercent);
        Assert.Equal(
            ["Good Girl", "Such a good girl", "Drifting peacefully", "Waking up pink"],
            rig.BouncingText.Current.Phrases);

        // Delayed features are applied OFF, exactly as upstream applies them (:1288-1296): Morning
        // Drift's pink filter starts at minute 10 and its bubbles at minute 5.
        Assert.False(rig.PinkFilter.Current.Enabled);
        Assert.False(rig.Spiral.Current.Enabled);
        Assert.False(rig.BubblePop.Current.Enabled);
        Assert.Equal(1, rig.BubblePop.Current.PerMinute);

        Assert.True(rig.MindWipe.Current.Enabled);
        Assert.Equal(40, rig.MindWipe.Current.VolumePercent);
        Assert.False(rig.Video.Current.Enabled);
        Assert.False(rig.LockCard.Current.Enabled);
        Assert.False(rig.BubbleCount.Current.Enabled);
    }

    [Fact]
    public async Task ASessionWhoseFeaturesStartAtZero_TurnsThemOnAtItsOwnLevels()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();

        rig.Run.Start(Built("good_girls_dont_cum"));

        // Pink filter starts at minute 0 here, so it is ON at the session's START opacity (:1290).
        Assert.True(rig.PinkFilter.Current.Enabled);
        Assert.Equal(10, rig.PinkFilter.Current.OpacityPercent);
        // The spiral starts at minute 5, so it is not on yet.
        Assert.False(rig.Spiral.Current.Enabled);

        // The three nullable rates are applied because this session sets them (:1342-1344).
        Assert.True(rig.Video.Current.Enabled);
        Assert.Equal(2, rig.Video.Current.PerHour);
        Assert.True(rig.LockCard.Current.Enabled);
        Assert.Equal(2, rig.LockCard.Current.PerHour);
        Assert.True(rig.BubbleCount.Current.Enabled);
        Assert.Equal(2, rig.BubbleCount.Current.PerHour);

        // 180 flashes an hour is upstream's own ceiling and the port's clamp
        // (SessionPresetDocument.MaxFlashesPerHour), so it survives intact.
        Assert.Equal(180, rig.Flash.Current.FlashesPerHour);
    }

    [Fact]
    public async Task WhenTheSessionEnds_TheUsersOwnSettingsComeBack_ByteIdenticalOnDisk()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();
        await rig.SaveEverything();
        var before = rig.ReadEveryDocument();

        rig.Run.Start(Built("morning_drift"));
        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.NotEqual(7, rig.Flash.Current.FlashesPerHour); // the session really took over

        Assert.True(rig.Run.Stop());
        await rig.SaveEverything();

        Assert.Equal(before, rig.ReadEveryDocument());

        // And in memory too, which is what a module reads.
        Assert.Equal(7, rig.Flash.Current.FlashesPerHour);
        Assert.Equal(3, rig.Flash.Current.ImagesPerFlash);
        Assert.Equal(66, rig.Visuals.Current.FlashOpacityPercent);
        Assert.Equal(9, rig.BouncingText.Current.Speed);
        Assert.Equal(["MY OWN WORDS"], rig.BouncingText.Current.Phrases);
        Assert.Equal(["MINE ONLY"], rig.Subliminal.Current.ActivePhrases());
        Assert.True(rig.PinkFilter.Current.Enabled);
        Assert.Equal(25, rig.PinkFilter.Current.OpacityPercent);
        Assert.Equal(77, rig.MindWipe.Current.VolumePercent);
    }

    [Fact]
    public async Task ACompletedSessionRestoresTheDialsToo_NotOnlyAStoppedOne()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();
        await rig.SaveEverything();
        var before = rig.ReadEveryDocument();

        rig.Run.Start(Built("morning_drift"));
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.True(rig.Outcome!.Completed);
        await rig.SaveEverything();
        Assert.Equal(before, rig.ReadEveryDocument());
    }

    [Fact]
    public async Task ASecondStartIsRefused_AndDoesNotOverwriteTheSnapshot()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();
        await rig.SaveEverything();
        var before = rig.ReadEveryDocument();

        Assert.True(rig.Run.Start(Built("morning_drift")));
        // A second start with a DIFFERENT session: refused, and — the part that matters — it does
        // not snapshot the first session's dials as if they were the user's.
        Assert.False(rig.Run.Start(Built("good_girls_dont_cum")));
        Assert.Equal("morning_drift", rig.Run.Current!.Id);
        Assert.Equal(12, rig.Flash.Current.FlashesPerHour);

        rig.Run.Stop();
        await rig.SaveEverything();
        Assert.Equal(before, rig.ReadEveryDocument());
    }

    // =====================================================================================
    //  THE ORDINARY ENGINE UNDERNEATH
    // =====================================================================================

    [Fact]
    public async Task StartingAScriptedSession_StartsTheOrdinaryEngine_OnTheSESSIONSDials()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();
        Assert.False(rig.Engine.Running);

        rig.Run.Start(Built("morning_drift"));

        // WPF starts the engine from the rack when it is not already running
        // (MainWindow/MainWindow.Presets.cs:1509-1512).
        Assert.True(rig.Engine.Running);
        // The module armed AFTER the dials were replaced, which is the whole reason the port
        // applies before it arms: this port's modules read their dials when they arm.
        Assert.Equal([12], rig.Effect.ArmedWith);

        rig.Run.Stop();

        // Upstream leaves the engine running after a session ends
        // (Services/Session/SessionEngine.cs:287-425 stops
        // no engine); the port re-arms it so the restored dials are what runs.
        Assert.True(rig.Engine.Running);
        Assert.Equal([12, 7], rig.Effect.ArmedWith);
    }

    [Fact]
    public async Task AnEngineTheUserAlreadyStarted_IsReArmedRatherThanLeftOnTheOldDials()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();
        rig.Engine.Start();
        Assert.Equal([7], rig.Effect.ArmedWith);

        rig.Run.Start(Built("morning_drift"));

        Assert.True(rig.Engine.Running);
        Assert.Equal([7, 12], rig.Effect.ArmedWith);
        Assert.Equal(1, rig.Effect.Disarms);
    }

    [Fact]
    public async Task AnEngineTheUserStoppedMidSession_StaysStopped()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();
        rig.Run.Start(Built("morning_drift"));
        rig.Engine.Stop();

        rig.Run.Stop();

        Assert.False(rig.Engine.Running);
        Assert.Equal([12], rig.Effect.ArmedWith);
        // The dials still came back: the restore does not depend on the engine.
        Assert.Equal(7, rig.Flash.Current.FlashesPerHour);
    }

    // =====================================================================================
    //  THE RAMPS — upstream UpdateRampingValues (Services/Session/SessionEngine.cs:564-661).
    //  Pure arithmetic first: no clock, no rig, and every number worked from a shipped file.
    // =====================================================================================

    [Fact]
    public void TheFlashTrio_RampsOpacityAndFrequency_AndPINSTheScale()
    {
        // Good Girls Don't Cum is the session that ramps both: 35 %→90 % opacity and 180→600 an
        // hour over 60 minutes. Halfway through, Lerp gives 62.5 and upstream TRUNCATES to int
        // (:586) — 62, not 63 — and 390 an hour lands on the shared ceiling of 180 (the clamp at
        // Models/AppSettings.cs:911, and this port's own SessionPresetDocument.MaxFlashesPerHour).
        var ramping = Ramp(Built("good_girls_dont_cum"), 30);

        Assert.Equal(62, ramping.FlashOpacityPercent);
        Assert.Equal(180, ramping.FlashesPerHour);
        Assert.Null(ramping.FlashScalePercent);

        // Distant Doll ramps neither, and sets a scale: 150 % for the WHOLE session, pinned rather
        // than lerped (:596-599). Early and late give the same number, which is the pin.
        var pinned = Built("distant_doll");
        Assert.Equal(150, Ramp(pinned, 5).FlashScalePercent);
        Assert.Equal(150, Ramp(pinned, 40).FlashScalePercent);
        Assert.Null(Ramp(pinned, 40).FlashOpacityPercent);
        Assert.Null(Ramp(pinned, 40).FlashesPerHour);

        // And a session that ramps nothing and leaves the scale at 100 parks nothing at all: the
        // user's own flash values stay in charge.
        var quiet = Ramp(Built("morning_drift"), 15);
        Assert.Null(quiet.FlashOpacityPercent);
        Assert.Null(quiet.FlashesPerHour);
        Assert.Null(quiet.FlashScalePercent);
    }

    [Fact]
    public void ThePinkRampBeginsAtItsOwnMinute_AndMeasuresProgressOverWhatIsLEFT()
    {
        // Morning Drift: 30 minutes, pink filter 0 %→15 % from minute 10. The denominator is
        // (total - start) = 20, not 30 (:613-614) — which is what makes the filter reach its full
        // 15 % at the session's END rather than at minute 43.
        var session = Built("morning_drift");

        Assert.Null(Ramp(session, 9.5).PinkOpacityPercent);
        Assert.Equal(0d, Ramp(session, 10).PinkOpacityPercent!.Value, 6);
        Assert.Equal(7.5, Ramp(session, 20).PinkOpacityPercent!.Value, 6);
        Assert.Equal(11.25, Ramp(session, 25).PinkOpacityPercent!.Value, 6);
        Assert.Equal(15d, Ramp(session, 30).PinkOpacityPercent!.Value, 6);
    }

    [Fact]
    public void TheSpiralRampsOnlyAfterItsMinute_AndOnlyWhenItReallyRamps()
    {
        // Good Girls Don't Cum: spiral 5 %→30 % from minute 5 of 60.
        var session = Built("good_girls_dont_cum");

        Assert.Null(Ramp(session, 4.9).SpiralOpacityPercent);
        Assert.Equal(5d, Ramp(session, 5).SpiralOpacityPercent!.Value, 6);
        Assert.Equal(17.5, Ramp(session, 32.5).SpiralOpacityPercent!.Value, 6);

        // A spiral whose start and end opacities are equal is left alone even past its minute —
        // upstream's #897 fix (:620-625): driving the overlay with a constant parked a ramp hold
        // that froze the user's own slider for the whole session. The pink filter deliberately does
        // NOT carry this guard, and the line above proves the two stayed different.
        var constant = Built("good_girls_dont_cum");
        constant.Settings.SpiralOpacityEnd = constant.Settings.SpiralOpacity;
        Assert.Null(Ramp(constant, 32.5).SpiralOpacityPercent);
        Assert.NotNull(Ramp(constant, 32.5).PinkOpacityPercent);
    }

    [Fact]
    public void OneCurveShapesEveryRampInTheSession_AndAllOfThemKeepTheirEndpoints()
    {
        // Half way through Good Girls Don't Cum: pink 10 %→50 % from minute 0, so pink progress is
        // exactly 0.5 and each curve's own value at 0.5 is visible in the answer
        // (Helpers/RampCurves.cs:47-73).
        var session = Built("good_girls_dont_cum");

        Assert.Equal(30d, Ramp(session, 30, RampCurve.Linear).PinkOpacityPercent!.Value, 6);
        Assert.Equal(15d, Ramp(session, 30, RampCurve.EaseIn).PinkOpacityPercent!.Value, 6);
        Assert.Equal(45d, Ramp(session, 30, RampCurve.EaseOut).PinkOpacityPercent!.Value, 6);
        Assert.Equal(30d, Ramp(session, 30, RampCurve.SCurve).PinkOpacityPercent!.Value, 6);

        // Exponential is the back-loaded one: past ease-in, still well short of half way.
        var exponential = Ramp(session, 30, RampCurve.Exponential).PinkOpacityPercent!.Value;
        Assert.InRange(exponential, 15.0, 30.0);

        // The SAME curve shapes the flash ramp on the same tick (:570 computes one progress for all
        // of them): ease-in's 0.125 takes 35 %→90 % to 41.875, truncated to 41.
        Assert.Equal(41, Ramp(session, 30, RampCurve.EaseIn).FlashOpacityPercent);
        Assert.Equal(62, Ramp(session, 30, RampCurve.Linear).FlashOpacityPercent);

        // Every curve preserves the endpoints, which is what lets a session reach exactly its
        // configured maximum whichever one the user picked (Helpers/RampCurves.cs:39-42).
        foreach (var curve in Enum.GetValues<RampCurve>())
        {
            Assert.Equal(10d, Ramp(session, 0, curve).PinkOpacityPercent!.Value, 6);
            Assert.Equal(50d, Ramp(session, 60, curve).PinkOpacityPercent!.Value, 6);
        }
    }

    [Fact]
    public void TheBubbleFrequencyClimbsOneStepPerWholeFiveMinutes_AndNotForABurstSession()
    {
        // Upstream's step, not a lerp, and the curve does not touch it (:640-642): one extra bubble
        // a minute per whole five minutes since the bubbles' own start minute.
        var session = Built("good_girls_dont_cum"); // 1 a minute, from minute 5, not intermittent

        Assert.Null(ScriptedSessionRamp.BubblesPerMinute(session.Settings, 4.9));
        Assert.Equal(1, ScriptedSessionRamp.BubblesPerMinute(session.Settings, 5));
        Assert.Equal(1, ScriptedSessionRamp.BubblesPerMinute(session.Settings, 9.99));
        Assert.Equal(2, ScriptedSessionRamp.BubblesPerMinute(session.Settings, 10));
        Assert.Equal(4, ScriptedSessionRamp.BubblesPerMinute(session.Settings, 20));
        Assert.Equal(9, ScriptedSessionRamp.BubblesPerMinute(session.Settings, 47.5));

        // An intermittent session's bubbles arrive as scheduled bursts instead, so the frequency
        // ramp is skipped even when its start minute has passed (:636).
        var burst = Built("good_girls_dont_cum");
        burst.Settings.BubblesIntermittent = true;
        Assert.Null(ScriptedSessionRamp.BubblesPerMinute(burst.Settings, 20));

        // ...and a session whose bubbles start with it never ramps them at all (:636's `> 0`).
        var immediate = Built("good_girls_dont_cum");
        immediate.Settings.BubblesStartMinute = 0;
        Assert.Null(ScriptedSessionRamp.BubblesPerMinute(immediate.Settings, 20));
    }

    [Fact]
    public void ADelayedStartIsJitteredThreeMinutesEitherWay_AndNeverGoesNegative()
    {
        // Upstream RandomizeStartTimes (:777-805): offset = NextDouble() * 6 - 3.
        Assert.Equal(13d, ScriptedSessionRamp.JitterStartMinute(true, 10, new FixedRandom(1.0)), 6);
        Assert.Equal(7d, ScriptedSessionRamp.JitterStartMinute(true, 10, new FixedRandom(0.0)), 6);
        Assert.Equal(10d, ScriptedSessionRamp.JitterStartMinute(true, 10, new FixedRandom(0.5)), 6);

        // Math.Max(0, ...) (:785): a start at minute 2 pulled back three minutes is minute 0, never
        // minus one — a negative start would put the feature's whole ramp behind the session.
        Assert.Equal(0d, ScriptedSessionRamp.JitterStartMinute(true, 2, new FixedRandom(0.0)), 6);

        // Only an ENABLED feature with a start minute past zero is jittered (:782, :793). The
        // second of those is load-bearing: ScriptedSessionDials.Apply decides "this feature starts
        // with the session" from the file's own 0, and a jittered 0 would disagree with it.
        Assert.Equal(10d, ScriptedSessionRamp.JitterStartMinute(false, 10, new FixedRandom(1.0)), 6);
        Assert.Equal(0d, ScriptedSessionRamp.JitterStartMinute(true, 0, new FixedRandom(1.0)), 6);
    }

    // =====================================================================================
    //  THE RAMP AS A RUNNING SESSION SEES IT, and the dial it must never touch
    // =====================================================================================

    [Fact]
    public async Task TheRampClimbs_AndTheUsersOwnPinkOpacityNeverMovesWithIt()
    {
        // THE fact of this slice. Upstream drives the pink overlay DIRECTLY instead of writing the
        // ramped value into App.Settings.Current.PinkFilterOpacity, because that setting auto-saved
        // and an app kill mid-session froze the ramp's maximum into settings.json permanently —
        // "the screen keeps getting more pink and stays that way" (:604-610, issues #471/#476).
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials(); // the user's own pink filter: on, 25 %
        await rig.SaveEverything();
        var before = rig.ReadEveryDocument();

        rig.Run.Start(Built("morning_drift")); // pink 0 %→15 % from minute 10 of 30

        rig.Clock.Advance(TimeSpan.FromMinutes(12));
        Assert.Equal(1.5, rig.Run.Ramp.PinkOpacityPercent!.Value, 6);
        // The dial carries the ramp's FIRST sample, written once when the filter came on...
        Assert.Equal(0, rig.PinkFilter.Current.OpacityPercent);

        rig.Clock.Advance(TimeSpan.FromMinutes(8));
        Assert.Equal(7.5, rig.Run.Ramp.PinkOpacityPercent!.Value, 6);
        // ...and the climb past it reaches the parked value and NOTHING else.
        Assert.Equal(0, rig.PinkFilter.Current.OpacityPercent);

        // No clean stop, no restore: this is the kill. Nothing this session has done has reached
        // the user's file, so the 25 % they chose is still what is on disk.
        Assert.Equal(before, rig.ReadEveryDocument());
        Assert.Equal(25, rig.PersistedInt(PinkFilterPresetDocument.FileName, "opacityPercent"));
    }

    [Fact]
    public async Task WhenTheSessionEnds_TheParkedRampIsHandedBack()
    {
        // Upstream clears the parked flash trio unconditionally and BEFORE the restore (:340-343):
        // an overlay left parked would keep overriding the user's values for the rest of the run.
        await using var rig = await Rig.StartAsync();
        rig.Run.Start(Built("good_girls_dont_cum"));

        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        var ramp = rig.Run.Ramp;
        Assert.True(ramp.Any);
        Assert.Equal(62, ramp.FlashOpacityPercent);
        Assert.Equal(180, ramp.FlashesPerHour);
        Assert.Equal(30d, ramp.PinkOpacityPercent!.Value, 6);
        Assert.Equal(5 + (25 * 25.0 / 55), ramp.SpiralOpacityPercent!.Value, 6);

        Assert.True(rig.Run.Stop());

        Assert.Equal(ScriptedSessionRamp.None, rig.Run.Ramp);
        Assert.False(rig.Run.Ramp.Any);
    }

    [Fact]
    public async Task TheBubbleFrequencyIsTheOneRampedValueThatDoesMoveTheUsersDial()
    {
        // The asymmetry is upstream's own: the bubble ramp writes App.Settings.Current
        // .BubblesFrequency and calls RefreshFrequency() (:644-647) where every other ramped value
        // is parked beside the user's. The snapshot is what makes that safe.
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials(); // the user's own: 19 a minute
        await rig.SaveEverything();
        var before = rig.ReadEveryDocument();

        rig.Run.Start(Built("good_girls_dont_cum")); // 1 a minute, from minute 5 of 60
        Assert.Equal(1, rig.BubblePop.Current.PerMinute);

        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, rig.BubblePop.Current.PerMinute);

        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(4, rig.BubblePop.Current.PerMinute);

        Assert.True(rig.Run.Stop());

        Assert.Equal(19, rig.BubblePop.Current.PerMinute);
        await rig.SaveEverything();
        Assert.Equal(before, rig.ReadEveryDocument());
    }

    [Fact]
    public async Task TheUsersOwnCurveShapesTheRamps_UnlessTheSessionBringsItsOwn()
    {
        // Upstream resolves the curve on EVERY tick as `settings.RampCurve ?? App.Settings.Current
        // .RampCurve` (:569) — one global dial shared by both of its ramp systems. In this port
        // that dial is the Intensity Ramp module's, which is the second caller
        // Effects/RampCurves.cs:52-56 predicted.
        await using var rig = await Rig.StartAsync();
        rig.RampCurvePreset.Mutate(d => d.Curve = RampCurve.EaseIn);

        rig.Run.Start(Built("good_girls_dont_cum"));
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        // Ease-in at half way is 0.125, so the pink filter is at 15 % where linear would be 30 %.
        Assert.Equal(15d, rig.Run.Ramp.PinkOpacityPercent!.Value, 6);
        Assert.True(rig.Run.Stop());

        // A session that names its own curve overrides the user's for its whole run.
        var ownCurve = Built("good_girls_dont_cum");
        ownCurve.Settings.RampCurve = RampCurve.Linear;
        rig.Run.Start(ownCurve);
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(30d, rig.Run.Ramp.PinkOpacityPercent!.Value, 6);

        // And the file member it comes from really binds, rather than landing in extension data.
        var parsed = ScriptedSession.Parse(
            """{ "id": "x", "durationMinutes": 10, "settings": { "rampCurve": "easeIn" } }""");
        Assert.Equal(RampCurve.EaseIn, parsed!.Settings.RampCurve);
        Assert.True(parsed.Settings.ExtensionData is null or { Count: 0 });
    }

    // =====================================================================================
    //  THE DELAYED FEATURE STARTS — upstream CheckDelayedFeatures (:663-772). Slice 1 applied
    //  them OFF at t=0 and nothing turned them on.
    // =====================================================================================

    [Fact]
    public async Task ThePinkFilterTurnsItselfOnAtItsOwnMinute_AtTheSESSIONSOpacity_AndStaysOn()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials(); // the user's pink filter is ON at 25 %
        rig.Run.Start(Built("morning_drift")); // ...and this session starts it at minute 10, at 0 %

        // Applied OFF at t=0 (:1288-1296) — the module was armed and its own dial refused.
        Assert.False(rig.PinkModule.Enabled);
        Assert.Equal(EffectReasonCodes.EffectDialOff, rig.ArmCode(PinkFilterEffect.EffectId));

        rig.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.False(rig.PinkModule.Enabled);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));

        // On, live, and at the SESSION's opacity rather than the 25 % the user had: the module
        // reads its dial when it arms, and the arm happened here rather than at the next START.
        Assert.True(rig.PinkModule.Enabled);
        Assert.Equal(0, rig.PinkModule.Tint.OpacityPercent);
        Assert.Equal(EffectReasonCodes.PinkFilterTransparent, rig.ArmCode(PinkFilterEffect.EffectId));

        // The ticks after it leave the filter alone. Upstream's guard is "the session wants it and
        // the user's live dial has not got it yet" (:688) — not "toggle it".
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(rig.PinkModule.Enabled);
    }

    [Fact]
    public async Task TheSpiralAndTheBubblesTurnOnAtTheirOwnMinutes_OnTheSessionsDials()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials(); // spiral at 88 %, bubbles at 19 a minute, both on
        rig.Run.Start(Built("good_girls_dont_cum")); // spiral and bubbles both start at minute 5

        Assert.True(rig.PinkModule.Enabled); // this session's pink filter starts WITH it (:1290)
        Assert.False(rig.SpiralModule.Enabled);
        Assert.False(rig.BubbleModule.Enabled);

        rig.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(rig.SpiralModule.Enabled);
        Assert.False(rig.BubbleModule.Enabled);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(rig.SpiralModule.Enabled);
        Assert.Equal(5, rig.Spiral.Current.OpacityPercent); // the session's, not the user's 88
        Assert.True(rig.BubbleModule.Enabled);
        Assert.Equal(1, rig.BubblePop.Current.PerMinute); // the session's, not the user's 19

        // Both really went live: the spiral's refusal is now its EMPTY LIBRARY, which is this
        // port's answer to the "are there any spiral files" probe upstream runs here (:702-715) —
        // recorded verbatim instead of silently switching the session's dial off.
        Assert.Equal(EffectReasonCodes.SpiralNoImage, rig.ArmCode(SpiralOverlayEffect.EffectId));
        Assert.Equal(EffectReasonCodes.EffectNoSurface, rig.ArmCode(BubblePopEffect.EffectId));
    }

    [Fact]
    public async Task AnIntermittentSessionsBubblesAreLeftToTheirBursts()
    {
        // Distant Doll's bubbles are intermittent and start with the session, so Apply leaves them
        // off and this is what must NOT turn them on (:731's conjunction).
        await using (var rig = await Rig.StartAsync())
        {
            rig.WriteTheUsersDials();
            rig.Run.Start(Built("distant_doll"));
            rig.Clock.Advance(TimeSpan.FromMinutes(30));

            Assert.False(rig.BubbleModule.Enabled);
        }

        // The half of that conjunction the shipped files cannot show on their own: intermittent
        // bubbles WITH a start minute. The burst schedule that would serve them is not in this
        // slice, and a steady spawner is not a substitute for it.
        await using var burstRig = await Rig.StartAsync();
        var burst = Built("good_girls_dont_cum");
        burst.Settings.BubblesIntermittent = true;
        burstRig.WriteTheUsersDials();
        burstRig.Run.Start(burst);
        burstRig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.False(burstRig.BubbleModule.Enabled);
        Assert.Equal(1, burstRig.BubblePop.Current.PerMinute); // and its frequency never climbed
    }

    [Fact]
    public async Task TheJitterMovesTheRealTurnOn_AndTheRampMeasuresFromTheJitteredMinute()
    {
        // The same session as the fact above, with the ±3 minute jitter pinned to its top
        // (:784): Morning Drift's pink filter starts at 13 rather than at 10.
        await using var rig = await Rig.StartAsync(jitterSample: 1.0);
        rig.Run.Start(Built("morning_drift"));

        Assert.Equal(13d, rig.Run.PinkStartMinute, 6);
        // Its spiral is switched off in the file, so its start minute is never jittered (:793).
        Assert.Equal(0d, rig.Run.SpiralStartMinute, 6);

        rig.Clock.Advance(TimeSpan.FromMinutes(12));
        Assert.False(rig.PinkModule.Enabled);
        Assert.Null(rig.Run.Ramp.PinkOpacityPercent);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(rig.PinkModule.Enabled);
        Assert.Equal(0d, rig.Run.Ramp.PinkOpacityPercent!.Value, 6);

        // ...and the ramp's denominator is what is left after the JITTERED minute: 17, not 20.
        rig.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(15.0 * 2 / 17, rig.Run.Ramp.PinkOpacityPercent!.Value, 6);
    }

    private static ScriptedSession Built(string id) =>
        ScriptedSession.ReadBuiltIns().Single(s => s.Id == id);

    /// <summary>One tick's ramp for a shipped session, with no jitter on either start minute — the
    /// pure-arithmetic facts' shorthand.</summary>
    private static ScriptedSessionRamp Ramp(
        ScriptedSession session, double elapsedMinutes, RampCurve curve = RampCurve.Linear) =>
        ScriptedSessionRamp.Compute(
            session.Settings,
            elapsedMinutes,
            session.DurationMinutes,
            curve,
            session.Settings.PinkFilterStartMinute,
            session.Settings.SpiralStartMinute);

    /// <summary>
    /// The rig: a temp data directory, the eleven real documents a scripted session borrows, a real
    /// engine with one counting module in it, and both clocks by hand.
    /// </summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly List<IBackgroundParticipant> _stores;
        private readonly List<Func<Task>> _saves;

        private Rig(string directory, OperationRegistry registry, CollectingLog log, double jitterSample)
        {
            Directory = directory;
            Log = log;

            Flash = Store<SessionPresetDocument>(
                registry, log, directory, SessionPresetDocument.FileName,
                SessionPresetDocument.CurrentSchemaVersion);
            Visuals = Store<VisualsPresetDocument>(
                registry, log, directory, VisualsPresetDocument.FileName,
                VisualsPresetDocument.CurrentSchemaVersion);
            Subliminal = Store<SubliminalPresetDocument>(
                registry, log, directory, SubliminalPresetDocument.FileName,
                SubliminalPresetDocument.CurrentSchemaVersion);
            BouncingText = Store<BouncingTextPresetDocument>(
                registry, log, directory, BouncingTextPresetDocument.FileName,
                BouncingTextPresetDocument.CurrentSchemaVersion);
            PinkFilter = Store<PinkFilterPresetDocument>(
                registry, log, directory, PinkFilterPresetDocument.FileName,
                PinkFilterPresetDocument.CurrentSchemaVersion);
            Spiral = Store<SpiralPresetDocument>(
                registry, log, directory, SpiralPresetDocument.FileName,
                SpiralPresetDocument.CurrentSchemaVersion);
            BubblePop = Store<BubblePopPresetDocument>(
                registry, log, directory, BubblePopPresetDocument.FileName,
                BubblePopPresetDocument.CurrentSchemaVersion);
            MindWipe = Store<MindWipePresetDocument>(
                registry, log, directory, MindWipePresetDocument.FileName,
                MindWipePresetDocument.CurrentSchemaVersion);
            Video = Store<MandatoryVideoPresetDocument>(
                registry, log, directory, MandatoryVideoPresetDocument.FileName,
                MandatoryVideoPresetDocument.CurrentSchemaVersion);
            LockCard = Store<LockCardPresetDocument>(
                registry, log, directory, LockCardPresetDocument.FileName,
                LockCardPresetDocument.CurrentSchemaVersion);
            BubbleCount = Store<BubbleCountPresetDocument>(
                registry, log, directory, BubbleCountPresetDocument.FileName,
                BubbleCountPresetDocument.CurrentSchemaVersion);
            RampCurvePreset = Store<IntensityRampPresetDocument>(
                registry, log, directory, IntensityRampPresetDocument.FileName,
                IntensityRampPresetDocument.CurrentSchemaVersion);

            _stores =
            [
                Flash, Visuals, Subliminal, BouncingText, PinkFilter, Spiral, BubblePop, MindWipe,
                Video, LockCard, BubbleCount, RampCurvePreset,
            ];
            _saves =
            [
                () => Flash.SaveImmediate(),
                () => Visuals.SaveImmediate(),
                () => Subliminal.SaveImmediate(),
                () => BouncingText.SaveImmediate(),
                () => PinkFilter.SaveImmediate(),
                () => Spiral.SaveImmediate(),
                () => BubblePop.SaveImmediate(),
                () => MindWipe.SaveImmediate(),
                () => Video.SaveImmediate(),
                () => LockCard.SaveImmediate(),
                () => BubbleCount.SaveImmediate(),
                () => RampCurvePreset.SaveImmediate(),
            ];

            // The three modules a delayed start turns on are the REAL ones, over the same documents
            // the session borrows: what a delayed start has to prove is that the module went live
            // mid-session on the SESSION's dial, and a stand-in cannot be wrong about that in the
            // way the product can. Their surfaces are null — nothing here draws — so each reports
            // its own refusal, which is exactly what makes "it engaged" distinguishable from "its
            // dial was off" in ArmOutcomes.
            var signal = new EffectSignal(new UiDispatchBoundary());
            Effect = new CountingEffect(Flash);
            PinkModule = new PinkFilterEffect(registry.OwnerFor("RigPinkFilter"), signal, PinkFilter);
            SpiralModule = new SpiralOverlayEffect(
                registry.OwnerFor("RigSpiral"), signal, Spiral, static () => null);
            BubbleModule = new BubblePopEffect(registry.OwnerFor("RigBubblePop"), signal, BubblePop);
            Engine = new SessionEngine([Effect, PinkModule, SpiralModule, BubbleModule], Flash);
            Dials = new ScriptedSessionDials(
                Flash, Visuals, Subliminal, BouncingText, PinkFilter, Spiral, BubblePop, MindWipe,
                Video, LockCard, BubbleCount);
            Clock = new ManualScriptedClock(new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero));
            Run = new ScriptedSessionRun(
                Engine, Dials, Clock, RampCurvePreset, random: new FixedRandom(jitterSample));
            Run.PhaseChanged += (phase, index) => Phases.Add((phase.StartMinute, phase.Name));
            Run.ProgressUpdated += progress => Progress.Add(progress);
            Run.Ended += outcome => Outcomes.Add(outcome);
        }

        public string Directory { get; }

        public CollectingLog Log { get; }

        public PersistenceStore<SessionPresetDocument> Flash { get; }

        public PersistenceStore<VisualsPresetDocument> Visuals { get; }

        public PersistenceStore<SubliminalPresetDocument> Subliminal { get; }

        public PersistenceStore<BouncingTextPresetDocument> BouncingText { get; }

        public PersistenceStore<PinkFilterPresetDocument> PinkFilter { get; }

        public PersistenceStore<SpiralPresetDocument> Spiral { get; }

        public PersistenceStore<BubblePopPresetDocument> BubblePop { get; }

        public PersistenceStore<MindWipePresetDocument> MindWipe { get; }

        public PersistenceStore<MandatoryVideoPresetDocument> Video { get; }

        public PersistenceStore<LockCardPresetDocument> LockCard { get; }

        public PersistenceStore<BubbleCountPresetDocument> BubbleCount { get; }

        /// <summary>The user's own easing curve, which a scripted session READS and never
        /// borrows.</summary>
        public PersistenceStore<IntensityRampPresetDocument> RampCurvePreset { get; }

        public CountingEffect Effect { get; }

        public PinkFilterEffect PinkModule { get; }

        public SpiralOverlayEffect SpiralModule { get; }

        public BubblePopEffect BubbleModule { get; }

        public SessionEngine Engine { get; }

        public ScriptedSessionDials Dials { get; }

        public ManualScriptedClock Clock { get; }

        public ScriptedSessionRun Run { get; }

        public List<(int StartMinute, string Name)> Phases { get; } = [];

        public List<ScriptedSessionProgress> Progress { get; } = [];

        public List<ScriptedSessionOutcome> Outcomes { get; } = [];

        public ScriptedSessionOutcome? Outcome => Outcomes.Count > 0 ? Outcomes[^1] : null;

        /// <param name="jitterSample">What the run's injected <see cref="Random"/> hands back for
        /// every sample. The default 0.5 is the middle of upstream's ±3 minute window
        /// (<c>Services/Session/SessionEngine.cs:784</c>), so a start minute lands exactly where
        /// the file says it does and every OTHER fact can name whole minutes; the jitter's own fact
        /// passes 1.0 and 0.0.</param>
        public static async Task<Rig> StartAsync(double jitterSample = 0.5)
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "ccp-scripted-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var rig = new Rig(directory, new OperationRegistry(), new CollectingLog(), jitterSample);
            foreach (var store in rig._stores)
            {
                await store.StartAsync(TestContext.Current.CancellationToken);
            }

            return rig;
        }

        /// <summary>
        /// The user's own dials, written through the documents the modules read. Deliberately
        /// different from every value the shipped sessions carry, so "it came back" cannot be
        /// satisfied by a default.
        /// </summary>
        public void WriteTheUsersDials()
        {
            Flash.Mutate(d =>
            {
                d.FlashEnabled = true;
                d.FlashesPerHour = 7;
                d.ImagesPerFlash = 3;
            });
            Visuals.Mutate(d => d.FlashOpacityPercent = 66);
            Subliminal.Mutate(d =>
            {
                d.Enabled = true;
                d.PerMinute = 11;
                d.DurationFrames = 6;
                d.OpacityPercent = 91;
                d.Phrases = new Dictionary<string, bool>(StringComparer.Ordinal) { ["MINE ONLY"] = true };
            });
            BouncingText.Mutate(d =>
            {
                d.Enabled = true;
                d.Speed = 9;
                d.SizePercent = 210;
                d.OpacityPercent = 44;
                d.Phrases = ["MY OWN WORDS"];
            });
            PinkFilter.Mutate(d =>
            {
                d.Enabled = true;
                d.OpacityPercent = 25;
            });
            Spiral.Mutate(d =>
            {
                d.Enabled = true;
                d.OpacityPercent = 88;
            });
            BubblePop.Mutate(d =>
            {
                d.Enabled = true;
                d.PerMinute = 19;
            });
            MindWipe.Mutate(d =>
            {
                d.Enabled = true;
                d.VolumePercent = 77;
                d.PerHour = 33;
            });
            Video.Mutate(d =>
            {
                d.Enabled = true;
                d.PerHour = 5;
            });
            LockCard.Mutate(d =>
            {
                d.Enabled = true;
                d.PerHour = 9;
            });
            BubbleCount.Mutate(d =>
            {
                d.Enabled = true;
                d.PerHour = 6;
            });
        }

        /// <summary>Flush every document and wait for quiescence — the outcome the persistence
        /// contract lets a fact assert, rather than the racy dirty flag.</summary>
        public async Task SaveEverything()
        {
            foreach (var save in _saves)
            {
                await save();
            }
        }

        /// <summary>Every document's bytes, by file name, in a fixed order.</summary>
        public IReadOnlyList<(string File, string Json)> ReadEveryDocument() =>
        [
            .. System.IO.Directory.GetFiles(Directory, "*.json")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(path => (File: Path.GetFileName(path), Json: File.ReadAllText(path))),
        ];

        /// <summary>One number as it really sits ON DISK, read from the file rather than from the
        /// document in memory — which is the difference the pink ramp's fact turns on.</summary>
        public int PersistedInt(string fileName, string property)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, fileName)));
            return document.RootElement.GetProperty(property).GetInt32();
        }

        /// <summary>What a module said the last time this engine armed it. Null before the first
        /// START, and the reason CODE only — the detail is prose, the code is the contract.</summary>
        public string? ArmCode(string effectId) => Engine.ArmOutcomes.TryGetValue(effectId, out var state)
            ? state switch
            {
                CapabilityState.Unavailable unavailable => unavailable.Reason.Code,
                CapabilityState.Degraded degraded => degraded.Reason.Code,
                _ => null,
            }
            : null;

        public async ValueTask DisposeAsync()
        {
            // The real modules park an owned operation while they are armed; a rig that walks away
            // from a running engine leaves those generations live.
            Engine.Stop();

            foreach (var store in _stores)
            {
                await store.StopAsync();
            }

            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory that will not delete is not this fact's subject.
            }
        }

        private static PersistenceStore<TModel> Store<TModel>(
            OperationRegistry registry, ILogSink log, string directory, string fileName, int version)
            where TModel : class, new() =>
            new(registry.OwnerFor(fileName), log, Path.Combine(directory, fileName), version);
    }

    /// <summary>
    /// One real module in the rack, whose only job is to record the DIAL IT WAS ARMED WITH. That is
    /// the observation the re-arm facts need: "the module ran on the session's settings" is a claim
    /// about what it read when the session took it, not about what the document says afterwards.
    /// </summary>
    private sealed class CountingEffect(PersistenceStore<SessionPresetDocument> preset) : ISessionEffect
    {
        private readonly List<int> _armedWith = [];

        public string Id => "flash";

        public string Title => "Flash Images";

        public bool Enabled => preset.Current.FlashEnabled;

        public EffectDotState Dot => Enabled ? EffectDotState.Armed : EffectDotState.Off;

        public Task<OperationOutcome>? Completion => null;

        public event Action? Changed;

        public IReadOnlyList<int> ArmedWith
        {
            get { lock (_armedWith) { return [.. _armedWith]; } }
        }

        public int Disarms { get; private set; }

        public void SetEnabled(bool enabled)
        {
            preset.Mutate(d => d.FlashEnabled = enabled);
            Changed?.Invoke();
        }

        public CapabilityState Arm()
        {
            lock (_armedWith)
            {
                _armedWith.Add(preset.Current.FlashesPerHour);
            }

            return new CapabilityState.Available(
                $"armed at {preset.Current.FlashesPerHour} flashes an hour");
        }

        public void Disarm() => Disarms++;
    }

    /// <summary>
    /// Both clocks, by hand.
    ///
    /// <para><see cref="Advance"/> is ordinary time passing: the wall clock and the monotonic one
    /// move together and timers come due. <see cref="JumpWallClock"/> is the subject of this file:
    /// ONLY the wall clock moves, and no timer moves with it, because
    /// <see cref="System.Threading.Timer"/> counts an elapsed DURATION and not a wall-clock
    /// instant — a tick armed a second ago is still a second from firing however the wall clock has
    /// been dragged underneath it.</para>
    /// </summary>
    private sealed class ManualScriptedClock(DateTimeOffset start) : IScriptedClock
    {
        private readonly List<Entry> _timers = [];
        private DateTimeOffset _wall = start;
        private TimeSpan _monotonic = TimeSpan.Zero;

        public DateTimeOffset Now
        {
            get { lock (_timers) { return _wall; } }
        }

        public TimeSpan Monotonic
        {
            get { lock (_timers) { return _monotonic; } }
        }

        /// <summary>Live (uncancelled) timers. After a stop this must be zero.</summary>
        public int PendingCount
        {
            get { lock (_timers) { return _timers.Count(t => !t.Cancelled); } }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            Entry entry;
            lock (_timers)
            {
                entry = new Entry
                {
                    Due = _monotonic + (due < TimeSpan.Zero ? TimeSpan.Zero : due),
                    Fire = fire,
                };
                _timers.Add(entry);
            }

            return new CancelHandle(this, entry);
        }

        /// <summary>Time passes, honestly: both clocks move and whatever became due
        /// fires.</summary>
        public void Advance(TimeSpan by)
        {
            lock (_timers)
            {
                _wall += by;
                _monotonic += by;
            }

            Pump();
        }

        /// <summary>The wall clock jumps and nothing else does — an NTP correction, a
        /// resume from sleep, or a user who set the clock.</summary>
        public void JumpWallClock(TimeSpan by)
        {
            lock (_timers)
            {
                _wall += by;
            }
        }

        private void Pump()
        {
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers
                        .Where(t => !t.Cancelled && t.Due <= _monotonic)
                        .OrderBy(t => t.Due)
                        .FirstOrDefault();
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
            public TimeSpan Due;

            public required Action Fire;

            public bool Cancelled;
        }

        private sealed class CancelHandle(ManualScriptedClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    entry.Cancelled = true;
                }
            }
        }
    }

    /// <summary>
    /// A <see cref="Random"/> whose every sample is the same number, so a jittered start minute is
    /// an exact minute a fact can name. This is what "inject the randomness the way the clock is
    /// injected" buys: upstream's <c>_random.NextDouble()</c>
    /// (<c>Services/Session/SessionEngine.cs:784</c>) is unobservable, and a start time nothing can
    /// pin is a start time no fact can check.
    /// </summary>
    private sealed class FixedRandom(double sample) : Random
    {
        public override double NextDouble() => sample;

        protected override double Sample() => sample;
    }

    private sealed class CollectingLog : ILogSink
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get { lock (_lines) { return [.. _lines]; } }
        }

        public void Log(string message)
        {
            lock (_lines)
            {
                _lines.Add(message);
            }
        }
    }
}
