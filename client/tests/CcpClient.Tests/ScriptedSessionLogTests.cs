using System.Text.Json;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The POST-SESSION MEDIA LOG and the two windows it feeds — upstream's
/// <c>Services/Session/SessionLogService.cs</c>, <c>Windows/SessionCompleteWindow.xaml(.cs)</c> and
/// <c>Windows/SessionLogHistoryWindow.xaml(.cs)</c>.
///
/// <para><b>The two rules this file exists to pin</b> are upstream's two constants and the code
/// around them: the persist rule at <c>SessionLogService.cs:93-94</c>, which is an OR over BOTH a
/// media count and a duration, and the retention cap at <c>:20</c>, which evicts the OLDEST after
/// the newest has already been written (<c>:97-98</c>, <c>:254-274</c>).</para>
///
/// <para><b>No clock is read anywhere in here.</b> Every instant is one the fact chose, and the
/// end-to-end facts drive the real modules on two injected clocks — the session clock the flash and
/// video modules pace on, and the scripted clock the run measures itself with.</para>
/// </summary>
public class ScriptedSessionLogTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    // =====================================================================================
    //  RULE ONE — what is written, and what is deliberately not
    // =====================================================================================

    [Fact]
    public void TheSkipIsBOTHConditions_AndItIsAnOR_SoOnlyAShortSILENTRunIsDropped()
    {
        // Upstream's whole rule, on one line (SessionLogService.cs:94):
        //     bool persist = log.Media.Count > 0 || duration >= PersistenceMinDuration;
        // Every corner of it, because the sentence people repeat about it ("runs under 30 seconds
        // are not persisted") is missing half the condition and the wrong half.
        Assert.Equal(TimeSpan.FromSeconds(30), ScriptedSessionLogStore.PersistenceMinDuration);

        // No media, under the floor: THE ONLY case that is dropped.
        Assert.False(ScriptedSessionLogStore.ShouldPersist(Log(TimeSpan.FromSeconds(29.999), media: 0)));
        Assert.False(ScriptedSessionLogStore.ShouldPersist(Log(TimeSpan.Zero, media: 0)));

        // ONE image is enough, however short the run. This is the half an `&&` would eat.
        Assert.True(ScriptedSessionLogStore.ShouldPersist(Log(TimeSpan.FromSeconds(1), media: 1)));

        // EXACTLY thirty seconds is kept: upstream compares `>=`, not `>`.
        Assert.True(ScriptedSessionLogStore.ShouldPersist(Log(TimeSpan.FromSeconds(30), media: 0)));
        Assert.True(ScriptedSessionLogStore.ShouldPersist(Log(TimeSpan.FromMinutes(45), media: 0)));
    }

    [Fact]
    public void AShortSilentRunWritesNoFile_ButItsRecapStillAppears()
    {
        using var temp = new TempFolder();
        var store = new ScriptedSessionLogStore(temp.Path, new NullSink());
        var seen = new List<ScriptedSessionLog>();
        store.LogReady += seen.Add;

        var log = store.Complete(Outcome(TimeSpan.FromSeconds(20), completed: false));

        // Upstream puts the persist inside an `if` and the raise OUTSIDE it (:95-101), so the user
        // who started a session by accident and stopped it still gets told what happened; only the
        // folder is spared.
        Assert.Empty(Directory.Exists(store.Folder) ? Directory.GetFiles(store.Folder) : []);
        Assert.Single(seen);
        Assert.Same(log, seen[0]);
        Assert.Empty(store.LoadRecent());
    }

    [Fact]
    public void TheRecapIsHandedALogThatIsALREADYONDISK()
    {
        // Upstream's own ordering note, at the subscription: "SessionEngine raises LogReady AFTER
        // it fires SessionCompleted, so ... the dialog is shown from this hook"
        // (MainWindow/MainWindow.xaml.cs:373-375), and the raise is the last line of EndSession
        // (:97-101 — persist, prune, THEN raise). A recap raised first would render a run whose
        // file the history window could not find a second later.
        using var temp = new TempFolder();
        var store = new ScriptedSessionLogStore(temp.Path, new NullSink());
        var onDiskWhenRaised = -1;
        store.LogReady += log => onDiskWhenRaised = Directory.GetFiles(store.Folder).Length;

        store.Complete(Outcome(TimeSpan.FromMinutes(30), completed: true));

        Assert.Equal(1, onDiskWhenRaised);
    }

    // =====================================================================================
    //  RULE TWO — twenty, and the twenty-first is written before the oldest goes
    // =====================================================================================

    [Fact]
    public void TheTwentyFirstRunIS_WRITTEN_AndTheOLDESTIsTheOneEvicted()
    {
        // The failure this forbids is a cap that refuses the 21st write instead of making room for
        // it: upstream persists FIRST and prunes second (SessionLogService.cs:97-98), and the prune
        // deletes from index MaxRetainedLogs of a NEWEST-FIRST list (:262-268).
        using var temp = new TempFolder();
        var store = new ScriptedSessionLogStore(temp.Path, new NullSink());

        for (var i = 0; i < 21; i++)
        {
            store.Complete(Outcome(
                TimeSpan.FromMinutes(30), completed: true, startedAt: Noon.AddMinutes(i)));
        }

        Assert.Equal(20, ScriptedSessionLogStore.MaxRetainedLogs);
        Assert.Equal(20, Directory.GetFiles(store.Folder).Length);

        // The 21st is there, so it was not merely refused...
        Assert.True(File.Exists(Path.Combine(store.Folder, NameFor(Noon.AddMinutes(20)))));

        // ...and the FIRST is the one that went. Runs 2 through 21 survive, exactly.
        Assert.False(File.Exists(Path.Combine(store.Folder, NameFor(Noon))));
        Assert.Equal(
            Enumerable.Range(1, 20).Select(i => NameFor(Noon.AddMinutes(i))).OrderBy(n => n, StringComparer.Ordinal),
            Directory.GetFiles(store.Folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));

        // And what the user is offered is those twenty, newest first.
        var recent = store.LoadRecent();
        Assert.Equal(20, recent.Count);
        Assert.Equal(Noon.AddMinutes(20), recent[0].StartedAt);
        Assert.Equal(Noon.AddMinutes(1), recent[19].StartedAt);
    }

    [Fact]
    public void TheCapHoldsOnTheREADToo_SoAFolderSomebodyFilledByHandStillShowsTwenty()
    {
        // Upstream caps the read as well as the prune (`files.Take(MaxRetainedLogs)`, :125). It is
        // reachable without a bug: the logs are ordinary files in the user's own folder.
        using var temp = new TempFolder();
        var store = new ScriptedSessionLogStore(temp.Path, new NullSink());
        Directory.CreateDirectory(store.Folder);
        for (var i = 0; i < 25; i++)
        {
            var log = Log(TimeSpan.FromMinutes(5), media: 0) with { StartedAt = Noon.AddMinutes(i) };
            File.WriteAllText(
                Path.Combine(store.Folder, ScriptedSessionLogStore.FileNameFor(log)),
                JsonSerializer.Serialize(log, ScriptedSession.JsonOptions));
        }

        var recent = store.LoadRecent();
        Assert.Equal(20, recent.Count);
        Assert.Equal(Noon.AddMinutes(24), recent[0].StartedAt);
        Assert.Equal(Noon.AddMinutes(5), recent[19].StartedAt);
    }

    [Fact]
    public void ACorruptLogIsSkipped_NotThrown_AndTheRestStillOpen()
    {
        // Upstream catches per FILE, not per folder (:127-136): one unreadable log must not take
        // the whole Recent Sessions window with it.
        using var temp = new TempFolder();
        var store = new ScriptedSessionLogStore(temp.Path, new NullSink());
        store.Complete(Outcome(TimeSpan.FromMinutes(30), completed: true, startedAt: Noon));
        File.WriteAllText(Path.Combine(store.Folder, "20260824_130000_broken.json"), "{ not json");

        var recent = store.LoadRecent();

        Assert.Single(recent);
        Assert.Equal(Noon, recent[0].StartedAt);
    }

    [Fact]
    public void TheFileNameIsUpstreamsFormat_AndNoSessionIdCanWriteOutsideTheFolder()
    {
        // Upstream's name (:242) — the leading timestamp is what makes a plain string sort
        // chronological, which is the mechanism BOTH the newest-first read and the oldest-first
        // eviction are built on.
        Assert.Equal(
            "20260824_120000_morning_drift.json",
            ScriptedSessionLogStore.FileNameFor(
                Log(TimeSpan.FromMinutes(5), media: 0) with { StartedAt = Noon, SessionId = "morning_drift" }));

        // Upstream's SanitizeId (:276-286), and it is a trust boundary rather than tidiness: a
        // session id is data from a file, and an id carrying a separator would otherwise choose
        // where the log is written.
        var escaped = ScriptedSessionLogStore.FileNameFor(
            Log(TimeSpan.Zero, media: 0) with { StartedAt = Noon, SessionId = "../../etc/passwd" });
        Assert.DoesNotContain("/", escaped, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", escaped, StringComparison.Ordinal);
        Assert.Equal("20260824_120000_.._.._etc_passwd.json", escaped);

        // An id-less log still gets a name (:278).
        Assert.Equal(
            "20260824_120000_session.json",
            ScriptedSessionLogStore.FileNameFor(
                Log(TimeSpan.Zero, media: 0) with { StartedAt = Noon, SessionId = "" }));
    }

    // =====================================================================================
    //  WHAT IS AND IS NOT IN THE LOG
    // =====================================================================================

    [Fact]
    public void AMediaEntryOnDiskCarriesAKINDANDAMINUTE_AndNothingElseAtAll()
    {
        // THE PORT'S MEDIA-LOGGING RULE, PROVED STRUCTURALLY RATHER THAN PROMISED. Upstream's entry
        // persists `file_path` and `display_name` (Models/SessionLog.cs:24-28) into a file that
        // outlives the run. This build's flash and video events have never carried a path — they
        // go straight to the surface (Effects/FlashImagesEffect.cs:151-155,
        // Effects/MandatoryVideoEffect.cs:9-10) — so the entry cannot carry one, and this asserts
        // the SERIALIZED PROPERTY SET the way AiDiagnosticRecord's content-freedom is asserted:
        // a new field on the record fails this fact by name instead of shifting a count.
        using var temp = new TempFolder();
        var store = new ScriptedSessionLogStore(temp.Path, new NullSink());
        store.RecordImages(2, TimeSpan.FromMinutes(3));
        store.RecordVideo(TimeSpan.FromMinutes(7));
        store.Complete(Outcome(TimeSpan.FromMinutes(30), completed: true, startedAt: Noon));

        var json = File.ReadAllText(Path.Combine(store.Folder, NameFor(Noon)));
        using var document = JsonDocument.Parse(json);
        var media = document.RootElement.GetProperty("media");

        Assert.Equal(3, media.GetArrayLength());
        foreach (var entry in media.EnumerateArray())
        {
            Assert.Equal(
                ["kind", "sessionTime"],
                entry.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        }

        Assert.Equal("image", media[0].GetProperty("kind").GetString());
        Assert.Equal("image", media[1].GetProperty("kind").GetString());
        Assert.Equal("video", media[2].GetProperty("kind").GetString());
    }

    [Fact]
    public void AFlashThatDrewNothingRecordsNothing()
    {
        // Upstream returns before touching the log when the draw came back empty
        // (SessionLogService.cs:178). The port's flash reports the same fact as a COUNT
        // (Effects/FlashEvent.PoolWasEmpty), so an empty pool logs no phantom images.
        using var temp = new TempFolder();
        var store = new ScriptedSessionLogStore(temp.Path, new NullSink());
        store.RecordImages(0, TimeSpan.FromMinutes(1));
        var log = store.Complete(Outcome(TimeSpan.FromSeconds(10), completed: false));

        Assert.Empty(log.Media);
        Assert.False(Directory.Exists(store.Folder));
    }

    // =====================================================================================
    //  THE WIRE — the real modules, the real run, two injected clocks
    // =====================================================================================

    [Fact]
    public async Task EveryImageAFlashREALLYDREWIsInTheLog_AtTheRunsOwnOffset()
    {
        await using var rig = await LiveRig.StartAsync(FlashOnly());
        rig.Start();

        // Two flashes, three minutes apart on the SESSION's clock, each drawing the session's own
        // two images. The scripted clock is what says how far in each one was.
        rig.Scripted.Advance(TimeSpan.FromMinutes(2));
        rig.Session.FireNextTimer();
        rig.Scripted.Advance(TimeSpan.FromMinutes(3));
        rig.Session.FireNextTimer();

        Assert.Equal(2, rig.Participant.Flash.FlashCount);

        // THE PATHS WENT TO THE SURFACE. That is the other half of the content-freedom claim and it
        // is end-to-end rather than structural: the same firing that put two file names on the
        // screen put a COUNT in the log (Effects/FlashImagesEffect.cs:151-155).
        Assert.Equal(2, rig.Flash.Shown.Count);
        Assert.Equal(["image-0.png", "image-1.png"], rig.Flash.Shown[0]);

        var log = rig.StopAndTakeLog(completed: false);

        // ONE ENTRY PER IMAGE, which is upstream's own loop over the paths a flash drew
        // (SessionLogService.cs:185-196) — not one per flash.
        Assert.Equal(
            [
                (ScriptedMediaKind.Image, TimeSpan.FromMinutes(2)),
                (ScriptedMediaKind.Image, TimeSpan.FromMinutes(2)),
                (ScriptedMediaKind.Image, TimeSpan.FromMinutes(5)),
                (ScriptedMediaKind.Image, TimeSpan.FromMinutes(5)),
            ],
            log.Media.Select(m => (m.Kind, m.SessionTime)));

        Assert.Equal(4, log.ImageCount);
        Assert.Equal(0, log.VideoCount);
        Assert.Equal("0 videos · 4 images", SessionRecapNotices.MediaCount(log));
    }

    [Fact]
    public async Task AFlashOUTSIDEAScriptedSessionIsNotInAnyLog()
    {
        // Upstream's guard, twice over: it subscribes only while a session is active (:150-171) and
        // every handler still checks `_activeLog == null` (:182). The port keeps the second, read
        // off the run itself — so an ordinary engine session, which this build has and which is not
        // a scripted one, logs nothing.
        await using var rig = await LiveRig.StartAsync(FlashOnly());
        rig.Participant.Preset.Mutate(d =>
        {
            d.FlashEnabled = true;
            d.ImagesPerFlash = 2;
        });
        rig.Participant.Engine.Start();
        rig.Session.FireNextTimer();
        Assert.Equal(1, rig.Participant.Flash.FlashCount);

        // Now the scripted session, with one flash inside it.
        rig.Start();
        rig.Scripted.Advance(TimeSpan.FromMinutes(1));
        rig.Session.FireNextTimer();
        var log = rig.StopAndTakeLog(completed: false);

        // The flash before it is not in the log; the flash inside it is.
        Assert.Equal(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)],
            log.Media.Select(m => m.SessionTime));

        // And one after the stop is not either.
        rig.Session.FireNextTimer();
        var second = rig.Store.Complete(Outcome(TimeSpan.FromMinutes(30), completed: true, startedAt: Noon));
        Assert.Empty(second.Media);
    }

    [Fact]
    public async Task EveryClipTheSessionREALLYSTARTEDIsInTheLog()
    {
        await using var rig = await LiveRig.StartAsync(VideoOnly());
        rig.Start();

        rig.Scripted.Advance(TimeSpan.FromMinutes(4));
        rig.Session.FireNextTimer();

        // A clip is on screen, so upstream's next firing is dropped rather than stacked
        // (Effects/MandatoryVideoEffect.cs:247-252) — and a firing that never played must not be
        // logged as one.
        rig.Scripted.Advance(TimeSpan.FromMinutes(1));
        rig.Session.FireNextTimer();

        rig.Video.RaiseEnded();
        rig.Scripted.Advance(TimeSpan.FromMinutes(1));
        rig.Session.FireNextTimer();

        var log = rig.StopAndTakeLog(completed: false);

        Assert.Equal(
            [
                (ScriptedMediaKind.Video, TimeSpan.FromMinutes(4)),
                (ScriptedMediaKind.Video, TimeSpan.FromMinutes(6)),
            ],
            log.Media.Select(m => (m.Kind, m.SessionTime)));
        Assert.Equal("2 videos · 0 images", SessionRecapNotices.MediaCount(log));
    }

    [Fact]
    public async Task ARunThatFINISHEDAndARunTheUserSTOPPEDAreBothLogged_AndTellApart()
    {
        // Upstream logs both endings and separates them on one field:
        // "Aborted sessions still get a log (xpForLog == 0) so the post-session dialog shows what
        // played even when the user cut things short" (Services/Session/SessionEngine.cs:420-423).
        await using var rig = await LiveRig.StartAsync(FlashOnly(durationMinutes: 1));
        rig.Start();
        rig.Scripted.Advance(TimeSpan.FromSeconds(40));
        var aborted = rig.StopAndTakeLog(completed: false);

        rig.Start();

        // Past its own one-minute duration: the run ends ITSELF on the tick
        // (Services/Session/SessionEngine.cs:512-517), which is the completion path.
        rig.Scripted.Advance(TimeSpan.FromSeconds(61));

        Assert.False(rig.Participant.Scripted.Running);
        var completed = rig.LastLog!;

        Assert.False(aborted.Completed);
        Assert.True(completed.Completed);
        Assert.Equal(SessionRecapNotices.EndedEarly, SessionRecapNotices.Headline(aborted));
        Assert.Equal(SessionRecapNotices.GoodGirl, SessionRecapNotices.Headline(completed));
        Assert.Equal(2, rig.Store.LoadRecent().Count);
    }

    // =====================================================================================
    //  THE RECAP AND THE HISTORY, as sentences
    // =====================================================================================

    [Fact]
    public void TheRecapSaysHowItEnded_AndOneSessionGetsItsOwnHeadline()
    {
        var drift = Log(TimeSpan.FromMinutes(30), media: 0) with
        {
            SessionId = "morning_drift", SessionName = "Morning Drift", SessionIcon = "\U0001F305",
            Completed = true,
        };

        // SessionCompleteWindow.xaml.cs:78-88.
        Assert.Equal("Good Girl!", SessionRecapNotices.Headline(drift));
        Assert.Equal("\U0001F305 Morning Drift Completed", SessionRecapNotices.Subtitle(drift));

        var gamer = drift with { SessionId = "gamer_girl", SessionName = "Gamer Girl", SessionIcon = "\U0001F3AE" };
        Assert.Equal("GG, Good Girl!", SessionRecapNotices.Headline(gamer));

        // An abort loses the word "Completed" as well as the headline (:87-88).
        var stopped = drift with { Completed = false };
        Assert.Equal("Session Ended Early", SessionRecapNotices.Headline(stopped));
        Assert.Equal("\U0001F305 Morning Drift", SessionRecapNotices.Subtitle(stopped));

        // The history's two status words and their colours (SessionLogHistoryWindow.xaml.cs:91-100).
        Assert.Equal("Completed", SessionRecapNotices.Status(drift));
        Assert.Equal("Aborted", SessionRecapNotices.Status(stopped));
        Assert.Equal("#FF90EE90", SessionRecapNotices.StatusColour(drift));
        Assert.Equal("#FFFFA500", SessionRecapNotices.StatusColour(stopped));
    }

    [Fact]
    public void TheRecapsDurationDoesNotWRAPAtSixtyMinutes_WhichUpstreamsDoes()
    {
        // A REAL UPSTREAM DEFECT, NOT PORTED. SessionCompleteWindow.xaml.cs:95 formats
        // `log.Duration.Minutes` — the MINUTES COMPONENT, which wraps at 60 — so a COMPLETED
        // good_girls_dont_cum, the longest shipped session at exactly sixty minutes, shows the user
        // "00:00". Its own history row does not have the bug (SessionLogHistoryWindow.xaml.cs:83-85
        // branches on TotalHours). The port uses the total-minute form already on this surface
        // (SessionRackNotices.Clock, MainWindow/MainWindow.Presets.cs:1752), in BOTH windows.
        Assert.Equal("60:00", SessionRackNotices.Clock(TimeSpan.FromMinutes(60)));
        Assert.Equal("75:30", SessionRackNotices.Clock(TimeSpan.FromMinutes(75) + TimeSpan.FromSeconds(30)));
        Assert.Equal("00:07", SessionRackNotices.Clock(TimeSpan.FromSeconds(7)));

        // The media rows use the same one, so a clip two hours into a marathon reads 120:xx rather
        // than starting again from zero.
        Assert.Equal(
            "120:05  VIDEO",
            SessionRecapNotices.MediaRow(
                new ScriptedMediaEntry(
                    ScriptedMediaKind.Video, TimeSpan.FromMinutes(120) + TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public void NoRecapCellNamesAFileOrPromisesXP_AndBothRefusalsSayWhyOnScreen()
    {
        var log = Log(TimeSpan.FromMinutes(30), media: 0) with
        {
            SessionName = "Morning Drift",
            Media =
            [
                new ScriptedMediaEntry(ScriptedMediaKind.Video, TimeSpan.FromMinutes(1)),
                new ScriptedMediaEntry(ScriptedMediaKind.Image, TimeSpan.FromMinutes(2)),
            ],
        };

        // Upstream's row is time, type, DISPLAY NAME and an open-folder arrow
        // (SessionCompleteWindow.xaml:145-167). The port's row is time and type, and the window
        // says so rather than leaving a blank column.
        Assert.Equal("01:00  VIDEO", SessionRecapNotices.MediaRow(log.Media[0]));
        Assert.Equal("02:00  IMAGE", SessionRecapNotices.MediaRow(log.Media[1]));
        Assert.Contains("never a name or a path", SessionRecapNotices.NamesNotRecorded, StringComparison.Ordinal);
        Assert.Contains("No XP", SessionRecapNotices.AwardsNotComputed, StringComparison.Ordinal);

        // The count cell IS upstream's, verbatim (en.json:2797) — it is the part of the recap that
        // survives the refusal intact, because a count is what this build has always kept.
        Assert.Equal("1 videos · 1 images", SessionRecapNotices.MediaCount(log));
        Assert.Equal("0 videos · 0 images", SessionRecapNotices.MediaCount(log with { Media = [] }));

        // The rack no longer claims the recap and the history are missing, because they are not.
        Assert.DoesNotContain("recap", SessionRackNotices.Absences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("history", SessionRackNotices.Absences, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the XP award", SessionRackNotices.Absences, StringComparison.Ordinal);
    }

    [Fact]
    public void AHistoryRowNamesTheRunTheDurationAndTheCounts_InLocalTime()
    {
        var log = Log(TimeSpan.FromMinutes(45), media: 0) with
        {
            SessionName = "Distant Doll",
            SessionIcon = "\U0001F3AF",
            StartedAt = Noon,
            Media = [new ScriptedMediaEntry(ScriptedMediaKind.Image, TimeSpan.Zero)],
        };

        Assert.Equal("\U0001F3AF Distant Doll", SessionRecapNotices.HistoryTitleFor(log));

        // The port's clock is UTC (slice 1's recorded divergence) and upstream's is local
        // (SessionLogService.cs:57). A user is shown their OWN wall clock either way, so the row
        // converts back — and this fact states it as a relationship rather than as a fixed string,
        // because a fixed one would only be true in one timezone.
        var row = SessionRecapNotices.HistoryRow(log);
        Assert.Contains(
            Noon.ToLocalTime().DateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture),
            row,
            StringComparison.Ordinal);
        Assert.Contains("45:00", row, StringComparison.Ordinal);
        Assert.Contains("0 videos · 1 images", row, StringComparison.Ordinal);

        Assert.Equal("3 sessions", SessionRecapNotices.HistoryCount(3));
        Assert.Equal(
            "No session logs yet. Run a session and one will appear here.", SessionRecapNotices.NoHistory);
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    private static string NameFor(DateTimeOffset startedAt) =>
        ScriptedSessionLogStore.FileNameFor(
            Log(TimeSpan.Zero, media: 0) with { StartedAt = startedAt, SessionId = "morning_drift" });

    private static ScriptedSessionLog Log(TimeSpan duration, int media) => new()
    {
        SessionId = "morning_drift",
        SessionName = "Morning Drift",
        Duration = duration,
        StartedAt = Noon,
        EndedAt = Noon + duration,
        Media = [.. Enumerable.Range(0, media).Select(
            i => new ScriptedMediaEntry(ScriptedMediaKind.Image, TimeSpan.FromSeconds(i)))],
    };

    private static ScriptedSessionOutcome Outcome(
        TimeSpan duration, bool completed, DateTimeOffset? startedAt = null)
    {
        var start = startedAt ?? Noon;
        return new ScriptedSessionOutcome(
            new ScriptedSession { Id = "morning_drift", Name = "Morning Drift", Icon = "\U0001F305" },
            duration,
            completed,
            start,
            start + duration);
    }

    /// <summary>A session with ONE module on, so exactly one timer is ever pending on the session
    /// clock and <see cref="StepClock.FireNextTimer"/> is unambiguous.</summary>
    private static ScriptedSession FlashOnly(int durationMinutes = 30) => new()
    {
        Id = "flash_only",
        Name = "Flash Only",
        Icon = "\U0001F4F8",
        DurationMinutes = durationMinutes,
        Settings = new ScriptedSessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 60,
            FlashPerHourEnd = 60,
            FlashImages = 2,
        },
    };

    private static ScriptedSession VideoOnly() => new()
    {
        Id = "video_only",
        Name = "Video Only",
        Icon = "\U0001F4FC",
        DurationMinutes = 30,
        Settings = new ScriptedSessionSettings
        {
            MandatoryVideosEnabled = true,
            VideosPerHour = 60,
        },
    };

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ccp-session-log-" + Guid.NewGuid().ToString("N"));
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
                // A temp directory that will not delete is not this fact's subject.
            }
        }
    }

    /// <summary>
    /// The REAL participant over REAL documents, with the REAL modules firing — the
    /// <c>FlashSurfacePresenterTests</c> rig, plus the scripted clock so a run can be driven by
    /// hand. Two clocks, and they are genuinely different things: the SESSION clock is what a
    /// module paces on, the SCRIPTED clock is what the run measures itself with.
    /// </summary>
    private sealed class LiveRig : IAsyncDisposable
    {
        private LiveRig(
            ApplicationHost host,
            SessionParticipant participant,
            StepClock session,
            HandScriptedClock scripted,
            RecordedFlashSurface flash,
            RecordedVideoSurface video,
            ScriptedSession run,
            string directory)
        {
            Host = host;
            Participant = participant;
            Session = session;
            Scripted = scripted;
            Flash = flash;
            Video = video;
            Run = run;
            Directory = directory;
            Store.LogReady += log => LastLog = log;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Participant { get; }

        public ScriptedSessionLogStore Store => Participant.MediaLog;

        public StepClock Session { get; }

        public HandScriptedClock Scripted { get; }

        public RecordedFlashSurface Flash { get; }

        public RecordedVideoSurface Video { get; }

        public ScriptedSession Run { get; }

        public string Directory { get; }

        /// <summary>The last log <see cref="ScriptedSessionLogStore.LogReady"/> announced — the
        /// only way to see the log of a session that ended by itself.</summary>
        public ScriptedSessionLog? LastLog { get; private set; }

        public static async Task<LiveRig> StartAsync(ScriptedSession run)
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "ccp-session-log-live-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);

            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var sessionClock = new StepClock();
            var scripted = new HandScriptedClock();
            var flash = new RecordedFlashSurface();
            var video = new RecordedVideoSurface();

            // BOTH SURFACES ARE DOUBLES, and it is not squeamishness about drawing: the product's
            // real presenters put a SECOND timer on this clock per firing (the flash's hide,
            // Effects/FlashSurfacePresenter.cs:227; the video's frame cadence,
            // Effects/VideoSurfacePresenter.cs:542), which would make "fire the next timer"
            // ambiguous. Measured, not assumed — the first draft of this rig counted one flash
            // where it fired two, because the second step consumed a hide.
            var participant = new SessionParticipant(
                infra,
                directory,
                sessionClock,
                new StubImagePool(),
                flash,
                onSignalThread: () => true,
                videoSurface: video,
                videoClips: new StubClipPool(directory),
                scriptedClock: scripted);
            var host = new ApplicationHost(log, [participant], new StartupTrace(), registry, infra.UiDispatch);
            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new LiveRig(host, participant, sessionClock, scripted, flash, video, run, directory);
        }

        public void Start() => Assert.True(Participant.Scripted.Start(Run));

        public ScriptedSessionLog StopAndTakeLog(bool completed)
        {
            Assert.True(Participant.Scripted.Stop(completed));
            return LastLog ?? throw new InvalidOperationException("the stop announced no log");
        }

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory that will not delete is not this fact's subject.
            }
        }
    }

    /// <summary>
    /// The session clock, stepped ONE TIMER AT A TIME. <see cref="FireNextTimer"/> moves the clock
    /// to the earliest pending timer's due instant and fires exactly that one — so a module whose
    /// interval carries a random variance still produces an exact number of firings, with no
    /// wall-clock anywhere.
    /// </summary>
    private sealed class StepClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + (due < TimeSpan.Zero ? TimeSpan.Zero : due), Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void FireNextTimer()
        {
            Entry? next;
            lock (_timers)
            {
                next = _timers.OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    throw new InvalidOperationException("no timer is pending on the session clock");
                }

                _timers.Remove(next);
                UtcNow = next.Due > UtcNow ? next.Due : UtcNow;
            }

            next.Fire();
        }

        private void Cancel(Entry entry)
        {
            lock (_timers)
            {
                _timers.Remove(entry);
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(StepClock clock, Entry entry) : IDisposable
        {
            public void Dispose() => clock.Cancel(entry);
        }
    }

    /// <summary>Both of the run's clocks, by hand — the <c>ScriptedSessionTests</c> shape.</summary>
    private sealed class HandScriptedClock : IScriptedClock
    {
        private readonly List<Entry> _timers = [];
        private DateTimeOffset _wall = Noon;
        private TimeSpan _monotonic = TimeSpan.Zero;

        public DateTimeOffset Now
        {
            get { lock (_timers) { return _wall; } }
        }

        public TimeSpan Monotonic
        {
            get { lock (_timers) { return _monotonic; } }
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

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            lock (_timers)
            {
                _wall += by;
                _monotonic += by;
            }

            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= _monotonic).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private void Cancel(Entry entry)
        {
            lock (_timers)
            {
                _timers.Remove(entry);
            }
        }

        private sealed class Entry
        {
            public TimeSpan Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(HandScriptedClock clock, Entry entry) : IDisposable
        {
            public void Dispose() => clock.Cancel(entry);
        }
    }

    /// <summary>Where the flash's images really go. It exists to keep the real presenter's HIDE
    /// timer off the session clock, and it earns its keep twice: it is also what proves the drawn
    /// PATHS reached the surface while only a COUNT reached the log.</summary>
    private sealed class RecordedFlashSurface : IFlashSurface
    {
        public List<IReadOnlyList<string>> Shown { get; } = [];

        public int Hides { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public void Show(IReadOnlyList<string> paths)
        {
            Shown.Add(paths);
            LastPlacement = new CapabilityState.Available("the double allowed it");
        }

        public void HideAll() => Hides++;
    }

    private sealed class StubImagePool : IFlashImagePool
    {
        public IReadOnlyList<string> Draw(int count) =>
            [.. Enumerable.Range(0, Math.Max(0, count)).Select(i => $"image-{i}.png")];
    }

    private sealed class StubClipPool(string folder) : IVideoClipPool
    {
        public int ActiveCount => 3;

        public string Folder => folder;

        public string? Draw() => "clip.mp4";
    }

    /// <summary>The video surface double from <c>MandatoryVideoModuleTests</c>, narrowed: it mirrors
    /// the product where the product's own state matters — a Begin that succeeded leaves the
    /// surface SHOWING, which is what makes the module drop the next firing.</summary>
    private sealed class RecordedVideoSurface : IVideoSurface
    {
        private Action? _onEnded;

        public bool Showing { get; private set; }

        public bool Running => Showing;

        public bool Engaged => Showing;

        public bool CanReachADisplay => true;

        public int FramesDecoded => 0;

        public int FramesHeld => 0;

        public int FramesAdvanced => 0;

        public CcpClient.Desktop.Video.VideoSurfaceObservation LastObservation =>
            CcpClient.Desktop.Video.VideoSurfaceObservation.NotAsked;

        public string? PlayingClip { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Begin(
            string clipPath, TimeSpan maxLength, Action onEnded, IVideoFramePainter? painter = null)
        {
            _onEnded = onEnded;
            Showing = true;
            PlayingClip = clipPath;
            LastPlacement = new CapabilityState.Available("the double allowed it");
            return LastPlacement;
        }

        public void End()
        {
            Showing = false;
            PlayingClip = null;
        }

        public void RaiseEnded()
        {
            var ended = _onEnded;
            End();
            ended?.Invoke();
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
}
