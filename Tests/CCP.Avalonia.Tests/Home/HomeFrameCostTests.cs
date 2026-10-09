using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests.Home;

/// <summary>
/// The dashboard lag (owner, 2026-10-09: "still only on dashboard ... unbearable"). The Home grid's
/// fog repaints at 30 fps under every tile, and Avalonia's compositor redraws everything under a
/// dirty rect, so each tile must be cheap to redraw: its art + title face is a BitmapCache, and an
/// on tile's breathing glow is a sibling layer, never an Effect on the tile itself (an Effect makes
/// the whole tile an offscreen layer re-rendered and blurred every animation frame).
/// Set CCP_HOME_BENCH=1 to print the per-frame cost of the full Home (headless Skia, CPU).
/// </summary>
public sealed class HomeFrameCostTests
{
    private static Task WithHome(Action<MainShellWindow, SettingsTabView> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            BoardHeadTests.EnsureApp();
            BoardHeadTests.Pin();
            MainShellWindow? shell = null;
            try
            {
                shell = new MainShellWindow { Width = 1600, Height = 1000 };
                shell.Show();
                for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                Dispatcher.UIThread.RunJobs();
                body(shell, shell.Named<SettingsTabView>("SettingsTab")!);
            }
            finally
            {
                shell?.Close();
                BoardHeadTests.Unpin();
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Tile_faces_are_cached_and_the_on_glow_is_not_an_effect_on_the_tile() => WithHome((shell, tab) =>
    {
        var cards = tab.GetVisualDescendants().OfType<FeatureCard>().ToList();
        var splits = tab.GetVisualDescendants().OfType<SplitFeatureCard>().ToList();
        Assert.NotEmpty(cards);
        Assert.NotEmpty(splits);
        foreach (var c in cards.Cast<UserControl>().Concat(splits))
        {
            c.IsActiveSet(true);
            Dispatcher.UIThread.RunJobs();
            var root = c.FindControl<Border>("RootBorder")!;
            Assert.Null(root.Effect);
            Assert.IsType<BitmapCache>(c.FindControl<Panel>("StaticFace")!.CacheMode);
            var glow = c.FindControl<Border>("GlowLayer")!;
            Assert.True(glow.BoxShadow.Count > 0);
            c.IsActiveSet(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, glow.Opacity);   // off: no glow
        }
    });

    [Fact]
    public Task Bench_home_frame_cost() => WithHome((shell, tab) =>
    {
        if (Environment.GetEnvironmentVariable("CCP_HOME_BENCH") != "1") return;
        var fx = tab.FindControl<AmbientFxCanvas>("MosaicFx")!;
        foreach (var c in tab.GetVisualDescendants().OfType<UserControl>().Where(u => u is FeatureCard or SplitFeatureCard).Take(6))
            c.IsActiveSet(true);
        void Frame()
        {
            fx.StepForTests(1);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        for (int i = 0; i < 20; i++) Frame();
        const int n = 90;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) Frame();
        sw.Stop();
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-home-bench.txt"), FormattableString.Invariant($"HOME BENCH {sw.Elapsed.TotalMilliseconds / n:0.00} ms/frame over {n} frames") + Environment.NewLine);
    });
}

internal static class CardTestExt
{
    public static void IsActiveSet(this UserControl c, bool on)
    {
        if (c is FeatureCard f) f.IsActive = on;
        else if (c is SplitFeatureCard s) { s.IsActiveA = on; s.IsActiveB = on; }
    }
}
