using System.Linq;
using System.Threading.Tasks;
using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>main-sync 7e337d085 (ccp-bugs #640): the Awareness cooldown sliders walk the shared
/// stop ladder up to an hour; the setting keeps plain seconds and the label shows "m:ss" past a minute.</summary>
public sealed class AwarenessCooldownLadderTests
{
    [Fact]
    public Task CooldownSlidersWalkTheLadderUpToAnHour() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (global, same) = (s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds);
        var host = new Window();
        try
        {
            (s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds) = (3600, 37);
            var view = new AwarenessTabView();
            host.Content = view;
            host.Show();
            Dispatcher.UIThread.RunJobs();

            var g = view.FindControl<Slider>("SliderAwarenessGlobalCooldown")!;
            Assert.Equal(73, g.Value);                       // the last stop, not clamped to 180
            Assert.Equal("60:00", view.FindControl<TextBlock>("TxtAwarenessGlobalCooldown")!.Text);
            Assert.Equal(3600, s.KeywordGlobalCooldownSeconds);
            // Off the ladder: the label keeps the stored seconds, the slider sits at the nearest stop.
            Assert.Equal("37s", view.FindControl<TextBlock>("TxtAwarenessSameWordCooldown")!.Text);
            Assert.Equal(37, s.KeywordPerKeywordCooldownSeconds);

            g.Value = 31;                                    // index 31 = 40 s
            Assert.Equal(40, s.KeywordGlobalCooldownSeconds);
            Assert.Equal("40s", view.FindControl<TextBlock>("TxtAwarenessGlobalCooldown")!.Text);
            g.Value = 50;                                    // the 30 s stops: 150 + 2*30 = 210 s
            Assert.Equal(210, s.KeywordGlobalCooldownSeconds);
            Assert.Equal("3:30", view.FindControl<TextBlock>("TxtAwarenessGlobalCooldown")!.Text);

            // c9ad99c04 (#1321): the page grid follows the window up to its MaxWidth instead of
            // shrinking to its content (WPF measured 606 DIP in a 1400 DIP window).
            host.Width = 1400; host.Height = 900;
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
            var page = view.GetLogicalDescendants().OfType<Grid>().First(x => x.MaxWidth == 1300);
            Assert.Equal(1300, page.Bounds.Width, 0);
        }
        finally
        {
            host.Close();
            (s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds) = (global, same);
            CoreSettings.Save();
        }
        return Task.CompletedTask;
    });
}
