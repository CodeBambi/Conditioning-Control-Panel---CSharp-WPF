using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF StartExclusivesMotion / ApplyFreeTodayPulse: the spotlight's Ken Burns drift and
/// the FREE TODAY breath follow the ambient-loop gate and park with the page; the veil pill never
/// says "Lab" (tiers are Basic and Prime in copy).</summary>
public sealed class ExclusivesFlairTests
{
    [Fact]
    public Task KenBurnsAndFreePulseFollowTheGateAndPark() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var view = new ExclusivesTabView { Width = 1400, Height = 900 };
        var host = new Window { Width = 1400, Height = 900, Content = view };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            bool loops = AmbientFxCanvas.Env.AllowAmbientLoops;
            view.SetMotion(true);
            var spot = view.FindControl<Image>("SpotArtImage")!;
            var pill = view.FindControl<Border>("SpotFreeToday")!;
            Assert.Equal(loops, spot.Classes.Contains("kenburns"));
            Assert.Equal(loops, pill.Classes.Contains("pulse"));
            view.SetMotion(false);
            Assert.DoesNotContain("kenburns", spot.Classes);
            Assert.DoesNotContain("pulse", pill.Classes);

            var unlock = Loc.Get("plans_chip_unlock");
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.DoesNotContain(texts, t => t.Contains("LAB ACCESS"));
            Assert.Contains(unlock, texts);
        }
        finally { view.SetMotion(false); host.Close(); }
        return Task.CompletedTask;
    });
}
