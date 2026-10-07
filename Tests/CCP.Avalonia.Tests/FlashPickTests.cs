using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 7.0.3 flash picks on this head: #627 shuffle bag, #658 images-per-flash range.</summary>
public sealed class FlashPickTests
{
    /// <summary>#627: the folder is re-listed per burst, and still every file comes up once
    /// before any repeats (with replacement shows ~63% of 300).</summary>
    [Fact]
    public void A_relisted_folder_is_walked_in_full_before_anything_repeats()
    {
        FlashOverlay.DiskBag.Reset();
        var seen = new HashSet<string>();
        for (var i = 0; i < 300; i++)
        {
            var files = Enumerable.Range(0, 300).Select(n => $"/img/{n:000}.png").ToList();   // fresh list per burst
            seen.Add(FlashOverlay.NextPath(files)!);
        }
        Assert.Equal(300, seen.Count);
    }

    /// <summary>#658: with the switch on, every burst loads a count inside [Fewest, Images]
    /// (either order), and both ends come up; a test override still wins.</summary>
    [Fact]
    public void A_random_burst_rolls_its_count_inside_the_range()
    {
        var s = new ConditioningControlPanel.Models.AppSettings { SimultaneousImagesRandom = true, SimultaneousImagesMin = 2, SimultaneousImages = 6 };
        var rng = new System.Random(5);
        var seen = new HashSet<int>();
        for (var i = 0; i < 500; i++) seen.Add(FlashOverlay.BurstCount(null, s, rng));
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, seen.OrderBy(n => n));
        Assert.Equal(9, FlashOverlay.BurstCount(9, s, rng));
    }

    /// <summary>#658: the Random image count switch saves and reveals the Fewest row.</summary>
    [Fact]
    public async Task Random_image_count_switch_reveals_the_floor_and_saves()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (random, min) = (s.SimultaneousImagesRandom, s.SimultaneousImagesMin);
            s.SimultaneousImagesRandom = false;
            var card = new FlashFeatureControl();
            var host = new Window { Content = card };
            host.Show();
            try
            {
                var row = card.FindControl<Grid>("RowImagesMin")!;
                Assert.False(row.IsVisible);
                card.FindControl<CheckBox>("ChkRandomImages")!.IsChecked = true;
                Assert.True(s.SimultaneousImagesRandom);
                Assert.True(row.IsVisible);
                card.FindControl<Slider>("SliderImagesMin")!.Value = 3;
                Assert.Equal(3, s.SimultaneousImagesMin);
            }
            finally
            {
                host.Close();
                (s.SimultaneousImagesRandom, s.SimultaneousImagesMin) = (random, min);
            }
            return Task.CompletedTask;
        });
    }
}
