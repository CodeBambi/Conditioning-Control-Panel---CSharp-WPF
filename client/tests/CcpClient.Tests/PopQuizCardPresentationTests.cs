using CcpClient.Desktop.Input;
using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Floor guards over the POP QUIZ CARD's two manifest checks
/// (<c>client/tools/verify/checks.json</c>), following the rack's, the session rack's and the
/// toast's precedent.
///
/// <para><b>This surface is unlike every other one in the manifest, and the guards say how.</b> The
/// card is not an Avalonia control: it is a raw Win32 popup the product creates and paints with GDI
/// (<see cref="Win32InputPresence"/>), so its two colours are the PAINTER's own constants rather
/// than a brush in a style. The first guard asserts exactly that link, through the
/// <c>COLORREF</c> byte order, so a repainted card cannot leave the manifest describing the old
/// one.</para>
///
/// <para><b>What these are and are not.</b> They are LEXICAL guards over a JSON file on disk. They
/// prove the two checks exist, still claim the class a headless frame cannot discharge, name the
/// colours the product really paints, and sample a band no glyph can enter. They prove nothing
/// about pixels — the capture does that, and the record of it is
/// <c>artifacts/windows-popquiz-card-asking.png</c>, on which <c>popquiz-card-ground</c> scored
/// 1.000 and <c>popquiz-card-question-ink</c> 0.048, both 0.000 on a real capture of the dashboard
/// and of a rail door.</para>
/// </summary>
/// <remarks>
/// <b>Why this class is in the real-desktop collection when it touches no desktop.</b> It names
/// <see cref="Win32InputPresence"/> to read two compile-time constants out of it, and
/// <c>RealDesktopCollectionGuardTests</c> is deliberately LEXICAL: a class that mentions that type
/// takes membership AND the base that arms the window floor on the running thread. Evading the guard
/// by copying the two hex strings in by hand would break exactly the link the first fact exists to
/// hold, so the serialisation is paid instead. Nothing here opens a window, reads a pixel or asks
/// the operating system for anything.
/// </remarks>
[Collection(nameof(RealDesktopCollection))]
public class PopQuizCardPresentationTests : RealDesktopFacts
{
    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static IReadOnlyList<ManifestCheck> CardChecks() =>
        [.. CheckManifest.Load(ManifestPath())
            .Where(c => string.Equals(c.Surface, "popquiz-card", StringComparison.Ordinal))];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    /// <summary>A <c>COLORREF</c> is <c>0x00BBGGRR</c>; the manifest is <c>#RRGGBB</c>. The swap is
    /// the whole point of this file's first guard, so it is written out once here.</summary>
    private static string HexOf(uint colorRef) =>
        $"#{colorRef & 0xFF:X2}{(colorRef >> 8) & 0xFF:X2}{(colorRef >> 16) & 0xFF:X2}";

    /// <summary>
    /// <b>The manifest names the colours the product really paints</b>, read out of the painter's
    /// own constants rather than copied. <see cref="Win32InputPresence.BackgroundColour"/> and
    /// <see cref="Win32InputPresence.QuestionColour"/> are <c>COLORREF</c>s (0x00BBGGRR), which is
    /// exactly the trap this guard exists for: <c>0x00B469FF</c> reads as "B4 69 FF" and a check
    /// written from the literal would have sampled for <c>#B469FF</c>, a lilac the card never draws,
    /// and scored 0.000 on a perfectly good capture.
    ///
    /// <para>Both claim <c>presentation-verified</c>, because both are about composited pixels on a
    /// real display; a check quietly demoted to <c>draw-verified</c> would let a headless run claim
    /// a pixel it never read.</para>
    /// </summary>
    [Fact]
    public void TheTwoChecksNameThePaintersOwnColours_ThroughTheCOLORREFByteOrder()
    {
        var checks = CardChecks();
        Assert.Equal(
            ["popquiz-card-ground", "popquiz-card-question-ink"],
            checks.Select(c => c.Name).Order(StringComparer.Ordinal));

        foreach (var check in checks)
        {
            Assert.Equal(CheckManifest.EvidencePresentation, check.EvidenceClass);
            Assert.Equal("asking", check.State);
        }

        var ground = checks.Single(c => c.Name == "popquiz-card-ground");
        var ink = checks.Single(c => c.Name == "popquiz-card-question-ink");

        Assert.Equal(HexOf(Win32InputPresence.BackgroundColour), ground.ExpectedColor);
        Assert.Equal(HexOf(Win32InputPresence.QuestionColour), ink.ExpectedColor);

        // And the swap really is a swap — the literal byte order would have named a different
        // colour, which is the mistake this guard catches.
        Assert.Equal("#1A1A2E", ground.ExpectedColor);
        Assert.Equal("#FF69B4", ink.ExpectedColor);
        Assert.NotEqual("#2E1A1A", ground.ExpectedColor);
        Assert.NotEqual("#B469FF", ink.ExpectedColor);
    }

    /// <summary>
    /// <b>The ground band cannot contain a glyph, and that is arithmetic rather than luck.</b> Every
    /// text band the card paints is inset from the left by at least <c>width / 12</c>
    /// (<see cref="Win32InputPresence"/>'s <c>Band</c>), so a sample strip that ends before 1/12 of
    /// the card is background at any card size, on any display. Widen the band past that and this
    /// names the number it crossed.
    ///
    /// <para>Column 0 is excluded for the reason the toast's own band records: it is the window's
    /// outermost pixel and belongs to the frame rather than to the fill.</para>
    /// </summary>
    [Fact]
    public void TheGroundBandEndsBeforeTheFirstColumnAnyGlyphCanReach()
    {
        var ground = CardChecks().Single(c => c.Name == "popquiz-card-ground");
        var rect = ground.Region.Rect;
        Assert.NotNull(rect);

        const double firstInkColumn = 1.0 / 12;
        Assert.True(rect!.X > 0, $"the ground band starts at column {rect.X}, which is the window's own edge");
        Assert.True(
            rect.X + rect.W <= firstInkColumn,
            $"the ground band ends at {rect.X + rect.W} of the card, but the painter insets every text band by "
            + $"width/12 = {firstInkColumn:0.####}; past that a glyph can enter the strip this check calls ground");

        // The full height, because the card's margin is background top to bottom and a band that
        // sampled a slice could sit above or below where a defect appeared.
        Assert.Equal(0.0, rect.Y);
        Assert.Equal(1.0, rect.H);
    }

    /// <summary>
    /// Neither check accepts any other declared state's colour. Compared against the WHOLE manifest
    /// rather than against this surface's own pair, for the reason the goon page's guard states: a
    /// check that cannot fail on another surface's real capture is not saying the card rendered, it
    /// is saying something was on the screen.
    /// </summary>
    [Fact]
    public void NoPopQuizCardCheckAcceptsAnotherDeclaredStatesColour()
    {
        var all = CheckManifest.Load(ManifestPath());
        var card = CardChecks();
        Assert.NotEmpty(card);
        Assert.True(all.Count - card.Count >= 3,
            $"only {all.Count - card.Count} non-card check(s) in the manifest — this guard would be nearly vacuous");

        foreach (var check in card)
        {
            var expected = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            foreach (var other in all.Where(c => !string.Equals(c.Surface, check.Surface, StringComparison.Ordinal)))
            {
                var otherColour = CheckManifest.ParseColor(other.ExpectedColor, $"check '{other.Name}':");
                var separation = Math.Max(
                    Math.Abs(expected.R - otherColour.R),
                    Math.Max(Math.Abs(expected.G - otherColour.G), Math.Abs(expected.B - otherColour.B)));
                Assert.True(check.Tolerance < separation,
                    $"'{check.Name}' ({check.ExpectedColor}, tolerance {check.Tolerance}) accepts "
                    + $"'{other.Name}' ({other.ExpectedColor}) — they are {separation} apart per channel, so a "
                    + $"capture of {other.Surface}/{other.State} could pass a check that exists to say a pop quiz "
                    + "card was really on the screen");
            }
        }
    }
}
