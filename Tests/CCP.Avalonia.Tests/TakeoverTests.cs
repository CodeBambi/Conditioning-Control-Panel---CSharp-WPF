using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Takeover on this head (shell-autonomy): the switch arms Core AutonomyScheduler only with
/// consent + entitlement, the state hero follows it, Lockdown refuses stopping it (#514) and panic
/// stops it (brief safety rule) without unticking the saved switch.</summary>
public sealed class TakeoverTests
{
    [Fact]
    public async Task SwitchArmsItLockdownHoldsItPanicStopsIt()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            s.AutonomyConsentGiven = true;
            s.AutonomyResumeOnStartup = false;
            var shell = new MainShellWindow();
            shell.Show();
            Border Pill() => shell.GetVisualDescendants().OfType<Border>().First(b => b.Name == "TakeoverActivePill");
            string? Text(string name) => shell.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Name == name).Text;
            LockdownService? ld = null;
            try
            {
                // No entitlement: the setting saves, nothing arms.
                CoreEntitlement.HasPremiumProvider = () => false;
                shell.SetAutonomyEnabled(true);
                Assert.False(shell.Autonomy.IsEnabled);

                CoreEntitlement.HasPremiumProvider = () => true;
                Assert.True(shell.SetAutonomyEnabled(true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(shell.Autonomy.IsEnabled);
                Assert.True(Pill().IsVisible);

                // Lockdown: neither the switch nor the panic key stops a running Takeover.
                ld = LockdownService.Current = new LockdownService();
                ld.Activate(TimeSpan.FromMinutes(30));
                var escapes = 0;
                ld.EscapeAttempted += _ => escapes++;
                Assert.True(shell.SetAutonomyEnabled(false));
                Assert.Equal(0, escapes);   // WPF #514: the message only, no Stop tripwire
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Assert.True(shell.Autonomy.IsEnabled);
                Assert.True(s.AutonomyModeEnabled);
                ld.Deactivate();

                // Panic stops her; the saved switch stays as the user set it.
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 10));
                Dispatcher.UIThread.RunJobs();
                Assert.False(shell.Autonomy.IsEnabled);
                Assert.True(s.AutonomyModeEnabled);
                Assert.False(Pill().IsVisible);
                // The tab shows her stopped, too.
                Assert.Equal("○ DORMANT", Text("TxtTakeoverStatus"));

            }
            finally
            {
                shell.Autonomy.Stop();
                ld?.Dispose();
                LockdownService.Current = null;
                CoreEntitlement.HasPremiumProvider = null;
                s.AutonomyModeEnabled = s.AutonomyConsentGiven = false;
                CoreEngine.Stop();
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    private sealed class FakeBubbleCountHost : IBubbleCountHost
    {
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) { }
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 0;
    }

    /// <summary>takeover-actions: the actions ported to this head do what WPF's PerformAction does,
    /// the ones with no surface are never picked, panic hands the pink pulse back, and the voice
    /// hint follows the mic state.</summary>
    [Fact]
    public async Task PortedActionsPerformMissingOnesAreNeverPicked()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            s.AutonomyConsentGiven = true;
            s.AutonomyResumeOnStartup = false;
            var shell = new MainShellWindow();
            shell.Show();
            var oldBubbleCount = CoreEngine.BubbleCount;
            var (pinkOn, pinkOpacity) = (s.PinkFilterEnabled, s.PinkFilterOpacity);
            try
            {
                // No surface on this head: never picked (WPF skips an unavailable action).
                foreach (var a in new[] { AutonomyActionType.SpiralPulse, AutonomyActionType.BrainDrainPulse,
                             AutonomyActionType.WebVideo, AutonomyActionType.WallpaperShuffle, AutonomyActionType.SpokenMantra })
                    Assert.False(shell.Autonomy.CanPerform(a), a.ToString());
                Assert.False(shell.Autonomy.CanPerform(AutonomyActionType.MindWipe));   // CoreMindWipe unseeded
                Assert.False(shell.Autonomy.CanPerform(AutonomyActionType.PinkFilterPulse));   // no compositor headless

                CoreEntitlement.HasPremiumProvider = () => true;
                Assert.True(shell.SetAutonomyEnabled(true));
                Dispatcher.UIThread.RunJobs();

                // Bubble Count: a forced game (WPF TriggerGame(forceTest: true)), engine off, card off.
                var bc = CoreEngine.BubbleCount = new BubbleCountScheduler(new FakeBubbleCountHost());
                s.BubbleCountEnabled = false;
                Assert.True(shell.Autonomy.CanPerform(AutonomyActionType.BubbleCount));
                shell.PerformAutonomy(AutonomyActionType.BubbleCount);
                Assert.True(bc.IsBusy);
                bc.ForceCleanup();

                // Pink pulse: boosted and held up with the engine off, then handed back on panic.
                s.PinkFilterEnabled = false;
                s.PinkFilterOpacity = 10;
                shell.PerformAutonomy(AutonomyActionType.PinkFilterPulse);
                Assert.True(s.PinkFilterEnabled);
                Assert.Equal(30, s.PinkFilterOpacity);
                Assert.True(PinkFilterOverlay.PulseHold);
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Dispatcher.UIThread.RunJobs();
                Assert.False(shell.Autonomy.IsEnabled);
                Assert.False(s.PinkFilterEnabled);
                Assert.Equal(10, s.PinkFilterOpacity);
                Assert.False(PinkFilterOverlay.PulseHold);

                // Voice hint (WPF RefreshAutonomyVoiceHint): off, then on with no speech engine.
                var tab = shell.GetLogicalDescendants().OfType<global::ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>().First();
                var hint = tab.GetLogicalDescendants().OfType<TextBlock>().First(x => x.Name == "TxtAutonomyVoiceHint");
                s.AutonomyCanTriggerVoiceCommand = false;
                tab.RefreshAutonomyVoiceHint();
                Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get("takeover_voice_hint_off"), hint.Text);
                s.AutonomyCanTriggerVoiceCommand = s.MicConsentGiven = true;
                tab.RefreshAutonomyVoiceHint();
                Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get("takeover_voice_hint_no_mic"), hint.Text);
            }
            finally
            {
                shell.Autonomy.Stop();
                CoreEngine.BubbleCount = oldBubbleCount;
                CoreEntitlement.HasPremiumProvider = null;
                s.AutonomyModeEnabled = s.AutonomyConsentGiven = false;
                s.AutonomyCanTriggerVoiceCommand = s.MicConsentGiven = false;
                Dispatcher.UIThread.RunJobs();
                PinkFilterOverlay.PulseHold = false;
                (s.PinkFilterEnabled, s.PinkFilterOpacity) = (pinkOn, pinkOpacity);
                CoreEngine.Stop();
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Headless shell with Takeover armed, a stepped pulse clock and the pink/bubble state
    /// saved; <paramref name="body"/> gets the shell and the queued 30 s timer callbacks.</summary>
    private static Task WithArmedShell(Action<MainShellWindow, System.Collections.Generic.List<Action>> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var resume = s.AutonomyResumeOnStartup;
            s.AutonomyConsentGiven = true;
            s.AutonomyResumeOnStartup = false;
            var (pinkOn, pinkOpacity) = (s.PinkFilterEnabled, s.PinkFilterOpacity);
            var (start, stop) = (CoreBubbles.StartAction, CoreBubbles.StopAction);
            var shell = new MainShellWindow();
            shell.Show();
            var timers = new System.Collections.Generic.List<Action>();
            shell.PulseTimer = (a, _) => timers.Add(a);
            try
            {
                CoreEntitlement.HasPremiumProvider = () => true;
                Assert.True(shell.SetAutonomyEnabled(true));
                Dispatcher.UIThread.RunJobs();
                body(shell, timers);
            }
            finally
            {
                shell.Autonomy.Stop();
                Dispatcher.UIThread.RunJobs();
                PinkFilterOverlay.PulseHold = false;
                (s.PinkFilterEnabled, s.PinkFilterOpacity) = (pinkOn, pinkOpacity);
                (CoreBubbles.StartAction, CoreBubbles.StopAction) = (start, stop);
                s.AutonomyResumeOnStartup = resume;
                CoreEntitlement.HasPremiumProvider = null;
                s.AutonomyModeEnabled = s.AutonomyConsentGiven = false;
                CoreEngine.Stop();
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });

    /// <summary>WPF PulsePinkFilter's own restore after 30 s, and #441a: a slider moved during the
    /// pulse is kept rather than snapped back.</summary>
    [Fact]
    public Task PinkPulseEndsAfter30sAndKeepsASliderMove() => WithArmedShell((shell, timers) =>
    {
        var s = CoreSettings.Current;
        (s.PinkFilterEnabled, s.PinkFilterOpacity) = (false, 10);
        shell.PerformAutonomy(AutonomyActionType.PinkFilterPulse);
        Assert.Equal(30, s.PinkFilterOpacity);
        Assert.Single(timers)();
        Assert.False(s.PinkFilterEnabled);
        Assert.Equal(10, s.PinkFilterOpacity);
        Assert.False(PinkFilterOverlay.PulseHold);

        timers.Clear();
        shell.PerformAutonomy(AutonomyActionType.PinkFilterPulse);
        s.PinkFilterOpacity = 20;   // the user drags the slider mid-pulse
        Assert.Single(timers)();
        Assert.False(s.PinkFilterEnabled);
        Assert.Equal(20, s.PinkFilterOpacity);
    });

    /// <summary>Exit (app or shell close) during a pulse saves the user's own tint, not the boost.</summary>
    [Fact]
    public Task ExitDuringPinkPulseSavesTheUsersTint() => WithArmedShell((shell, _) =>
    {
        var s = CoreSettings.Current;
        (s.PinkFilterEnabled, s.PinkFilterOpacity) = (false, 10);
        shell.PerformAutonomy(AutonomyActionType.PinkFilterPulse);
        Assert.True(s.PinkFilterEnabled);
        (bool, int) saved = default;
        global::ConditioningControlPanel.Avalonia.App.SaveSettingsOnExit(shell, () => saved = (s.PinkFilterEnabled, s.PinkFilterOpacity));
        Assert.Equal((false, 10), saved);

        shell.PerformAutonomy(AutonomyActionType.PinkFilterPulse);
        shell.Close();   // the Closed handler hands the pulse back too
        Assert.Equal((false, 10), (s.PinkFilterEnabled, s.PinkFilterOpacity));
    });

    /// <summary>WPF: a running session owns the overlays, so the pink pulse is skipped.</summary>
    [Fact]
    public Task PinkPulseSkipsWhileASessionRuns() => WithArmedShell((shell, timers) =>
    {
        var s = CoreSettings.Current;
        (s.PinkFilterEnabled, s.PinkFilterOpacity) = (false, 10);
        var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            shell.StartSession(new global::ConditioningControlPanel.Models.Session { Id = "pulse_test", Name = "Pulse", Icon = "x", DurationMinutes = 1 });
            Assert.True(runner.IsRunning);
            shell.PerformAutonomy(AutonomyActionType.PinkFilterPulse);
            Assert.False(s.PinkFilterEnabled);
            Assert.Equal(10, s.PinkFilterOpacity);
            Assert.Empty(timers);
        }
        finally
        {
            runner.Stop();
            CoreSession.IsSessionRunningProvider = null;
            global::ConditioningControlPanel.Avalonia.App.Sessions = null;
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
        }
    });

    /// <summary>WPF StartStop.cs:485: stopping the engine cancels Takeover's pulses, so a restart
    /// inside 30 s can pulse again and the old timer cannot end the new run's bubbles.</summary>
    [Fact]
    public Task EngineStopRetiresPulses() => WithArmedShell((shell, timers) =>
    {
        int starts = 0, stops = 0;
        CoreBubbles.StartAction = () => starts++;
        CoreBubbles.StopAction = () => stops++;
        shell.PerformAutonomy(AutonomyActionType.StartBubbles);
        shell.OnEngineStopped();
        var stopsAfterEngineStop = stops;
        shell.PerformAutonomy(AutonomyActionType.StartBubbles);   // restart within 30 s
        Assert.Equal(2, starts);
        Assert.Equal(2, timers.Count);
        timers[0]();   // the old run's timer
        Assert.Equal(stopsAfterEngineStop, stops);
        timers[1]();
        Assert.Equal(stopsAfterEngineStop + 1, stops);
    });

    /// <summary>A Takeover preset comment is WPF Giggle (Speech.cs:240): dropped while an AI request
    /// is in flight or an AI bubble shows, kept out of chat history, sound on every fifth.</summary>
    [Fact]
    public Task PresetCommentIsWpfGiggle() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var tube = new global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            var history = tube.ChatHistory.Count;
            tube.StartThinkingAnimation();   // an AI request in flight
            tube.Giggle("preset zero");
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual("preset zero", text.Text);
            tube.StopThinkingAnimation();

            tube.Giggle("preset one");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("preset one", text.Text);
            Assert.Equal(history, tube.ChatHistory.Count);

            tube.GigglePriority("ai reply", false, aiGenerated: true);
            Dispatcher.UIThread.RunJobs();
            tube.Giggle("preset two");   // an AI bubble is up
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ai reply", text.Text);

            var sounds = Enumerable.Range(0, 10).Select(_ => tube.NextPresetGiggleSound()).ToArray();
            Assert.Equal(2, sounds.Count(x => x));
        }
        finally { tube.Close(); }
        return Task.CompletedTask;
    });
}
