using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Fix wave, 9 Oct 2026 (shell lane): the header wobble and the marquee drum no longer run
/// Animation.RunAsync on a Transform (it throws, and from a timer tick it took the app down), the
/// bottom bar Save writes, and the Home tiles and bubbles that did nothing now navigate.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ShellFixWaveTests
{
    private static MainShellWindow Open()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var w = new MainShellWindow();
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static SettingsTabView Home(MainShellWindow w) => w.Named<SettingsTabView>("SettingsTab")!;

    [Fact]
    public Task TheInviteTicketWobblesWithoutThrowing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var w = Open();
        try
        {
            var ticket = w.Named<Button>("BtnInviteTicket")!;
            ticket.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            w.WobbleInviteTicketForTest();
            Dispatcher.UIThread.RunJobs();

            if (AmbientFxCanvas.Env.AllowTransitions)
            {
                Assert.NotNull(w.InviteWobbleRuns.Tilt);
                Assert.NotNull(w.InviteWobbleRuns.Pop);
            }
            w.InviteWobbleRuns.Tilt?.Stop();
            w.InviteWobbleRuns.Pop?.Stop();
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheMarqueeDrumRollsWithoutThrowing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var w = Open();
        try
        {
            var roll = typeof(MainShellWindow).GetMethod("RollBannerDrum", BindingFlags.NonPublic | BindingFlags.Static)!;
            var outgoing = new TextBlock { Text = "a" };
            var incoming = new TextBlock { Text = "b" };
            bool rolled = (bool)roll.Invoke(null, new object[] { outgoing, incoming })!;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ConditioningControlPanel.Fx.BannerFxRules.Roll(AmbientFxCanvas.Env.Level), rolled);
            // Both faces carry the scale + slide pair the roll writes.
            Assert.IsType<TransformGroup>(outgoing.RenderTransform);
            Assert.IsType<TransformGroup>(incoming.RenderTransform);

            // The sampled tween itself: a delayed run on a bare transform lands on its rest value.
            var slide = new TranslateTransform();
            var run = MainShellWindow.SampledTween(slide, TranslateTransform.YProperty, 1, u => 10 * u, rest: 0);
            Assert.NotNull(run);
            Assert.Equal(0, slide.Y);   // u = 0 applied at once
            run!.Stop();
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SaveFlushesSettingsAndRunsItsTick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var w = Open();
        try
        {
            int before = w.SaveClicks, fx = w.SaveAbsorbRuns;
            var save = w.Named<Button>("BtnSaveAll")!;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before + 1, w.SaveClicks);
            if (AmbientFxCanvas.Env.AllowTransitions) Assert.Equal(fx + 1, w.SaveAbsorbRuns);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheMysteryTileOpensTodaysFreeFeature() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = CoreEntitlement.IsFreeTodayProvider;
        var w = Open();
        try
        {
            CoreEntitlement.IsFreeTodayProvider = k => k == "awareness";
            Assert.Equal("awareness", SettingsTabView.TodayFreeKey());
            Home(w).CardMystery_Click(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("awareness", w.CurrentTab);

            CoreEntitlement.IsFreeTodayProvider = k => k == "remote";
            Home(w).CardMystery_Click(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("remotecontrol", w.CurrentTab);

            Assert.Equal("play", SettingsTabView.MysteryTabFor("dtrh"));
            Assert.Null(SettingsTabView.MysteryTabFor(null));
        }
        finally { CoreEntitlement.IsFreeTodayProvider = old; w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheHomeQuickBubblesOpenTheirPages() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var w = Open();
        try
        {
            var home = Home(w);
            w.ShowTab("quests");
            Dispatcher.UIThread.RunJobs();
            home.VelvetBtnWebcam_Click(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("appsettings", w.CurrentTab);

            w.ShowTab("quests");
            Dispatcher.UIThread.RunJobs();
            home.VelvetBtnSystem_Click(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("appsettings", w.CurrentTab);

            home.VelvetBtnAppInfo_Click(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            var popup = home.AppInfoPopup;
            Assert.NotNull(popup);
            popup!.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(home.AppInfoPopup);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StatPillsShowOnlyWithTheirSkill() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        bool hadTime = s.UnlockedSkills.Contains("pink_hours"), hadFire = s.UnlockedSkills.Contains("good_girl_streak");
        var w = Open();
        try
        {
            s.UnlockedSkills.Remove("pink_hours");
            s.UnlockedSkills.Remove("good_girl_streak");
            w.UpdateStatPills();
            Assert.False(w.Named<Border>("PillConditioningTime")!.IsVisible);
            Assert.False(w.Named<Border>("StreakFirePill")!.IsVisible);

            s.UnlockedSkills.Add("pink_hours");
            s.UnlockedSkills.Add("good_girl_streak");
            w.UpdateStatPills();
            Assert.True(w.Named<Border>("PillConditioningTime")!.IsVisible);
            Assert.True(w.Named<Border>("StreakFirePill")!.IsVisible);
            Assert.EndsWith("s", w.Named<TextBlock>("TxtPillConditioningTime")!.Text);
        }
        finally
        {
            if (!hadTime) s.UnlockedSkills.Remove("pink_hours");
            if (!hadFire) s.UnlockedSkills.Remove("good_girl_streak");
            w.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WhatsNewShowsOnceForAChangedVersionAndStampsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var oldSeen = s.LastSeenVersion;
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var oldNotes = CoreReleaseContent.PatchNotesProvider;
        var w = Open();
        try
        {
            CoreReleaseContent.AppVersionProvider = () => "9.9.9";
            CoreReleaseContent.PatchNotesProvider = () => "notes";
            int shown = 0;
            w.WhatsNewPresenter = (_, _) => { shown++; return Task.CompletedTask; };

            s.LastSeenVersion = "";          // fresh install: stamped, told nothing
            await w.ShowWhatsNewIfNeededAsync();
            Assert.Equal(0, shown);
            Assert.Equal("9.9.9", s.LastSeenVersion);

            s.LastSeenVersion = "9.9.8";     // an update: shown once, then stamped
            await w.ShowWhatsNewIfNeededAsync();
            await w.ShowWhatsNewIfNeededAsync();
            Assert.Equal(1, shown);
            Assert.Equal("9.9.9", s.LastSeenVersion);
        }
        finally
        {
            s.LastSeenVersion = oldSeen;
            CoreReleaseContent.AppVersionProvider = oldVersion;
            CoreReleaseContent.PatchNotesProvider = oldNotes;
            w.Close();
        }
    });

    [Fact]
    public Task TheModPickerEndsWithTheManagerRow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        MainShellWindow.SuppressModManagerForTest = true;
        var w = Open();
        try
        {
            w.InitializeModSelector();
            if (global::ConditioningControlPanel.Avalonia.App.Mods is null || w.AvailableMods.Count == 0) return Task.CompletedTask;   // no mod service in this run
            var last = w.AvailableMods[^1];
            Assert.Equal(MainShellWindow.ModManagerEntryId, last.Id);

            var combo = w.Named<ComboBox>("ModSelectorCombo")!;
            int before = w.ModManagerRowPicks;
            combo.SelectedItem = last;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, w.ModManagerRowPicks);
            Assert.NotSame(last, combo.SelectedItem);   // the chip went back to a real mod
        }
        finally { MainShellWindow.SuppressModManagerForTest = false; w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RichPresenceRefusesWithoutALinkedDiscord() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        if (s.HasLinkedDiscord) return Task.CompletedTask;   // this profile is linked: nothing to refuse
        bool old = s.DiscordRichPresenceEnabled;
        SettingsTabView.SuppressDialogsForTest = true;
        var w = Open();
        try
        {
            s.DiscordRichPresenceEnabled = false;
            var box = Home(w).FindControl<CheckBox>("ChkQuickDiscordRichPresence")!;
            Home(w).SyncQuickRichPresence();
            box.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(box.IsChecked == true);
            Assert.False(s.DiscordRichPresenceEnabled);
        }
        finally { s.DiscordRichPresenceEnabled = old; SettingsTabView.SuppressDialogsForTest = false; w.Close(); }
        return Task.CompletedTask;
    });
}
