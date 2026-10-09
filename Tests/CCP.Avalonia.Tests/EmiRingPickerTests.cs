using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiRingPicker: the tiles ARE EmiState.Pins through EmiSuggester, over the real
/// catalogue with its card art; six is the whole ring; "let her choose" clears the store.</summary>
public sealed class EmiRingPickerTests
{
    [Fact]
    public Task TilesWriteTheOnePinStore() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var pins = EmiState.Current.Pins;
        var saved = pins.ToList();
        var host = new Window { Width = 700, Height = 600 };
        try
        {
            pins.Clear();
            pins.Add("vault");   // pinned elsewhere (the ring's right-click) before the wall opens
            var picker = new EmiRingPicker();
            host.Content = picker;
            host.Show();
            Dispatcher.UIThread.RunJobs();

            var tiles = picker.FindControl<WrapPanel>("PnlRing")!.Children.OfType<ToggleButton>().ToList();
            Assert.Equal(EmiTargets.All.Where(t => t.Available).Select(t => t.Id), tiles.Select(t => (string)t.Tag!));
            ToggleButton Tile(string id) => tiles.Single(t => (string)t.Tag! == id);
            Assert.True(Tile("vault").IsChecked);
            Assert.Contains(tiles, t => t.GetLogicalDescendants().OfType<Image>().Any(i => i.Source != null));

            var free = tiles.Where(t => t.IsEnabled && t.IsChecked != true).Take(5).ToList();
            foreach (var t in free) t.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(EmiSuggester.MaxPins, pins.Count);
            Assert.All(free, t => Assert.Contains((string)t.Tag!, pins));
            // Full: every unchecked tile is out of reach, so the seventh is seen coming.
            Assert.All(tiles.Where(t => t.IsChecked != true), t => Assert.False(t.IsEnabled));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("emi_desk_ring_full"), picker.HintText);

            Tile("vault").IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("vault", pins);

            picker.ResetPins();
            Assert.Empty(pins);
            Assert.All(tiles, t => Assert.False(t.IsChecked));
            Assert.False(picker.CanReset);
        }
        finally
        {
            host.Close();
            pins.Clear(); pins.AddRange(saved);
        }
        return Task.CompletedTask;
    });
}
