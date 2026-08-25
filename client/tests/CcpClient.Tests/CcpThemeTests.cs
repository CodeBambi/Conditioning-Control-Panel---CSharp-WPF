using System.Text.RegularExpressions;
using Avalonia.Media;
using CcpClient.Desktop.Themes;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The theme model: where its six colours come from, what arithmetic derives the rest, and which
/// keys it is and is not allowed to touch.
///
/// <para>These are pure facts over source and over a <see cref="Color"/> struct — no Avalonia
/// application, no window, no pixel. <b>What they do not show:</b> that any of it renders. That the
/// theme really reaches a composited pixel is a headed claim and the record of it is the
/// <c>studio-dial/live</c> capture, whose sampled band measured 728 px of <c>#11111A</c> and 208 px
/// of <c>#E84393</c> — the theme's panel colour and the theme's accent — where it had measured the
/// seed's panel colour and the machine's Windows accent before.</para>
/// </summary>
public class CcpThemeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    private static string UpstreamFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "ConditioningControlPanel", .. parts]));

    private static string TokenFile() =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "client", "src", "CcpClient.Desktop", "Themes", "Ccp.axaml"));

    /// <summary>Collapses runs of whitespace so a comparison of two source expressions is about the
    /// arithmetic and not about where each file happens to wrap.</summary>
    private static string Squeeze(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>
    /// <b>"CCP Default" is a MOD, and these are its bytes, read out of the shipping product rather
    /// than copied into this file.</b>
    ///
    /// <para>This is the fact the whole packet rests on. <c>Resources/Theme/Colors.xaml</c> is a
    /// design-time SEED in both products; what a user with no mod installed actually sees is the
    /// built-in mod <c>CreateCCPDefault</c> declares (<c>Models/BuiltInMods.cs:908-926</c>),
    /// applied over that seed before the shell is shown. Measured headed on the shipping product
    /// against a throwaway data directory: these six account for most of the window while four of
    /// the seed dictionary's own values are down at single-digit PIXEL counts.</para>
    ///
    /// <para>Read from source, so a mod theme edited upstream reds here by name instead of leaving
    /// the port quietly wearing last year's skin.</para>
    /// </summary>
    [Fact]
    public void TheBuiltInDefaultIsTheShippingProductsOwnModTheme()
    {
        var mods = UpstreamFile("Models", "BuiltInMods.cs");

        // The Theme block of CreateCCPDefault, and nothing else: three other built-in mods declare
        // a ModTheme in this same file, so the search is anchored on the factory's own name.
        var factory = mods.IndexOf("private static ModManifest CreateCCPDefault()", StringComparison.Ordinal);
        Assert.True(factory > 0, "WPF Models/BuiltInMods.cs no longer declares CreateCCPDefault()");

        var block = Regex.Match(mods[factory..], @"Theme = new ModTheme\s*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, "CreateCCPDefault no longer declares a ModTheme — the port's built-in is unanchored");

        static Color Field(string body, string name)
        {
            var m = Regex.Match(body, name + @"\s*=\s*""(?<hex>#[0-9A-Fa-f]{6})""");
            Assert.True(m.Success, $"CCP Default's ModTheme no longer declares {name}");
            return Color.Parse(m.Groups["hex"].Value);
        }

        var body = block.Groups["body"].Value;
        var theme = CcpTheme.CcpDefault;

        Assert.Equal(Field(body, "AccentColor"), theme.Accent);
        Assert.Equal(Field(body, "AccentLightColor"), theme.AccentLight);
        Assert.Equal(Field(body, "AccentDarkColor"), theme.AccentDark);
        Assert.Equal(Field(body, "BackgroundColor"), theme.Background);
        Assert.Equal(Field(body, "PanelColor"), theme.Panel);
        Assert.Equal(Field(body, "SurfaceColor"), theme.Surface);

        // And the six really are six different colours — a parse that silently produced one value
        // six times would satisfy every assertion above.
        Assert.Equal(6, new[]
        {
            theme.Accent, theme.AccentLight, theme.AccentDark,
            theme.Background, theme.Panel, theme.Surface,
        }.Distinct().Count());
    }

    /// <summary>
    /// <b>The two shade functions are upstream's expressions, character for character.</b>
    ///
    /// <para>A lightening that is CLOSE to upstream's is the worst possible outcome: it is invisible
    /// in review, invisible under every tolerance in the check manifest, and shows up only when a
    /// pixel-exact comparison against the shipping product disagrees for a reason nobody can find.
    /// The trap is real and specific — <c>(byte)</c> on a <c>double</c> TRUNCATES, and rounding
    /// instead moves <c>PanelAccent</c> by a unit on two channels.</para>
    ///
    /// <para>So this reads the three-line body out of <c>MainWindow.xaml.cs</c> and compares it to
    /// the port's own, with only <c>Color.FromRgb</c> and whitespace normalised. Upstream editing
    /// the arithmetic reds here rather than in a capture six weeks later.</para>
    /// </summary>
    [Theory]
    [InlineData("LightenColor", "(byte)Math.Min(255, c.R + (255 - c.R) * amount), (byte)Math.Min(255, c.G + (255 - c.G) * amount), (byte)Math.Min(255, c.B + (255 - c.B) * amount)")]
    [InlineData("DarkenColor", "(byte)Math.Max(0, c.R * (1 - amount)), (byte)Math.Max(0, c.G * (1 - amount)), (byte)Math.Max(0, c.B * (1 - amount))")]
    public void TheShadeArithmeticIsUpstreamsOwnExpression(string upstreamName, string expected)
    {
        var refresh = UpstreamFile("MainWindow", "MainWindow.xaml.cs");

        var body = Regex.Match(
            refresh,
            @"private static Color " + upstreamName + @"\(Color c, double amount\)\s*\{\s*return Color\.FromRgb\((?<args>[^;]*)\);");
        Assert.True(body.Success,
            $"WPF MainWindow.xaml.cs no longer declares {upstreamName}(Color, double) in the shape this port copied");

        Assert.Equal(expected, Squeeze(body.Groups["args"].Value));
    }

    /// <summary>
    /// <b>The truncation, demonstrated rather than described</b>, on the one value it actually
    /// decides: <c>LightenColor(#11111A, .15)</c> lands on 52.7 / 52.7 / 60.35, and every channel
    /// goes DOWN. Round instead and this is <c>#35353C</c>.
    ///
    /// <para>The result is also the strongest single piece of evidence in this packet that the
    /// arithmetic is upstream's: <c>#34343C</c> is declared in no dictionary in either product, and
    /// it is 57,780 pixels — 1.28% — of a headed capture of the shipping product's window, where
    /// the seed's <c>PanelAccent</c> #2E2E4A is nine pixels.</para>
    /// </summary>
    [Fact]
    public void TheShadeFunctionsTruncateRatherThanRound()
    {
        Assert.Equal(Color.Parse("#FF34343C"), CcpTheme.Lighten(Color.Parse("#11111A"), 0.15));
        Assert.Equal(Color.Parse("#FF4C4C53"), CcpTheme.Lighten(Color.Parse("#11111A"), 0.25));

        // The rounded answers, named so the assertions above cannot be satisfied by an
        // implementation that rounds.
        Assert.NotEqual(Color.Parse("#FF35353C"), CcpTheme.Lighten(Color.Parse("#11111A"), 0.15));

        // Darken's own truncation, on a channel that lands at .95: 147 * 0.85 = 124.95 -> 124.
        Assert.Equal(Color.Parse("#FFC5387C"), CcpTheme.Darken(Color.Parse("#E84393"), 0.15));
        Assert.NotEqual(Color.Parse("#FFC5397D"), CcpTheme.Darken(Color.Parse("#E84393"), 0.15));

        // Both clamps, at the ends nothing in this palette reaches but any later mod might.
        Assert.Equal(Colors.White, CcpTheme.Lighten(Colors.White, 1.0));
        Assert.Equal(Colors.Black, CcpTheme.Darken(Colors.Black, 1.0));
        Assert.Equal(Colors.White, CcpTheme.Lighten(Colors.Black, 1.0));
        Assert.Equal(Colors.Black, CcpTheme.Darken(Colors.White, 1.0));
    }

    /// <summary>
    /// <b>Two keys are DERIVED and are not allowed to become constants.</b>
    ///
    /// <para>Upstream recomputes <c>PanelAccent</c> and <c>PanelAccentHover</c> from the ACTIVE
    /// mod's panel colour every time it writes them (<c>MainWindow.xaml.cs:1611-1612</c>), so a
    /// port that pinned the two values CCP Default's panel colour happens to produce would look
    /// identical for exactly as long as nobody supplied another one. This asks a second, invented
    /// theme for its tokens, which is the only way to tell the two implementations apart.</para>
    /// </summary>
    [Fact]
    public void PanelAccentAndItsHoverAreDerivedFromTheThemesPanelColour()
    {
        var other = CcpTheme.CcpDefault with { Panel = Color.Parse("#402040") };
        var tokens = other.Tokens();

        Assert.Equal(Color.Parse("#402040"), tokens["PanelBg"]);
        Assert.Equal(CcpTheme.Lighten(Color.Parse("#402040"), 0.15), tokens["PanelAccent"]);
        Assert.Equal(CcpTheme.Lighten(Color.Parse("#402040"), 0.25), tokens["PanelAccentHover"]);

        // And they really moved: a pinned pair would still be answering with the default's.
        var pinned = CcpTheme.CcpDefault.Tokens();
        Assert.NotEqual(pinned["PanelAccent"], tokens["PanelAccent"]);
        Assert.NotEqual(pinned["PanelAccentHover"], tokens["PanelAccentHover"]);
    }

    /// <summary>
    /// <b>The theme writes every key it names, and touches nothing a mod does not supply.</b>
    ///
    /// <para>Both halves are defects nothing else catches. A key the theme writes that the token
    /// dictionary never declares is a colour nobody reads — it does not warn, does not throw, and
    /// leaves the surface it was meant for on the seed. And a key the theme writes that a mod
    /// theme does NOT supply is the opposite mistake: upstream leaves <c>ElevatedSurface</c>, the
    /// three inks and the semantics exactly where the seed put them, and that asymmetry is
    /// MEASURABLE on the shipping product — <c>#222240</c> is 5.13% of its window while
    /// <c>#2E2E4A</c>, the seed value of the mod-derived <c>PanelAccent</c> next to it, is nine
    /// pixels.</para>
    /// </summary>
    [Fact]
    public void EveryThemedKeyIsDeclaredInTheTokenFile_AndNothingElseIsThemed()
    {
        var declared = new HashSet<string>(
            Regex.Matches(TokenFile(), @"<Color x:Key=""(\w+)"">").Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);
        Assert.True(declared.Count > 15, $"only {declared.Count} colour keys read out of the token file");

        var tokens = CcpTheme.CcpDefault.Tokens();

        // Five grounds, three accents, and Fluent's family of seven. Pinned so a key added or
        // dropped is a decision somebody has to make on purpose.
        Assert.Equal(15, tokens.Count);

        foreach (var key in tokens.Keys)
        {
            Assert.True(declared.Contains(key),
                $"the theme writes '{key}', which the token dictionary does not declare — that colour reaches "
                + "nothing, silently, and the surface that wanted it stays on the seed");
        }

        // The keys a mod theme does not supply. Named individually rather than derived, because
        // "which colours are brand and which are structure" is a DECISION and a decision that
        // drifts is how a port stops matching what it was ported from.
        foreach (var untouched in (string[])
                 ["ElevatedSurface", "SeatBg", "TextLight", "TextMuted", "TextDim", "NeonPurple", "Danger", "Warning"])
        {
            Assert.True(declared.Contains(untouched), $"the token file no longer declares '{untouched}'");
            Assert.False(tokens.ContainsKey(untouched),
                $"the theme rewrites '{untouched}', which no ModTheme supplies (WPF Models/ModManifest.cs). "
                + "Upstream leaves it on the seed and the port must, or the two products stop matching");
        }
    }

    /// <summary>
    /// <b>The accent ladder lands in the port's three keys in upstream's order</b>, and the shell's
    /// two are the ladder's DARK and LIGHT steps rather than the base.
    ///
    /// <para>That mapping is not cosmetic. The pop quiz card's question ink is upstream's own GDI
    /// <c>COLORREF</c> <c>#FF69B4</c> and is not themed at all, so a shell livery that landed on
    /// the accent BASE would collide with a check declared at a tolerance no widening can fix. The
    /// separations under this theme are 38 for the base and 71 for the dark step — and SIX for the
    /// light step, which is why <c>popquiz-card-question-ink</c> was re-derived down to a tolerance
    /// of 4. Two pinks six apart on two surfaces is the shipping product's own palette, not a port
    /// defect, and this names it so a later edit cannot make it quietly worse.</para>
    /// </summary>
    [Fact]
    public void TheAccentLadderLandsInTheShellsThreeKeys_AndTheLightStepReallyIsSixFromThePopQuizInk()
    {
        var theme = CcpTheme.CcpDefault;
        var tokens = theme.Tokens();

        Assert.Equal(theme.Accent, tokens["PinkColor"]);
        Assert.Equal(theme.AccentDark, tokens["ShellAccent"]);
        Assert.Equal(theme.AccentLight, tokens["ShellAccentBright"]);
        Assert.Equal(theme.Accent, tokens["SystemAccentColor"]);

        // The card's ink out of the MANIFEST rather than out of the painter's constant, and
        // deliberately: naming Win32InputPresence would move this whole class into the
        // real-desktop collection (RealDesktopCollectionGuardTests is lexical) and serialise a
        // file that opens nothing. PopQuizCardPresentationTests already holds the manifest to the
        // COLORREF, so reading it here chains onto that link instead of duplicating it.
        var manifestPath = Path.Combine(RepoRoot(), "client", "tools", "verify", "checks.json");
        var inkCheck = CcpVerify.CheckManifest.Load(manifestPath)
            .Single(c => string.Equals(c.Name, "popquiz-card-question-ink", StringComparison.Ordinal));
        var (ir, ig, ib) = CcpVerify.CheckManifest.ParseColor(inkCheck.ExpectedColor, "popquiz-card-question-ink:");
        var card = Color.FromRgb(ir, ig, ib);

        static int Apart(Color a, Color b) =>
            Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

        Assert.Equal(38, Apart(tokens["PinkColor"], card));
        Assert.Equal(71, Apart(tokens["ShellAccent"], card));
        Assert.Equal(6, Apart(tokens["ShellAccentBright"], card));

        // And the consequence, stated where the collision is rather than only where it bites: the
        // card's ink check cannot reach as far as the light step, or a photograph of a selected
        // rail door would satisfy a check that exists to say a pop quiz card was on the screen.
        Assert.True(inkCheck.Tolerance < 6,
            $"popquiz-card-question-ink has tolerance {inkCheck.Tolerance} and ShellAccentBright is 6 away — "
            + "the shell's selection livery would pass the card's own ink check");
    }
}
