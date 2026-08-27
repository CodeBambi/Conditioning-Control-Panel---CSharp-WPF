using System.Windows;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1050 - the Circe (Locked mod) sidebar auto-hid the moment the cursor left. The collapse grace
/// re-check asked <c>Panel.IsMouseOver || Strip.IsMouseOver</c>, two HIT-TEST questions, and the
/// Locked mod re-themes the strip narrower, so an ordinary drift cleared both surfaces while the
/// pointer was still visually on the HUD. The re-check is geometric now:
/// <see cref="ChaosHudWindow.CursorInGrace"/>, the pure half of it, is what these cover.
/// </summary>
public class ChaosHudGraceGeometryTests
{
    private static readonly Rect Panel = new Rect(0, 100, 300, 400);   // expanded panel
    private static readonly Rect Strip = new Rect(0, 140, 40, 320);    // the (mod-resizable) strip
    private const double Margin = 26;

    [Fact]
    public void CursorInsideThePanelStaysOpen()
    {
        Assert.True(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(150, 300), Margin));
    }

    [Fact]
    public void CursorJustOutsideTheEdgeIsStillWithinTheGrace()
    {
        // The reported drift: a few DIPs past the panel's edge, no longer over anything.
        Assert.True(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(310, 300), Margin));
        Assert.True(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(150, 90), Margin));
    }

    [Fact]
    public void ANarrowerStripDoesNotShrinkTheGrace()
    {
        // A mod re-theming the strip to 12px wide must not change the answer while the panel is up.
        var narrow = new Rect(0, 140, 12, 320);
        Assert.Equal(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(310, 300), Margin),
                     ChaosHudWindow.CursorInGrace(Panel, narrow, new Point(310, 300), Margin));
    }

    [Fact]
    public void CursorWellClearOfTheHudCollapses()
    {
        Assert.False(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(900, 300), Margin));
        Assert.False(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(150, 700), Margin));
    }

    [Fact]
    public void TheUnionCoversTheStripEvenWhenItSitsOutsideThePanel()
    {
        var strip = new Rect(0, 600, 40, 80);   // collapsed strip parked below the panel
        Assert.True(ChaosHudWindow.CursorInGrace(Panel, strip, new Point(20, 640), Margin));
    }

    [Fact]
    public void EmptyRectsAreIgnoredAndTwoEmptyRectsNeverHold()
    {
        Assert.True(ChaosHudWindow.CursorInGrace(Rect.Empty, Strip, new Point(20, 300), Margin));
        Assert.True(ChaosHudWindow.CursorInGrace(Panel, Rect.Empty, new Point(150, 300), Margin));
        Assert.False(ChaosHudWindow.CursorInGrace(Rect.Empty, Rect.Empty, new Point(0, 0), Margin));
    }

    [Fact]
    public void NegativeMarginIsTreatedAsZeroRatherThanShrinkingTheHud()
    {
        Assert.True(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(150, 300), -50));
        Assert.False(ChaosHudWindow.CursorInGrace(Panel, Strip, new Point(310, 300), -50));
    }
}
