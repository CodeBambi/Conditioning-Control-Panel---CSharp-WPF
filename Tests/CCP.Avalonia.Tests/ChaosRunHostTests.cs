using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The run-lifecycle slice of WPF ChaosModeService on a stepped clock: countdown -> 4 Hz
/// ticks -> recap; nothing ticks while the overlay is hidden or paused; panic during the countdown
/// or mid-run tears everything down; the recap never saves.</summary>
public sealed class ChaosRunHostTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static ChaosRunConfig Cfg() => new() { DurationSec = 60, WaveCount = 3 };

    private static void Ticks(int n) { for (int i = 0; i < n; i++) ChaosRunHost.Tick(); }

    [Fact]
    public async Task A_run_counts_down_ticks_and_ends_on_the_recap_without_saving()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var meta = ChaosMeta.State;
            int runs = meta.RunsCompleted; long best = meta.BestScore; int sparks = meta.Sparks;
            try
            {
                ChaosRunHost.StartRun(Cfg());
                var overlay = ChaosRunHost.Overlay!;
                var state = ChaosRunHost.State!;
                Assert.True(overlay.IsVisible);
                Assert.Empty(overlay.FindControl<StackPanel>("ResultsBody")!.Children);   // ForRun: no sample recap (its bark/sfx)

                Ticks(8);   // countdown still up: the clock has not started
                Assert.Equal(0, state.ElapsedSec);

                overlay.FinishCountdown();   // the countdown's completion is BeginRun
                Assert.True(ChaosRunHost.IsDescending);
                Ticks(84);   // 21 s
                Assert.Equal(21, state.ElapsedSec, 3);
                Assert.Equal(2, state.WaveIndex);

                overlay.Hide();   // P01: hidden overlay -> nothing ticks
                Ticks(40);
                Assert.Equal(21, state.ElapsedSec, 3);
                overlay.Show();

                ChaosRunHost.ToggleManualPause();   // held: the clock waits
                Ticks(40);
                Assert.Equal(21, state.ElapsedSec, 3);
                ChaosRunHost.ToggleManualPause();

                Ticks(160);   // to 61 s: past the 60 s run
                Assert.False(ChaosRunHost.IsDescending);
                Assert.Null(ChaosRunHost.Hud);
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                overlay.UpdateLayout();
                Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "BEST STREAK");
                Assert.Equal((runs, best, sparks), (meta.RunsCompleted, meta.BestScore, meta.Sparks));   // no payout
                Assert.Same(meta, ChaosMeta.State);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Panic_during_the_countdown_and_mid_run_ends_the_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            var shell = new MainShellWindow();
            shell.Show();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            try
            {
                // During the countdown.
                ChaosRunHost.StartRun(Cfg());
                var overlay = ChaosRunHost.Overlay!;
                var hud = ChaosRunHost.Hud!;
                shell.HandlePanicKeyPress(t0);
                Assert.False(ChaosRunHost.IsActive);
                Assert.False(overlay.IsVisible || hud.IsVisible);
                overlay.FinishCountdown();   // a late countdown completion must not revive it
                Assert.False(ChaosRunHost.IsDescending);

                // Mid-run (a later, uncounted press: 3 s on).
                ChaosRunHost.StartRun(Cfg());
                overlay = ChaosRunHost.Overlay!;
                hud = ChaosRunHost.Hud!;
                overlay.FinishCountdown();
                var state = ChaosRunHost.State!;
                Ticks(8);
                shell.HandlePanicKeyPress(t0.AddSeconds(3));
                Assert.False(ChaosRunHost.IsActive || ChaosRunHost.IsDescending);
                Assert.False(overlay.IsVisible || hud.IsVisible);
                double at = state.ElapsedSec;
                Ticks(8);   // a stale timer tick after the teardown does nothing
                Assert.Equal(at, state.ElapsedSec);
            }
            finally { ChaosRunHost.ForceShutdown(); shell.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Hud_exit_surfaces_the_run_onto_the_recap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                var overlay = ChaosRunHost.Overlay!;
                overlay.FinishCountdown();
                Ticks(4);
                var exit = ChaosRunHost.Hud!.GetVisualDescendants().OfType<Button>()
                    .Single(b => b.Content is TextBlock { Text: "✖ wake up" });
                exit.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(ChaosRunHost.IsDescending);
                Assert.True(ChaosRunHost.IsActive);   // recap up until dismissed
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                overlay.Close();   // "wake up": OnDismissed tears down
                Assert.False(ChaosRunHost.IsActive);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }
}
