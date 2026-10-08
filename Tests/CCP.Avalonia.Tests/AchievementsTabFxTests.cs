using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Achievement tile FX (WPF TabFxPresetsQuestsAchievements.cs:692-834, EventFx.cs:276-344) on a stepped
/// clock, and the Season Recap re-view (WPF MainWindow.AchievementsTab.cs:115-151) from the shell's nav button.</summary>
public sealed class AchievementsTabFxTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task StaggerTiltRevealAndBurstFollowTheUserPath() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var clock = new SteppedClock();
        AchievementsTabView.Time = clock;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var dir = Directory.CreateTempSubdirectory("ccp-ach-fx-").FullName;
        Window? w = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            var view = new AchievementsTabView(engine);
            w = new Window { Width = 1200, Height = 900, Content = view };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            void Frames(int ms)
            {
                for (int t = 0; t < ms; t += 16)
                {
                    clock.Now += TimeSpan.FromMilliseconds(16).Ticks;
                    view.StepFx();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            // Entrance stagger on show: the free grid fades in from a 10 px rise, slot 6 onwards together.
            view.IsVisible = false;
            view.IsVisible = true;
            var free = view.FindControl<WrapPanel>("AchievementGrid")!.Children.OfType<Control>().ToArray();
            Assert.Equal(0, free[0].Opacity);
            Assert.Equal(10, Slide(free[0]).Y);
            Assert.True(view.FxRunning);
            Frames(100);
            Assert.InRange(free[0].Opacity, 0.1, 0.99);
            Assert.True(free[0].Opacity > free[3].Opacity, "tiles must stagger, not land together");
            Frames(160);                                         // past slot 6's 240 ms start
            Assert.True(free[6].Opacity > 0, "slot 6 has started");
            Assert.True(free[5].Opacity > free[6].Opacity, "slots 0-6 are staggered");
            Assert.Equal(free[6].Opacity, free[20].Opacity);   // capped at 6 slots: the rest ride slot 6's clock
            Frames(600);
            Assert.All(free, c => Assert.Equal(1, c.Opacity));
            Assert.False(view.FxRunning);                       // the clock stops when nothing moves

            // Hiding mid-stagger lands every tile and stops the clock (P01).
            view.IsVisible = false; view.IsVisible = true;
            Frames(32);
            view.IsVisible = false;
            Assert.False(view.FxRunning);
            Assert.All(free, c => Assert.Equal(1, c.Opacity));
            view.IsVisible = true;
            Frames(800);

            // Unlock while the grid is on screen: blur dissolves, tile settles from 1.08, sparks burst on it.
            var earned = view.Cards.Single(c => (string)c.Tag! == "plastic_initiation");
            var badge = earned.GetVisualDescendants().OfType<Image>().First();
            engine.CheckLevelAchievements(10);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, view.Bursts);
            Assert.Equal(1.08, Scale(earned).ScaleX, 3);
            Assert.Equal(15, Assert.IsType<BlurEffect>(badge.Effect).Radius, 3);
            Frames(160);
            Assert.InRange(((BlurEffect)badge.Effect!).Radius, 0.1, 14.9);
            Frames(300);
            Assert.Equal(1, Scale(earned).ScaleX, 3);
            Assert.Null(badge.Effect);                          // the 0-radius effect is dropped
            Assert.False(view.FxRunning);

            // Holo tilt: an unlocked tile tilts toward the side the pointer entered and lifts; leave settles it.
            var host = (Control)badge.GetVisualParent()!;
            var entry = earned.TranslatePoint(new Point(10, 60), w)!.Value;
            Assert.InRange(entry.Y, 0, 900);
            w.MouseMove(entry, RawInputModifiers.None);
            Frames(200);
            Assert.Equal(-0.8, Rotate(host).Angle, 3);
            Assert.Equal(1.02, ((ScaleTransform)((TransformGroup)host.RenderTransform!).Children[0]).ScaleX, 3);
            w.MouseMove(new Point(1195, 895), RawInputModifiers.None);
            Frames(200);
            Assert.Equal(0, Rotate(host).Angle, 3);

            // A locked tile offers nothing.
            var locked = view.Cards.Single(c => (string)c.Tag! == "dumb_bimbo");
            var lockedHost = (Control)locked.GetVisualDescendants().OfType<Image>().First().GetVisualParent()!;
            var p = locked.TranslatePoint(new Point(10, 60), w)!.Value;
            w.MouseMove(p, RawInputModifiers.None);
            Frames(200);
            Assert.Equal(0, Rotate(lockedHost).Angle, 3);
        }
        finally
        {
            w?.Close();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            AchievementsTabView.Time = TimeProvider.System;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MotionOffHiddenTabAndInactiveWindowRefuseTheCelebration() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var clock = new SteppedClock();
        AchievementsTabView.Time = clock;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var dir = Directory.CreateTempSubdirectory("ccp-ach-fx-off-").FullName;
        Window? w = null;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            var view = new AchievementsTabView(engine);
            w = new Window { Width = 1200, Height = 900, Content = view };
            w.Show();
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            ToggleButton Card(string id) => view.Cards.Single(c => (string)c.Tag! == id);
            Image Badge(Control c) => c.GetVisualDescendants().OfType<Image>().First();

            // MotionLevel Off: no stagger, no reveal tween, no sparks - everything lands at once.
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            view.IsVisible = false; view.IsVisible = true;
            Assert.False(view.FxRunning);
            Assert.All(view.Cards, c => Assert.Equal(1, c.Opacity));
            engine.CheckLevelAchievements(10);
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FxRunning);
            Assert.Equal(0, view.Bursts);
            Assert.Equal(1, Scale(Card("plastic_initiation")).ScaleX, 3);
            Assert.Null(Badge(Card("plastic_initiation")).Effect);

            // Unlock while the tab is hidden: no reveal and no burst on a tab nobody sees.
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            view.IsVisible = false;
            engine.CheckLevelAchievements(20);
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FxRunning);
            Assert.Equal(0, view.Bursts);
            Assert.Equal(1, Scale(Card("dumb_bimbo")).ScaleX, 3);
            view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            // Window minimised (WPF EventFxAllowed): the tile still reveals, but no sparks. The headless platform
            // never deactivates a shown window, so the IsActive arm of the same gate is asserted, not driven.
            w.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            engine.CheckLevelAchievements(50);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, view.Bursts);
            Assert.True(view.FxRunning);   // the reveal settle

            // Restored: the next unlock bursts (proves the refusals above are the gate, not a broken layer).
            w.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.IsActive);
            engine.CheckLevelAchievements(75);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, view.Bursts);
        }
        finally
        {
            w?.Close();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            AchievementsTabView.Time = TimeProvider.System;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LeaderboardNavShowsSeasonRecapReviewOnlyWithASnapshot() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var recaps = Path.Combine(CorePaths.UserData, "season-recaps");
        MainShellWindow? shell = null;
        try
        {
            if (Directory.Exists(recaps)) Directory.Delete(recaps, true);
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var nav = shell.Named<Button>("BtnLeaderboard")!;
            var review = shell.Named<LeaderboardTabView>("LeaderboardTab")!.FindControl<Button>("BtnViewSeasonRecap")!;

            nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(review.IsVisible);                      // nothing to re-view

            SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-08" });
            nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(review.IsVisible);

            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(shell.LastSeasonRecap);
            Assert.True(shell.LastSeasonRecap!.IsVisible);
            shell.LastSeasonRecap.Close();
        }
        finally
        {
            shell?.Close();
            if (Directory.Exists(recaps)) Directory.Delete(recaps, true);
        }
        return Task.CompletedTask;
    });

    private static TransformGroup Group(Control c) => (TransformGroup)c.RenderTransform!;
    private static ScaleTransform Scale(Control c) => Group(c).Children.OfType<ScaleTransform>().First();
    private static TranslateTransform Slide(Control c) => Group(c).Children.OfType<TranslateTransform>().First();
    private static RotateTransform Rotate(Control c) => Group(c).Children.OfType<RotateTransform>().First();

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
