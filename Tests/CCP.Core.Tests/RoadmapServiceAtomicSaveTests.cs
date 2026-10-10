using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// roadmap.json safety: temp-then-rename saves, and an unreadable file is kept as
/// roadmap.json.corrupt-* instead of being replaced by defaults on the next save.
/// Uses the internal (progressPath, diaryPath) ctor so nothing touches CorePaths.UserData.
/// </summary>
public sealed class RoadmapServiceAtomicSaveTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-roadmap-atomic-").FullName;
    private string ProgressPath => Path.Combine(_dir, "roadmap.json");
    private string DiaryPath => Path.Combine(_dir, "roadmap_diary");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void SaveLeavesNoTempFileAndWritesTheSameBytesAsBefore()
    {
        using var roadmap = new RoadmapService(ProgressPath, DiaryPath);
        roadmap.StartStep("t1_step1");
        roadmap.Save();

        Assert.False(File.Exists(ProgressPath + ".tmp"));
        // The pre-change Save was File.WriteAllText(path, Serialize(Progress, WriteIndented)).
        var expected = JsonSerializer.Serialize(roadmap.Progress, new JsonSerializerOptions { WriteIndented = true });
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(ProgressPath));
    }

    [Fact]
    public void CorruptFileIsPreservedAndNotOverwrittenByNextSave()
    {
        const string garbage = "{ \"Track1Unlocked\": tru";
        File.WriteAllText(ProgressPath, garbage);

        using (var roadmap = new RoadmapService(ProgressPath, DiaryPath))
        {
            roadmap.StartStep("t1_step1");
            roadmap.Save();
        }

        var backups = Directory.GetFiles(_dir, "roadmap.json.corrupt-*");
        Assert.Single(backups);
        Assert.Equal(garbage, File.ReadAllText(backups[0]));
        // The live file is the fresh defaults, now valid JSON.
        Assert.NotNull(JsonSerializer.Deserialize<RoadmapProgress>(File.ReadAllText(ProgressPath)));
    }

    [Fact]
    public void ValidFileRoundTripsUnchanged()
    {
        using (var first = new RoadmapService(ProgressPath, DiaryPath))
        {
            first.StartStep("t1_step1");
            first.Save();
        }
        var saved = File.ReadAllBytes(ProgressPath);

        using (var second = new RoadmapService(ProgressPath, DiaryPath))
        {
            second.Save();
        }

        Assert.Equal(saved, File.ReadAllBytes(ProgressPath));
        Assert.Empty(Directory.GetFiles(_dir, "roadmap.json.corrupt-*"));
    }
}
