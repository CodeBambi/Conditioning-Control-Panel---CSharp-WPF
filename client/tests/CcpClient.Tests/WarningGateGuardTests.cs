using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The guards behind the build-warning gate (<c>client/tests/floor/check-warnings.mjs</c>).
///
/// <para>WHY THE GATE EXISTS. Every landed wave of this port reported "0 warnings / 0 errors" and
/// not one of those claims was mechanically checked. An earlier wave discovered its own reading filter,
/// <c>grep -E "error|warning CS|Build succ"</c>, is structurally incapable of matching
/// <c>warning xUnit2013</c>; it had reported clean four times off that stream. This suite pins the
/// correction so it cannot rot back.</para>
///
/// <para>WHY THE GATE FORCES A NON-INCREMENTAL BUILD. Measured on the base tree, with a
/// real <c>CS0219</c> sitting in a source file: the project's own mandated build command reported
/// <c>1 Warning(s)</c> on the compile that produced it and <c>0 Warning(s)</c> on the very next
/// run, because MSBuild skipped <c>CoreCompile</c> for an up-to-date project. A warning is a
/// property of the COMPILATION, not of the assembly. A gate that does not force compilation is
/// therefore vacuous, and would sign off a tree with live warnings in it.</para>
///
/// <para>THE CENTRAL TRAP THIS SUITE ALSO PINS. <c>check-floor.mjs</c> DELIBERATELY DOES NOT BUILD
/// (<c>client/docs/port-lessons.md:204 @ a8d32c219</c>) and its <c>assertBuildIsFresh</c> stale-build guard exists
/// because it once measured the previous wave's assemblies and called them a regression. It used to
/// run <c>dotnet test --no-build</c>; as of 2026-08-23 it runs the xunit v3 ASSEMBLY directly with
/// <c>-trx</c>, which cannot build by construction, so the invariant is now stronger rather than
/// weaker. <see cref="TheTestFloorStillRunsNoBuild_AndKeepsItsStaleBuildGuard"/> is the pin that stops
/// a later lane teaching it to build while citing this packet as precedent, and it binds BOTH shapes:
/// a returning <c>dotnet</c> invocation may still only ever carry the <c>test</c> verb.</para>
///
/// <para>NEVER-SKIP SHAPE. These facts read repository files with <c>File.ReadAllText</c> and no
/// existence predicate: a missing file throws and the fact FAILS, which is the intended refusal.
/// The absent <c>File.Exists</c> is deliberate and not an oversight.</para>
/// </summary>
public partial class WarningGateGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] GateParts = ["client", "tests", "floor", "check-warnings.mjs"];
    private static readonly string[] FloorParts = ["client", "tests", "floor", "check-floor.mjs"];
    private static readonly string[] SolutionParts = ["client", "CcpClient.sln"];

    /// <summary>The diagnostic line the retired filter could not match, verbatim.</summary>
    private const string XunitWarningLine =
        @"C:\repo\client\tests\CcpClient.Tests\Probe.cs(42,9): warning xUnit2013: " +
        @"Do not use Assert.Equal() to check for collection size. [C:\repo\p.csproj]";

    /// <summary>The retired reading filter, kept executable rather than narrated.</summary>
    private const string RetiredFilterPattern = "error|warning CS|Build succ";

    [GeneratedRegex(@"""dotnet""\s*,\s*\[\s*""(\w+)""")]
    private static partial Regex DotnetInvocation();

    /// <summary>Every <c>"dotnet"</c> token in the file, with whatever follows the comma. Needed
    /// because <see cref="DotnetInvocation"/> only binds the LITERAL argument-array shape: a build
    /// smuggled in as <c>execFileSync("dotnet", buildArgs)</c> leaves the literal verb set
    /// <c>[test]</c> intact and would pass the verb check. Found at this gate's code review.</summary>
    [GeneratedRegex(@"""dotnet""\s*,\s*(\S)")]
    private static partial Regex DotnetArgumentHead();

    [GeneratedRegex(@"export const BUILD_ARGS = \[(?<body>[\s\S]*?)\];")]
    private static partial Regex BuildArgsArray();

    [GeneratedRegex(@"export const COLD_ARGS = \[(?<body>[^\]]*)\];")]
    private static partial Regex ColdArgsArray();

    [GeneratedRegex(@"const WARNING_WITH_CODE = /(?<pattern>.+)/;")]
    private static partial Regex WarningPatternLiteral();

    [GeneratedRegex(@"Project\(""[^""]*""\)\s*=\s*""[^""]*""\s*,\s*""([^""]+\.csproj)""")]
    private static partial Regex SolutionProject();

    [Fact]
    public void TheTestFloorStillRunsNoBuild_AndKeepsItsStaleBuildGuard()
    {
        // The trap this gate was written not to fall into. A warning gate needs a build; the tempting
        // shortcut is to teach the floor to build and read its output. That would delete the very
        // signal `assertBuildIsFresh` exists to raise (client/docs/port-lessons.md:204 @ a8d32c219, and the
        // wave-30 observation of 1022 counted against a source tree containing 1018).
        var floor = File.ReadAllText(Path.Combine([FindRepoRoot(), .. FloorParts]));

        // ZERO dotnet invocations is the CURRENT and strongest state: the floor runs the prebuilt
        // assembly, so there is no verb to get wrong. It is accepted here, but only together with the
        // direct-runner assertions at the end of this fact — without those, "no dotnet invocation"
        // would also be true of a floor that had been gutted, and this pin would pass on a gate that
        // runs nothing at all.
        var verbs = DotnetInvocation().Matches(floor).Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.True(verbs.Length == 0 || verbs is ["test"],
            $"check-floor.mjs invokes dotnet with verb(s) [{string.Join(", ", verbs)}] — the test floor must "
            + "never build. It may run the test assembly directly (its current shape) or `dotnet test`, "
            + "and nothing else. The warning gate is SEPARATE precisely so the floor would not become a "
            + "builder: making it build deletes the signal assertBuildIsFresh exists to raise "
            + "(client/docs/port-lessons.md:204 @ a8d32c219).");
        // The verb check alone binds only the literal `"dotnet", ["verb"` shape, so a build passed
        // as a VARIABLE (`execFileSync("dotnet", buildArgs)`) would slip past it with the verb set
        // still reading [test]. Require every dotnet invocation's argument list to be a literal
        // array, so the verb check above can actually see all of them.
        var argumentHeads = DotnetArgumentHead().Matches(floor).Select(m => m.Groups[1].Value).ToArray();
        var nonLiteral = argumentHeads.Where(h => h != "[").ToArray();
        Assert.True(nonLiteral.Length == 0,
            $"check-floor.mjs passes a NON-LITERAL argument list to dotnet (heads: [{string.Join(", ", argumentHeads)}]). "
            + "The verb check above can only read literal arrays, so a build hidden behind a variable "
            + "would leave the verb set reading [test] and this pin would pass. Keep the floor's dotnet "
            + "invocations literal, or this guard is decorative.");

        // The floor no longer shells out to `dotnet` at all: it runs the xunit v3 assembly directly
        // (testAssemblyPath + "-trx"), which CANNOT build, so "does not build" is now structural
        // rather than a flag it has to remember to pass. Bind that shape, or a lane could delete the
        // direct-runner path and this pin would still read green on the verb check above (which is
        // vacuously satisfied by ZERO dotnet invocations).
        Assert.Contains("testAssemblyPath", floor, StringComparison.Ordinal);
        Assert.Contains("\"-trx\"", floor, StringComparison.Ordinal);
        // And the stale-build guard stays, because running a prebuilt assembly is exactly the
        // condition under which measuring YESTERDAY's binaries is possible.
        Assert.Contains("assertBuildIsFresh", floor, StringComparison.Ordinal);
        Assert.Contains("STALE BUILD", floor, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWarningGateForcesANonIncrementalBuild_BecauseAnUpToDateBuildReportsZeroWarnings()
    {
        var gate = File.ReadAllText(Path.Combine([FindRepoRoot(), .. GateParts]));
        var args = BuildArgsArray().Match(gate);
        Assert.True(args.Success,
            "check-warnings.mjs no longer exports a parseable BUILD_ARGS array — this guard binds the "
            + "exact arguments the gate builds with and refuses to go blind on them");

        var body = args.Groups["body"].Value;
        // Without this flag the gate is VACUOUS: measured, a second build over an unchanged tree
        // holding a live CS0219 reports "0 Warning(s)" because CoreCompile is skipped.
        Assert.Contains("\"--no-incremental\"", body, StringComparison.Ordinal);
        Assert.Contains("\"build\"", body, StringComparison.Ordinal);
        Assert.Contains("\"-c\"", body, StringComparison.Ordinal);
        Assert.Contains("CONFIGURATION", body, StringComparison.Ordinal);
        // ...and the configuration it names is the one the workflow's mandated build uses.
        Assert.Contains("const CONFIGURATION = \"Debug\"", gate, StringComparison.Ordinal);
        // The measurement that justifies the flag must stay attached to it, or the next reader
        // deletes the flag for being slow.
        Assert.Contains("0 Warning(s)", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWarningGateBuildArgumentsWeakenNoWarning()
    {
        // "Never silence a warning to make the gate pass" has to bind the gate itself first: a
        // gate that quietly builds with NoWarn or a lowered WarningLevel would report a clean tree
        // for the same reason the filtered greps did.
        var gate = File.ReadAllText(Path.Combine([FindRepoRoot(), .. GateParts]));
        var buildArgs = BuildArgsArray().Match(gate);
        var coldArgs = ColdArgsArray().Match(gate);
        Assert.True(coldArgs.Success,
            "check-warnings.mjs no longer exports a parseable COLD_ARGS array — the opt-in --cold mode "
            + "appends arguments to the build, so it is bound by exactly the same no-weakening rule");
        var body = buildArgs.Groups["body"].Value + "\n" + coldArgs.Groups["body"].Value;

        string[] banned = ["nowarn", "warnaserror", "warninglevel", "runanalyzers", "-p:", "/p:", "noanalyzers"];
        var found = banned.Where(b => body.Contains(b, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.True(found.Length == 0,
            $"check-warnings.mjs BUILD_ARGS/COLD_ARGS carry warning-weakening switch(es): {string.Join(", ", found)}. "
            + "The gate must observe the warnings the mandated build emits, never a quieter build.");
    }

    [Fact]
    public void TheWarningGateObservesEveryProjectInTheSolution_WhichIsAlsoItsBoundary()
    {
        var root = FindRepoRoot();
        var gate = File.ReadAllText(Path.Combine([root, .. GateParts]));
        var sln = File.ReadAllText(Path.Combine([root, .. SolutionParts]));

        // Discovery from the solution, never a hardcoded list — the same fail-closed principle as
        // check-floor.mjs's discoverTestProjects.
        Assert.Contains("discoverSolutionProjects", gate, StringComparison.Ordinal);
        Assert.Contains("CcpClient.sln", gate, StringComparison.Ordinal);
        Assert.Contains("produced NO output line", gate, StringComparison.Ordinal);

        var projects = SolutionProject().Matches(sln)
            .Select(m => Path.GetFileNameWithoutExtension(m.Groups[1].Value.Replace('\\', '/')))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // The gate's covered surface, pinned by name. This is the BOUNDARY as much as the coverage:
        // these four projects in Debug are what a green gate speaks about, and the Release/RID
        // publish path and the two legacy trees are outside it. A new project joining the solution
        // fails here so its coverage is confirmed rather than assumed.
        string[] expected = ["CcpClient.Desktop", "CcpClient.HeadlessTests", "CcpClient.Tests", "CcpVerify"];
        Assert.Equal(expected, projects);
    }

    [Fact]
    public void TheGatesOwnRegexMatchesTheLineSp113sFilterCouldNotMatch()
    {
        // Not a re-implementation: the pattern is lifted OUT of the shipped gate and executed here,
        // so this fact binds the regex that actually runs.
        var gate = File.ReadAllText(Path.Combine([FindRepoRoot(), .. GateParts]));
        var literal = WarningPatternLiteral().Match(gate);
        Assert.True(literal.Success,
            "check-warnings.mjs no longer declares WARNING_WITH_CODE as a single-line regex literal — "
            + "this fact executes the gate's own pattern and refuses to fall back to a copy of it");

        var shipped = new Regex(literal.Groups["pattern"].Value);
        var retired = new Regex(RetiredFilterPattern);

        // THE EXHIBIT. The retired filter cannot see this line; the shipped pattern can, and names
        // its code. Four "0 warnings" reports were made off a stream filtered the retired way.
        Assert.False(retired.IsMatch(XunitWarningLine),
            "the retired filter is recorded as unable to match the xUnit2013 diagnostic line, but it "
            + "matched it here — the exhibit is wrong and every record built on it must be re-checked");
        var matched = shipped.Match(XunitWarningLine);
        Assert.True(matched.Success, $"the shipped warning pattern did not match {XunitWarningLine}");
        Assert.Equal("xUnit2013", matched.Groups["code"].Value);

        // ...and it does not mistake MSBuild's summary line or a project output line for a warning,
        // which is what would make a red gate unreadable rather than a green one untrue.
        Assert.False(shipped.IsMatch("    0 Warning(s)"),
            "the shipped warning pattern counted MSBuild's summary line as a diagnostic");
        Assert.False(shipped.IsMatch(@"  CcpClient.Tests -> C:\out\CcpClient.Tests.dll"),
            "the shipped warning pattern counted a project output line as a diagnostic");

        // The corpus inside the gate carries the same case, so `--self-test` covers it with no build.
        Assert.Contains("xUnit2013", gate, StringComparison.Ordinal);
        Assert.Contains(RetiredFilterPattern, gate, StringComparison.Ordinal);
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the warning-gate guard refuses to skip");
    }
}
