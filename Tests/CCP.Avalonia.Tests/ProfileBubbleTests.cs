using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The header profile bubble and its menu paint the account truth (WPF MainWindow.ProfileBubble.cs
/// RefreshProfileBubble / RefreshProfileMenu / ProfileMenuAccount_Click / OpenPublicProfilePage), and the Profile
/// tab's FX follow WPF MainWindow.ProfileFx.cs (OG border loop gate, search focus glow).</summary>
public sealed class ProfileBubbleTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task BubbleAndMenuPaintAccountAndDoorsAct() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider,
            CoreAccount.HasLabAccessProvider, CoreAccount.HasPremiumAccessProvider, MainShellWindow.OpenProfileLink);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.ShareProfilePicture = false;
        bool signedIn = true;
        CoreAccount.IsLoggedInProvider = () => signedIn;
        CoreAccount.DisplayNameProvider = () => "Bambi Doll";
        CoreAccount.HasLabAccessProvider = () => signedIn;
        CoreAccount.HasPremiumAccessProvider = () => signedIn;
        string? opened = null;
        MainShellWindow.OpenProfileLink = url => { opened = url; return true; };
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.UpdateQuickLoginUI();   // the sign-in choke point repaints the bubble
            Dispatcher.UIThread.RunJobs();
            T N<T>(string name) where T : Control => shell.FindControl<T>(name)!;

            Assert.Equal(LeaderboardEntryData.BuildInitials("Bambi Doll"), N<TextBlock>("ProfileBubbleInitials").Text);
            Assert.True(N<Image>("ProfileBubbleTierBadge").IsVisible);
            Assert.NotNull(N<Image>("ProfileBubbleTierBadge").Source);

            shell.OpenProfileBubbleMenu();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Bambi Doll", N<TextBlock>("ProfileMenuName").Text);
            Assert.True(N<TextBlock>("ProfileMenuName").IsVisible);
            Assert.Equal("🧪", N<TextBlock>("ProfileMenuBadge").Text);
            Assert.True(N<TextBlock>("ProfileMenuBadge").IsVisible);
            Assert.Equal(Loc.Get("btn_logout"), N<Button>("ProfileMenuAccountBtn").Content);
            Assert.True(N<Button>("ProfileMenuAccountBtn").IsVisible);

            var publicRow = shell.GetLogicalDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, Loc.Get("profile_menu_public")));
            publicRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MainShellWindow.ProfileSharingUrl, opened);
            Assert.False(shell.FindControl<global::Avalonia.Controls.Primitives.Popup>("ProfileBubblePopup")!.IsOpen);

            signedIn = false;
            shell.UpdateQuickLoginUI();
            shell.ShowTab("discord");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("?", N<TextBlock>("ProfileBubbleInitials").Text);
            Assert.False(N<Image>("ProfileBubbleTierBadge").IsVisible);
            var share = shell.ProfilePage!.FindControl<Button>("BtnProfileShare")!;
            Assert.False(share.IsEnabled);
            Assert.Equal(Loc.Get("profile_btn_share_tip_locked"), ToolTip.GetTip(share));

            shell.OpenProfileBubbleMenu();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("account_chip_sign_in"), N<TextBlock>("ProfileMenuName").Text);
            Assert.False(N<TextBlock>("ProfileMenuBadge").IsVisible);
            Assert.Equal(Loc.Get("account_chip_sign_in"), N<Button>("ProfileMenuAccountBtn").Content);
            N<Button>("ProfileMenuAccountBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("appsettings", shell.CurrentTab);   // signed out: Settings > Account, not a logout
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider,
                CoreAccount.HasLabAccessProvider, CoreAccount.HasPremiumAccessProvider, MainShellWindow.OpenProfileLink) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LevelUpFlashesTheGoldRing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.MotionLevel = MotionLevel.Full;
        s.OfflineMode = true;             // ProgressionBank's login gate
        s.OfflineUsername = "bubble-test";
        s.PlayerLevel = 3;
        s.PlayerXP = 0;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var ring = shell.FindControl<Ellipse>("ProfileBubbleGlowRing")!;
            Assert.Equal(0, ring.Opacity);
            ProgressionBank.Add(XpCurve.GetXPForLevel(3, XpCurve.EpochOf(s)), "Quest");
            double peak = 0;
            for (int i = 0; i < 30; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                peak = Math.Max(peak, ring.Opacity);
            }
            Assert.True(peak > 0, "the level-up never lit the bubble's gold ring");
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            s.OfflineMode = false;
            CoreSettings.ServiceProvider = saved;
        }
        return Task.CompletedTask;
    });

    /// <summary>H12: the equipped preset bust beats initials, an unknown one falls back to them quietly; a flash
    /// wobbles the bubble (2.5 s throttle) and a subliminal shimmers it (4 s throttle); Motion Off does neither.</summary>
    [Fact]
    public Task BustBeatsInitialsAndFlashAndSubliminalReact() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.MotionLevel = MotionLevel.Full;
        s.ShareProfilePicture = false;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.DisplayNameProvider = () => "Bambi Doll";
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var initials = shell.FindControl<TextBlock>("ProfileBubbleInitials")!;
            var fill = shell.FindControl<Ellipse>("ProfileBubbleFill")!;

            s.ProfileCosmetics.AvatarId = CosmeticsPool.AvatarIds.First();
            shell.RefreshProfileBubble();
            Assert.True(shell.ProfileBubbleShowsBust);
            Assert.False(initials.IsVisible);
            Assert.IsType<ImageBrush>(fill.Fill);

            s.ProfileCosmetics.AvatarId = "no-such-avatar";
            shell.RefreshProfileBubble();
            Assert.False(shell.ProfileBubbleShowsBust);
            Assert.True(initials.IsVisible);
            Assert.Equal(LeaderboardEntryData.BuildInitials("Bambi Doll"), initials.Text);

            CoreTubeEvents.RaiseFlashAboutToDisplay();
            CoreTubeEvents.RaiseFlashAboutToDisplay();
            CoreTubeEvents.RaiseSubliminalDisplayed();
            CoreTubeEvents.RaiseSubliminalDisplayed();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, shell.ProfileBubbleWobbles);    // the second flash is inside the throttle
            Assert.Equal(1, shell.ProfileBubbleShimmers);
            Assert.InRange(shell.FindControl<Grid>("ProfileBubbleVisual")!.Opacity, 0.0, 1.0);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OgBorderSpinsOnlyWhileShownAndSearchGlows() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            shell.ShowTab("discord");
            Dispatcher.UIThread.RunJobs();
            var page = shell.ProfilePage!;
            var og = page.FindControl<Border>("OgBorderContainer")!;
            page.FindControl<Grid>("ProfileCardWrapper")!.IsVisible = true;
            og.IsVisible = true;                                // an OG card came up
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsActive);
            Assert.True(shell.OgBorderLoopRunning);
            Assert.NotNull(og.Background!.Transform);

            shell.ShowTab("settings");                          // the tab hides: the loop stops (P01)
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.OgBorderLoopRunning);
            shell.ShowTab("discord");
            // Read at the show, before the dispatcher pumps: the stagger runs on the wall clock (460 ms at
            // most), and a cold or loaded run spent longer than that inside RunJobs, so the fade had finished.
            var cards = page.FindControl<StackPanel>("ProfileColumnStack")!.Children.Where(c => c.IsVisible).ToList();
            Assert.True(cards.Count > 1);
            Assert.True(cards[^1].Opacity < 1, "the entrance stagger did not hold the last card back");
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(shell.OgBorderLoopRunning);
            page.FindControl<Grid>("ProfileCardWrapper")!.IsVisible = false;   // a search found no one
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.OgBorderLoopRunning);
            page.FindControl<Grid>("ProfileCardWrapper")!.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.OgBorderLoopRunning);
            s.MotionLevel = MotionLevel.Reduced;               // no ambient loops
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.OgBorderLoopRunning);

            s.MotionLevel = MotionLevel.Off;                    // instant colour
            var search = page.FindControl<TextBox>("TxtProfileSearch")!;
            search.Focus();
            Dispatcher.UIThread.RunJobs();
            var brush = (SolidColorBrush)page.FindControl<Border>("ProfileSearchBox")!.BorderBrush!;
            Assert.Equal(0xC0, brush.Color.A);
            shell.FindControl<Button>("BtnProfileBubble")!.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, brush.Color.A);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = saved;
        }
        return Task.CompletedTask;
    });
}
