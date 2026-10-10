using System;
using System.IO;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The head half of the lifetime counters: the 30 s tick writes achievements.json only when a
/// counter moved, the overlay minute tracker tells the engine when the overlay hides, and Start / Stop
/// hand the running engine to Core and take it back.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps AchievementEngine.Current
public sealed class AchievementAutosaveTests
{
    private static AchievementEngine Engine(string dir) => new(new AchievementStore(Path.Combine(dir, "achievements.json")));

    [Fact]
    public Task TheTickSavesOnlyADirtyEngine() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-ach-auto-").FullName;
        try
        {
            var e = Engine(dir);
            var file = Path.Combine(dir, "achievements.json");
            AchievementAutosave.Tick(e);
            await Task.Delay(150);
            Assert.False(File.Exists(file));                 // an idle tick never writes the file

            e.TrackSkillPointsEarned(2);
            Assert.True(e.IsDirty);
            await e.SaveIfDirtyAsync();
            Assert.False(e.IsDirty);
            Assert.Equal(2, new AchievementStore(file).Load().TotalSkillPointsEarned);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    });

    [Fact]
    public Task StartHandsTheEngineToCore_StopTakesItBack() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        await Task.CompletedTask;
        var dir = Directory.CreateTempSubdirectory("ccp-ach-auto-").FullName;
        var before = AchievementEngine.Current;
        try
        {
            var e = Engine(dir);
            AchievementAutosave.Start(e);
            Assert.Same(e, AchievementEngine.Current);
            Assert.Equal(TimeSpan.FromSeconds(30), AchievementAutosave.Interval);   // WPF's autosave, never every second
            AchievementAutosave.Stop();
            Assert.Null(AchievementEngine.Current);
        }
        finally
        {
            AchievementAutosave.Stop();
            AchievementEngine.Attach(before);
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public Task AnOverlayThatHidesTellsItsListener() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        await Task.CompletedTask;
        double minutes = 0; int hidden = 0;
        var now = TimeSpan.Zero;
        var q = new OverlayQuestMinutes(m => minutes += m) { Clock = () => now, Hidden = () => hidden++ };
        q.Follow(true);
        now = TimeSpan.FromSeconds(3);
        q.Sample(true);
        Assert.Equal(0.05, minutes, 3);
        Assert.Equal(0, hidden);
        q.Follow(false);
        Assert.Equal(1, hidden);
        Assert.False(q.TickRunning);
    });
}
