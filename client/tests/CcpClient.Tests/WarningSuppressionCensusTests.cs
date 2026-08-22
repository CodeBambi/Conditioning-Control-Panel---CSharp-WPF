using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The census that covers the one hole a warning gate can NEVER cover.
///
/// <para><c>client/tests/floor/check-warnings.mjs</c> reads MSBuild's output. A SUPPRESSED warning
/// never reaches that output: <c>NoWarn</c>, an inline pragma disable, a code-analysis suppression
/// attribute, an <c>.editorconfig</c> severity of <c>none</c> and a lowered <c>WarningLevel</c> all
/// act before anything is printed. So the gate is structurally blind to them, and saying that
/// without doing anything about it would leave "0 warnings" one csproj line away from meaning
/// nothing again.</para>
///
/// <para>This is the second instrument, and it is LEXICAL, not behavioural: it pins the suppression
/// sites that exist under <c>client/</c> by file and code. A new suppression <b>of an enumerated
/// shape</b> fails here with file:line and has to be declared deliberately, in the same commit,
/// with a reason — the same admission discipline <c>allowedSkips</c> uses. It does not judge whether
/// a pinned suppression is justified. <b>And it does NOT make adding a suppression impossible to do
/// quietly</b>, which an earlier draft of this comment claimed: a shape outside the enumeration
/// below is silent, and two shapes were outside it until a reviewer executed the pattern. The
/// census's failure mode is SILENCE, and its completeness is a property of a hand-maintained list,
/// not of the compiler.</para>
///
/// <para>ENUMERATED SHAPES — the census binds exactly these, and claims nothing else: inline
/// pragma disables in <c>*.cs</c>; <c>NoWarn</c> in <c>*.csproj</c>, <c>*.props</c> and
/// <c>*.targets</c>; code-analysis suppression attributes INCLUDING the <c>[assembly:]</c> and
/// <c>[module:]</c> targeted forms; <c>GlobalSuppressions.cs</c>; <c>.editorconfig</c> AND
/// <c>.globalconfig</c>/<c>*.globalconfig</c>, both under <c>client/</c> and on the directory walk
/// up to the repository root; and the project-level warning-policy properties.</para>
///
/// <para>WHAT IT STILL CANNOT SEE. An analyzer config file placed ABOVE the repository root
/// (Roslyn keeps walking to the drive root; this census stops at the repo root, deliberately); an
/// analyzer package removed entirely or downgraded; a severity lowered by a package's own default
/// or by a legacy ruleset; and any suppression expressed in a file shape not in the list above.
/// <c>.globalconfig</c> and the targeted attribute forms are in that list only because this file's
/// CODE REVIEW found the first draft missing both — a two-line <c>.globalconfig</c> carrying
/// <c>dotnet_diagnostic.CS0219.severity = none</c> is auto-included by the SDK with ZERO csproj
/// references and silences a real warning under the gate's own forced-full build. Adding a shape
/// to this list is cheap; assuming the list is complete is how the hole reopens.</para>
///
/// <para>NEVER-SKIP SHAPE. These facts enumerate directories and read files with no existence
/// predicate: a missing tree throws and the fact FAILS.</para>
/// </summary>
public partial class WarningSuppressionCensusTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    /// <summary>
    /// Every warning suppression under <c>client/</c>, as <c>path::CODE</c>, sorted. Measured at
    /// this file's land, on base <c>4332b8b9</c>. Nine inline pragma sites, all CS0067 ("event is never used"
    /// on a deliberately inert implementation of an interface event), and one project-level NoWarn,
    /// AVLN3001, which <c>CLAUDE.md</c> records as intentional: <c>App</c> and <c>MainWindow</c>
    /// have no parameterless constructor and are never built by the runtime XAML loader.
    /// </summary>
    private static readonly string[] PinnedSuppressions =
    [
        "client/src/CcpClient.Desktop/CcpClient.Desktop.csproj::AVLN3001",
        "client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelDemoDrive.cs::CS0067",
        "client/src/CcpClient.Desktop/Features/Dtrh/DtrhLoomWindow.axaml.cs::CS0067",
        "client/src/CcpClient.Desktop/Features/Intake/IntakePassService.cs::CS0067",
        "client/tests/CcpClient.HeadlessTests/DtrhVideoWindowHeadlessTests.cs::CS0067",
        "client/tests/CcpClient.HeadlessTests/IntakePageHeadlessTests.cs::CS0067",
        "client/tests/CcpClient.HeadlessTests/IntakePageHeadlessTests.cs::CS0067",
        "client/tests/CcpClient.HeadlessTests/IntakePageHeadlessTests.cs::CS0067",
        "client/tests/CcpClient.HeadlessTests/IntakePageHeadlessTests.cs::CS0067",
        "client/tests/CcpClient.Tests/DtrhFxRouterTests.cs::CS0067",
    ];

    [GeneratedRegex(@"#pragma\s+warning\s+disable\s+([A-Za-z0-9_, ]+)")]
    private static partial Regex PragmaDisable();

    [GeneratedRegex(@"<NoWarn>([^<]*)</NoWarn>")]
    private static partial Regex NoWarnElement();

    // The (?:assembly|module) target is not decoration. An assembly-level suppression in ANY .cs
    // file suppresses a diagnostic for the WHOLE assembly, and the first draft of this pattern
    // missed exactly that form — for the analyzer class (xUnit2013) this packet was written over.
    // Found at the code review by executing the pattern against the five shapes; that
    // comparison now runs on every suite run instead of being trusted.
    [GeneratedRegex(@"\[\s*(?:(?:assembly|module)\s*:\s*)?(?:System\.Diagnostics\.CodeAnalysis\.)?SuppressMessage\b")]
    private static partial Regex SuppressionAttribute();

    // Analyzer configuration file shapes. BOTH are applied by the compiler before MSBuild prints
    // anything, and a .globalconfig needs no csproj reference at all — the SDK auto-includes it.
    private static readonly string[] AnalyzerConfigPatterns = [".editorconfig", ".globalconfig", "*.globalconfig"];

    [GeneratedRegex(@"<(TreatWarningsAsErrors|WarningsNotAsErrors|WarningLevel|AnalysisLevel|EnableNETAnalyzers|RunAnalyzers|RunAnalyzersDuringBuild)>([^<]*)</\1>")]
    private static partial Regex WarningPolicyElement();

    [Fact]
    public void EveryWarningSuppressionUnderTheClientTree_IsPinnedByFileAndCode()
    {
        var root = FindRepoRoot();
        var client = Path.Combine(root, "client");
        var observed = new List<string>();

        foreach (var file in EnumerateSources(client, "*.cs"))
        {
            var relative = Relative(root, file);
            var text = File.ReadAllText(file);
            foreach (Match m in PragmaDisable().Matches(text))
            {
                foreach (var code in m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    observed.Add($"{relative}::{code}");
                }
            }
        }

        foreach (var file in EnumerateSources(client, "*.csproj").Concat(EnumerateSources(client, "*.props")).Concat(EnumerateSources(client, "*.targets")))
        {
            var relative = Relative(root, file);
            var text = File.ReadAllText(file);
            foreach (Match m in NoWarnElement().Matches(text))
            {
                foreach (var code in m.Groups[1].Value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    // $(NoWarn) inherits whatever the SDK already set; only the codes this repo adds
                    // are its own decision, and only those are pinned.
                    if (!code.StartsWith('$'))
                    {
                        observed.Add($"{relative}::{code}");
                    }
                }
            }
        }

        observed.Sort(StringComparer.Ordinal);
        var pinned = PinnedSuppressions.OrderBy(s => s, StringComparer.Ordinal).ToArray();

        var added = Subtract(observed, pinned);
        var removed = Subtract(pinned, observed);

        Assert.True(added.Count == 0 && removed.Count == 0,
            "the warning-suppression census under client/ no longer matches its pin.\n"
            + (added.Count > 0
                ? "  NEW, UNPINNED suppression(s): " + string.Join(", ", added)
                  + "\n  A warning suppressed here is INVISIBLE to client/tests/floor/check-warnings.mjs, which "
                  + "reads build output. Adding one is allowed only as a deliberate, reasoned decision recorded "
                  + "in the same commit — never as the way to make a gate pass.\n"
                : string.Empty)
            + (removed.Count > 0
                ? "  pinned suppression(s) that no longer exist: " + string.Join(", ", removed)
                  + "\n  If a suppression was legitimately removed, drop its line from PinnedSuppressions in the "
                  + "same commit; a stale pin hides the next addition.\n"
                : string.Empty)
            + $"  observed {observed.Count}, pinned {pinned.Length}.");
    }

    [Fact]
    public void NoRepositoryWideAnalyzerConfigurationCanReachTheClientTree()
    {
        // Analyzer-config discovery is a DIRECTORY WALK and does not stop at Directory.Build.props,
        // so a file added anywhere from client/ up to the drive root could lower a diagnostic to
        // `none` and the build-output gate would never see the warning it silenced. Today there is
        // no such file at any level, and that absence is worth pinning because it is invisible.
        //
        // BOTH SHAPES, and the second was missed by this census's first draft (code review):
        // a .globalconfig is auto-included by the SDK with NO csproj reference, so two lines
        //     is_global = true
        //     dotnet_diagnostic.CS0219.severity = none
        // are enough to silence a warning the gate's own forced-full build otherwise prints. That
        // was measured at review and reproduced in this lane: gate GREEN over a live CS0219.
        var root = FindRepoRoot();
        var client = Path.Combine(root, "client");

        // The walk stops at the REPOSITORY ROOT, and that limit is deliberate and named: Roslyn's
        // own discovery continues past it to the drive root, so an .editorconfig placed ABOVE the
        // repository would still apply and is outside this census. It is also outside the
        // repository's control, so a red there would be un-actionable in-tree. The gap is recorded
        // in client/docs/verification-harness.md rather than papered over.
        var found = new List<string>();
        foreach (var pattern in AnalyzerConfigPatterns)
        {
            found.AddRange(EnumerateSources(client, pattern).Select(f => Relative(root, f)));
            for (var dir = new DirectoryInfo(client); dir is not null; dir = dir.Parent)
            {
                found.AddRange(Directory.EnumerateFiles(dir.FullName, pattern, SearchOption.TopDirectoryOnly)
                    .Select(f => Relative(root, f)));
                if (string.Equals(dir.FullName.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        var configs = found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.True(configs.Length == 0,
            "an analyzer configuration file now applies to the client tree: " + string.Join(", ", configs)
            + ". Severities set there (especially `= none`) are applied before MSBuild prints anything, "
            + "so client/tests/floor/check-warnings.mjs is structurally blind to them — and a "
            + ".globalconfig takes effect with no csproj reference at all. If the file is wanted, extend "
            + "this census to read its dotnet_diagnostic severities in the same commit; do not simply "
            + "delete this fact.");

        // Code-analysis suppression attributes and the global suppressions file: neither exists today.
        //
        // The pattern is EXECUTED against every targeted form before it is used to assert an absence,
        // because an absence reported by a pattern that cannot see the shape is not evidence — which
        // is precisely what the review found in the first draft. The probes are assembled from
        // fragments deliberately: written as contiguous literals they would be found by the scan
        // below and would red this fact. Do not "tidy" them into single strings.
        const string attr = "Suppress" + "Message";
        string[] mustMatch =
        [
            "[" + attr + "(\"a\",\"b\")]",
            "[System.Diagnostics.CodeAnalysis." + attr + "(\"a\",\"b\")]",
            "[assembly: " + attr + "(\"xUnit\",\"xUnit2013\")]",
            "[assembly:" + attr + "(\"xUnit\",\"xUnit2013\")]",
            "[module: " + attr + "(\"a\",\"b\")]",
        ];
        var unseen = mustMatch.Where(s => !SuppressionAttribute().IsMatch(s)).ToArray();
        Assert.True(unseen.Length == 0,
            "the suppression-attribute pattern cannot see: " + string.Join(" | ", unseen)
            + ". Every absence this fact reports is worthless for the forms it cannot match. The first "
            + "the first draft saw the bare and fully-qualified forms and missed [assembly:] and [module:], "
            + "which suppress for the WHOLE assembly from any file.");

        string[] mustNotMatch = ["[Fact]", "[GeneratedRegex(\"x\")]", "[assembly: AvaloniaTestApplication(typeof(X))]"];
        var falsePositives = mustNotMatch.Where(s => SuppressionAttribute().IsMatch(s)).ToArray();
        Assert.True(falsePositives.Length == 0,
            "the suppression-attribute pattern matched a non-suppression: " + string.Join(" | ", falsePositives));

        var attributes = EnumerateSources(client, "*.cs")
            .Where(f => SuppressionAttribute().IsMatch(File.ReadAllText(f)))
            .Select(f => Relative(root, f))
            .ToArray();
        Assert.True(attributes.Length == 0,
            "code-analysis suppression attribute(s) appeared under client/: " + string.Join(", ", attributes)
            + ". Pin them in WarningSuppressionCensusTests with a reason, in the same commit.");

        var globalSuppressions = EnumerateSources(client, "GlobalSuppressions.cs").Select(f => Relative(root, f)).ToArray();
        Assert.True(globalSuppressions.Length == 0,
            "a GlobalSuppressions.cs appeared under client/: " + string.Join(", ", globalSuppressions));

        // Project-level warning policy: nothing under client/ sets one today, so every warning the
        // compiler raises is printed at its natural severity and the gate can see all of it.
        var policies = new List<string>();
        foreach (var file in EnumerateSources(client, "*.csproj").Concat(EnumerateSources(client, "*.props")).Concat(EnumerateSources(client, "*.targets")))
        {
            foreach (Match m in WarningPolicyElement().Matches(File.ReadAllText(file)))
            {
                policies.Add($"{Relative(root, file)}::{m.Groups[1].Value}={m.Groups[2].Value}");
            }
        }

        Assert.True(policies.Count == 0,
            "project-level warning policy appeared under client/: " + string.Join(", ", policies)
            + ". A raised WarningLevel or a disabled analyzer set changes what the build emits, which "
            + "changes what the warning gate can observe; pin it here with its reason.");
    }

    private static IEnumerable<string> EnumerateSources(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal)
                     && !f.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>Multiset difference: what is in <paramref name="left"/> and not matched in
    /// <paramref name="right"/>, counting repeats (four identical pragma sites in one file are four
    /// pins, not one).</summary>
    private static List<string> Subtract(IEnumerable<string> left, IEnumerable<string> right)
    {
        var remaining = new List<string>(right);
        var result = new List<string>();
        foreach (var item in left)
        {
            var index = remaining.IndexOf(item);
            if (index >= 0)
            {
                remaining.RemoveAt(index);
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} "
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the suppression census refuses to skip");
    }
}
