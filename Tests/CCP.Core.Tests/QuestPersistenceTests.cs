using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// quests.json (System.Text.Json, written by the head's QuestService) and
/// quest_definitions_cache.json (Newtonsoft, written by QuestDefinitionService) against golden
/// files produced by the PRE-MOVE WPF models and serializer calls under TZ=Europe/Berlin.
/// Regenerate them the same way when a persisted field is added on purpose.
/// </summary>
public sealed class QuestPersistenceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-quest-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name);

    /// <summary>The persisted contract of <see cref="QuestProgress"/>: name, CLR type, order,
    /// taken from the WPF model before it moved.</summary>
    private static readonly (string Name, Type Type)[] Persisted =
    {
        ("DailyQuest", typeof(ActiveQuest)),
        ("DailyQuests", typeof(List<ActiveQuest>)),
        ("WeeklyQuest", typeof(ActiveQuest)),
        ("DailyRerollsUsed", typeof(int)),
        ("WeeklyRerollsUsed", typeof(int)),
        ("DailyRerollResetDate", typeof(DateTime?)),
        ("WeeklyRerollResetDate", typeof(DateTime?)),
        ("DailyQuestGeneratedAt", typeof(DateTime?)),
        ("WeeklyQuestGeneratedAt", typeof(DateTime?)),
        ("DailyQuestsCompletedToday", typeof(int)),
        ("DailyCompletionResetDate", typeof(DateTime?)),
        ("TotalDailyQuestsCompleted", typeof(int)),
        ("TotalWeeklyQuestsCompleted", typeof(int)),
        ("TotalXPFromQuests", typeof(int)),
        ("DailyQuestCompletionDates", typeof(List<DateTime>)),
        ("quest_completion_log", typeof(List<QuestLogEntry>)),
        ("DailyRolledUnresolved", typeof(bool)),
        ("WeeklyRolledUnresolved", typeof(bool)),
        ("OwnerUnifiedId", typeof(string)),
        ("LastPremiumSeenUtc", typeof(DateTime?)),
    };

    [Fact]
    public void QuestProgressShapeMatchesWpfAndFixture()
    {
        var info = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
            .GetTypeInfo(typeof(QuestProgress));
        var props = info.Properties.Where(p => p.Get != null).ToArray();

        Assert.Equal(Persisted, props.Select(p => (p.Name, p.PropertyType)).ToArray());
        Assert.All(props, p => { Assert.NotNull(p.Set); Assert.Null(p.CustomConverter); });

        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("quests_golden.json")));
        Assert.Equal(Persisted.Select(p => p.Name), doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void QuestsJsonRoundTripsByteIdentical() => InBerlin(() =>
    {
        var golden = File.ReadAllText(Fixture("quests_golden.json"));
        // QuestService.LoadProgress / Save, verbatim options.
        var progress = JsonSerializer.Deserialize<QuestProgress>(golden)!;
        var again = JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(golden, again);
        Assert.Equal(3, progress.DailyQuests.Count);
        Assert.Equal("s\u00E9ason_\u30D3", progress.QuestCompletionLog[1].Q);
    });

    [Fact]
    public void DefinitionsCacheRoundTripsByteIdentical() => InBerlin(() =>
    {
        var golden = File.ReadAllBytes(Fixture("quest_definitions_cache_golden.json"));
        var path = Path.Combine(_dir, "quest_definitions_cache.json");
        File.WriteAllBytes(path, golden);

        using var svc = new QuestDefinitionService(_dir);
        svc.LoadCache();
        File.Delete(path);
        svc.SaveCache();

        Assert.Equal(golden, File.ReadAllBytes(path));
        Assert.Equal(17, svc.Version);
        Assert.Equal(new[] { "daily_flash_50", "daily_cam", "s\u00E9ason_\u30D3" }, svc.GetDailyQuests().Select(q => q.Id));
        Assert.True(Directory.Exists(Path.Combine(_dir, "quest-images")));
    });

    [Fact]
    public void NoCacheFallsBackToEmbeddedDefinitions()
    {
        using var svc = new QuestDefinitionService(_dir);
        svc.LoadCache();

        Assert.Equal(0, svc.Version);
        Assert.Equal(QuestDefinition.DailyQuests.Select(q => q.Id), svc.GetDailyQuests().Select(q => q.Id));
        Assert.Equal(QuestDefinition.WeeklyQuests.Select(q => q.Id), svc.GetWeeklyQuests().Select(q => q.Id));
    }

    /// <summary>Local-kind dates carry the zone's offset, so pin it (Unix honours TZ only).</summary>
    internal static void InBerlin(Action body)
    {
        var old = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", "Europe/Berlin");
        TimeZoneInfo.ClearCachedData();
        try
        {
            if (TimeZoneInfo.Local.GetUtcOffset(new DateTime(2025, 3, 14, 12, 0, 0)) != TimeSpan.FromHours(1))
                Assert.Skip("Cannot pin local time zone to Europe/Berlin on this platform");
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", old);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
