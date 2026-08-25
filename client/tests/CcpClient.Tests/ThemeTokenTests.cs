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
    /// next page pastes <c>#FFE8E0EE</c> again — that is precisely how the product arrived at 102
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
}
