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
        _runner.Tick(TimeSpan.FromSeconds(59));
        Assert.True(_runner.IsRunning);
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

    private static string Fixture([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "session_log_runner_wpf.json");
}
