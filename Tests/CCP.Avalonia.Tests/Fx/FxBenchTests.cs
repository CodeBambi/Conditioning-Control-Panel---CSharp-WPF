using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Xunit;

namespace CCP.Avalonia.Tests.Fx;

/// <summary>The ambient layers' UI-thread cost per frame (sim + Skia paint), printed per preset.</summary>
public sealed class FxBenchTests(ITestOutputHelper output)
{
    [Fact]
    public Task EveryPresetPaintsWellInsideAThirtyFpsFrame() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        var s = CoreSettings.Current;
        var perf = s.PerformanceMode;
        try
        {
            s.PerformanceMode = false;   // Quality tier: the full particle budget
            var rows = FxBench.Run(60);
            output.WriteLine(FxBench.Format(rows));
            Assert.Equal(FxBench.Presets.Length, rows.Count);
            // A loose ceiling: a 33 ms frame shared with layout and everything else. The number that
            // matters is printed; this only catches a layer gone pathological.
            Assert.All(rows, r => Assert.True(r.MsPerFrame < 16, $"{r.Name}: {r.MsPerFrame:0.00} ms/frame"));
            Assert.All(rows, r => Assert.True(r.BackingW > 0 && r.BackingH > 0, $"{r.Name} never painted"));
        }
        finally { s.PerformanceMode = perf; }
        return Task.CompletedTask;
    });
}
