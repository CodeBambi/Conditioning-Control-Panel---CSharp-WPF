using System.Text.Json;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The SURFACE half of the scripted session: what the rack row says, what the two confirmations
/// promise, and the composition that lets a surface start one at all.
///
/// <para><b>Everything here is pure or file-backed — no Avalonia, by design.</b> The sentences live
/// in <see cref="SessionRackNotices"/> rather than in AXAML precisely so they can be pinned without
/// mounting a window (the <c>SchedulerPanelNotices</c> precedent); the visual tree, the input and
/// the wiring are pinned next door in <c>CcpClient.HeadlessTests.SessionRackHeadlessTests</c>.</para>
///
/// <para><b>The composition facts run against the REAL participant over REAL documents in a temp
/// directory</b>, for the reason every session fact in this suite gives: a double diverges from the
/// product exactly where the defect lives. The one substitution is the clock, which is a declared
/// seam on the composition root.</para>
/// </summary>
public class ScriptedSessionSurfaceTests
{
    // =====================================================================================
    //  THE ROW — upstream's cells, and the two it deliberately does not have
    // =====================================================================================

    [Fact]
    public void EveryShippedSessionsRow_CarriesUpstreamsCells_OffTheRealFiles()
    {
        var byId = ScriptedSession.ReadBuiltIns().ToDictionary(s => s.Id, StringComparer.Ordinal);
        var drift = byId["morning_drift"];

        Assert.Equal("\U0001F305", SessionRackNotices.RowIcon(drift));
        Assert.Equal("Easy · 30 min", SessionRackNotices.RowMeta(drift));

        // The FIRST LINE of the authored description, trimmed — upstream's blurb cell
        // (MainWindow/MainWindow.SessionIO.cs:470-478). The file's description runs to four
        // paragraphs; a row carrying all of them is the mutation this pins.
        Assert.Equal(
            "Let the morning carry you gently into that soft, floaty space...",
            SessionRackNotices.RowBlurb(drift));
        Assert.DoesNotContain("\n", SessionRackNotices.RowBlurb(drift), StringComparison.Ordinal);

        Assert.Equal("Medium · 45 min", SessionRackNotices.RowMeta(byId["gamer_girl"]));
        Assert.Equal("Hard · 60 min", SessionRackNotices.RowMeta(byId["good_girls_dont_cum"]));
        Assert.Equal("Easy · 45 min", SessionRackNotices.RowMeta(byId["distant_doll"]));
        Assert.Equal("\U0001F3AE", SessionRackNotices.RowIcon(byId["gamer_girl"]));

        // Upstream's own fallbacks, both reachable from a hand-written or imported file rather than
        // from these four (SessionIO.cs:431, :470-471).
        Assert.Equal("\U0001F3AC", SessionRackNotices.RowIcon(new ScriptedSession { Icon = "  " }));
        Assert.Equal("Custom session", SessionRackNotices.RowBlurb(new ScriptedSession()));
    }

    [Fact]
    public void NoRowPromisesXP_BecauseNothingInThisBuildAwardsAny()
    {
        // The files all carry a bonusXP and upstream's row prints it (en.json:73, "+{0} XP").
        // Refused deliberately, and asserted so the refusal cannot be undone by accident on the one
        // surface whose job is telling the user what they are agreeing to.
        var sessions = ScriptedSession.ReadBuiltIns();
        Assert.NotEmpty(sessions);
        Assert.All(sessions, session => Assert.True(session.BonusXP > 0));
        Assert.All(
            sessions,
            session => Assert.DoesNotContain("XP", SessionRackNotices.RowMeta(session), StringComparison.Ordinal));
    }

    [Fact]
    public void TheDifficultyStripeCarriesUpstreamsOwnFourColours()
    {
        // Resources/Theme/Colors.xaml:191-197. The stripe is upstream's at-a-glance channel for the
        // star rating this port strips out of the words (MainWindow.SessionIO.cs:421-426), so the
        // four must stay distinct AND stay upstream's.
        Assert.Equal("#FF57D9A3", SessionRackNotices.DifficultyStripe(ScriptedSessionDifficulty.Easy));
        Assert.Equal("#FFF5C242", SessionRackNotices.DifficultyStripe(ScriptedSessionDifficulty.Medium));
        Assert.Equal("#FFFF8A4C", SessionRackNotices.DifficultyStripe(ScriptedSessionDifficulty.Hard));
        Assert.Equal("#FFF23557", SessionRackNotices.DifficultyStripe(ScriptedSessionDifficulty.Extreme));

        var all = Enum.GetValues<ScriptedSessionDifficulty>()
            .Select(SessionRackNotices.DifficultyStripe)
            .ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    // =====================================================================================
    //  THE TWO CONFIRMATIONS
    // =====================================================================================

    [Fact]
    public void TheStartConfirmationNamesTheDuration_AndKeepsUpstreamsPromiseWordForWord()
    {
        var drift = Built("morning_drift");

        Assert.Equal("Start Morning Drift?", SessionRackNotices.StartConfirmTitle(drift));
        Assert.Equal("Duration: 30 minutes", SessionRackNotices.StartConfirmDuration(drift));

        // MainWindow/MainWindow.Presets.cs:1467-1470, and it is the contract the restore keeps:
        // both halves, in upstream's words.
        Assert.Contains(
            "Your current settings will be temporarily replaced.",
            SessionRackNotices.SettingsPromise,
            StringComparison.Ordinal);
        Assert.Contains(
            "They will be restored when the session ends.",
            SessionRackNotices.SettingsPromise,
            StringComparison.Ordinal);
        Assert.Equal("Ready to begin?", SessionRackNotices.ReadyToBegin);

        // No blank line anywhere in the strip's text: an empty line wedges the layout pass on these
        // plates in Avalonia 12.1.1 (MainWindow.axaml:275-280, measured on the refusal plate).
        Assert.DoesNotContain("\n\n", SessionRackNotices.SettingsPromise, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStopConfirmationNamesTheSessionAndBothClocks_InUpstreamsMMSS()
    {
        var drift = Built("morning_drift");
        var progress = new ScriptedSessionProgress(
            TimeSpan.FromSeconds(216), TimeSpan.FromSeconds(1584), 12);

        Assert.Equal(
            "You're currently in a session: \U0001F305 Morning Drift",
            SessionRackNotices.StopConfirmSubject(drift));
        Assert.Equal(
            "Time elapsed: 03:36 — Time remaining: 26:24",
            SessionRackNotices.StopConfirmTiming(progress));

        // TOTAL minutes, never the minutes COMPONENT — upstream's own
        // `{((int)elapsed.TotalMinutes):D2}:{elapsed.Seconds:D2}` (MainWindow.Presets.cs:1895-1896).
        // A 60-minute session is one of the four shipped ones, so the wrap is reachable on a real
        // file rather than only in principle.
        Assert.Equal("61:07", SessionRackNotices.Clock(TimeSpan.FromSeconds(3667)));
        Assert.Equal("Are you sure you want to quit?", SessionRackNotices.StopConfirmQuestion);
    }

    [Fact]
    public void TheOneButtonCarriesTheTimeLeft_AsUpstreamsOwnCaptionDoes()
    {
        // en.json:2321 "STOP SESSION ({0}:{1})", written on every tick
        // (MainWindow/MainWindow.Presets.cs:1752), and en.json:1331 idle.
        Assert.Equal("Start Session", SessionRackNotices.StartButtonIdle);
        Assert.Equal(
            "STOP SESSION (26:24)",
            SessionRackNotices.StopButtonRunning(
                new ScriptedSessionProgress(TimeSpan.FromSeconds(216), TimeSpan.FromSeconds(1584), 12)));
    }

    [Fact]
    public void TheReadoutIsOneReading_TruncatedNeverRounded()
    {
        var drift = Built("morning_drift");

        Assert.Equal(
            "Phase 2 of 5 — Pink Awakening: Pink filter begins its gradual embrace",
            SessionRackNotices.PhaseLine(drift, drift.Phases[1], 1));
        Assert.Equal("This session has no named phases.", SessionRackNotices.PhaseLine(drift, null, 0));

        // 99.9% is 99, not 100: a session one tick from its end must not read as finished, which is
        // what a rounding formatter would say for the last 30 seconds of it.
        Assert.Equal(
            "99% — 29:57 elapsed, 00:03 remaining",
            SessionRackNotices.ProgressLine(
                new ScriptedSessionProgress(
                    TimeSpan.FromSeconds(1797), TimeSpan.FromSeconds(3), 99.9)));

        Assert.Equal(
            "Nothing is running. Pick a session, then press Start Session.",
            SessionRackNotices.IdleLine(null));
        Assert.Equal(
            "Morning Drift is selected — 30 minutes, 5 phases. Press Start Session to begin.",
            SessionRackNotices.IdleLine(drift));
    }

    // =====================================================================================
    //  THE COMPOSITION — the gap this slice exists to close
    // =====================================================================================

    [Fact]
    public async Task TheComposedParticipantOwnsOneScriptedRun_OverItsOwnEngineAndItsOwnDocuments()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();

        // The gap slices 1 and 2 left: nothing constructed a run. This is the fact that it is
        // constructed, that it is reachable from the object the shell already holds, and that it
        // is wired to the SAME engine and the SAME documents every module reads — not to copies.
        Assert.False(rig.Session.Scripted.Running);
        Assert.Null(rig.Session.Scripted.Current);

        Assert.True(rig.Session.Scripted.Start(Built("morning_drift")));

        Assert.True(rig.Session.Engine.Running);
        Assert.Equal("morning_drift", rig.Session.Scripted.Current?.Id);
        Assert.Equal("Settling In", rig.Session.Scripted.CurrentPhase?.Name);

        // Morning Drift's own dials, in the documents the user's own dials were in a line ago.
        Assert.Equal(12, rig.Session.Preset.Current.FlashesPerHour);
        Assert.Equal(2, rig.Session.SubliminalPreset.Current.PerMinute);
        Assert.Equal(30, rig.Session.VisualsPreset.Current.FlashOpacityPercent);

        Assert.True(rig.Session.Scripted.Stop());

        Assert.Equal(7, rig.Session.Preset.Current.FlashesPerHour);
        Assert.Equal(11, rig.Session.SubliminalPreset.Current.PerMinute);
        Assert.Equal(66, rig.Session.VisualsPreset.Current.FlashOpacityPercent);
    }

    [Fact]
    public async Task ClosingTheAppMidSession_PERSISTS_TheUsersDialsAndNotTheSessions()
    {
        // THE DEFECT THIS FORBIDS IS UPSTREAM'S #471/#476 CLASS, and wiring a surface is what made
        // it reachable: a running session holds eleven documents at ITS values, dirty and
        // deliberately unwritten, and the teardown flush (persistence contract §11) writes every
        // dirty document. Without the stop at the head of FlushAsync the app would persist the
        // SESSION's dials over the user's own and never give them back — which is the exact
        // opposite of what the confirmation promised.
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersDials();
        Assert.True(rig.Session.Scripted.Start(Built("morning_drift")));
        Assert.Equal(12, rig.Session.Preset.Current.FlashesPerHour);

        await rig.Session.FlushAsync(TimeSpan.FromSeconds(5));

        Assert.False(rig.Session.Scripted.Running);
        Assert.Equal(7, rig.Session.Preset.Current.FlashesPerHour);

        // ON DISK, not merely in memory: what the next launch will read is the whole claim.
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(rig.Directory, CcpClient.Desktop.Persistence.SessionPresetDocument.FileName),
                TestContext.Current.CancellationToken));
        Assert.Equal(7, document.RootElement.GetProperty("flashesPerHour").GetInt32());
    }

    private static ScriptedSession Built(string id) =>
        ScriptedSession.ReadBuiltIns().Single(s => s.Id == id);

    /// <summary>
    /// The REAL participant over REAL documents in a temp directory, started through the REAL host
    /// — the <c>SessionSpineTests</c> rig, narrowed to what a surface fact needs.
    /// </summary>
    private sealed class Rig : IAsyncDisposable
    {
        private Rig(ApplicationHost host, SessionParticipant session, string directory)
        {
            Host = host;
            Session = session;
            Directory = directory;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public string Directory { get; }

        public static async Task<Rig> StartAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ccp-scripted-surface-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var registry = new OperationRegistry();
            var log = new CollectingSurfaceLog();
            var infra = new ParticipantInfrastructure(registry, new UiDispatchBoundary(), log);
            var session = new SessionParticipant(infra, dir);
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);
            var outcome = await host.StartParticipantsAsync(TestContext.Current.CancellationToken);
            Assert.IsType<StartupOutcome.Success>(outcome);
            return new Rig(host, session, dir);
        }

        /// <summary>Values no shipped session carries, so "it came back" cannot be satisfied by a
        /// default (the <c>ScriptedSessionTests</c> rig's rule).</summary>
        public void WriteTheUsersDials()
        {
            Session.Preset.Mutate(d =>
            {
                d.FlashEnabled = true;
                d.FlashesPerHour = 7;
            });
            Session.SubliminalPreset.Mutate(d =>
            {
                d.Enabled = true;
                d.PerMinute = 11;
            });
            Session.VisualsPreset.Mutate(d => d.FlashOpacityPercent = 66);
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
                // A temp directory that will not delete is not a failure of the thing under test.
            }
        }
    }

    private sealed class CollectingSurfaceLog : ILogSink
    {
        private readonly List<string> _lines = [];

        public void Log(string message)
        {
            lock (_lines)
            {
                _lines.Add(message);
            }
        }
    }
}
