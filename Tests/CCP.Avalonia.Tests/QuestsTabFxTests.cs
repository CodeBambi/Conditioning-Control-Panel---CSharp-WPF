using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Quests tab's motion and completion effects on a stepped clock: season title shimmer
/// (WPF MainWindow.Animations.cs:35), panel particle drift (Theme/MainWindow.xaml AnimatedGradientPanel storyboard),
/// the quest-complete burst (MainWindow.EventFx.cs:362) and the QuestComplete haptic post (App.xaml.cs:422).</summary>
public sealed class QuestsTabFxTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void ShimmerAndParticlesRunOnlyWhileShown() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldTime = QuestsTabView.Time;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new SteppedClock();
        QuestsTabView.Time = clock;
        Window? w = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var view = new QuestsTabView();
            w = new Window { Width = 1200, Height = 900, Content = view };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.AmbientRunning);

            var title = view.FindControl<TextBlock>("TxtSeasonTitle")!;
            var brush = Assert.IsType<LinearGradientBrush>(title.Foreground);
            var glow = Assert.IsType<DropShadowEffect>(title.Effect);
            var particle = view.GetVisualDescendants().OfType<Canvas>().First(c => c.Classes.Contains("qparticles"))
                .Children.OfType<Ellipse>().First();
            var drift = Assert.IsType<TranslateTransform>(particle.RenderTransform);

            void At(double seconds) { clock.Now += TimeSpan.FromSeconds(seconds).Ticks; view.StepAmbient(); }

            At(2);                                              // particle 1: Y 0->-60 over 8 s, X 0->15 over 6 s
            Assert.Equal(-15, drift.Y, 3);
            Assert.Equal(5, drift.X, 3);
            Assert.Equal(-1 + 4.0 / 3, brush.StartPoint.Point.X, 3);  // 2/3 through the 3 s sweep
            Assert.Equal(4.0 / 3, brush.EndPoint.Point.X, 3);
            Assert.Equal(0.7, glow.Opacity, 3);                 // 0.3->0.9 over 1.5 s, reversing: 0.5 s back down

            At(8);                                              // t=10: Y on its way back (8 s up, 8 s down)
            Assert.Equal(-45, drift.Y, 3);
            At(7);                                              // t=17: leg over, held at 0 until the 24 s repeat
            Assert.Equal(0, drift.Y, 3);
            Assert.Equal(1, view.ParticleScans);                // the tree is walked once, not every tick (P07)

            // The roadmap's wide panel is templated only when its sub-tab opens; its particles join then.
            view.FindControl<Button>("BtnQuestSubRoadmap")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var wide = view.GetVisualDescendants().OfType<Canvas>().Single(c => Equals(c.Tag, "wide"))
                .Children.OfType<Ellipse>().First();
            At(4);                                              // t=21: wide particle 1 X (15 over 6 s) leg over -> 0; Y (-40 over 8 s) too
            At(4);                                              // t=25 -> 1 s into the repeat: Y = -5
            Assert.Equal(-5, Assert.IsType<TranslateTransform>(wide.RenderTransform).Y, 3);
            Assert.Equal(2, view.ParticleScans);
            view.FindControl<Button>("BtnQuestSubDaily")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Hidden: the clock stops and everything returns to the static look (P01).
            view.IsVisible = false;
            Assert.False(view.AmbientRunning);
            Assert.Equal(0, drift.X);
            Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), brush.StartPoint);
            Assert.Equal(0.6, glow.Opacity, 3);

            // Reduced motion: WPF's shimmer refuses (MotionFx.AllowAmbientLoops); the panel storyboard does not.
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            view.IsVisible = true;
            Assert.True(view.AmbientRunning);
            At(2);
            Assert.Equal(-15, drift.Y, 3);
            Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), brush.StartPoint);
            Assert.Equal(0.6, glow.Opacity, 3);
        }
        finally
        {
            try { w?.Close(); } catch { }
            QuestsTabView.Time = oldTime;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public void CompletionBurstsOnTheBarAndPostsTheHaptic() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-quests-fx-").FullName;
        var oldQuests = AvApp.Quests;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldEffects = CoreQuests.PlayCompletionEffectsProvider;
        var oldHaptics = CoreHaptics.Service;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var hs = new HapticSettings { Enabled = true };
        hs.EnsureV2Migrated();
        hs.V2.Provider("lovense").Enabled = false;
        hs.V2.Provider("mock").Enabled = true;
        var haptics = new HapticService(hs);
        var posted = new List<string>();
        haptics.HapticTriggered += (_, label) => posted.Add(label);
        QuestService? quests = null;
        Window? w = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreHaptics.Service = haptics;
            CoreQuests.PlayCompletionEffectsProvider = AvApp.PlayQuestCompletionEffects;   // what StartQuests seeds
            quests = new QuestService(null, dir);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            AvApp.Quests = quests;

            var view = new QuestsTabView();
            w = new Window { Width = 1200, Height = 900, Content = view };
            w.Show();
            Dispatcher.UIThread.RunJobs();

            var pop = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
            quests.TrackBubblesPopped(pop.TargetValue);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, view.Bursts);
            Assert.Single(posted, p => p.StartsWith("QuestComplete", StringComparison.Ordinal));

            // Off-tab: no burst on a hidden bar (WPF moves it to the nav button; not on this head yet).
            view.IsVisible = false;
            quests.TrackSpiralMinutes(10_000);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, quests.GetDailyQuestsCompletedToday());
            Assert.Equal(1, view.Bursts);
        }
        finally
        {
            try { w?.Close(); } catch { }
            quests?.Dispose();
            haptics.Dispose();
            CoreHaptics.Service = oldHaptics;
            CoreQuests.PlayCompletionEffectsProvider = oldEffects;
            CoreSettings.ServiceProvider = oldSettings;
            AvApp.Quests = oldQuests;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    });
}
