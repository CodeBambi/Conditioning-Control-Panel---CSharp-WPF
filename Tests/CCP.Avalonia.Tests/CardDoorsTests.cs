using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 3 r5 (owner, 2026-10-09: "clicking on the cards for anything that opens the browser
/// window in play or premium doesnt work"): every Play wall game card and every Premium game card
/// opens its game window the way WPF 7.1.5 does (Play shims -> LaunchPlay*, OpenExclusiveFeature),
/// through the launcher's own launch path, with the launcher never shown.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class CardDoorsTests
{
    private static void Run(Action<MainShellWindow> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var (oldIn, oldLab) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => true;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();
                LauncherWindow.Instance?.Close();
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (oldIn, oldLab);
                shell.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    /// <summary>The ids of the live game windows (GameWindow keeps them in its private Open list).</summary>
    private static string[] OpenIds()
    {
        var list = (System.Collections.Generic.List<GameWindow>)typeof(GameWindow)
            .GetField("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        lock (list) return list.Select(w => w.Spec.Id).ToArray();
    }

    [Theory]
    [InlineData("BtnPlayBreakoutDemo", "breakoutdemo")]
    [InlineData("BtnPlayBreakout", "breakout")]
    [InlineData("BtnPlayGoon", "goon")]
    [InlineData("BtnPlayChess", "piecebypiece")]
    [InlineData("BtnPlayBackRoom", "backroom")]
    [InlineData("BtnPlayDtrh", "dtrh")]
    [InlineData("BtnPlayArcademy", "arcademy")]
    public void PlayCard_OpensItsGame_LauncherNeverShows(string button, string id) => Run(shell =>
    {
        shell.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        var play = shell.GetVisualDescendants().OfType<PlayTabView>().First();
        var btn = play.FindControl<Button>(button)!;
        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(id, OpenIds().SingleOrDefault());
        Assert.False(LauncherWindow.Instance?.IsVisible ?? false);
        Assert.True(shell.IsVisible);
    });

    /// <summary>A real pointer press on the Play button (the top row of the wall), not a raised
    /// Click: nothing painted over the card (lock band, badge) may swallow it.</summary>
    [Theory]
    [InlineData("BtnPlayBreakout", "breakout")]
    [InlineData("BtnPlayGoon", "goon")]
    public void PlayCard_RealPointerPress_ReachesTheButton(string button, string id) => Run(shell =>
    {
        shell.Width = 1600; shell.Height = 1000;
        shell.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        var play = shell.GetVisualDescendants().OfType<PlayTabView>().First();
        var btn = play.FindControl<Button>(button)!;
        btn.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var at = btn.TranslatePoint(new Point(btn.Bounds.Width / 2, btn.Bounds.Height / 2), shell)!.Value;
        shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
        shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(id, OpenIds().SingleOrDefault());
    });

    [Theory]
    [InlineData("backroom", "backroom")]
    [InlineData("breakout", "breakout")]
    [InlineData("goon", "goon")]
    [InlineData("dtrh", "dtrh")]
    [InlineData("arcademy", "arcademy")]
    public void PremiumCard_OpensItsGame(string key, string id) => Run(shell =>
    {
        Assert.True(ExclusivesTabView.IsOnThisBuild(key));
        shell.OpenExclusiveFeature(key);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(id, OpenIds().SingleOrDefault());
        Assert.False(LauncherWindow.Instance?.IsVisible ?? false);
    });
}
