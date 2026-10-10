using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF 7.1.5 MainWindow.QuestStamps.cs on the port: four plates from live quest state, a slot
/// flipping to done pops once and its gold flash removes itself, hover swells a plate and relaxes
/// its tilt and opens the card, leaving puts both back, and nothing wears an Effect.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class HeaderQuestStampsTests
{
    [Fact]
    public Task StampsFollowQuests_PopOnce_ZoomOnHover_AndCleanUp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-j1-stamps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var oldQuests = AvApp.Quests;
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        QuestService? quests = null;
        MainShellWindow? w = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            quests = new QuestService(null, dir);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            AvApp.Quests = quests;

            w = new MainShellWindow { QuestStampMotionOverride = true };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.InitializeQuestStamps();
            var host = w.Named<Grid>("QuestStampHost")!;
            Assert.True(host.IsVisible);
            Assert.Equal(4, w.QuestStampCount);
            Assert.Equal(MainShellWindow.QuestStampWeeklySize, w.QuestStampCell("w")!.Width);
            Assert.Equal((1.0, MainShellWindow.QuestStampAngles[0]), w.QuestStampPose("d0"));
            Assert.Equal(0, w.QuestStampPops);                       // first paint never pops

            // A slot flips to done: one pop, one flash, and the flash removes itself.
            var pop = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
            quests.TrackBubblesPopped(pop.TargetValue);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, w.QuestStampPops);
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<Visual>().Append(host), v => v.Effect != null);
            for (int i = 0; i < 80 && w.QuestStampFlashesLive > 0; i++) { await Task.Delay(16); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(0, w.QuestStampFlashesLive);
            w.RefreshQuestStamps();                                   // a later repaint does not pop again
            Assert.Equal(1, w.QuestStampPops);

            // Hover: the IN and the OUT (snapped, motion off for the pose asserts).
            w.QuestStampMotionOverride = false;
            w.HoverQuestStampForTests("d1", true);
            Assert.Equal("d1", w.HoveredQuestStamp);
            Assert.Equal((MainShellWindow.QuestStampHoverScale, MainShellWindow.QuestStampAngles[1] * 0.25), w.QuestStampPose("d1"));
            Assert.True(w.QuestStampCardPainted);
            Assert.False(string.IsNullOrEmpty(w.QuestStampCardName));
            Assert.InRange(w.QuestStampCardOpacity, 0, 1);
            w.HoverQuestStampForTests("d1", false);
            Assert.Null(w.HoveredQuestStamp);
            Assert.Equal((1.0, MainShellWindow.QuestStampAngles[1]), w.QuestStampPose("d1"));

            // The card leads with the quest's own art (the picture the Quests tab shows), and a finished
            // quest wears the check over it.
            var rush = QuestDefinition.DailyQuests.Find(d => d.Id == "flash_rush_d")!;
            w.HoverQuestStampForTests("d1", true);
            Assert.NotNull(w.QuestStampCardArt);
            Assert.Same(ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView.GetQuestArt(rush), w.QuestStampCardArt!.Source);
            Assert.False(w.QuestStampCardDoneShown);
            w.HoverQuestStampForTests("d1", false);
            w.HoverQuestStampForTests("d0", true);
            Assert.True(w.QuestStampCardDoneShown);

            // A mod switch: the decoded art is dropped, the card closes, the stamps repaint (no pop).
            w.DropArtCache();
            Assert.Equal(1, w.QuestStampArtDrops);
            Assert.Null(w.HoveredQuestStamp);
            Assert.Equal(4, w.QuestStampCount);
            Assert.Equal(1, w.QuestStampPops);
            w.DropArtCache();                                         // the shell's re-skin may call it too
            Assert.Equal(2, w.QuestStampArtDrops);

            // No service: the cluster collapses and hands its room back.
            AvApp.Quests = null;
            w.InitializeQuestStamps();
            Assert.False(host.IsVisible);
            Assert.Equal(0, w.QuestStampCount);
        }
        finally
        {
            try { w?.Close(); } catch { }
            quests?.Dispose();
            CoreSettings.ServiceProvider = oldSettings;
            AvApp.Quests = oldQuests;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    });
}
