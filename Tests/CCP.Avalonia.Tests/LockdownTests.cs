using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The three Lockdown shipping blockers (docs/avalonia-decisions.md, Lockdown / Emergency
/// Exit): the phrase path works under Strict Lock through the real controls, keys are ignored only at
/// the global layer, and a killed run gets its panic key and Strict Lock back.</summary>
public sealed class LockdownTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Arm(bool forceStrict, bool disablePanic)
    {
        var s = CoreSettings.Current;
        s.PanicKeyEnabled = true;
        s.PanicKey = "F8";
        s.StrictLockEnabled = false;
        s.LockdownForceStrictLock = forceStrict;
        s.LockdownDisablePanicKey = disablePanic;
    }

    [Fact]
    public async Task PhraseTypedThroughTheRealControlsEndsALockdownUnderStrictLock()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: true, disablePanic: true);
            var ld = LockdownService.Current = new LockdownService();
            var tab = new LockdownTabView();
            var win = new Window { Content = tab, Width = 1200, Height = 1000 };
            win.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(30));
                Dispatcher.UIThread.RunJobs();
                Assert.True(CoreSettings.Current.StrictLockEnabled);
                Assert.True(tab.LockdownActivePanel.IsVisible);
                Assert.False(tab.LockdownSetupPanel.IsVisible);

                // The slab: a notice with the steps, never the box, never an end.
                tab.BtnEmergencyExit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(tab.TxtEmergencyExitNotice.IsVisible);
                Assert.Contains("let me out", tab.TxtEmergencyExitNotice.Text);
                Assert.False(tab.TxtLockdownExit.IsVisible);
                Assert.True(ld.IsActive);

                void FiveTaps()
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // hit testing reads the last frame
                    Dispatcher.UIThread.RunJobs();
                    var p = tab.TxtLockdownTimer.TranslatePoint(
                        new Point(20, tab.TxtLockdownTimer.Bounds.Height / 2), win)!.Value;   // on the digits
                    for (var i = 0; i < 5; i++)
                    {
                        win.MouseDown(p, MouseButton.Left);
                        win.MouseUp(p, MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                    }
                }
                void Type(string text)
                {
                    tab.TxtLockdownExit.Focus();
                    win.KeyTextInput(text);
                    win.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                FiveTaps();
                Assert.True(tab.TxtLockdownExit.IsVisible);
                Type("let me in");                      // wrong: box cleared + hidden, still locked
                Assert.False(tab.TxtLockdownExit.IsVisible);
                Assert.True(ld.IsActive);

                FiveTaps();
                Type("let me out");
                Dispatcher.UIThread.RunJobs();
                Assert.False(ld.IsActive);
                Assert.False(CoreSettings.Current.StrictLockEnabled);   // the pre-lockdown value
                Assert.True(CoreSettings.Current.PanicKeyEnabled);
                Assert.True(tab.LockdownSetupPanel.IsVisible);
            }
            finally
            {
                ld.Dispose();
                LockdownService.Current = null;
                win.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task GlobalPanicStopAndExitAreRefusedUnderLockdownAndWorkAfter()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: false, disablePanic: false);   // panic flag stays on: only Lockdown refuses
            var s = CoreSettings.Current;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                s.FlashEnabled = true;
                shell.StartEngine();
                ld.Activate(TimeSpan.FromMinutes(30));
                Assert.True(s.PanicKeyEnabled);

                shell.HandlePanicKeyPress(t0);
                shell.HandlePanicKeyPress(t0.AddSeconds(0.5));   // a double press would exit
                MainShellWindow.StopEverything();                // tray panic
                shell.RequestExit();
                shell.Close();                                   // the X
                Dispatcher.UIThread.RunJobs();
                Assert.True(CoreEngine.IsRunning);
                Assert.False(closed);

                // A TextBox in the same window still takes keys: only the global layer is ignored.
                var box = new TextBox();
                shell.Content = box;
                Dispatcher.UIThread.RunJobs();
                box.Focus();
                shell.KeyTextInput("typed");
                Assert.Equal("typed", box.Text);

                ld.Deactivate();
                shell.HandlePanicKeyPress(t0.AddSeconds(10));
                Assert.False(CoreEngine.IsRunning);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                ld.Dispose();
                LockdownService.Current = null;
                s.FlashEnabled = false;
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task APresetLoadedMidLockdownCannotSwitchStrictLockOff()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: true, disablePanic: false);
            var s = CoreSettings.Current;
            s.BubbleCountStrictLock = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                var tab = shell.Named<PresetsTabView>("PresetsTab")!;
                var loose = ConditioningControlPanel.Models.Preset.FromSettings(s, "Loose");
                loose.StrictLockEnabled = loose.BubbleCountStrictLock = false;
                ld.Activate(TimeSpan.FromMinutes(30));
                Assert.True(tab.LoadPreset(loose));
                Assert.True(s.StrictLockEnabled && s.BubbleCountStrictLock);   // WPF LockdownStrictHold.Keep
                ld.Deactivate();
                Assert.True(tab.LoadPreset(loose));
                Assert.False(s.BubbleCountStrictLock);                         // no lockdown: the preset wins
            }
            finally
            {
                ld.Dispose();
                LockdownService.Current = null;
                s.BubbleCountStrictLock = false;
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AKilledLockdownGetsThePanicKeyAndStrictLockBackOnTheNextStart()
    {
        Arm(forceStrict: true, disablePanic: true);
        var s = CoreSettings.Current;
        var killed = new LockdownService();
        killed.Activate(TimeSpan.FromMinutes(30));
        Assert.False(s.PanicKeyEnabled);
        Assert.True(s.StrictLockEnabled);
        Assert.True(File.Exists(Path.Combine(CorePaths.UserData, "lockdown_recovery.json")));

        // The process dies here: no Deactivate. The next start runs the recovery first.
        LockdownService.RecoverIfNeeded();
        Assert.True(s.PanicKeyEnabled);
        Assert.False(s.StrictLockEnabled);
        Assert.False(File.Exists(Path.Combine(CorePaths.UserData, "lockdown_recovery.json")));
        killed.Dispose();
    }
}
