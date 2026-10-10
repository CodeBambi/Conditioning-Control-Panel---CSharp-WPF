using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// FX law (AGENTS.md "GPU CACHE + 60 Hz TRAP"): an infinite Avalonia <c>Animation</c> anywhere in a
/// window makes the WHOLE window compose at 60 Hz, which the owner felt as lag. Ambient loops ride
/// the shared 30 fps beat (<see cref="BeatLoop"/>, <see cref="VisibleBeat"/>, BreathClock,
/// FrameClock). The scan keeps the infinite Animation from coming back; the rest pins the tools.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class NoInfiniteAnimationTests
{
    /// <summary>
    /// Files allowed to say "IterationCount ... Infinite" in live code or markup, each with its
    /// reason. EMPTY on purpose: add a line only with a justification a reviewer can check.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    private static readonly Regex Infinite = new(
        @"IterationCount\s*(\.|=\s*""?\s*)\s*Infinite|IterationType\s*\.\s*Infinite|IterationCount\s*\.\s*Parse",
        RegexOptions.IgnoreCase);

    private static readonly Regex CsComments = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex XmlComments = new(@"<!--.*?-->", RegexOptions.Singleline);

    [Fact]
    public void TheHeadHasNoInfiniteAnimation()
    {
        var head = Path.Combine(RepoRoot(), "CCP.Avalonia");
        var hits = new List<string>();
        foreach (var path in Directory.EnumerateFiles(head, "*.*", SearchOption.AllDirectories))
        {
            bool cs = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            bool axaml = path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);
            if (!cs && !axaml) continue;
            var sep = Path.DirectorySeparatorChar;
            if (path.Contains($"{sep}obj{sep}") || path.Contains($"{sep}bin{sep}")) continue;

            var code = (cs ? CsComments : XmlComments).Replace(File.ReadAllText(path), "");
            if (!Infinite.IsMatch(code)) continue;
            var rel = Path.GetRelativePath(head, path).Replace('\\', '/');
            if (!Allowed.ContainsKey(rel)) hits.Add(rel);
        }
        Assert.True(hits.Count == 0,
            "An infinite Animation composes its whole window at 60 Hz. Move the loop to the shared beat "
            + "(Helpers/VisibleBeat, BeatLoop, BreathClock) or justify it in Allowed: " + string.Join(", ", hits));
    }

    [Fact]
    public void TheScanSeesBothSpellings()
    {
        Assert.Matches(Infinite, "IterationCount = IterationCount.Infinite,");
        Assert.Matches(Infinite, "<Animation Duration=\"0:0:4\" IterationCount=\"INFINITE\">");
        Assert.Matches(Infinite, "new IterationCount(0, IterationType.Infinite)");
        Assert.DoesNotMatch(Infinite, "IterationCount = new IterationCount(3),");
    }

    [Fact]
    public void TheStatusDotsGlowThroughASiblingLayerNeverAnEffect()
    {
        var head = Path.Combine(RepoRoot(), "CCP.Avalonia");
        var shell = CsComments.Replace(File.ReadAllText(Path.Combine(head, "Views", "Windows", "MainShellWindow.TabFxTakeoverLabStatus.cs")), "");
        Assert.DoesNotContain("DropShadowEffect", shell);
        foreach (var (tab, dot) in new[]
                 {
                     ("AwarenessTabView", "AwarenessStatusDot"), ("BlinkTrainerTabView", "BlinkTrainerStatusDot"),
                     ("HapticsTabView", "HapticStatusDot"), ("SheListeningTabView", "SL_StatusDot"),
                 })
        {
            var axaml = File.ReadAllText(Path.Combine(head, "Views", "Tabs", tab + ".axaml"));
            int glow = axaml.IndexOf("Tag=\"StatusGlow\"", StringComparison.Ordinal);
            int at = axaml.IndexOf("x:Name=\"" + dot + "\"", StringComparison.Ordinal);
            Assert.True(glow >= 0 && at > glow && at - glow < 400, tab + ": the glow layer must sit just before " + dot);
        }
    }

    [Fact]
    public void BeatCurvesStayInRangeAndHitTheirKeys()
    {
        var keys = new[] { (0d, 0.15), (0.34, 0.95), (1d, 0.15) };
        Assert.Equal(0.15, BeatLoop.Keys(0, keys, sine: true), 6);
        Assert.Equal(0.95, BeatLoop.Keys(0.34, keys, sine: true), 6);
        Assert.Equal(0.15, BeatLoop.Keys(1, keys, sine: true), 6);
        Assert.Equal(0.15, BeatLoop.Keys(-3, keys), 6);    // holds the first key before the track
        Assert.Equal(0.15, BeatLoop.Keys(9, keys), 6);     // and the last one after it
        Assert.Equal(0.55, BeatLoop.Keys(0.17, keys), 6);  // linear: halfway up the first leg
        for (double t = 0; t < 30; t += 1.0 / 30)
        {
            Assert.InRange(BeatLoop.Breath(t, 2.5), 0, 1);
            Assert.InRange(BeatLoop.PingPong(t, 0.45), 0, 1);
            Assert.InRange(BeatLoop.Saw(t, 4), 0, 1);
            Assert.InRange(BeatLoop.Keys(BeatLoop.Saw(t, 1.45), keys, sine: true), 0.15, 0.95);
        }
        Assert.Equal(1, BeatLoop.PingPong(0.45, 0.45), 6);
        Assert.Equal(0, BeatLoop.PingPong(0.90, 0.45), 6);
    }

    [Fact]
    public async Task ALoopRunsOnlyWhileShownAndAllowed_AndParksAtRest()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
            Window? w = null;
            try
            {
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = false;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();

                var decor = new Border { Width = 20, Height = 20 };
                var signal = new Border { Width = 20, Height = 20 };
                var page = new StackPanel { Children = { decor, signal } };
                int rests = 0;
                bool wanted = true;
                var decorBeat = VisibleBeat.Attach(decor, t => decor.Opacity = 0.5, () => { decor.Opacity = 1; rests++; }, () => wanted);
                var signalBeat = VisibleBeat.Attach(signal, t => signal.Opacity = 0.5, () => signal.Opacity = 1, decoration: false);
                Assert.False(decorBeat.IsRunning);            // not in a window yet
                Assert.Equal(1, decor.Opacity);

                w = new Window { Content = page, Width = 200, Height = 200 };
                w.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(decorBeat.IsRunning && signalBeat.IsRunning, "the loops never started in a shown window");
                Assert.Equal(0.5, decor.Opacity);              // the first step paints at once

                page.IsVisible = false;                        // another tab
                Assert.False(decorBeat.IsRunning || signalBeat.IsRunning, "a loop kept ticking on a hidden page");
                Assert.Equal(1, decor.Opacity);                // parked at rest
                page.IsVisible = true;
                Assert.True(decorBeat.IsRunning && signalBeat.IsRunning, "coming back did not re-arm the loops");

                s.MotionLevel = MotionLevel.Off;               // the user turns motion off
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.False(decorBeat.IsRunning, "decoration kept moving at motion Off");
                Assert.True(signalBeat.IsRunning, "a loading signal must not be silenced by the motion level");
                Assert.Equal(1, decor.Opacity);
                s.MotionLevel = MotionLevel.Full;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.True(decorBeat.IsRunning, "motion Full did not re-arm the decoration");

                wanted = false;                                // the state that asked for it ended
                decorBeat.Refresh();
                Assert.False(decorBeat.IsRunning);
                wanted = true;
                decorBeat.Refresh();
                Assert.True(decorBeat.IsRunning);

                int before = rests;
                w.Close();
                w = null;
                Dispatcher.UIThread.RunJobs();
                Assert.False(decorBeat.IsRunning || signalBeat.IsRunning, "a loop outlived its window");
                Assert.True(rests > before);
            }
            finally
            {
                w?.Close();
                s.MotionLevel = motion;
                s.PerformanceMode = perf;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ATokenLoopStopsWithItsTokenAndDropsItselfWhenItsTargetLeaves()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Window? w = null;
            try
            {
                var a = new Border { Width = 10, Height = 10 };
                var b = new Border { Width = 10, Height = 10 };
                var page = new StackPanel { Children = { a, b } };
                w = new Window { Content = page, Width = 100, Height = 100 };
                w.Show();
                Dispatcher.UIThread.RunJobs();

                using var cts = new CancellationTokenSource();
                var byToken = BeatLoop.Run(a, cts.Token, t => a.Opacity = 0.4);
                Assert.True(byToken.IsRunning);
                Assert.Equal(0.4, a.Opacity);
                cts.Cancel();
                Assert.False(byToken.IsRunning, "cancelling the token must stop the loop");

                var orphan = BeatLoop.Run(b, CancellationToken.None, t => b.Opacity = 0.4);
                Assert.True(orphan.IsRunning);
                page.Children.Remove(b);                       // the row is rebuilt
                for (int i = 0; i < 5 && orphan.IsRunning; i++)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(40);
                }
                Assert.False(orphan.IsRunning, "a loop kept ticking after its target left the window");
            }
            finally { w?.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheTierBadgeHumsOnTheBeatWithoutAnEffect()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
            Window? w = null;
            try
            {
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = false;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                var badge = new TierBadge { Tier = 2, Width = 120, Height = 60 };
                w = new Window { Content = badge, Width = 300, Height = 200 };
                w.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(badge.IsAnimating, "the badge never hummed - the test proves nothing");
                Assert.Null(badge.TierSign.Effect);            // the glow is a picture under the sign
                foreach (var child in badge.Children) Assert.InRange(child.Opacity, 0, 1);

                badge.IsVisible = false;
                Assert.False(badge.IsAnimating);
                Assert.Equal(1, badge.TierSign.Opacity);       // parked at rest
            }
            finally
            {
                w?.Close();
                s.MotionLevel = motion;
                s.PerformanceMode = perf;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
            }
            return Task.CompletedTask;
        });
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
