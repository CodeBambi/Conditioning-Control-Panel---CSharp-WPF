using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests.Home;

/// <summary>
/// The smoothness lane (owner, 2026-10-09: "as smooth as the WPF one"). Pins the structural
/// findings from the owner's Release trace (Platform/RenderBudget.cs): the GPU cache holds the
/// whole Home working set, the presentation switches parse, and the shell chrome's ambient loops
/// ride the shared 30 fps beat with no Effect in the header (an infinite Avalonia Animation or an
/// Effect breath composed the whole window at 60 Hz, each frame a full-window blit).
/// </summary>
public sealed class SmoothnessTests
{
    [Fact]
    public void Gpu_cache_holds_the_whole_Home_working_set_with_headroom()
    {
        Assert.True(RenderBudget.GpuResourceCacheBytes >= RenderBudget.HomeWorkingSetBytes * 3 / 2,
            $"cache {RenderBudget.GpuResourceCacheBytes} vs working set {RenderBudget.HomeWorkingSetBytes}");
        // Avalonia's default (~28 MB) is below the working set: that is the bug being pinned.
        Assert.True(1024L * 600 * 4 * 12 < RenderBudget.HomeWorkingSetBytes);
    }

    [Fact]
    public void Presentation_switches_parse_in_priority_order_and_ignore_junk()
    {
        static Win32CompositionMode? Map(string s) => s switch
        {
            "winui" => Win32CompositionMode.WinUIComposition,
            "swapchain" => Win32CompositionMode.LowLatencyDxgiSwapChain,
            "redirection" => Win32CompositionMode.RedirectionSurface,
            _ => null,
        };
        Assert.Null(RenderBudget.ParseModes<Win32CompositionMode>(null, Map));
        Assert.Null(RenderBudget.ParseModes<Win32CompositionMode>("  ", Map));
        Assert.Null(RenderBudget.ParseModes<Win32CompositionMode>("nonsense", Map));
        var modes = RenderBudget.ParseModes<Win32CompositionMode>(" SwapChain, redirection ,swapchain", Map)!;
        Assert.Equal(new[] { Win32CompositionMode.LowLatencyDxgiSwapChain, Win32CompositionMode.RedirectionSurface }, modes);
    }

    [Fact]
    public void Adapter_choice_defaults_to_the_primary_and_honours_index_or_name()
    {
        var names = new string?[] { "Intel(R) Graphics", "NVIDIA GeForce RTX 5080" };
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, null));
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, ""));
        Assert.Equal(1, RenderBudget.ChooseAdapter(names, "1"));
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, "7"));      // out of range: primary
        Assert.Equal(1, RenderBudget.ChooseAdapter(names, "nvidia"));
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, "amd"));    // unknown name: primary
        Assert.Equal(0, RenderBudget.ChooseAdapter(Array.Empty<string?>(), "1"));
    }

    [Fact]
    public Task Header_halo_is_a_BoxShadow_layer_with_no_Effect_and_breathes_on_the_beat() =>
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

                var host = shell.FindControl<Border>("HeaderBannerGlowHost");
                var layer = shell.FindControl<Border>("HeaderBannerGlowLayer");
                Assert.NotNull(host);
                Assert.NotNull(layer);
                Assert.Null(host!.Effect);
                Assert.Null(layer!.Effect);
                // No container in the header row wears an Effect (one makes its whole subtree an
                // offscreen layer re-rendered whenever anything inside moves). The static text glows
                // on the TextBlocks themselves stay: nothing animates them, so they render once.
                var header = shell.FindControl<Control>("HeaderBannerHost");
                Assert.NotNull(header);
                var scope = header!.GetVisualAncestors().OfType<Grid>().First();
                Assert.All(scope.GetVisualDescendants().OfType<Visual>().Where(v => v.Effect != null),
                    v => Assert.IsType<TextBlock>(v));
                // The breath, when it runs, is a BreathClock (30 fps beat); a headless shell is never
                // the active window, so the halo rests at GlowRest and no 60 Hz Animation is attached.
                Assert.False(shell.BannerBreathRunning);
                Assert.False(shell.XpMeniscusBreathRunning);
                Assert.Equal(ConditioningControlPanel.Fx.BannerFxRules.GlowRest, layer.Opacity, 3);
                Assert.True(layer.BoxShadow.Count > 0);
                Assert.Equal(ConditioningControlPanel.Fx.BannerFxRules.GlowBlur, layer.BoxShadow[0].Blur, 3);
            }
            finally
            {
                shell?.Close();
                BoardHeadTests.Unpin();
            }
            return Task.CompletedTask;
        });
}
