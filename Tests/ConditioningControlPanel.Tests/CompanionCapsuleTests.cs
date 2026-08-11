using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Pins both halves of the finding behind <see cref="CompanionCapsule"/>.
///
/// <para>The mockup's chips are <c>border-radius:999px</c>. CSS clamps that to a stadium; WPF does
/// not — <see cref="Border"/> clamps the X and Y radii to half the width and half the height
/// independently, so an over-large radius draws a full ellipse. The theme's comment used to claim
/// the opposite, and every chip, pill, tag, badge and switch on the Companion page was rendering as
/// a lens because of it.</para>
///
/// <para>So: one test proves the ellipse (the reason the helper exists), one proves the helper's
/// rule (radius = height / 2), and one proves the shape that comes out is actually a stadium. If
/// someone ever "simplifies" the attached property back to a constant, the third one falls over.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionCapsuleTests
{
    private const double ChipWidth = 110;
    private const double ChipHeight = 26;

    // Every WPF render suite shares one STA thread, via WpfRenderHarness. WPF caches a
    // ResourceDictionary per pack URI, so these suites and the Settings-section suites end
    // up holding the SAME Brush and Style instances - and those take thread affinity from
    // whichever thread realizes them first. Two threads means the second one dies with
    // "The calling thread cannot access this object because a different thread owns it".
    // The harness leaves Application.Resources empty outside its own bodies, so nothing
    // here sees a theme it did not merge itself.
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>Lays a chip-sized Border out inside a host, the way it lives on the page.</summary>
    private static Border Realize(Action<Border>? configure = null)
    {
        var border = new Border
        {
            Width = ChipWidth,
            Height = ChipHeight,
            Background = Brushes.White
        };
        configure?.Invoke(border);

        var host = new Grid { Width = ChipWidth, Height = ChipHeight };
        host.Children.Add(border);
        host.Measure(new Size(ChipWidth, ChipHeight));
        host.Arrange(new Rect(0, 0, ChipWidth, ChipHeight));
        host.UpdateLayout();
        return border;
    }

    /// <summary>
    /// Fraction of the box the shape actually paints. A rectangle is 1.0, a stadium with r = h/2 is
    /// about 0.95, and an inscribed ellipse is pi/4 = 0.785 — which is the number that gave the bug
    /// away.
    /// </summary>
    private static double FillRatio(Border border)
    {
        var bitmap = new RenderTargetBitmap((int)ChipWidth, (int)ChipHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(border);

        int stride = (int)ChipWidth * 4;
        var pixels = new byte[stride * (int)ChipHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        int painted = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 128) painted++;
        }
        return painted / (ChipWidth * ChipHeight);
    }

    [Fact]
    public void AnOverLargeCornerRadius_DrawsAnEllipse_NotAStadium()
    {
        // The whole reason CompanionCapsule exists. 999 was the literal translation of the mockup
        // and it is wrong in WPF: pi/4 of the box, an inscribed ellipse, a lens.
        OnStaThread(() =>
        {
            var lens = Realize(b => b.CornerRadius = new CornerRadius(999));
            double ratio = FillRatio(lens);

            Assert.True(ratio < 0.85,
                $"a 999 corner radius filled {ratio:P1} of the box — if this is now stadium-shaped " +
                "(~95%), WPF changed and CompanionCapsule can be reconsidered");
            Assert.True(ratio > 0.70, $"unexpectedly empty render ({ratio:P1}) — the harness is wrong, not the claim");
        });
    }

    [Fact]
    public void TheCapsule_DrawsAStadium()
    {
        OnStaThread(() =>
        {
            var capsule = Realize(b => CompanionCapsule.SetIsCapsule(b, true));
            CompanionCapsule.Apply(capsule);

            double ratio = FillRatio(capsule);
            Assert.True(ratio > 0.92, $"the capsule filled only {ratio:P1} of the box — that is not a stadium");
        });
    }

    [Fact]
    public void TheRuleIsHalfTheRenderedHeight()
    {
        OnStaThread(() =>
        {
            var border = Realize(b => CompanionCapsule.SetIsCapsule(b, true));
            CompanionCapsule.Apply(border);

            Assert.Equal(ChipHeight / 2, border.CornerRadius.TopLeft);
            Assert.Equal(ChipHeight / 2, border.CornerRadius.BottomRight);
        });
    }

    [Fact]
    public void ItTracksAHeightChange_AndCostsNothingWhenNothingMoved()
    {
        OnStaThread(() =>
        {
            var border = Realize(b => CompanionCapsule.SetIsCapsule(b, true));
            CompanionCapsule.Apply(border);
            Assert.Equal(13, border.CornerRadius.TopLeft);

            // A chip that grows (a bigger font, a wrapped label, the 41px input) must re-pin.
            border.Height = 41;
            border.Measure(new Size(ChipWidth, 41));
            border.Arrange(new Rect(0, 0, ChipWidth, 41));
            border.UpdateLayout();
            CompanionCapsule.Apply(border);
            Assert.Equal(20.5, border.CornerRadius.TopLeft);

            // And an identical re-application is a no-op: the guard against a host that could
            // otherwise turn this into a write-per-frame loop.
            var before = border.CornerRadius;
            CompanionCapsule.Apply(border);
            Assert.Equal(before, border.CornerRadius);
        });
    }

    [Fact]
    public void AnUnarrangedBorderIsLeftAlone()
    {
        // ActualHeight is 0 before the first layout pass — half of nothing is not a radius, and
        // writing one would flash a square chip on the first frame.
        OnStaThread(() =>
        {
            var border = new Border();
            CompanionCapsule.SetIsCapsule(border, true);
            CompanionCapsule.Apply(border);
            Assert.Equal(new CornerRadius(0), border.CornerRadius);
        });
    }

    [Fact]
    public void TurningItOffDetaches_AndLeavesTheRadiusWhereItIs()
    {
        OnStaThread(() =>
        {
            var border = Realize(b => CompanionCapsule.SetIsCapsule(b, true));
            CompanionCapsule.Apply(border);
            Assert.Equal(13, border.CornerRadius.TopLeft);

            CompanionCapsule.SetIsCapsule(border, false);
            border.Height = 41;
            border.Measure(new Size(ChipWidth, 41));
            border.Arrange(new Rect(0, 0, ChipWidth, 41));
            border.UpdateLayout();

            // Detached: the handler is gone, so the radius is whatever it was.
            Assert.Equal(13, border.CornerRadius.TopLeft);
        });
    }

    [Fact]
    public void ItIgnoresAnythingThatIsNotABorder()
    {
        // The property is set from styles and templates; landing it on a Grid must be inert rather
        // than an InvalidCastException at tree-build time.
        OnStaThread(() =>
        {
            var grid = new Grid();
            CompanionCapsule.SetIsCapsule(grid, true);
            Assert.True(CompanionCapsule.GetIsCapsule(grid));
        });
    }
}
