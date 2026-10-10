using System;
using System.IO;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// k25: Alt+Tab while the engine runs marks the session (WPF MainWindow.xaml.cs:913), driven here
/// without a hook; and Start / Stop hand Core its UI post and take it back.
/// </summary>
[Collection(RunsAloneCollection.Name)]   // swaps AchievementEngine.Current, its UI post and the running probe
public sealed class AchievementAltTabTests
{
    private const int Tab = 0x09, LeftAlt = 0xA4, RightAlt = 0xA5, Shift = 0x10;

    [Fact]
    public Task AltTab_CountsOnlyWithAltHeld_AndOnlyWhileTheEngineRuns() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        await Task.CompletedTask;
        var dir = Directory.CreateTempSubdirectory("ccp-ach-alt-").FullName;
        var (before, post, running) = (AchievementEngine.Current, AchievementEngine.UiPost, AchievementAutosave.EngineRunning);
        try
        {
            var e = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            AchievementEngine.UiPost = a => a();
            AchievementEngine.Attach(e);
            var on = false;
            AchievementAutosave.EngineRunning = () => on;

            AchievementAutosave.OnKeyDown(Tab);                       // a bare Tab
            AchievementAutosave.OnKeyDown(Shift); AchievementAutosave.OnKeyDown(Tab);
            Assert.False(e.Progress.AltTabPressedThisSession);

            AchievementAutosave.OnKeyDown(LeftAlt);
            AchievementAutosave.OnKeyDown(Tab);                       // engine idle: not a session
            Assert.False(e.Progress.AltTabPressedThisSession);

            AchievementAutosave.OnKeyUp(LeftAlt);
            on = true;
            AchievementAutosave.OnKeyDown(Tab);                       // Alt released
            Assert.False(e.Progress.AltTabPressedThisSession);

            AchievementAutosave.OnKeyDown(RightAlt);
            AchievementAutosave.OnKeyDown(Tab);
            Assert.True(e.Progress.AltTabPressedThisSession);
            AchievementAutosave.OnKeyUp(RightAlt);
        }
        finally
        {
            AchievementAutosave.EngineRunning = running;
            AchievementEngine.Attach(before);
            AchievementEngine.UiPost = post;
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public Task StartGivesCoreAUiPost_StopGivesTheOldOneBack() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        await Task.CompletedTask;
        var dir = Directory.CreateTempSubdirectory("ccp-ach-alt-").FullName;
        var (before, post) = (AchievementEngine.Current, AchievementEngine.UiPost);
        try
        {
            var e = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            AchievementAutosave.Start(e);
            Assert.NotSame(post, AchievementEngine.UiPost);
            var ran = false;
            AchievementEngine.OnCurrent(_ => ran = true, "test");     // on the UI thread: runs inline
            Assert.True(ran);
            AchievementAutosave.Stop();
            Assert.Same(post, AchievementEngine.UiPost);
            Assert.Null(AchievementEngine.Current);
        }
        finally
        {
            AchievementAutosave.Stop();
            AchievementEngine.Attach(before);
            AchievementEngine.UiPost = post;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
