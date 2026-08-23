using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The unbounded-wait guard: the sibling of <see cref="TestTimingGuardTests"/>, and deliberately
/// the SAME idiom rather than a second one (bound the wait, or carry the shared
/// <c>// wallclock-allow: &lt;reason&gt;</c> marker AND a pin here). The wall-clock guard bans
/// timing LITERALS at the wait. The two shapes below hide a wait where no literal is visible at
/// the wait at all, so neither that token ban nor an ordinary read of the diff sees them:
///
/// <para>(a) UNBOUNDED JOIN — <c>Task.WhenAll</c>/<c>Task.WaitAll</c>, or an <c>await</c> of a
/// <c>TaskCompletionSource</c> signal, with no budget around it. This suite has no per-test
/// timeout, so a participant that never completes HANGS the whole run instead of failing it: the
/// run dies by outer kill with no failing test name, which is the one failure mode a suite must
/// never have. The bounded forms are <c>TestWait.Until</c> (a window plus a verdict) or a
/// token-bounded <c>WaitAsync</c>.</para>
///
/// <para>(b) INJECTED BUDGET — a deadline handed INTO the subject by name
/// (<c>longPollTimeout:</c>, <c>teardownBudget:</c>, a <c>…Budget</c> field) and then waited on.
/// It is a wall-clock wait laundered through a parameter: the wait itself shows no banned token,
/// and the machine still decides the outcome. Either the budget's elapsing IS the subject (keep
/// the short literal, mark it, pin it — <c>TestWait</c> population 2), or it must not decide the
/// outcome and belongs on <see cref="TestWait.InjectedBudget"/>.</para>
///
/// <para>SCOPE, STATED PLAINLY BECAUSE IT IS THE HONEST MEASURE OF THE GUARD: this scans
/// <c>client/tests/**</c> only, exactly as the wall-clock guard does. The known unbounded join in
/// PRODUCT code — <c>ApplicationHost.ShutdownAsync</c> awaiting each participant's
/// <c>StopAsync()</c> with no budget of its own, beside an
/// <c>OperationRegistry.CancelAndDrainAsync</c> that DOES bound its drain — is outside this scan,
/// and would not be caught by these shapes even if the scan were widened: a bare
/// <c>await x.StopAsync()</c> is textually indistinguishable from every ordinary await. That one is
/// a lifecycle fix, not a lint finding, and this guard does not claim it.</para>
/// </summary>
public class UnboundedWaitGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] TestsParts = ["client", "tests"];

    /// <summary>Exempt wholesale: the approved helper, the wall-clock guard (whose pin list quotes
    /// budget sites verbatim) and this guard (which holds every shape as a string literal).</summary>
    private static readonly string[] ExemptFileNames =
        ["TestWait.cs", "TestTimingGuardTests.cs", "UnboundedWaitGuardTests.cs"];

    /// <summary>(repo-relative path under <c>client/tests</c>, exact trimmed code before the
    /// marker, expected occurrence count). Unmarked shape → fail. Marked but unpinned → fail. Pin
    /// count mismatch → fail (stale pins cannot rot). Adding a pin is the review friction.</summary>
    private static readonly (string Path, string Code, int Count)[] Pins =
    [
        // Shape (a): the awaited signal IS the subject — a deliberately uncooperative provider, or
        // the product's parked-operation shape — and the test releases it on every path.
        ("CcpClient.Tests/AiMemoryPipelineTests.cs", "await _gate.Task.ConfigureAwait(false);", 1),
        ("CcpClient.Tests/AiOperationPipelineTests.cs", "await _gate.Task.ConfigureAwait(false);", 1),
        ("CcpClient.Tests/AsyncLifecycleTests.cs", "await stopped.Task;", 2),
        // Shape (b): the budget's elapsing IS the subject (TestWait population 2).
        ("CcpClient.Tests/DtrhLoopbackContractTests.cs", "longPollTimeout: TimeSpan.FromMilliseconds(200));", 1),
        ("CcpClient.Tests/SoundArbitrationTests.cs", "private static readonly TimeSpan GiveUpBudget = TimeSpan.FromMilliseconds(200);", 1),
        ("CcpClient.Tests/SoundArbitrationTests.cs", "private static readonly TimeSpan ConstructionBudget = TimeSpan.FromMilliseconds(200);", 1),
    ];

    [Fact]
    public void NoUnboundedJoinsOrInjectedBudgetsInClientTests()
    {
        var testsRoot = Path.Combine([FindRepoRoot(), .. TestsParts]);
        Assert.True(Directory.Exists(testsRoot),
            $"client/tests not found at {testsRoot} — the unbounded-wait guard refuses to skip");

        var violations = new List<string>();
        var seen = new Dictionary<(string Path, string Code), int>();
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (ExemptFileNames.Any(e => normalized.EndsWith("/" + e, StringComparison.Ordinal)))
            {
                continue;
            }

            var relative = normalized[(normalized.IndexOf("tests/", StringComparison.Ordinal) + "tests/".Length)..];
            foreach (var finding in UnboundedWaitScan.Scan(File.ReadAllText(file)))
            {
                if (finding.Reason is null)
                {
                    violations.Add($"{relative}:{finding.Line}: unbounded {finding.Shape} — bound it with TestWait, " +
                        "inject TestWait.InjectedBudget, or record the reason in a // wallclock-allow: marker AND pin " +
                        "it in UnboundedWaitGuardTests: " + finding.Code);
                    continue;
                }

                if (finding.Reason.Length == 0)
                {
                    violations.Add($"{relative}:{finding.Line}: the wallclock-allow marker carries no reason — " +
                        $"name WHY this {finding.Shape} cannot hang the run");
                    continue;
                }

                var key = (relative, finding.Code);
                if (!Pins.Any(p => p.Path == key.relative && p.Code == finding.Code))
                {
                    violations.Add($"{relative}:{finding.Line}: marked {finding.Shape} is NOT PINNED in " +
                        "UnboundedWaitGuardTests — that edit is the review gate: " + finding.Code);
                    continue;
                }

                seen[key] = seen.GetValueOrDefault(key) + 1;
            }
        }

        foreach (var pin in Pins)
        {
            var actual = seen.GetValueOrDefault((pin.Path, pin.Code));
            if (actual != pin.Count)
            {
                violations.Add($"stale pin: {pin.Path} \"{pin.Code}\" expected {pin.Count} occurrence(s), found {actual} — " +
                    "update or remove the pin (stale pins cannot rot)");
            }
        }

        Assert.True(violations.Count == 0,
            "unbounded-wait guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    // ---------- the scanner's own facts: each fails on one shape, on three lines of source ----------

    [Fact]
    public void Scan_FlagsAJoinWithNoBudget()
    {
        var finding = Assert.Single(UnboundedWaitScan.Scan("        await Task.WhenAll(tasks);\n"));
        Assert.Equal("join", finding.Shape);
        Assert.Equal(1, finding.Line);
        Assert.Null(finding.Reason);
    }

    [Fact]
    public void Scan_AcceptsAJoinBoundedByTheApprovedHelper_EvenAcrossLines()
    {
        // The bounded idiom wraps the join in TestWait.Until on a PREVIOUS line: a line-local check
        // would red here, and an author silencing it would land back on the unbounded form.
        Assert.Empty(UnboundedWaitScan.Scan(
            "        var handles = Arm();\n        await TestWait.Until(\n            Task.WhenAll(signals),\n            \"every schedule to run\");\n"));
    }

    [Fact]
    public void Scan_FlagsABareSignalAwait_ButNotATokenBoundedOne()
    {
        Assert.Equal("signal await", Assert.Single(UnboundedWaitScan.Scan("        await started.Task;\n")).Shape);
        Assert.Empty(UnboundedWaitScan.Scan("        await started.Task.WaitAsync(token);\n"));
        Assert.Empty(UnboundedWaitScan.Scan("        await Task.CompletedTask;\n"));
    }

    [Fact]
    public void Scan_FlagsABudgetInjectedByName_ButNotTheSharedOne()
    {
        var finding = Assert.Single(UnboundedWaitScan.Scan(
            "        var s = new Server(inbox, longPollTimeout: TimeSpan.FromMilliseconds(200));\n"));
        Assert.Equal("injected budget", finding.Shape);
        Assert.Empty(UnboundedWaitScan.Scan("        var s = new Server(inbox, longPollTimeout: TestWait.InjectedBudget);\n"));
        // An equality COMPARISON against a budget is an assertion about one, not an injection of one.
        Assert.Empty(UnboundedWaitScan.Scan("        Assert.True(requestTimeout == TimeSpan.FromSeconds(1));\n"));
    }

    [Fact]
    public void Scan_ReadsTheMarkerReason_AndIgnoresShapesQuotedInComments()
    {
        var finding = Assert.Single(UnboundedWaitScan.Scan(
            "        await stopped.Task; // wallclock-allow: the parked operation IS the subject\n"));
        Assert.Equal("the parked operation IS the subject", finding.Reason);
        Assert.Equal("await stopped.Task;", finding.Code);
        Assert.Empty(UnboundedWaitScan.Scan("        // the tail after `await stopped.Task` is a thread-pool continuation\n"));
        Assert.Empty(UnboundedWaitScan.Scan("        /// <summary>bounded by Task.WhenAll(x) elsewhere</summary>\n"));
    }

    [Fact]
    public void Scan_LeavesLinesTheWallClockGuardAlreadyOwns_ToThatGuard()
    {
        // One site pinned in two guards is two things to keep in sync and one of them to forget.
        // The sample is CONCATENATED so this file never carries the wall-clock guard's
        // budget-assignment token contiguously (its scan reads comments too, as this comment
        // learned): neither guard is exempted from the other, and both keep full reach here.
        Assert.Empty(UnboundedWaitScan.Scan(
            "            RequestTimeout =" + " TimeSpan.FromMilliseconds(800), // wallclock-allow: the budget's elapsing IS the subject\n"));
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the unbounded-wait guard refuses to skip");
    }
}

/// <summary>
/// The scanner, kept apart from the fact that walks the tree so every shape can be failed on a
/// three-line string instead of on the repository (the inversion is a test, not a manual step).
/// A text scan, not a syntax tree: the shapes are lexical, and a Roslyn dependency for a lint this
/// size is the trade nobody wants to maintain.
/// </summary>
internal static class UnboundedWaitScan
{
    internal const string Marker = "// wallclock-allow:";

    internal readonly record struct Finding(int Line, string Shape, string Code, string? Reason);

    /// <summary><c>Boundable</c> shapes are waits, so wrapping them in a window clears them. An
    /// injected budget IS the wall clock: nothing around it can bound it, so it is never cleared
    /// by context — only by the shared budget, by a marker plus a pin, or by deletion.</summary>
    private static readonly (string Shape, Regex Pattern, bool Boundable)[] Shapes =
    [
        ("join", new Regex(@"Task\.(?:WhenAll|WaitAll)\s*\(", RegexOptions.Compiled), true),
        ("signal await", new Regex(@"await\s+[A-Za-z_][A-Za-z0-9_.]*\.Task\b", RegexOptions.Compiled), true),
        // A deadline handed in by NAME. Deliberately name-driven: a bare TimeSpan literal is an
        // ordinary data value 300+ times over in this suite (durations, cooldowns, session lengths),
        // so a shape-only rule would be noise nobody could act on. The `(?<![=!<>])[:=](?!=)`
        // separator excludes ==, !=, <= and >=, which are assertions about a budget.
        ("injected budget", new Regex(
            @"\b\w*(?:[Tt]imeout|[Bb]udget|[Dd]eadline|[Gg]race|[Ll]inger)\s*(?<![=!<>])[:=](?!=)\s*TimeSpan\.From",
            RegexOptions.Compiled), false),
    ];

    /// <summary>Constructs that give a join or a signal await a bound: the approved helper, or a
    /// cancellation-token-bounded <c>WaitAsync</c>. Checked over the whole STATEMENT, because the
    /// bounded idiom routinely wraps a join that begins on the next line.</summary>
    private static readonly string[] Bounds = ["TestWait.", "WaitAsync("];

    internal static IEnumerable<Finding> Scan(string text)
    {
        var findings = new List<Finding>();
        foreach (var (shape, pattern, boundable) in Shapes)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var lineStart = match.Index == 0 ? 0 : text.LastIndexOf('\n', match.Index - 1) + 1;
                var lineEnd = text.IndexOf('\n', match.Index);
                var line = lineEnd < 0 ? text[lineStart..] : text[lineStart..lineEnd];
                var commentAt = line.IndexOf("//", StringComparison.Ordinal);

                // The shape sits inside a comment: prose quoting the code it explains.
                if (commentAt >= 0 && commentAt < match.Index - lineStart)
                {
                    continue;
                }

                // Lines the wall-clock guard already owns are pinned THERE, exactly once.
                if (TestTimingGuardTests.ForbiddenTokens.Any(t => line.Contains(t, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (boundable && Bounds.Any(b => Statement(text, match.Index).Contains(b, StringComparison.Ordinal)))
                {
                    continue;
                }

                var markerAt = line.IndexOf(Marker, StringComparison.Ordinal);
                findings.Add(new Finding(
                    text.AsSpan(0, lineStart).Count('\n') + 1,
                    shape,
                    (markerAt < 0 ? line : line[..markerAt]).Trim(),
                    markerAt < 0 ? null : line[(markerAt + Marker.Length)..].Trim()));
            }
        }

        return findings;
    }

    /// <summary>The enclosing statement: back to the previous <c>;</c>, <c>{</c> or <c>}</c>, forward
    /// to the next <c>;</c>. Errs toward a SHORTER statement, which errs toward reporting.</summary>
    private static string Statement(string text, int index)
    {
        var start = index == 0 ? 0 : text.LastIndexOfAny([';', '{', '}'], index - 1) + 1;
        var end = text.IndexOf(';', index);
        return end < 0 ? text[start..] : text[start..(end + 1)];
    }
}
