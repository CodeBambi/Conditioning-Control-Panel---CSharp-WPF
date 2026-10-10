using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Circe's Tab, the look: the title with a padlock on every o, and the calendar's marks.
/// Ported from WPF ChasterTabRenderTests (The_title_hangs_a_padlock_on_every_o_and_leans_them_alternately,
/// The_calendar_is_a_square_a_day_for_the_demo_lock_with_the_key_after_the_last).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ChasterTabLookTests
{
    private static Task OnUi(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        body();
        return Task.CompletedTask;
    });

    [Fact]
    public void TheTitleSplitsIntoRunsAndPadlocks()
    {
        var parts = LockTitle.Split("Locktober");
        Assert.Equal(new[] { "L", "o", "ckt", "o", "ber" }, parts.Select(p => p.Run));
        Assert.Equal(new[] { false, true, false, true, false }, parts.Select(p => p.Padlock));
        Assert.Empty(LockTitle.Split(""));
        Assert.Equal(2, LockTitle.Split("OO").Count(p => p.Padlock));
    }

    [Fact]
    public Task TheTitleHangsAPadlockOnEveryOAndLeansThemAlternately() => OnUi(() =>
    {
        var title = new LockTitle { Text = "Too good to open", FontSize = 112 };
        Assert.Equal(6, title.PadlockCount);
        Assert.Equal(new[] { -10.0, 10, -10, 10, -10, 10 }, title.Leans);
        Assert.Equal(6, title.Glyphs.OfType<Image>().Count());
        Assert.All(title.Glyphs.OfType<Image>(), i => Assert.NotNull(i.Source));
        Assert.Equal(112, title.EffectiveFontSize);

        // a long name shrinks to fit on one line; it never wraps
        var natural = LockTitle.NaturalWidth(title.Text, 112);
        title.FitWidth = natural / 2;
        Assert.InRange(title.EffectiveFontSize, 55, 57);
        Assert.Equal(6, title.PadlockCount);
        title.Text = "";
        Assert.Equal(0, title.PadlockCount);
        Assert.Empty(title.Glyphs);
    });

    [Fact]
    public Task TheCalendarCrossesServedDaysRingsTonightStampsTheRestAndSticksTheKey() => OnUi(() =>
    {
        var before = ChasterHead.Service;
        ChasterHead.Service = null;
        try
        {
            var tab = new ChasterTabView();
            var today = new DateTime(2026, 10, 10, 12, 0, 0);
            var (start, end) = (today.AddDays(-2), today.AddDays(3));
            tab.BuildCalendar((start, end), today);
            var grid = tab.FindControl<global::Avalonia.Controls.Primitives.UniformGrid>("Calendar")!;
            var days = LockCalendar.CellsFor(start, end, today);
            Assert.Equal(days.Count, grid.Children.Count);
            for (var i = 0; i < days.Count; i++)
            {
                var plate = (Panel)((Border)grid.Children[i]).Child!;
                if (days[i].IsKey)
                {
                    var sticker = Assert.Single(plate.Children.OfType<Border>());
                    Assert.IsAssignableFrom<IGradientBrush>(sticker.Background);
                    Assert.IsType<Path>(sticker.Child);
                }
                else if (days[i].Served)
                {
                    var cross = Assert.Single(plate.Children.OfType<Canvas>());
                    Assert.Equal(2, cross.Children.OfType<Polyline>().Count());
                    Assert.All(cross.Children.OfType<Polyline>(), s => Assert.Equal(3, s.Points.Count));
                    Assert.Single(cross.Children.OfType<Ellipse>());
                }
                else if (days[i].Today)
                    Assert.Equal(2, Assert.Single(plate.Children.OfType<Canvas>()).Children.OfType<Path>().Count());
                else
                    Assert.NotNull(Assert.Single(plate.Children.OfType<Rectangle>()).OpacityMask);
            }

            // tonight's tag shows what is on the tab, and only while something is owed
            var tag = tab.FindControl<Border>("CalendarTag")!;
            Assert.False(tag.IsVisible);
            tab.RefreshTag(75);
            Assert.True(tag.IsVisible);
            Assert.Equal(CircesTab.Format(75), tab.FindControl<TextBlock>("TxtCalendarTag")!.Text);
            tab.RefreshTag(-30);
            Assert.False(tag.IsVisible);
            tab.RefreshTag(75);
            tab.BuildCalendar(null, today);
            Assert.False(tag.IsVisible);
        }
        finally { ChasterHead.Service = before; }
    });
}
