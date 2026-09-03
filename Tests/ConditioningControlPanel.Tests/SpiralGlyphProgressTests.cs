using System;
using System.IO;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE SPIRAL GLYPH'S ONE NUMBER — devotion progress toward the next stage, the
/// fraction that decides where the dot sits on the arm.
///
/// The rules pinned here: a null or non-positive `next_at` is the FINAL stage (the
/// server sends no threshold above the top rung) and reads as a full arc, not an
/// empty one; everything else is days/threshold clamped hard to 0..1 so a wire
/// value nobody sanitised lands on an end of the arc rather than off the control;
/// and day 0 - the real pre-begin rung - is an empty arc, not a hidden one.
///
/// Pure maths on purpose: the glyph is a WPF control, but nothing about WHERE the
/// dot goes needs a dispatcher to test.
/// </summary>
public class SpiralGlyphProgressTests
{
    [Fact]
    public void ZeroDays_IsEmptyArc()
    {
        Assert.Equal(0.0, SpiralGlyph.ComputeProgress(0, 7));
    }

    [Fact]
    public void MidClimb_IsTheFraction()
    {
        Assert.Equal(3 / 7.0, SpiralGlyph.ComputeProgress(3, 7), 10);
        Assert.Equal(0.5, SpiralGlyph.ComputeProgress(15, 30), 10);
    }

    [Fact]
    public void AtTheThreshold_IsFull()
    {
        // The day the stage flips: the server may still be reporting the old
        // next_at for a beat, and the arc must read as complete, not 100%-and-a-bit.
        Assert.Equal(1.0, SpiralGlyph.ComputeProgress(7, 7));
    }

    [Fact]
    public void PastTheThreshold_ClampsToFull()
    {
        Assert.Equal(1.0, SpiralGlyph.ComputeProgress(999, 7));
    }

    [Fact]
    public void NullNextAt_IsTheFinalStage_AndReadsFull()
    {
        // No rung above this one. "Nowhere left to go" is a completed arc.
        Assert.Equal(1.0, SpiralGlyph.ComputeProgress(0, null));
        Assert.Equal(1.0, SpiralGlyph.ComputeProgress(420, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NonPositiveNextAt_IsTreatedAsFinal(int nextAt)
    {
        // A zero threshold would divide by zero and a negative one would flip the
        // dot inward; both are junk, and both take the final-stage reading.
        Assert.Equal(1.0, SpiralGlyph.ComputeProgress(12, nextAt));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeDays_ClampToEmpty(int days)
    {
        Assert.Equal(0.0, SpiralGlyph.ComputeProgress(days, 30));
    }

    [Fact]
    public void EveryResult_StaysInsideTheArc()
    {
        foreach (var days in new[] { int.MinValue, -5, 0, 1, 6, 7, 8, 1000, int.MaxValue })
        foreach (var next in new int?[] { null, -3, 0, 1, 7, 30, int.MaxValue })
        {
            var p = SpiralGlyph.ComputeProgress(days, next);
            Assert.InRange(p, 0.0, 1.0);
        }
    }

    // =====================================================================================
    //  the two surfaces' safety properties, pinned at source level
    //
    //  Both are wiring facts a compile cannot see and only a play-test with a LIT descent
    //  key could observe at runtime - which is exactly the account almost nobody has. What
    //  can rot silently is the source: a Visibility default flipped during a refactor turns
    //  a dark rollout into a plate every account can see, and a literal hex creeping back in
    //  turns a mod switch into a pink spiral on a green mod (the 2026-08-13 sweep's whole
    //  bug class).
    // =====================================================================================

    private static string AppFile(params string[] parts) => SourceRoots.ReadProductFile(parts);

    /// <summary>The element's own opening tag, so a Collapsed somewhere else in the file
    /// cannot satisfy the assertion.</summary>
    private static string OpeningTag(string xaml, string name)
    {
        var start = xaml.IndexOf("x:Name=\"" + name + "\"", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " is gone from the XAML - re-read the file before fixing this scrape");
        var end = xaml.IndexOf('>', start);
        Assert.True(end > start, name + "'s tag never closes - re-read the file, then fix the scrape");
        return xaml.Substring(start, end - start);
    }

    [Fact]
    public void BothSurfacesShipCollapsed()
    {
        // ZERO FOOTPRINT WHEN DARK. Flip either default and every account outside the
        // server's rollout dial gets a door to a spiral it has no block for.
        Assert.Contains("Visibility=\"Collapsed\"",
                        OpeningTag(AppFile("Views", "Tabs", "DiscordTabView.xaml"), "ProfileSpiralPlate"),
                        StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"",
                        OpeningTag(AppFile("MainWindow", "MainWindow.xaml"), "ProfileMenuSpiralRow"),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardPlateTakesTheModAccentAndNotGold()
    {
        // LEVEL and RANK are gold because they are the leaderboard's currency. The spiral
        // is the descent's, and it must follow the mod chain rather than a baked hex.
        var tag = OpeningTag(AppFile("Views", "Tabs", "DiscordTabView.xaml"), "ProfileSpiralPlate");
        Assert.Contains("{DynamicResource", tag, StringComparison.Ordinal);
        Assert.DoesNotContain("FFD700", tag, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGlyphTakesItsAccentFromTheModChain()
    {
        // Pinned to the WPF head on purpose. This layer adds a second SpiralGlyph under
        // CCP.Avalonia/Controls/, so FindProductFile now sees two copies and refuses to guess —
        // which is exactly what it is for. The assertion below is about WPF's SetResourceReference,
        // an API with no Avalonia twin, so this test means the WPF copy and only that one.
        var src = File.ReadAllText(Path.Combine(
            SourceRoots.RepoRoot, "ConditioningControlPanel", "Controls", "SpiralGlyph.cs"));

        // SetResourceReference, never a ctor-baked SolidColorBrush: the second one is right
        // exactly once and wrong for every mod switch after it.
        Assert.Contains("SetResourceReference(Shape.StrokeProperty, \"PinkBrush\")", src, StringComparison.Ordinal);
        Assert.Contains("SetResourceReference(Shape.FillProperty, \"PinkBrush\")", src, StringComparison.Ordinal);
        Assert.DoesNotContain("new SolidColorBrush(", src, StringComparison.Ordinal);
    }

    [Fact]
    public void NeitherSurfaceGatesOnTheRailFlag()
    {
        // The rail's flag guards a WebView2 HWND in the nav rail. These are native glyphs;
        // their gate is block presence, which is the server's rollout dial. Wiring the flag
        // in here would leave both doors dark for every account, forever.
        // Comment lines stripped: the file EXPLAINS why the flag is absent, and the
        // explanation must not be what satisfies the assertion.
        var code = string.Join("\n", Array.FindAll(
            AppFile("MainWindow", "MainWindow.ProfileSpiral.cs").Split('\n'),
            l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("FlagEnabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DescentSpiralRailEnabled", code, StringComparison.Ordinal);
        // The gate that IS there: a real block, on your own card.
        Assert.Contains("block is not null && _profileViewingSelf", code, StringComparison.Ordinal);
    }
}
