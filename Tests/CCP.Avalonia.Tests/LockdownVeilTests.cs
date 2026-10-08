using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lockdown's structural shield (docs/avalonia-decisions.md, Lockdown veil). WPF veils the
/// launcher only (LauncherWindow.xaml:597); the panel stays usable and every door on it refuses, so
/// the panel half is a table of every control WPF greys or refuses under Lockdown.</summary>
public sealed class LockdownVeilTests
{
    private static void Run(Action<MainShellWindow, LockdownService> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = s.HasAcceptedAgeVerification = true;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            s.StrictLockEnabled = false;
            s.LockdownForceStrictLock = s.LockdownDisablePanicKey = true;
            s.MotionLevel = MotionLevel.Full;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell, ld);
            }
            finally
            {
                ld.Dispose();
                LockdownService.Current = null;
                LauncherWindow.Instance?.Close();
                LauncherWindow.Boot = BootDecision.PanelFirst;
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                shell.RequestExit();
                shell.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static void Frame()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // hit testing reads the last frame
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void LauncherVeil_FollowsLockdown_SwallowsClicksAndKeys_BreathesOnlyWhileShown() => Run((shell, ld) =>
    {
        var launcher = LauncherWindow.OpenFor(shell);
        var veil = launcher.FindControl<Border>("LockdownVeil")!;
        var cta = launcher.FindControl<Button>("PanelCta")!;
        var clicks = 0;
        cta.Click += (_, _) => clicks++;
        Frame();
        Assert.False(veil.IsVisible);
        Assert.False(launcher.VeilBreathing);

        ld.Activate(TimeSpan.FromMinutes(30));   // e.g. a remote/Takeover start while the launcher is up
        Frame();
        Assert.True(veil.IsVisible);
        Assert.Equal(Loc.Get("launcher_lockdown_veil"), launcher.FindControl<TextBlock>("LockdownText")!.Text);
        Assert.True(launcher.VeilBreathing);

        // A click on the panel CTA lands on the veil.
        var p = cta.TranslatePoint(new Point(cta.Bounds.Width / 2, cta.Bounds.Height / 2), launcher)!.Value;
        launcher.MouseDown(p, MouseButton.Left);
        launcher.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        // Keys: even a control that still holds focus takes no Enter/Space, and Tab moves nothing.
        cta.Focus();
        var focused = launcher.FocusManager?.GetFocusedElement();
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        launcher.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        launcher.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        launcher.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        launcher.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, clicks);
        Assert.Same(focused, launcher.FocusManager?.GetFocusedElement());
        Assert.True(launcher.IsVisible);

        // The allowed exits are the panel's, which no veil covers: the Lockdown slab still answers.
        var tab = shell.Named<LockdownTabView>("LockdownTab")!;
        tab.BtnEmergencyExit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.TxtEmergencyExitNotice.IsVisible);

        // No ticks on a hidden launcher (P01).
        launcher.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.VeilBreathing);
        launcher.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(AmbientFxCanvas.Env.AllowAmbientLoops);   // MotionLevel.Full, so the next asserts mean something
        Assert.True(launcher.VeilBreathing);
        launcher.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.VeilBreathing);
        launcher.WindowState = WindowState.Normal;
        Dispatcher.UIThread.RunJobs();
        Assert.True(launcher.VeilBreathing);

        ld.Deactivate();
        Frame();
        Assert.False(veil.IsVisible);
        Assert.False(launcher.VeilBreathing);
        cta.Focus();
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        launcher.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, clicks);   // the same key reaches it once the veil is gone
    });

    private static IEnumerable<string?> DialogTexts(Window owner) =>
        owner.OwnedWindows.SelectMany(w => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static void CloseDialogs(Window owner)
    {
        foreach (var w in owner.OwnedWindows.ToList()) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The shell has no veil, by WPF design (MainWindow stays usable; its Lockdown exits live
    /// there). Every door WPF greys or refuses under Lockdown must refuse here too. One row each:
    /// the attempt, then "did it escape?".</summary>
    [Fact]
    public void EveryShellDoorWpfShutsUnderLockdownRefuses() => Run((shell, ld) =>
    {
        var s = CoreSettings.Current;
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        CoreEngine.StoppedHook = shell.OnEngineStopped;
        s.FlashEnabled = true;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        shell.StartSession(new Session { Id = "veil_test", Name = "Veil Test", Icon = "🧪", DurationMinutes = 5 });
        var host = new Window { Width = 900, Height = 900 };
        host.Show();
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            s.AutonomyConsentGiven = true;
            s.AutonomyResumeOnStartup = false;
            Assert.True(shell.SetAutonomyEnabled(true));
            ShowTab(shell, "presets");
            ShowTab(shell, "lockdown");
            var launcher = LauncherWindow.OpenFor(shell);
            Dispatcher.UIThread.RunJobs();
            ld.Activate(TimeSpan.FromMinutes(30));
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.StrictLockEnabled && !s.PanicKeyEnabled);
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

            var rows = new (string Door, Action Attempt, Func<bool> Escaped)[]
            {
                // WPF StartStop.cs:45
                ("Start/Stop button", () => Click(shell.Named<Button>("BtnStart")!), () => !CoreEngine.IsRunning),
                ("tray Stop everything", MainShellWindow.StopEverything, () => !CoreEngine.IsRunning || runner.IsPaused),
                // WPF MainWindow.xaml.cs:866 (every global key, panic included)
                ("panic key x2", () => { shell.HandlePanicKeyPress(t0); shell.HandlePanicKeyPress(t0.AddSeconds(0.5)); },
                    () => !CoreEngine.IsRunning || closed),
                // WPF Launcher.cs:184, Settings.cs:451, WindowChrome.cs:131
                ("tray Exit", shell.RequestExit, () => closed),
                ("Settings Exit button", () => shell.BtnExit_Click(null, new RoutedEventArgs()),
                    () => closed || !DialogTexts(shell).Contains(Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"))),
                ("window X", shell.Close, () => closed),
                // WPF LauncherHost.cs:306 + Lab.cs:611
                ("back to launcher", () => LauncherWindow.BackToLauncher(shell), () => !shell.IsVisible),
                // WPF TabHistory.cs:84
                ("tab history Back", () => Click(shell.Named<Button>("BtnNavBack")!), () => shell.CurrentTab != "lockdown"),
                // WPF SettingsPaletteWindow.xaml.cs:80
                ("search palette", () => SettingsPaletteWindow.Toggle(shell), () => SettingsPaletteWindow.IsOpen),
                // WPF Autonomy.cs:44
                ("Takeover switch off", () => shell.SetAutonomyEnabled(false), () => !shell.Autonomy.IsEnabled),
                ("Takeover Start/Stop button", () => Click(shell.Named<BambiTakeoverTabView>("BambiTakeoverTab")!.BtnAutonomyStartStop),
                    () => !shell.Autonomy.IsEnabled),
                // WPF Presets.cs:1993 / 2033
                ("session Stop", () => shell.BtnStartSession_Click(null),
                    () => !runner.IsRunning || !DialogTexts(shell).Contains(Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_end_a_se"))),
                ("session Pause", () => Click(shell.Named<Button>("BtnPauseSession")!), () => runner.IsPaused),
                // WPF DataSettingsSection.xaml.cs:76
                ("factory reset", () => { var d = Host(host, new DataSettingsSection()); Click(d.FindControl<Button>("BtnFactoryReset")!); },
                    () => !DialogTexts(host).Contains(Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"))
                          || DialogTexts(host).Contains(Loc.Get("set2_reset_dialog1_title"))),
                // WPF Lab.cs:635-662 (greyed): strict toggles and the no-panic box hold
                ("Video strict toggle", () => Host(host, new VideoFeatureControl()).FindControl<CheckBox>("ChkStrict")!.IsChecked = false,
                    () => !s.StrictLockEnabled || host.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "ChkStrict").IsChecked != true),
                ("Bubble Count strict toggle", () => { s.BubbleCountStrictLock = true; Host(host, new BubbleCountFeatureControl()).FindControl<CheckBox>("ChkStrict")!.IsChecked = false; },
                    () => !s.BubbleCountStrictLock || host.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "ChkStrict").IsChecked != true),
                ("no-panic box", () => Host(host, new DevicesSettingsSection()).FindControl<CheckBox>("ChkNoPanic")!.IsChecked = false,
                    () => s.PanicKeyEnabled || NoPanic(host).IsChecked != true || NoPanic(host).IsEnabled
                          || ToolTip.GetTip(NoPanic(host)) as string != Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape")),
                // WPF Lab.cs:612: the CC Labs door is greyed, not only refused
                ("CC Labs button greyed", () => { }, () => shell.Named<Button>("BtnBackToLauncher")!.IsEnabled),
                // WPF LauncherHost.cs:413 and the launcher Stop link
                ("launcher close", launcher.RequestClose, () => !launcher.IsVisible),
                ("launcher Stop link", () => Click(launcher.FindControl<Button>("StopLink")!), () => !CoreEngine.IsRunning),
            };

            var escaped = new List<string>();
            foreach (var (door, attempt, didEscape) in rows)
            {
                attempt();
                Dispatcher.UIThread.RunJobs();
                if (didEscape()) escaped.Add(door);
                CloseDialogs(shell);
                CloseDialogs(host);
                if (closed) break;
            }
            Assert.Empty(escaped);

            // Given back on exit (WPF Lab.cs:707/741-745).
            ld.Deactivate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Named<Button>("BtnBackToLauncher")!.IsEnabled);
            Assert.True(NoPanic(host).IsEnabled);
            Assert.Null(ToolTip.GetTip(NoPanic(host)));
        }
        finally
        {
            CoreEntitlement.HasPremiumProvider = oldPremium;
            s.BubbleCountStrictLock = false;
            s.FlashEnabled = false;
            runner.Stop();
            AvApp.Sessions = null;
            CoreSession.IsSessionRunningProvider = null;
            host.Close();
        }
    });

    private static CheckBox NoPanic(Window host) =>
        host.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "ChkNoPanic");

    private static void ShowTab(MainShellWindow shell, string tab)
    {
        shell.ShowTab(tab);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Host<T>(Window host, T c) where T : Control
    {
        host.Content = c;
        Dispatcher.UIThread.RunJobs();
        return c;
    }

    /// <summary>WPF SetLockdownBadge / OnLockdownTick / LockdownBadge_Click (Lab.cs:672/753/771/814):
    /// the badge shows for the whole run, its clock follows the stepped service clock (masked by
    /// HideLockdownTimer), a click or Enter leads to the Lockdown tab, and it goes on exit.</summary>
    [Fact]
    public void TitleBarBadge_ShowsTheRunningClock_LeadsToTheLockdownTab_AndGoesOnExit() => Run((shell, ld) =>
    {
        var now = DateTime.UtcNow;
        ld.UtcNow = () => now;
        var tick = typeof(LockdownService).GetMethod("OnCountdownTick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var badge = shell.Named<Border>("LockdownBadge")!;
        var time = shell.Named<TextBlock>("TxtLockdownBadgeTime")!;
        try
        {
            shell.ShowTab("settings");
            Frame();
            Assert.False(badge.IsVisible);

            ld.Activate(TimeSpan.FromMinutes(30));
            Frame();
            Assert.True(badge.IsVisible);
            Assert.Equal("30:00", time.Text);

            now = now.AddMinutes(10);
            tick.Invoke(ld, new object?[] { null, EventArgs.Empty });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("20:00", time.Text);

            CoreSettings.Current.HideLockdownTimer = true;
            tick.Invoke(ld, new object?[] { null, EventArgs.Empty });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SessionClockLabel.LockdownClock(TimeSpan.Zero, true), time.Text);
            CoreSettings.Current.HideLockdownTimer = false;

            var p = badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), shell)!.Value;
            shell.MouseDown(p, MouseButton.Left);
            shell.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("lockdown", shell.CurrentTab);

            shell.ShowTab("settings");
            Frame();
            badge.Focus();
            shell.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("lockdown", shell.CurrentTab);
            Assert.True(ld.IsActive);   // a signpost: nothing about it ends the lockdown

            ld.Deactivate();
            Frame();
            Assert.False(badge.IsVisible);
        }
        finally { CoreSettings.Current.HideLockdownTimer = false; }
    });

    /// <summary>WPF StartEmergencyExitPulse (LockdownTabView.xaml.cs:267): the slab's glow breathes
    /// 0.26-0.62 / 24-42 on a 1.5 s sine while a lockdown runs, only while the tab shows (P01),
    /// never under LockdownPhotosafe or MotionLevel Off, never while minimised, and rests at 0.32 / 28 when stopped.</summary>
    [Fact]
    public void EmergencyExitGlow_BreathesOnlyWhileShown_AndNeverUnderPhotosafe() => Run((shell, ld) =>
    {
        var tab = shell.Named<LockdownTabView>("LockdownTab")!;
        var plate = tab.FindControl<Border>("EEPlate")!;
        try
        {
            CoreSettings.Current.LockdownPhotosafe = false;
            shell.ShowTab("lockdown");
            Frame();
            Assert.False(tab.EmergencyExitPulsing);   // setup panel: nothing to breathe

            ld.Activate(TimeSpan.FromMinutes(30));
            Frame();
            Assert.True(tab.EmergencyExitPulsing);
            tab.PaintEmergencyExitGlow(1500);   // the top of the breath
            var glow = Assert.IsType<global::Avalonia.Media.DropShadowEffect>(plate.Effect);
            Assert.Equal(0.62, glow.Opacity, 3);
            Assert.Equal(42, glow.BlurRadius, 3);

            shell.ShowTab("settings");
            Frame();
            Assert.False(tab.EmergencyExitPulsing);
            shell.ShowTab("lockdown");
            Frame();
            Assert.True(tab.EmergencyExitPulsing);

            ld.Deactivate();
            Frame();
            Assert.False(tab.EmergencyExitPulsing);
            glow = Assert.IsType<global::Avalonia.Media.DropShadowEffect>(plate.Effect);
            Assert.Equal(0.32, glow.Opacity, 3);
            Assert.Equal(28, glow.BlurRadius, 3);

            CoreSettings.Current.LockdownPhotosafe = true;
            ld.Activate(TimeSpan.FromMinutes(30));
            Frame();
            Assert.False(tab.EmergencyExitPulsing);
            ld.Deactivate();
            Frame();

            // Motion Off (WPF: SystemParameters.ClientAreaAnimation) holds the resting glow too.
            CoreSettings.Current.LockdownPhotosafe = false;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            ld.Activate(TimeSpan.FromMinutes(30));
            Frame();
            Assert.False(tab.EmergencyExitPulsing);
            ld.Deactivate();
            Frame();

            // P01: a minimised window stops the breath; restoring it brings it back.
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            ld.Activate(TimeSpan.FromMinutes(30));
            Frame();
            Assert.True(tab.EmergencyExitPulsing);
            shell.WindowState = WindowState.Minimized;
            Frame();
            Assert.False(tab.EmergencyExitPulsing);
            shell.WindowState = WindowState.Normal;
            Frame();
            Assert.True(tab.EmergencyExitPulsing);
        }
        finally
        {
            CoreSettings.Current.LockdownPhotosafe = false;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            shell.WindowState = WindowState.Normal;
        }
    });

    /// <summary>WPF LockdownTabView.xaml:202/218: the quest hint follows the picked duration
    /// (QuestService.LockdownQuestMinimum) and the hide-clock toggle masks the badge it feeds.</summary>
    [Fact]
    public void SetupPanel_QuestHintFollowsTheDuration_HideClockToggleMasksTheBadge() => Run((shell, ld) =>
    {
        var tab = shell.Named<LockdownTabView>("LockdownTab")!;
        var hint = tab.FindControl<TextBlock>("TxtLockdownQuestHint")!;
        var combo = tab.FindControl<ComboBox>("CmbLockdownDuration")!;
        var hide = tab.FindControl<CheckBox>("ChkLockdownHideTimer")!;
        try
        {
            shell.ShowTab("lockdown");
            Frame();
            Assert.True(hint.IsVisible);    // 10 minutes, the default: under the quest minimum
            combo.SelectedIndex = 3;        // 30 minutes
            Assert.False(hint.IsVisible);
            combo.SelectedIndex = 0;        // 5 minutes
            Assert.True(hint.IsVisible);

            hide.IsChecked = true;
            Assert.True(CoreSettings.Current.HideLockdownTimer);
            ld.Activate(TimeSpan.FromMinutes(30));
            Frame();
            Assert.Equal(SessionClockLabel.LockdownClock(TimeSpan.Zero, true),
                shell.Named<TextBlock>("TxtLockdownBadgeTime")!.Text);
            ld.Deactivate();
            Frame();
            hide.IsChecked = false;
            Assert.False(CoreSettings.Current.HideLockdownTimer);
        }
        finally { CoreSettings.Current.HideLockdownTimer = false; }
    });
}
