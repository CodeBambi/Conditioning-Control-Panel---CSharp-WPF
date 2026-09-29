using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Tests that share CoreSettings.Current, CoreEngine and the CoreSession seams run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class SessionStatics { public const string Name = "SessionStatics"; }

/// <summary>SessionRunner headless, driven by Tick, against WPF SessionEngine's run/stop/log behaviour.</summary>
[Collection(SessionStatics.Name)]
public sealed class SessionRunnerTests : IDisposable
{
    private const string Id = "runner-golden";
    private static readonly string[] AllowedSettingsChanges =
        { "TotalSessions", "RecentSessionStartsUtc", "LastSessionModId", "SameModRun" };
    private static readonly string[] Timestamps = { "started_at", "ended_at", "timestamp", "session_time_seconds" };

    private readonly SessionLogService _logs = new();
    private readonly SessionRunner _runner;
    private SessionLog? _ready;

    public SessionRunnerTests()
    {
        Assert.StartsWith(Path.GetTempPath(), _logs.LogsFolder);   // RoadmapTestProfile sandbox
        DeleteLogs();
        PhrasePoolCustody.Seed();   // the head's job (Program.cs), not the runner's
        _runner = new SessionRunner(_logs);
        _logs.LogReady += (_, e) => _ready = e.Log;
        CoreBouncingText.StartAction = CoreBouncingText.StopAction = null;
    }

    public void Dispose()
    {
        _runner.Stop();
        CoreEngine.Stop();
        CoreSession.IsSessionRunningProvider = null;
        CoreSession.NoteUserPhrasePoolEdit = null;
        CoreSession.ReapplyPhrasePoolOverrides = null;
        CoreSession.UserPhrasePoolsWhileOverriding = null;
        DeleteLogs();
    }

    private void DeleteLogs()
    {
        foreach (var f in Directory.GetFiles(_logs.LogsFolder, "*_" + Id + ".json")) File.Delete(f);
    }

    private static Session OneMinute() => new()
    {
        Id = Id, Name = "Runner Golden", Icon = "🌸", Difficulty = SessionDifficulty.Medium, DurationMinutes = 1,
        Settings = new SessionSettings
        {
            FlashEnabled = true, FlashPerHour = 77,
            SubliminalEnabled = true, SubliminalFrames = 9, SubliminalPhrases = { "zzsession" },
            BouncingTextEnabled = true, BouncingTextPhrases = { "zzbounce" },
            LockCardEnabled = true, LockCardFrequency = 7, LockCardPhrases = { "zzlock" },
        },
    };

    private static JObject SettingsJson() =>
        JObject.Parse(JsonConvert.SerializeObject(CoreSettings.Current, Formatting.Indented));   // = settings.json

    [Fact]
    public void OneMinuteSession_CompletesByTick_SavesLog_RestoresSettings_ReleasesPools()
    {
        var s = CoreSettings.Current;
        s.SubliminalDuration = 3;
        var before = SettingsJson();

        _runner.Start(OneMinute());
        Assert.True(CoreSession.IsSessionRunning && CoreEngine.IsRunning);
        Assert.NotNull(CoreSession.UserPhrasePoolsWhileOverriding!());
        Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning && LockCardScheduler.Instance.IsRunning);
        Assert.Equal((77, 9, 7), (s.FlashFrequency, s.SubliminalDuration, s.LockCardFrequency));
        Assert.True(s.SubliminalPool["zzsession"]);

        _logs.RecordImages(new[] { "/pics/a.png" });
        var ticks = 0;
        _runner.Ticked += () => ticks++;   // the head's clock labels (WPF ProgressUpdated)
        _runner.Tick(TimeSpan.FromSeconds(59));
        Assert.True(_runner.IsRunning);
        Assert.Equal(1, ticks);
        _runner.Tick(TimeSpan.FromSeconds(60));

        Assert.False(_runner.IsRunning || CoreSession.IsSessionRunning || CoreEngine.IsRunning);
        Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
        Assert.Null(CoreSession.UserPhrasePoolsWhileOverriding!());

        var after = SettingsJson();
        var changed = before.Properties().Where(p => !JToken.DeepEquals(p.Value, after[p.Name])).Select(p => p.Name).ToList();
        Assert.Contains("TotalSessions", changed);
        Assert.Empty(changed.Except(AllowedSettingsChanges));
        Assert.Equal(before.Properties().Select(p => p.Name), after.Properties().Select(p => p.Name));   // no new keys

        Assert.NotNull(_ready);
        var file = Path.Combine(_logs.LogsFolder, $"{_ready!.StartedAt:yyyyMMdd_HHmmss}_{Id}.json");
        var saved = JObject.Parse(File.ReadAllText(file));
        var golden = JObject.Parse(File.ReadAllText(Fixture()));
        foreach (var o in new[] { saved, golden })
            foreach (var t in o.Descendants().OfType<JProperty>().Where(p => Timestamps.Contains(p.Name)).ToList()) t.Remove();
        Assert.Equal(golden.ToString(), saved.ToString());
    }

    [Fact]
    public void EarlyStop_IsNotCompleted()
    {
        _runner.Start(OneMinute());
        _logs.RecordImages(new[] { "/pics/a.png" });
        _runner.Tick(TimeSpan.FromSeconds(30));
        _runner.Stop();

        Assert.False(_runner.IsRunning || CoreEngine.IsRunning);
        Assert.False(_ready!.Completed);
        Assert.Equal(0, _ready.XPEarned);
        Assert.Single(Directory.GetFiles(_logs.LogsFolder, "*_" + Id + ".json"));
    }

    [Fact]
    public void ShortSessionWithoutMedia_IsNotSaved()
    {
        _runner.Start(OneMinute());
        _runner.Stop();

        Assert.NotNull(_ready);
        Assert.Empty(Directory.GetFiles(_logs.LogsFolder, "*_" + Id + ".json"));
    }

    [Fact]
    public void DeferredStarts_FireAtTheirMinute_LockCardsGetTheRemainingWindow()
    {
        int btStarts = 0;
        CoreBouncingText.StartAction = () => btStarts++;
        var session = OneMinute();
        session.DurationMinutes = 2;
        session.Settings.BouncingTextStartMinute = 1;
        session.Settings.LockCardStartMinute = 1;
        _runner.Start(session);
        btStarts = 0;   // the engine arms by the saved flag first; the session then stops and defers it
        Assert.False(LockCardScheduler.Instance.IsRunning);

        _runner.Tick(TimeSpan.FromSeconds(59));
        Assert.Equal(0, btStarts);
        _runner.Tick(TimeSpan.FromSeconds(60));
        Assert.Equal(1, btStarts);
        Assert.True(LockCardScheduler.Instance.IsRunning);
        Assert.Equal(1.0, LockCardScheduler.Instance.LastWindowMinutes);   // #736: minutes left, not the duration
    }

    [Fact]
    public void Ledger_IsPersistedBeforeTheSessionOverrides()
    {
        var old = CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        CoreSettings.ServiceProvider = () => svc;
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        try
        {
            svc.Current.FlashFrequency = 20;
            var starts = svc.Current.RecentSessionStartsUtc.Count;
            _runner.Start(OneMinute());

            var disk = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(starts + 1, ((JArray)disk["RecentSessionStartsUtc"]!).Count);
            Assert.Equal(20, (int)disk["FlashFrequency"]!);   // the user's, not the session's 77
            Assert.Equal(77, svc.Current.FlashFrequency);
        }
        finally
        {
            _runner.Stop();
            svc.SaveImmediate();
            svc.SealForReset();
            CoreSettings.ServiceProvider = old;
            foreach (var f in Directory.GetFiles(CorePaths.UserData, "settings*")) File.Delete(f);
        }
    }

    [Fact]
    public void Pause_FreezesTheClock_StopsFeatures_AndCostsOneHundredXp()
    {
        var old = CoreProgression.AddXPProvider;
        double banked = 0;
        var oldXp = CoreSettings.Current.PlayerXP;
        // A bank that really banks: XPEarned is the ledger's gain, not the award handed over.
        CoreProgression.AddXPProvider = (xp, _) => { banked += xp; CoreSettings.Current.PlayerXP += xp; };
        try
        {
            var session = OneMinute();
            session.BonusXP = 800;
            _runner.Start(session);
            _logs.RecordImages(new[] { "/pics/a.png" });
            _runner.Tick(TimeSpan.FromSeconds(20));
            _runner.Pause();
            _runner.Pause();   // already paused: no second charge

            Assert.True(_runner.IsPaused && _runner.IsRunning && CoreSession.IsSessionRunning);
            Assert.Equal((1, 100), (_runner.PauseCount, _runner.XPPenalty));
            Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            var frozen = _runner.Elapsed;
            _runner.Tick(TimeSpan.FromSeconds(90));   // a paused session never completes
            Assert.True(_runner.IsRunning);
            Assert.Equal(frozen, _runner.Elapsed);

            _runner.Resume();
            _runner.Tick(TimeSpan.FromSeconds(60));

            var level = CoreSettings.Current.PlayerLevel;
            var expected = SessionXp.Compute(800, 1, level, TimeSpan.FromSeconds(60));
            Assert.NotEqual(SessionXp.Compute(800, 0, level, TimeSpan.FromSeconds(60)), expected);
            Assert.Equal(expected, _ready!.XPEarned);
            Assert.Equal(expected, banked);
        }
        finally { CoreProgression.AddXPProvider = old; CoreSettings.Current.PlayerXP = oldXp; }
    }

    [Fact]
    public void AGateThatRefusesTheAward_LogsZeroBanked()
    {
        var old = CoreProgression.AddXPProvider;
        CoreProgression.AddXPProvider = (_, _) => { };   // e.g. signed out: ProgressionBank's login gate
        try
        {
            var session = OneMinute();
            session.BonusXP = 800;
            _runner.Start(session);
            _runner.Tick(TimeSpan.FromSeconds(61));
            Assert.Equal(0, _ready!.XPEarned);
        }
        finally { CoreProgression.AddXPProvider = old; }
    }

    private sealed class QuizHost : IPopQuizHost
    {
        public int Closes;
        public bool IsQuizOpen => false;
        public bool IsLockCardOpen => false;
        public bool IsInteractionBusy => false;
        public bool Defer(Action replay) => false;
        public void DropDeferred() { }
        public void Open(bool isTest) { }
        public void CloseAll() => Closes++;
    }

    /// <summary>WPF SessionEngine pop quiz: start at session start (:1628), stop + close on pause (:527),
    /// back on resume only while enabled (:574), stop at the end (:372), toggle and rate restored (:1771).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PopQuiz_FollowsTheUserToggle_ThroughPauseResumeAndStop(bool enabled)
    {
        var s = CoreSettings.Current;
        var (oldEnabled, oldFreq, oldQuiz) = (s.PopQuizEnabled, s.PopQuizFrequency, CoreEngine.PopQuiz);
        var host = new QuizHost();
        using var quiz = new PopQuizScheduler(host);
        CoreEngine.PopQuiz = quiz;
        s.PopQuizFrequency = 5;
        try
        {
            // Engine already up with the opposite toggle: the session start itself must start/stop it.
            s.PopQuizEnabled = !enabled;
            CoreEngine.Start();
            s.PopQuizEnabled = enabled;
            _runner.Start(OneMinute());
            Assert.Equal(enabled, quiz.IsRunning);
            host.Closes = 0;

            _runner.Pause();
            Assert.False(quiz.IsRunning);
            Assert.Equal(enabled ? 1 : 0, host.Closes);

            _runner.Resume();
            Assert.Equal(enabled, quiz.IsRunning);

            s.PopQuizEnabled = !enabled;   // mid-session edits are session-scoped
            s.PopQuizFrequency = 99;
            _runner.Stop();
            Assert.False(quiz.IsRunning);
            Assert.Equal((enabled, 5), (s.PopQuizEnabled, s.PopQuizFrequency));
        }
        finally
        {
            _runner.Stop();
            CoreEngine.PopQuiz = oldQuiz;
            (s.PopQuizEnabled, s.PopQuizFrequency) = (oldEnabled, oldFreq);
        }
    }

    [Fact]
    public void Resume_RestartsOnlyFeaturesWhoseStartMinuteHasPassed()
    {
        int btStarts = 0;
        CoreBouncingText.StartAction = () => btStarts++;
        var session = OneMinute();
        session.DurationMinutes = 2;
        session.Settings.BouncingTextStartMinute = 1;
        session.Settings.LockCardStartMinute = 1;
        _runner.Start(session);
        _runner.Tick(TimeSpan.FromSeconds(30));
        _runner.Pause();
        btStarts = 0;

        _runner.Resume();
        Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning);   // minute 0: started before the pause
        Assert.Equal(0, btStarts);                                     // minute 1: still deferred
        Assert.False(LockCardScheduler.Instance.IsRunning);

        _runner.Tick(TimeSpan.FromSeconds(60));
        Assert.Equal(1, btStarts);
        Assert.True(LockCardScheduler.Instance.IsRunning);
    }

    [Fact]
    public void FlashRamp_FollowsElapsed_AndIsHandedBackOnStop()
    {
        var s = CoreSettings.Current;
        s.FlashOpacity = 90;
        var session = OneMinute();
        session.DurationMinutes = 2;
        session.Settings.RampCurve = RampCurve.Linear;
        session.Settings.FlashOpacity = 20; session.Settings.FlashOpacityEnd = 80;
        session.Settings.FlashPerHour = 10; session.Settings.FlashPerHourEnd = 70;
        session.Settings.FlashScale = 150;
        _runner.Start(session);

        _runner.Tick(TimeSpan.FromSeconds(30));   // 25 %
        Assert.Equal((35, 25, 150), (s.FlashOpacity, s.FlashFrequency, s.ImageScale));
        _runner.Tick(TimeSpan.FromSeconds(90));   // 75 %
        Assert.Equal((65, 55), (s.FlashOpacity, s.FlashFrequency));

        _runner.Stop();
        Assert.Equal(90, s.FlashOpacity);
    }

    [Fact]
    public void PinkTint_StartsWithinThreeMinutesOfItsMinute_ThenRamps()
    {
        var rng = new Random(7);
        var starts = Enumerable.Range(0, 2000).Select(_ => SessionRunner.RandomizedStart(true, 7, rng)).ToList();
        Assert.All(starts, m => Assert.InRange(m, 4.0, 10.0));
        Assert.True(starts.Min() < 4.1 && starts.Max() > 9.9);   // the whole window, not a narrower one
        Assert.Contains(Enumerable.Range(0, 200).Select(_ => SessionRunner.RandomizedStart(true, 1, rng)), m => m == 0);   // clamped at 0
        Assert.Equal(7, SessionRunner.RandomizedStart(false, 7, rng));
        Assert.Equal(0, SessionRunner.RandomizedStart(true, 0, rng));

        var session = OneMinute();
        session.DurationMinutes = 10;
        session.Settings.RampCurve = RampCurve.Linear;
        session.Settings.PinkFilterEnabled = true;
        session.Settings.PinkFilterStartMinute = 4;
        session.Settings.PinkFilterStartOpacity = 10;
        session.Settings.PinkFilterEndOpacity = 40;
        _runner.Start(session);
        var at = _runner.PinkStartMinute;
        Assert.InRange(at, 1.0, 7.0);
        Assert.False(CoreSettings.Current.PinkFilterEnabled);

        _runner.Tick(TimeSpan.FromMinutes(at - 0.01));
        Assert.False(CoreSettings.Current.PinkFilterEnabled);
        Assert.Null(_runner.PinkOpacity);
        _runner.Tick(TimeSpan.FromMinutes(at + 1e-4));   // TimeSpan rounds to the millisecond
        Assert.True(CoreSettings.Current.PinkFilterEnabled);
        Assert.Equal(10, _runner.PinkOpacity!.Value, 2);
        _runner.Tick(TimeSpan.FromMinutes((at + 10) / 2));   // halfway through the tint's own window
        Assert.Equal(25, _runner.PinkOpacity!.Value, 2);
    }

    private static string Fixture([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "session_log_runner_wpf.json");
}
