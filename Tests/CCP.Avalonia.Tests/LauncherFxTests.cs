using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Launcher slice 5 (WPF LauncherWindow.Fx/Choreo/Backdrop/Edge/Crossfade.cs): the frame clock runs
/// only while the launcher is shown and not minimised, honours the motion setting, and hover / Play beats
/// change state on a stepped clock. Sounds go through CoreAudio, captured here, never played.</summary>
public sealed class LauncherFxTests
{
    private static void Run(MotionLevel motion, Action<MainShellWindow, LauncherWindow, List<string>> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;   // P52: the upgrade-only What moved card would take the real clicks
            s.MotionLevel = motion;
            s.PerformanceMode = false;
            s.MasterVolume = 50;
            s.LauncherSoundEnabled = true;
            var cues = new List<string>();
            var (oldIn, oldLab, oldAudio) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreAudio.PlayOneShotProvider);
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => cues.Add(tag);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => true;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(LauncherWindow.BackToLauncher(shell));
                Dispatcher.UIThread.RunJobs();
                var launcher = LauncherWindow.Instance!;
                launcher.UpdateLayout();
                body(shell, launcher, cues);
            }
            finally
            {
                LauncherWindow.Instance?.Close();
                LockdownService.Current = null;
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreAudio.PlayOneShotProvider) = (oldIn, oldLab, oldAudio);
                shell.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static Border Tile(LauncherWindow w) => (Border)w.FindControl<UniformGrid>("GamesGrid")!.Children[0];

    private static void Step(LauncherWindow w, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.02) w.StepFx(0.02);
    }

    [Fact]
    public void Clock_RunsOnlyWhileShownAndNotMinimised() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        Assert.True(launcher.FxTicking);
        Step(launcher, 0.5);
        Assert.NotEqual(0, launcher.SpiralTurn.Angle);   // the backdrop turns
        Assert.True(launcher.FxTicking);

        launcher.WindowState = WindowState.Minimized;
        Assert.False(launcher.FxTicking);
        launcher.WindowState = WindowState.Normal;
        Assert.True(launcher.FxTicking);

        launcher.Hide();
        Assert.False(launcher.FxTicking);
        launcher.StepFx(0.1);                            // a stray tick on a hidden launcher parks, never runs
        Assert.False(launcher.FxTicking);
    });

    [Fact]
    public void MotionOff_StopsTheClock_AndHoverSnaps() => Run(MotionLevel.Off, (shell, launcher, _) =>
    {
        launcher.StepFx(0.05);
        Assert.False(launcher.FxTicking);
        Assert.Equal(0, launcher.SpiralTurn.Angle);
        Assert.Equal(1, launcher.FindControl<Panel>("RootGrid")!.Opacity);

        var tile = Tile(launcher);
        launcher.TileHover(tile, true);
        Assert.Equal(1.02, ((TransformGroup)tile.RenderTransform!).Children.OfType<ScaleTransform>().Single().ScaleX, 3);

        // Play hides at once: no beat is held without motion.
        tile.GetVisualDescendants().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        Assert.True(shell.IsVisible);
    });

    [Fact]
    public void ReducedMotion_KeepsTransitions_ButNoAmbientSpin() => Run(MotionLevel.Reduced, (_, launcher, _) =>
    {
        Step(launcher, 0.5);
        Assert.Equal(0, launcher.SpiralTurn.Angle);      // ambient loops are Full-only
        Assert.True(launcher.FxTicking);                 // the edge still breathes (WPF gates it on transitions)
    });

    [Fact]
    public void Hover_LiftsGlowsAndBleeds_ThenSettles() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        var tile = Tile(launcher);
        var scale = ((TransformGroup)tile.RenderTransform!).Children.OfType<ScaleTransform>().Single();
        var glow = launcher.FindControl<Border>("GlowLayer")!;
        launcher.TileHover(tile, true);
        Assert.Contains("launcher-hover", cues);
        Step(launcher, 0.5);
        Assert.Equal(1.02, scale.ScaleX, 3);
        Assert.Equal(0.7, ((DropShadowEffect)tile.Effect!).Opacity, 3);
        Assert.Equal(0.45, glow.Opacity, 3);

        launcher.TileHover(tile, false);
        Step(launcher, 0.5);
        Assert.Equal(1, scale.ScaleX, 3);
        Assert.Equal(0.28, glow.Opacity, 3);
    });

    [Fact]
    public void Play_HoldsTheHideForTheBeat_ShakesDimsAndFades() => Run(MotionLevel.Full, (shell, launcher, cues) =>
    {
        Assert.Contains("launcher-open", cues);
        Step(launcher, 0.4);                             // the fade-in lands
        var card = launcher.FindControl<Border>("PanelCard")!;
        Tile(launcher).GetVisualDescendants().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("launcher-launch", cues);
        Assert.True(launcher.IsVisible);                 // held for the beat

        launcher.StepFx(0.05);
        Assert.NotEqual(0, launcher.ShakeSlide.X);
        Step(launcher, 0.3);
        Assert.Equal(0.55, card.Opacity, 2);             // the rest stepped back
        Assert.True(launcher.IsVisible);

        Step(launcher, 0.3);                             // FadeLeadMs + FadeOutMs = 450 ms
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal(1, launcher.FindControl<Panel>("RootGrid")!.Opacity);
    });

    [Fact]
    public void Sounds_RespectTheLauncherSpeaker() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        cues.Clear();
        CoreSettings.Current.LauncherSoundEnabled = false;
        launcher.TileHover(Tile(launcher), true);
        launcher.FindControl<Button>("BtnMinimize")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(cues);
        CoreSettings.Current.LauncherSoundEnabled = true;
        launcher.WindowState = WindowState.Normal;
        launcher.FindControl<Button>("BtnMinimize")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "launcher-click" }, cues);
    });

    /// <summary>WPF LauncherWindow.Sound.cs: a real click on the title-bar speaker mutes in silence,
    /// repaints (slash, dim cone, unmute tooltip); unmuting answers with one click.</summary>
    [Fact]
    public void SpeakerButton_ByRealClick_MutesQuietly_UnmutesWithANote() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        var btn = launcher.FindControl<Button>("BtnSound")!;
        var slash = launcher.FindControl<global::Avalonia.Controls.Shapes.Path>("SoundSlash")!;
        void Press()
        {
            var p = btn.TranslatePoint(new Point(btn.Bounds.Width / 2, btn.Bounds.Height / 2), launcher)!.Value;
            launcher.MouseDown(p, MouseButton.Left);
            launcher.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(slash.IsVisible);
        Assert.Equal(Loc.Get("launcher_sound_mute"), ToolTip.GetTip(btn));
        cues.Clear();
        Press();
        Assert.False(CoreSettings.Current.LauncherSoundEnabled);
        Assert.True(slash.IsVisible);
        Assert.False(launcher.FindControl<global::Avalonia.Controls.Shapes.Path>("SoundWaves")!.IsVisible);
        Assert.Equal(Loc.Get("launcher_sound_unmute"), ToolTip.GetTip(btn));
        Assert.Empty(cues);
        Press();
        Assert.True(CoreSettings.Current.LauncherSoundEnabled);
        Assert.False(slash.IsVisible);
        Assert.Equal(new[] { "launcher-click" }, cues);
    });

    private sealed class SteppedUtc : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow.AddDays(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Hover_ByRealPointer_PlaysTheThrottledMelody() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        var clock = new SteppedUtc();
        LauncherWindow.LauncherSfx.Clock = clock;
        try
        {
            var tile = Tile(launcher);
            var scale = ((TransformGroup)tile.RenderTransform!).Children.OfType<ScaleTransform>().Single();
            var centre = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), launcher)!.Value;
            cues.Clear();
            launcher.MouseMove(centre);
            Step(launcher, 0.4);
            Assert.Equal(1.02, scale.ScaleX, 3);
            launcher.MouseMove(new Point(2, 2));
            launcher.MouseMove(centre);                  // re-entered inside MinGapMs: no second note
            Assert.Single(cues, c => c == "launcher-hover");
            clock.Now += TimeSpan.FromMilliseconds(LauncherMelody.MinGapMs + 1);
            launcher.MouseMove(new Point(2, 2));
            launcher.MouseMove(centre);
            Assert.Equal(2, cues.Count(c => c == "launcher-hover"));
        }
        finally { LauncherWindow.LauncherSfx.Clock = TimeProvider.System; }
    });

    [Fact]
    public void Play_OffScreen_IsSilent() => Run(MotionLevel.Full, (shell, launcher, cues) =>
    {
        launcher.Hide();                                 // a game boot / handoff plays with the launcher off screen
        cues.Clear();
        launcher.Play(LauncherCards.Find("intake")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(cues);
        Assert.True(shell.IsVisible);
    });

    [Fact]
    public void Deactivated_ParksTheSpirals() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        Step(launcher, 0.3);
        // Headless never moves activation between windows; drive the Deactivated/Activated handlers.
        launcher.OnFxActivated(false);
        double parked = launcher.SpiralTurn.Angle;
        Step(launcher, 0.5);
        Assert.Equal(parked, launcher.SpiralTurn.Angle);
        launcher.OnFxActivated(true);
        Step(launcher, 0.5);
        Assert.NotEqual(parked, launcher.SpiralTurn.Angle);
    });

    [Fact]
    public void Close_DropsAPendingExit() => Run(MotionLevel.Full, (shell, launcher, _) =>
    {
        Tile(launcher).GetVisualDescendants().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(launcher.IsVisible);                 // held for the beat
        launcher.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);                   // the held step never ran
    });
}
