using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>QuizStore against goldens made by the PRE-MOVE WPF QuizService code (its line ranges compiled
/// verbatim in a throwaway console app, TZ=Europe/Berlin); regenerate that way on purpose only.</summary>
public sealed class QuizStoreTests : IDisposable
{
    private static readonly string HistoryPath = Path.Combine(CorePaths.UserData, "quiz_history.json");
    private static readonly string CategoriesPath = Path.Combine(CorePaths.UserData, "custom_quiz_categories.json");

    public QuizStoreTests() => Directory.CreateDirectory(CorePaths.UserData);

    public void Dispose()
    {
        File.Delete(HistoryPath);
        File.Delete(CategoriesPath);
    }

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "Quiz", name);

    /// <summary>Golden minus its newest entry, then SaveEntry(that entry): must reproduce the golden.</summary>
    private static byte[] HistoryRoundTrip(string golden)
    {
        var entries = JsonConvert.DeserializeObject<List<QuizHistoryEntry>>(golden)!;
        File.WriteAllText(HistoryPath, JsonConvert.SerializeObject(entries.Skip(1).ToList(), Formatting.Indented));
        QuizStore.SaveEntry(entries[0]); // SaveEntry = LoadHistory + insert + write
        return File.ReadAllBytes(HistoryPath);
    }

    [Fact]
    public void HistoryRoundTripsByteIdentical() => QuestPersistenceTests.InBerlin(() =>
    {
        var golden = File.ReadAllBytes(Fixture("quiz_history_golden.json"));
        Assert.Equal(golden, HistoryRoundTrip(Encoding.UTF8.GetString(golden)));

        var loaded = QuizStore.LoadHistory();
        Assert.Equal(new[] { "obedience", "custom_velvet", "Obedience" }, loaded.Select(QuizStore.TrendKey));
        Assert.Equal(4, loaded[0].Answers[0].PointsEarned);
    });

    [Fact]
    public void BrokenHistoryFixtureFailsRoundTrip() => QuestPersistenceTests.InBerlin(() =>
    {
        var broken = File.ReadAllText(Fixture("quiz_history_golden.json")).Replace("\"ProfileText\"", "\"Profile\"");
        Assert.NotEqual(Encoding.UTF8.GetBytes(broken), HistoryRoundTrip(broken));

        File.WriteAllText(HistoryPath, "[{ not json");
        Assert.Empty(QuizStore.LoadHistory());
    });

    [Fact]
    public void CustomCategoriesRoundTripByteIdentical()
    {
        var golden = File.ReadAllBytes(Fixture("custom_quiz_categories_golden.json"));
        File.WriteAllBytes(CategoriesPath, golden);

        QuizStore.SaveCustomCategory(QuizStore.LoadCustomCategories()[0]); // replace in place
        Assert.Equal(golden, File.ReadAllBytes(CategoriesPath));
        QuizStore.DeleteCustomCategory("no_such_id");
        Assert.Equal(golden, File.ReadAllBytes(CategoriesPath));

        Assert.Equal(new[] { "sissy", "bambi", "obedience", "mindlessness", "submission", "custom_velvet" },
            QuizStore.GetAllCategories().Select(c => c.Id));
        Assert.Equal("custom_velvet", QuizStore.FindCategory("VELVET \u00C9DITION")!.Id);

        QuizStore.DeleteCustomCategory("custom_velvet");
        Assert.Empty(QuizStore.LoadCustomCategories());
    }

    [Fact]
    public void BrokenCategoriesFixtureFailsRoundTrip()
    {
        var broken = File.ReadAllText(Fixture("custom_quiz_categories_golden.json")).Replace("\"Color\"", "\"Colour\"");
        File.WriteAllText(CategoriesPath, broken);
        QuizStore.SaveCustomCategory(QuizStore.LoadCustomCategories()[0]);
        Assert.NotEqual(broken, File.ReadAllText(CategoriesPath));
    }

    /// <summary>Same inputs, same lines, as the pre-move generator.</summary>
    [Fact]
    public void ScoringTrendsAndFallbacksMatchPreMoveGolden() => QuestPersistenceTests.InBerlin(() =>
    {
        var hist = JsonConvert.DeserializeObject<List<QuizHistoryEntry>>(File.ReadAllText(Fixture("quiz_history_golden.json")))!;
        var cats = JsonConvert.DeserializeObject<List<QuizCategoryDefinition>>(File.ReadAllText(Fixture("custom_quiz_categories_golden.json")))!;
        var t0 = hist[0].TakenAt;
        var sb = new StringBuilder();
        var more = new List<QuizHistoryEntry>(hist)
        {
            new() { TakenAt = t0.AddDays(-1), Category = QuizCategory.Obedience, TotalScore = 35, MaxScore = 40, CategoryId = "obedience" },
            new() { TakenAt = t0.AddDays(1), Category = QuizCategory.Bambi, TotalScore = 7, MaxScore = 0, CategoryId = "bambi" },
            new() { TakenAt = t0.AddDays(2), Category = QuizCategory.Bambi, TotalScore = 5, MaxScore = 40, CategoryId = "bambi" },
        };
        foreach (var h in more) sb.AppendLine($"key {QuizStore.TrendKey(h)} name {QuizStore.DisplayName(h)}");
        foreach (var id in new[] { "obedience", "OBEDIENCE", "custom_velvet", "bambi", "Obedience", "nope" })
        {
            var t = QuizStore.GetScoreTrend(more, id);
            sb.AppendLine(t == null ? $"trend {id} null" : $"trend {id} {t.LatestPercent} {t.PreviousPercent} {t.AveragePercent} {t.QuizCount} {t.Direction} {t.DeltaPercent}");
        }
        var builtIns = QuizStore.GetBuiltInCategories();
        foreach (var c in builtIns)
        {
            sb.AppendLine($"cat {c.Id}|{c.Name}|{c.Description}|{c.Color}|{c.IsBuiltIn}|{c.EnumCategory}|{string.Join(",", c.Archetypes.Select(x => $"{x.Name}:{x.MinPercentage}-{x.MaxPercentage}:{x.Description}"))}");
            foreach (var pct in new[] { 0, 25.5, 26, 70.9, 71, 85, 86, 120 }) sb.AppendLine($"arch {c.Id} {pct} {c.GetArchetypeName(pct)}");
            foreach (var s in new[] { 10, 11, 28, 35 }) sb.AppendLine($"fbp {c.Id} {s} {c.GetFallbackProfile(s, 40)}");
        }
        sb.AppendLine($"arch empty {new QuizCategoryDefinition().GetArchetypeName(50)}");
        sb.AppendLine($"fbp empty {new QuizCategoryDefinition().GetFallbackProfile(3, 0)}");
        foreach (var cat in Enum.GetValues<QuizCategory>().Append((QuizCategory)99))
        {
            foreach (var def in new[] { null, cats[0] })
                foreach (var s in new[] { 0, 10, 11, 20, 21, 28, 34, 35, 40 })
                    sb.AppendLine($"profile {cat} {def?.Id ?? "-"} {s} {QuizStore.GetFallbackProfile(cat, def, s, 40)}");
            for (int n = 1; n <= 11; n++)
            {
                var q = QuizStore.GetFallbackQuestion(cat, n);
                sb.AppendLine($"q {cat} {n} {q.Number}|{q.QuestionText}|{string.Join("/", q.Answers)}|{string.Join(",", q.Points)}");
            }
        }
        Assert.Equal(File.ReadAllText(Fixture("quiz_scoring_golden_premove.txt")), sb.ToString());
    });
}
