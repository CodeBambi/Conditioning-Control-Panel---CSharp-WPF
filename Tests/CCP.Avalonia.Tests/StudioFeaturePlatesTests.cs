using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The five Studio feature pages that drew a flat wash now wear their art plates
/// (WPF ApplyFeatureArt: hero strip + side plate from Resources/features).</summary>
public sealed class StudioFeaturePlatesTests
{
    [Fact]
    public async Task Hero_and_side_plates_paint_the_feature_art()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            Func<UserControl>[] makers =
            {
                () => new FlashFeatureControl(), () => new VideoFeatureControl(), () => new SpiralFeatureControl(),
                () => new BubblePopFeatureControl(), () => new BouncingTextFeatureControl(),
            };
            foreach (var make in makers)
            {
                var card = make();
                var host = new Window { Content = card };
                host.Show();
                try
                {
                    Assert.IsType<ImageBrush>(card.FindControl<Border>("HeroArt")!.Background);
                    Assert.IsType<ImageBrush>(card.FindControl<Border>("SideArt")!.Background);
                }
                finally { host.Close(); }
            }
            return Task.CompletedTask;
        });
    }
}
