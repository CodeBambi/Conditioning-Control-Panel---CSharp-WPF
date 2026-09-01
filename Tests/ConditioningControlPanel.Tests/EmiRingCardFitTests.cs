using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// DOES THE NAME STILL FIT ON THE CARD?
///
/// <para>The owner's report on 2026-08-30 was "text on the EMI circle cards is too small", and the
/// fix was to raise the label - so the card had to grow to hold it. That trade is the kind that
/// looks fine on the one desktop it was tuned on and then clips "Subliminals" down the middle for
/// somebody at 175 %, because the arithmetic that ties the two together lives in a comment.</para>
///
/// <para>So it lives here instead. Press Start 2P advances exactly ONE EM per glyph (asserted
/// below against the shipped ttf, because the whole file leans on it), which makes a label's width
/// pure arithmetic: <c>characters x font size</c>. Everything else is read out of
/// <c>EmiRingWindow.xaml.cs</c> by name, so changing a constant without re-checking the fit fails
/// here rather than on somebody's desktop.</para>
///
/// <para>Two laws. A WORD must never break in the middle - "Sublimina / ls" is the failure this
/// exists to catch - and a whole LABEL must fit in the two lines the strip is given. "The
/// Arcademy" breaking at its space onto two lines is fine and is expected.</para>
/// </summary>
public class EmiRingCardFitTests
{
    /// <summary>The DPI scales the app actually ships on. 100 % is the trivial case; the others are not.</summary>
    private static readonly double[] Scales = { 1.0, 1.25, 1.5, 1.75, 2.0 };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string App(params string[] parts) =>
        Path.Combine(RepoRoot(), "ConditioningControlPanel", Path.Combine(parts));

    private static string RingSource() => File.ReadAllText(App("Windows", "EmiDesk", "EmiRingWindow.xaml.cs"));

    /// <summary>Pull one <c>const double</c> out of the ring window by name. Deliberately brittle.</summary>
    private static double Constant(string source, string name)
    {
        var m = Regex.Match(source, @"const\s+double\s+" + Regex.Escape(name) + @"\s*=\s*([0-9]+(?:\.[0-9]+)?)\s*;");
        Assert.True(m.Success, $"EmiRingWindow no longer declares a const double called {name}");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What the name strip has to write in, in DIPs. The card is a Border of <c>CardW</c> with a
    /// <c>CardBorderPinned</c> frame (the worst case - a pinned card's frame is the thick one),
    /// an inner grid inset by <c>CardSeam</c>, and the strip's own <c>LabelPadX</c> either side.
    /// </summary>
    private static double StripWidth(string src) =>
        Constant(src, "CardW")
        - 2 * Constant(src, "CardBorderPinned")
        - 2 * Constant(src, "CardSeam")
        - 2 * Constant(src, "LabelPadX");

    private static IEnumerable<(string Lang, string Key, string Label)> AllLabels()
    {
        var dir = SourceRoots.LanguagesDirectory;
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!p.Name.StartsWith("emi_desk_target_", StringComparison.Ordinal)) continue;
                if (p.Value.ValueKind != JsonValueKind.String) continue;
                var s = p.Value.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    yield return (Path.GetFileNameWithoutExtension(path), p.Name, s!);
            }
        }
    }

    /// <summary>
    /// A glyph's width in DIPs at a given DPI, the way <c>TextFormattingMode.Display</c> lays it
    /// out: one em, rounded UP to a whole device pixel and converted back. Rounding up is the
    /// pessimistic reading and the one a fit test wants.
    /// </summary>
    private static double GlyphDip(double fontSize, double scale) =>
        Math.Ceiling(fontSize * scale) / scale;

    // ------------------------------------------------------------------ the assumption

    /// <summary>
    /// EVERYTHING BELOW LEANS ON THIS. Press Start 2P is a square monospace: every glyph advances
    /// exactly 1.0 em, which is why an n-character label is n x FontSize DIPs wide and why the
    /// card can be sized off the catalogue with a multiplication. If the shipped ttf is ever
    /// swapped for a face with a different advance, every number in the fit comment is wrong.
    /// </summary>
    [Fact]
    public void The_shipped_pixel_font_advances_exactly_one_em_per_glyph()
    {
        var ttf = App("Resources", "emi", "fonts", "PressStart2P-latin.ttf");
        Assert.True(File.Exists(ttf), "the bundled pixel font is gone: " + ttf);

        var face = new GlyphTypeface(new Uri(ttf));
        Assert.Equal("Press Start 2P", face.FamilyNames.Values.First());

        foreach (char c in "AWMiljSubliminalsTheArcademy ")
        {
            Assert.True(face.CharacterToGlyphMap.TryGetValue(c, out ushort gi), $"no glyph for '{c}'");
            Assert.Equal(1.0, face.AdvanceWidths[gi], 3);
        }
    }

    /// <summary>
    /// The ring must use the RESOLVER, not a family name. Press Start 2P is shipped under
    /// Resources/emi/fonts and never installed, and a name lookup only ever sees installed faces -
    /// which is how every label on the ring came to be drawn in Consolas at roughly half the width
    /// the 8 DIP had been chosen for.
    /// </summary>
    [Fact]
    public void The_ring_gets_its_pixel_font_from_the_shipped_file_and_not_from_a_name()
    {
        var src = RingSource();

        Assert.Contains("FontFamily = EmiFace.PixelFont", src);
        Assert.DoesNotContain("FontFamily = new FontFamily(\"Press Start", src);

        // And it asks for whole-device-pixel advances, which is the other half of "a pixel font
        // goes to mush on a half-pixel offset" - the half that Layout()'s rounding cannot reach.
        Assert.Contains("TextFormattingMode.Display", src);
    }

    // ------------------------------------------------------------------ the two laws

    /// <summary>
    /// LAW ONE: no word ever breaks in the middle, at any DPI the app ships on. This is the one
    /// that decides <c>CardW</c>: "Subliminals" (11) is the longest word in the catalogue.
    /// </summary>
    [Fact]
    public void No_label_word_breaks_mid_word_at_any_shipped_dpi()
    {
        var src = RingSource();
        double font = Constant(src, "CardLabelFont");
        double strip = StripWidth(src);

        foreach (var (lang, key, label) in AllLabels())
        {
            foreach (var word in label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (double scale in Scales)
                {
                    double w = word.Length * GlyphDip(font, scale);
                    Assert.True(w <= strip + 0.001,
                        $"{lang}/{key}: \"{word}\" is {w:F1} DIPs at {scale:P0} and the strip is "
                        + $"{strip:F1}. It would break mid-word. Grow CardW or drop CardLabelFont.");
                }
            }
        }
    }

    /// <summary>
    /// LAW TWO: the whole label fits the two lines the strip allows. Greedy word wrapping, which
    /// is what WPF does with <c>TextWrapping.Wrap</c> once no single word overflows.
    /// </summary>
    [Fact]
    public void Every_label_fits_the_two_lines_the_strip_allows()
    {
        var src = RingSource();
        double font = Constant(src, "CardLabelFont");
        double strip = StripWidth(src);

        foreach (var (lang, key, label) in AllLabels())
        {
            foreach (double scale in Scales)
            {
                int lines = GreedyLines(label, GlyphDip(font, scale), strip);
                Assert.True(lines <= 2,
                    $"{lang}/{key}: \"{label}\" wraps onto {lines} lines at {scale:P0} in a "
                    + $"{strip:F1} DIP strip, and the strip only shows two.");
            }
        }
    }

    private static int GreedyLines(string label, double glyph, double strip)
    {
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int lines = 1, used = 0;

        foreach (var w in words)
        {
            int want = (used == 0) ? w.Length : used + 1 + w.Length;
            if (want * glyph <= strip + 0.001) { used = want; continue; }
            lines++;
            used = w.Length;
        }
        return lines;
    }

    // ------------------------------------------------------------------ the card itself

    /// <summary>
    /// The card is sized OFF the label, so it must not be quietly shrunk back under it: the strip
    /// has to keep the longest word plus real slack, and the card has to stay tall enough for two
    /// lines of it on top of some art.
    /// </summary>
    [Fact]
    public void The_card_is_big_enough_for_the_label_it_was_grown_for()
    {
        var src = RingSource();
        double font = Constant(src, "CardLabelFont");
        double line = Constant(src, "CardLabelLine");
        double cardH = Constant(src, "CardH");
        double strip = StripWidth(src);

        // The label is the reason the card grew. If it ever drops back to 8 the owner's report is
        // back with it.
        Assert.True(font >= 10.0, $"CardLabelFont fell back to {font}; 8 DIP was the complaint.");

        // Slack, not a hairline: greedy wrapping is exact only while nothing is borderline.
        int longest = AllLabels()
            .SelectMany(l => l.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Max(w => w.Length);
        Assert.True(strip >= longest * font + 4.0,
            $"the strip is {strip:F1} DIPs and the longest word ({longest} chars) wants "
            + $"{longest * font:F1}. That is not enough air to survive a DPI rounding.");

        // Two lines of label, its padding, and still most of the card left for the art.
        Assert.True(cardH >= 2 * line + 6 + 40,
            $"CardH is {cardH} and two lines of label plus padding already take {2 * line + 6}.");
    }
}
