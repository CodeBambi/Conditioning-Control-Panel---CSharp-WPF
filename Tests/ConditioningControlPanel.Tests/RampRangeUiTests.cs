using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The range-ramp half of <see cref="IntensityRampFeatureControl"/>, checked the way the rest of
/// the Studio rack is checked: realize the real XAML and assert the things that compile fine and
/// then fail silently in front of a user.
///
/// <para>Three of those here. A WPF <see cref="Slider"/> defaults to Maximum=10, which would clamp
/// a 0..300 percent on the way in and write the clamped value back out. The mode toggle must ship
/// exactly two items in the order the code-behind indexes (<c>SelectedIndex == 1</c> means Range).
/// And the preview polyline must actually receive points once the canvas has a size - a Canvas
/// that arranges to zero draws nothing and looks like a styling bug rather than a dead handler.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RampRangeUiTests
{
    private static void Realize(FrameworkElement element, double width = 488, double height = 700)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
        Assert.True(element.DesiredSize.Height > 0,
            $"{element.GetType().Name} measured to zero height - its content did not realize");
    }

    [Fact]
    public void TheModeToggleShipsExactlyTheTwoModesInIndexOrder()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var panel = new IntensityRampFeatureControl();
            Realize(panel);

            var combo = panel.FindName("CmbRampMode") as ComboBox;
            Assert.True(combo != null, "the ramp panel lost CmbRampMode");
            Assert.Equal(2, combo!.Items.Count);
            // The code-behind maps index 1 -> Range and everything else -> Multiplier, so the
            // ORDER is load-bearing, not cosmetic.
            Assert.Equal("Multiplier", ((ComboBoxItem)combo.Items[0]).Tag);
            Assert.Equal("Range", ((ComboBoxItem)combo.Items[1]).Tag);
        });
    }

    [Fact]
    public void TheRangeSlidersCoverTheWholeLegalPercentSpan()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var panel = new IntensityRampFeatureControl();
            Realize(panel);

            foreach (var name in new[] { "SliderRangeStart", "SliderRangeEnd" })
            {
                var slider = panel.FindName(name) as Slider;
                Assert.True(slider != null, $"the ramp panel lost {name}");
                Assert.Equal(0, slider!.Minimum);
                Assert.Equal(300, slider.Maximum);
                Assert.Equal(100, slider.Value); // the no-op default
            }
        });
    }

    [Fact]
    public void MultiplierModeIsTheDefaultAndHidesTheRangePair()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var panel = new IntensityRampFeatureControl();
            Realize(panel);

            // Two spellings of the same factor: never both on screen.
            Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("RowMultiplier")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)panel.FindName("RowRangeStart")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)panel.FindName("RowRangeEnd")).Visibility);
        });
    }

    [Fact]
    public void TheCurvePreviewDrawsOnceTheCanvasHasASize()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var panel = new IntensityRampFeatureControl();
            Realize(panel);

            var canvas = panel.FindName("CurvePreviewCanvas") as Canvas;
            Assert.True(canvas != null, "the ramp panel lost CurvePreviewCanvas");
            Assert.True(canvas!.ActualWidth > 1 && canvas.ActualHeight > 1,
                $"the preview canvas arranged to {canvas.ActualWidth}x{canvas.ActualHeight} - it will never draw");

            var line = panel.FindName("CurvePreviewLine") as Polyline;
            Assert.True(line != null, "the ramp panel lost CurvePreviewLine");
            Assert.True(line!.Points.Count > 2,
                "the preview polyline got no points - RedrawPreview never ran off SizeChanged");
        });
    }
}
