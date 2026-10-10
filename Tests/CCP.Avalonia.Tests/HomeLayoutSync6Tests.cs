using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Nav;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Main sync #6 Home layout. The favourites drawer and the fold arrow are pinned on the 7.1.5
/// parity Home by Home/HomeLayoutTests (the sync6 drawer and arrow were duplicates and went in
/// the 2026-10-10 merge); what stays here is the window default and the uniform fit.
/// </summary>
public sealed class HomeLayoutSync6Tests
{
    [Fact]
    public async Task WindowOpensAtTheWpfDefaultAndFitsUniformly()
    {
        // ce1159e23: 1661 x 1002 DIP; 30f656f78: a 1080p-tall work area shrinks both axes.
        var (w, h) = WindowFitRule.FitPx(2000, 1200, 1920, 1040);
        Assert.Equal((1733, 1040), (w, h));
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            try
            {
                Assert.Equal(WindowFitRule.DefaultWidthDip, shell.Width);
                Assert.Equal(WindowFitRule.DefaultHeightDip, shell.Height);
            }
            finally { shell.Close(); }
            return Task.CompletedTask;
        });
    }
}
