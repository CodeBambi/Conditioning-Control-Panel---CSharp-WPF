using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The token layer's source-level contract: what is declared, what references it, and what is no
/// longer allowed to be pasted. These are pure text facts on purpose — they hold over the SOURCE,
/// which is where the defect they guard against is committed, and they need no Avalonia runtime.
///
/// <para><b>What they do not show.</b> Nothing about rendering, and nothing about what any of
/// these colours look like. A file can declare a perfectly consistent palette and still paint an
/// unreadable screen.</para>
/// </summary>
public class ThemeTokenTests
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

    private static string SourceRoot() =>
        Path.Combine(RepoRoot(), "client", "src", "CcpClient.Desktop");

    private static string TokenFilePath() => Path.Combine(SourceRoot(), "Themes", "Ccp.axaml");

    private static string TokenFile() => File.ReadAllText(TokenFilePath());

    private static IEnumerable<string> Surfaces() =>
        Directory.EnumerateFiles(SourceRoot(), "*.axaml", SearchOption.AllDirectories)
            .Where(p => !string.Equals(Path.GetFileName(p), "Ccp.axaml", StringComparison.Ordinal));

    /// <summary>The shipping product's own colour dictionary, read as read-only evidence.</summary>
    private static string UpstreamThemeFile(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "Theme", name));

    /// <summary>Upstream's <c>Resources/Theme/Colors.xaml</c>, key to <c>#AARRGGBB</c>.</summary>
    private static Dictionary<string, string> UpstreamColours() =>
        Regex.Matches(UpstreamThemeFile("Colors.xaml"), @"<Color x:Key=""(\w+)"">(#[0-9A-Fa-f]{8})</Color>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant(), StringComparer.Ordinal);

    private static (byte R, byte G, byte B) Rgb(string argb) => (
        Convert.ToByte(argb.Substring(3, 2), 16),
        Convert.ToByte(argb.Substring(5, 2), 16),
        Convert.ToByte(argb.Substring(7, 2), 16));

    /// <summary>Every <c>x:Key</c> the token file declares, whatever its type.</summary>
    private static HashSet<string> DeclaredKeys() =>
        [.. Regex.Matches(TokenFile(), @"x:Key=""(\w+)""").Select(m => m.Groups[1].Value)];

    /// <summary>Colour keys only, mapped to the byte they declare.</summary>
    private static Dictionary<string, string> DeclaredColours() =>
        Regex.Matches(TokenFile(), @"<Color x:Key=""(\w+)"">(#[0-9A-Fa-f]{8})</Color>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant(), StringComparer.Ordinal);

    /// <summary>
    /// <b>Every token a surface references is really declared.</b>
    ///
    /// <para>This is the defect the indirection introduced and nothing else catches. A
    /// <c>DynamicResource</c> naming a key that does not exist does not fail the build, does not
    /// warn, and does not throw at runtime — the property simply keeps its default, so a
    /// mistyped brush key paints a transparent background or black text and the first person to
    /// notice is a user. 225 references went in mechanically; this is what holds the 226th.</para>
    /// </summary>
    [Fact]
    public void EveryTokenReferencedByASurfaceIsDeclaredInTheTokenFile()
    {
        var declared = DeclaredKeys();
        var missing = new List<string>();
        var referenced = 0;

        foreach (var file in Surfaces())
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{DynamicResource (\w+)\}"))
            {
                referenced++;
                if (!declared.Contains(m.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(file)} -> {m.Groups[1].Value}");
                }
            }
        }

        Assert.Empty(missing);

        // The corpus is real. A walk that found nothing would pass this fact silently forever,
        // which is exactly how a guard over a sweep stops guarding it.
        Assert.True(referenced >= 200,
            $"only {referenced} token references found across the surfaces — the sweep landed 225, "
            + "so either the walk is looking in the wrong place or the sweep has been undone");
    }

    /// <summary>
    /// <b>No surface re-pastes a colour the token file already names.</b>
    ///
    /// <para>The guard that makes the sweep stick. Converting 225 literals is worth nothing if the
    /// next page pastes <c>#FFF0F0F5</c> again — that is precisely how the product arrived at 102
    /// copies of one ink — and no compiler, linter or headless frame has any opinion about it.
    /// A colour genuinely new to the product still passes: this only refuses the bytes the token
    /// file has already given a name to.</para>
    /// </summary>
    [Fact]
    public void NoSurfacePastesAColourTheTokenFileAlreadyNames()
    {
        var named = DeclaredColours()
            .GroupBy(kv => kv.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.Join('/', g.Select(kv => kv.Key).Order(StringComparer.Ordinal)),
                StringComparer.Ordinal);

        var offenders = new List<string>();

        foreach (var file in Surfaces())
        {
            var text = File.ReadAllText(file);

            // Attribute positions only. Prose that quotes a colour is a citation, not a paint
            // instruction, and several comments in this tree legitimately name upstream's bytes.
            foreach (Match m in Regex.Matches(text, @"[A-Za-z_][\w.:]*\s*=\s*""(#[0-9A-Fa-f]{6,8})"""))
            {
                var hex = m.Groups[1].Value.ToUpperInvariant();
                if (hex.Length == 7)
                {
                    hex = "#FF" + hex[1..]; // six digits are opaque RGB
                }

                if (named.TryGetValue(hex, out var key))
                {
                    offenders.Add($"{Path.GetFileName(file)} pastes {m.Groups[1].Value}, which is the token '{key}'");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// <b>The application pins a dark variant, and does not follow the operating system.</b>
    ///
    /// <para><c>RequestedThemeVariant="Default"</c> means FOLLOW THE OS, and every ground in this
    /// product is a hard dark hex. The machine this port is built on runs dark, so the light case
    /// had never once been rendered — roughly ninety unstyled Fluent controls would have painted
    /// light chrome onto dark grounds, and no test, build or headless frame would have said a
    /// word. Read off the markup because that is where the switch lives; it establishes what the
    /// application ASKS FOR, and nothing about what a light-mode machine actually renders.</para>
    /// </summary>
    [Fact]
    public void TheApplicationPinsTheDarkVariantRatherThanFollowingTheOperatingSystem()
    {
        var app = File.ReadAllText(Path.Combine(SourceRoot(), "App.axaml"));

        Assert.Contains("RequestedThemeVariant=\"Dark\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedThemeVariant=\"Default\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedThemeVariant=\"Light\"", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The application and the headless test application load the same token dictionary.</b>
    ///
    /// <para>The suite's whole evidence base resolves colour through these keys now. If the two
    /// applications ever disagree about the token layer, the headless facts stop measuring the
    /// product — quietly, and while still passing, because a brush that resolves to nothing simply
    /// keeps its default. This holds the two in step by reading both files rather than by anyone
    /// remembering.</para>
    /// </summary>
    [Fact]
    public void TheTestApplicationLoadsTheSameTokenDictionaryTheProductDoes()
    {
        const string Uri = "avares://CcpClient.Desktop/Themes/Ccp.axaml";

        var app = File.ReadAllText(Path.Combine(SourceRoot(), "App.axaml"));
        var testApp = File.ReadAllText(Path.Combine(
            RepoRoot(), "client", "tests", "CcpClient.HeadlessTests", "TestApp.cs"));

        Assert.Contains(Uri, app, StringComparison.Ordinal);
        Assert.Contains(Uri, testApp, StringComparison.Ordinal);

        // And both set the font from the same key, which is the other half of what a window
        // inherits from the application.
        Assert.Contains("CcpFontFamily", app, StringComparison.Ordinal);
        Assert.Contains("CcpFontFamily", testApp, StringComparison.Ordinal);

        // The URI both of them name resolves to a real dictionary. Read rather than probed with
        // File.Exists on purpose: the vacuous-shape guard is right that a filesystem PREDICATE
        // inside a fact can quietly turn an assertion into a no-op, and reading the file makes the
        // absent case throw instead of skip.
        Assert.Contains("<ResourceDictionary", TokenFile(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Every brush in the token file is derived from a colour key that exists, dynamically.</b>
    ///
    /// <para>The brushes are the layer everything else binds to, and they are what makes runtime
    /// re-theming possible: upstream's own reason for the shape is written on the file it copies —
    /// "use DynamicResource so runtime color changes propagate"
    /// (WPF Resources/Theme/Brushes.xaml:4). A brush written with a baked literal instead would
    /// keep working, keep passing every colour assertion in this suite, and silently stop
    /// following its key — so the re-theme feature would ship, look correct in review, and leave
    /// that one surface on the old palette.</para>
    /// </summary>
    [Fact]
    public void EveryBrushInTheTokenFileFollowsAColourKeyDynamically()
    {
        var text = TokenFile();
        var colours = DeclaredColours();

        var brushes = Regex.Matches(text, @"<SolidColorBrush x:Key=""(\w+)"" Color=""([^""]+)""");
        Assert.True(brushes.Count >= 15, $"only {brushes.Count} brushes declared — the token file has been gutted");

        foreach (Match m in brushes)
        {
            var colourRef = Regex.Match(m.Groups[2].Value, @"^\{DynamicResource (\w+)\}$");
            Assert.True(colourRef.Success,
                $"brush '{m.Groups[1].Value}' bakes '{m.Groups[2].Value}' instead of following a colour key");
            Assert.True(colours.ContainsKey(colourRef.Groups[1].Value),
                $"brush '{m.Groups[1].Value}' follows '{colourRef.Groups[1].Value}', which no <Color> declares");
        }
    }

    /// <summary>
    /// <b>Every ground and every ink is the shipping product's own byte, and the table is
    /// exhaustive.</b>
    ///
    /// <para>This is the fact the palette flip exists for. The port was violet-tinted where the
    /// shipping product is navy-and-pink, and the owner's requirement is that the two look the
    /// same; the token layer made that a one-line-per-role edit, and this is what keeps it edited.
    /// Nothing else in this repository compares the two palettes: the headed manifest pins the
    /// PORT's bytes, so a token quietly moved back would be re-derived into checks.json by the
    /// next person and the divergence would simply become the new normal.</para>
    ///
    /// <para>The second half is the part that does not rot. A NEW colour token added to the file
    /// without a decision about where it came from fails here by name, because every declared
    /// colour must be either in the table below or in the small exception list beside it, and each
    /// exception carries its own reason.</para>
    ///
    /// <para><b>What it does not show.</b> That the values look right, that they contrast, or that
    /// anything renders. It is a byte comparison between two files.</para>
    /// </summary>
    [Fact]
    public void EveryGroundAndInkCarriesTheShippingProductsOwnValue()
    {
        // port key -> the key in WPF Resources/Theme/Colors.xaml it is taken from.
        var fromUpstream = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DarkerBg"] = "DarkerBg",                 // Colors.xaml:9
            ["SurfaceBg"] = "SurfaceBg",               // Colors.xaml:10
            ["PanelBg"] = "PanelBg",                   // Colors.xaml:7
            ["ElevatedSurface"] = "ElevatedSurface",   // Colors.xaml:71
            ["PanelAccent"] = "PanelAccent",           // Colors.xaml:19
            ["PanelAccentHover"] = "PanelAccentHover", // Colors.xaml:20
            ["TextLight"] = "TextLight",               // Colors.xaml:11
            ["TextMuted"] = "TextMuted",               // Colors.xaml:15
            ["TextDim"] = "TextDim",                   // Colors.xaml:74
            ["PinkColor"] = "PinkColor",               // Colors.xaml:5
            ["NeonPurple"] = "NeonPurple",             // Colors.xaml:129
        };

        // Declared here, with a reason each, because they are NOT a line of upstream's dictionary.
        var deliberateExceptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SeatBg"] = "upstream has no key for this tier; derived, and pinned by its own fact",
            ["ShellAccent"] = "upstream's accent DARK step, pinned by the accent-ladder fact",
            ["ShellAccentBright"] = "upstream's accent LIGHT step, pinned by the accent-ladder fact",
            ["Danger"] = "upstream builds it in code, MainWindow.StartStop.cs:756 FromRgb(255,107,107)",
            ["Warning"] = "no upstream equivalent: upstream's failure surface is a MessageBox",
        };

        var upstream = UpstreamColours();
        Assert.True(upstream.Count > 50, $"only {upstream.Count} colours read out of upstream's dictionary");

        foreach (var (portKey, upstreamKey) in fromUpstream)
        {
            Assert.True(DeclaredColours().TryGetValue(portKey, out var mine),
                $"the token file no longer declares '{portKey}'");
            Assert.True(upstream.TryGetValue(upstreamKey, out var theirs),
                $"WPF Resources/Theme/Colors.xaml no longer declares '{upstreamKey}'");
            Assert.True(string.Equals(mine, theirs, StringComparison.Ordinal),
                $"token '{portKey}' is {mine} but the shipping product's '{upstreamKey}' is {theirs} — the two "
                + "products are supposed to look the same, and this is the whole of that claim");
        }

        // Exhaustive: Fluent's seven accent keys are shadowed rather than owned, so they are named
        // by prefix; everything else must be accounted for above.
        foreach (var key in DeclaredColours().Keys)
        {
            if (key.StartsWith("SystemAccentColor", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(fromUpstream.ContainsKey(key) || deliberateExceptions.ContainsKey(key),
                $"colour token '{key}' is neither taken from upstream's dictionary nor listed as a deliberate "
                + "exception — a new palette entry is a decision, and undocumented is how the two products drift");
        }
    }

    /// <summary>
    /// <b>The three pinks are upstream's own accent ladder, in upstream's own order.</b>
    ///
    /// <para><c>RefreshThemeAwareElements</c> reads an accent, a DARK companion and a LIGHT one
    /// from the active mod and falls back to the shipping default's own triad when no mod supplies
    /// them (WPF MainWindow/MainWindow.xaml.cs:1565-1567); it then rewrites the keys
    /// <c>PinkColor</c>, <c>DarkPink</c> and <c>PinkButtonHovered</c> from exactly those three
    /// (:1655-1657). So the port's three accent keys are that ladder, and this reads the fallbacks
    /// out of the shipping source rather than restating them.</para>
    ///
    /// <para><b>Why ShellAccent is the DARK step and not simply PinkColor</b>, given upstream's
    /// START button really is <c>PinkBrush</c>: the pop quiz card's question ink is upstream's own
    /// COLORREF <c>0x00B469FF</c> = <c>#FF69B4</c>, it is declared in
    /// <c>client/tools/verify/checks.json</c> at tolerance 24, and
    /// <c>PopQuizCardPresentationTests</c> refuses any other surface's check within that distance.
    /// A separation of zero is not fixable by any tolerance, so the shell's CTA livery takes the
    /// ladder's dark step and the two stay 123 apart.</para>
    /// </summary>
    [Fact]
    public void TheThreeAccentsAreTheShippingProductsOwnAccentLadder()
    {
        var refresh = File.ReadAllText(
            Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));

        static string Fallback(string source, string getter)
        {
            var m = Regex.Match(source, getter + @"\(\)\s*\?\?\s*""(#[0-9A-Fa-f]{6})""");
            Assert.True(m.Success,
                $"WPF MainWindow.xaml.cs no longer states a fallback for {getter}() — the accent ladder this "
                + "palette is derived from has moved and the port's three pinks are now unanchored");
            return "#FF" + m.Groups[1].Value[1..].ToUpperInvariant();
        }

        var colours = DeclaredColours();
        var ladder = new (string Port, string Getter)[]
        {
            ("PinkColor", "GetAccentColorHex"),
            ("ShellAccent", "GetAccentDarkColorHex"),
            ("ShellAccentBright", "GetAccentLightColorHex"),
        };

        foreach (var (port, getter) in ladder)
        {
            var expected = Fallback(refresh, getter);
            Assert.True(colours.TryGetValue(port, out var mine), $"the token file no longer declares '{port}'");
            Assert.True(string.Equals(mine, expected, StringComparison.Ordinal),
                $"token '{port}' is {mine} but upstream's {getter}() fallback is {expected}");
        }

        // The ladder is only a ladder if its three rungs are apart: the pop quiz card's ink is the
        // base, and a shell accent within 24 of it collapses that surface's check.
        var (br, bg, bb) = Rgb(colours["PinkColor"]);
        foreach (var key in (string[])["ShellAccent", "ShellAccentBright"])
        {
            var (r, g, b) = Rgb(colours[key]);
            var apart = Math.Max(Math.Abs(br - r), Math.Max(Math.Abs(bg - g), Math.Abs(bb - b)));
            Assert.True(apart > 24,
                $"'{key}' is only {apart} from PinkColor on its widest channel; popquiz-card-question-ink is "
                + "PinkColor at tolerance 24 and PopQuizCardPresentationTests reds on any nearer neighbour");
        }
    }

    /// <summary>
    /// <b>SeatBg is derived, not invented.</b>
    ///
    /// <para>It is the one colour in the file that is not a line of the shipping product's
    /// dictionary, because upstream has no key for the tier — it seats controls straight on
    /// <c>SurfaceBg</c>. The port's rail door and companion dial seat do sit on their own step, and
    /// flattening that would put the door on the rack's ground, so the tier is kept and its value
    /// is the arithmetic midpoint of the two upstream tiers it sits between. A hand-picked
    /// "roughly right" value here is exactly how a palette drifts back off upstream's one channel
    /// at a time.</para>
    /// </summary>
    [Fact]
    public void SeatBgIsTheMidpointOfTheTwoUpstreamTiersItSitsBetween()
    {
        var colours = DeclaredColours();
        var (lr, lg, lb) = Rgb(colours["PanelBg"]);
        var (hr, hg, hb) = Rgb(colours["ElevatedSurface"]);
        var expected = $"#FF{(lr + hr) / 2:X2}{(lg + hg) / 2:X2}{(lb + hb) / 2:X2}";

        Assert.Equal(expected, colours["SeatBg"]);
    }
}
