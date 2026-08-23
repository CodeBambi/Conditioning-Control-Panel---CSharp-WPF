using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The floor's hold on <c>client/tools/citations/intra.mjs</c> — the INTRA-CLIENT citation rot
/// detector — and on the facts that bind it.
///
/// <para><b>Two different things are gated here, and they fail for different reasons.</b>
/// <see cref="TheIntraCitationDetector_FindsNoRot_OnThisTree"/> runs the detector against THIS
/// checkout and reds when a client-side citation stops describing what it points at: a file that
/// moved, a line that shrank away, a document section that was deleted, a basename that two files
/// now answer to. <see cref="TheIntraSelfTest_RunsClean_WithEveryAnchoredFactPassing"/> runs the
/// detector's own fixtured self-test, which is what keeps the first guard from going quietly
/// vacuous. Neither substitutes for the other: a detector that classifies nothing would pass the
/// first and fail the second.</para>
///
/// <para><b>Why the floor has to run the node file at all.</b> <c>check-floor.mjs</c> discovers
/// csproj entries under <c>client/tests/</c>, so a node script under <c>client/tools/</c> is
/// invisible to it and would only ever be as green as the last time somebody ran it by hand. That
/// is the same gap <see cref="CitationSelfTestGateTests"/> was written to close for
/// <c>detect.mjs</c>, and this class closes it for the second tool by the same mechanism and with
/// the same TAP contract.</para>
///
/// <para><b>The fact IDs are a NAME anchor, matched on the <c>In:</c> prefix only.</b> A fact can
/// be retitled freely; deleting one, or renaming its ID, reds
/// <see cref="AnchoredFactIds"/>. That is deliberate and it is the whole point of the mutation
/// facts: a refusal proved load-bearing by <c>I9m</c> is only proved for as long as <c>I9m</c>
/// still exists, and nothing else in this repository would notice its removal.</para>
///
/// <para><b>Exit codes are read as three states, not two.</b> The detector spends 0 on a clean
/// tree, 1 on rot and 2 on could-not-run, so this class distinguishes "your citations rotted" from
/// "the detector could not run" instead of reporting one as the other.</para>
/// </summary>
public sealed partial class IntraCitationTests
{
    private const string Interpreter = "node";

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] DetectorParts = ["client", "tools", "citations", "intra.mjs"];
    private static readonly string[] SelfTestParts = ["client", "tools", "citations", "intra-self-test.mjs"];

    /// <summary>
    /// Every fact ID <c>intra-self-test.mjs</c> carries, matched on the <c>In:</c> PREFIX and never
    /// on the title. The seven <c>m</c> suffixes are the MUTATION facts: each rewrites one span of
    /// the detector's own source and asserts the refusal it removed was actually suppressing a row.
    /// Those are the ones a reviewer should look for first — a refusal with no mutation beside it
    /// can be deleted with this suite green, which is the defect this list exists to prevent.
    /// </summary>
    private static readonly string[] AnchoredFactIds =
    [
        "I1", "I2", "I3", "I4", "I5", "I6",
        "I7", "I7m",
        "I8", "I8m",
        "I9", "I9m",
        "I10",
        "I11", "I11m",
        "I12", "I13", "I13m", "I14", "I15", "I15b", "I16",
        "I17", "I17m",
        "I18", "I18m",
        "I19", "I20",
    ];

    /// <summary>Measured on this machine: the detector is ~1.5 s over 623 corpus files and the
    /// self-test ~1.1 s over 22 temp-directory fixtures. Three minutes is the same window
    /// <see cref="CitationSelfTestGateTests"/> uses, and it is a failure ceiling rather than an
    /// expectation: neither run has ever approached it.</summary>
    private static readonly TimeSpan RunWindow = TimeSpan.FromMinutes(3);

    private static readonly Lazy<Task<ToolRun>> SharedDetectorRun =
        new(() => RunNodeAsync(ToolPath(DetectorParts), tapReporter: false));

    private static readonly Lazy<Task<ToolRun>> SharedSelfTestRun =
        new(() => RunNodeAsync(ToolPath(SelfTestParts), tapReporter: true));

    // ======================================================================================
    // 1. The gate itself: no intra-client citation rot in this checkout.
    // ======================================================================================

    [Fact]
    public async Task TheIntraCitationDetector_FindsNoRot_OnThisTree()
    {
        var run = await SharedDetectorRun.Value;

        Assert.False(
            run.ExitCode == 2,
            "the intra-client citation detector COULD NOT RUN, which is a different verdict from rot and must "
            + "never be repaired by editing a citation. stderr:\n" + Tail(run.StdErr, 2000));

        Assert.True(
            run.ExitCode == 0,
            "intra-client citation rot: a client-side `File.ext:NNN` reference or a `§N`/`Dnnn` document anchor no "
            + "longer describes what it points at. Every row below names the citer, what it cited and what the line "
            + "actually reads, so no file needs opening to fix it. A reference that is deliberately about a PAST "
            + "state carries a commit (`X @ 7527243e7`) and is not checked; widening anything else to clear this is "
            + "the banned repair.\n" + RowsOf(run.StdOut));
    }

    // ======================================================================================
    // 2. The facts that keep the gate above from going vacuous.
    // ======================================================================================

    [Fact]
    public async Task TheIntraSelfTest_RunsClean_WithEveryAnchoredFactPassing()
    {
        var run = await SharedSelfTestRun.Value;
        var transcript = Transcript.Parse(run);
        var problems = transcript.Problems();

        Assert.True(
            problems.Count == 0,
            "the intra-citation self-test transcript is not clean:\n  " + string.Join("\n  ", problems)
            + "\n\nstdout tail:\n" + Tail(run.StdOut, 3000) + "\n\nstderr tail:\n" + Tail(run.StdErr, 1000));

        var byId = transcript.ById();
        var missing = AnchoredFactIds.Where(id => !byId.ContainsKey(id)).ToList();
        var failing = AnchoredFactIds.Where(id => byId.TryGetValue(id, out var r) && !r.Ok).ToList();

        Assert.True(
            missing.Count == 0,
            $"anchored intra-citation fact(s) absent from the transcript: {string.Join(", ", missing)}. A fact that "
            + "vanished takes its guarantee with it — most of these are MUTATION facts, and a deleted mutation is "
            + "how a refusal becomes deletable with the suite green. If a fact was deliberately retired, retire the "
            + "ID from AnchoredFactIds in the same change and say why.\nSaw: " + string.Join(", ", byId.Keys.Order()));

        Assert.True(
            failing.Count == 0,
            $"anchored intra-citation fact(s) failing: {string.Join(", ", failing)}\n{transcript.Evidence(failing)}");
    }

    [Fact]
    public async Task EveryFactInTheScript_IsAnchored_SoANewOneCannotArriveUnwatched()
    {
        var transcript = Transcript.Parse(await SharedSelfTestRun.Value);
        var unanchored = transcript.ById().Keys.Where(id => !AnchoredFactIds.Contains(id)).Order().ToList();

        // The symmetric half of the anchor. Without it the list only catches DELETIONS, and a fact
        // added and later removed would come and go without ever being pinned.
        Assert.True(
            unanchored.Count == 0,
            $"intra-citation fact(s) in the script but not in AnchoredFactIds: {string.Join(", ", unanchored)}. Add "
            + "them: an unanchored fact can be deleted again with this suite green, which is exactly what the anchor "
            + "exists to stop.");
    }

    // ======================================================================================
    // Plumbing.
    // ======================================================================================

    private sealed record ToolRun(int ExitCode, string StdOut, string StdErr);

    private sealed record Result(string Id, bool Ok, string Name, string Detail);

    /// <summary>
    /// The TAP transcript, read as a machine format rather than scraped. TAP because it emits one
    /// column-0 <c>ok N - name</c> per fact, a <c>1..N</c> plan line and per-line counters — the
    /// default spec reporter emits none of those, so a node build that ignored the flag lands in
    /// <see cref="Problems"/>'s named "no TAP result lines" failure instead of passing over an
    /// empty result set. Same contract as <see cref="CitationSelfTestGateTests"/>.
    /// </summary>
    private sealed record Transcript(
        int ExitCode, IReadOnlyList<Result> Results, int? Plan, IReadOnlyDictionary<string, int> Counters)
    {
        public static Transcript Parse(ToolRun run)
        {
            var results = new List<Result>();
            var counters = new Dictionary<string, int>(StringComparer.Ordinal);
            int? plan = null;
            var lines = run.StdOut.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var planMatch = TapPlanLine().Match(line);
                if (planMatch.Success)
                {
                    plan = int.Parse(planMatch.Groups["n"].Value);
                    continue;
                }

                var counter = TapCounterLine().Match(line);
                if (counter.Success)
                {
                    counters[counter.Groups["key"].Value] = int.Parse(counter.Groups["value"].Value);
                    continue;
                }

                var result = TapResultLine().Match(line);
                if (!result.Success)
                {
                    continue;
                }

                var rest = result.Groups["rest"].Value;
                var id = FactIdPrefix().Match(rest);
                results.Add(new Result(
                    id.Success ? id.Groups["id"].Value : string.Empty,
                    !result.Groups["neg"].Success,
                    rest,
                    Detail(lines, i)));
            }

            return new Transcript(run.ExitCode, results, plan, counters);
        }

        /// <summary>The YAML block a failing TAP result carries, so a red names the assertion that
        /// tripped rather than only the fact that tripped.</summary>
        private static string Detail(string[] lines, int resultIndex)
        {
            var block = new StringBuilder();
            for (var i = resultIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("...", StringComparison.Ordinal))
                {
                    break;
                }

                if (line.StartsWith("ok ", StringComparison.Ordinal) || line.StartsWith("not ok ", StringComparison.Ordinal))
                {
                    break;
                }

                block.AppendLine(line);
            }

            return block.ToString();
        }

        public List<string> Problems()
        {
            var problems = new List<string>();
            if (ExitCode != 0)
            {
                problems.Add($"the script exited {ExitCode}");
            }

            if (Results.Count == 0)
            {
                problems.Add("no TAP result lines were parsed — the reporter flag did not take effect");
            }

            // The plan is checked against the results actually parsed. A transcript truncated
            // mid-run has a healthy-looking tail and this is the only thing that catches it.
            if (Plan is null)
            {
                problems.Add("no TAP plan line (1..N) — the transcript is truncated or is not TAP");
            }
            else if (Plan.Value != Results.Count)
            {
                problems.Add($"the plan says {Plan.Value} fact(s) but {Results.Count} result line(s) were parsed");
            }

            foreach (var name in new[] { "tests", "pass" })
            {
                if (!Counters.ContainsKey(name))
                {
                    problems.Add($"the transcript carries no `# {name}` counter");
                }
            }

            foreach (var name in new[] { "fail", "cancelled", "skipped", "todo" })
            {
                if (Counters.TryGetValue(name, out var value) && value != 0)
                {
                    problems.Add($"`# {name}` is {value}; a skipped or todo citation fact is a quarantined one");
                }
            }

            if (Counters.TryGetValue("tests", out var tests) && Results.Count != tests)
            {
                problems.Add($"`# tests` is {tests} but {Results.Count} result line(s) were parsed");
            }

            return problems;
        }

        public Dictionary<string, Result> ById() =>
            Results.Where(r => r.Id.Length > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        public string Evidence(IEnumerable<string> ids)
        {
            var byId = ById();
            var text = new StringBuilder();
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var result))
                {
                    continue;
                }

                text.AppendLine(result.Name);
                text.AppendLine(result.Detail);
            }

            return text.ToString();
        }
    }

    /// <summary>Only the class rows of the detector's report, which is what a failure needs. The
    /// coverage block is derived output and would bury the finding.</summary>
    private static string RowsOf(string stdout)
    {
        var lines = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var end = Array.FindIndex(lines, l => l.StartsWith("## INTRA-CLIENT COVERAGE", StringComparison.Ordinal));
        return string.Join('\n', end > 0 ? lines[..end] : lines);
    }

    private static string Tail(string text, int max) => text.Length <= max ? text : text[^max..];

    private static string ToolPath(string[] parts) => Path.Combine([FindRepoRoot(), .. parts]);

    /// <summary>
    /// Runs a node script to completion and returns its exit code and both streams. Both streams
    /// are read concurrently with the wait: the transcript is larger than a pipe buffer and reading
    /// after the exit would deadlock. The wait goes through the approved bounded helper; there is
    /// no sleep, no bare delay and no clock poll here. A missing interpreter or a missing script is
    /// a FAILURE naming it, never a skip.
    /// </summary>
    private static async Task<ToolRun> RunNodeAsync(string script, bool tapReporter)
    {
        if (!File.Exists(script))
        {
            throw new InvalidOperationException(
                $"the intra-citation tool is missing at {script}. Its absence is exactly the state this gate exists "
                + "to detect, so this guard refuses to skip when its subject is gone");
        }

        var start = new ProcessStartInfo(Interpreter)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepoRoot(),
            // node writes UTF-8. Without these the redirected readers use the console code page on
            // Windows and mangle the § anchors and fact titles this class quotes back.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (tapReporter)
        {
            start.ArgumentList.Add("--test-reporter=tap");
        }

        start.ArgumentList.Add(script);

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"could not start `{Interpreter}` to run {Path.GetFileName(script)}. node is a hard requirement of "
                + "this tree (both citation gates are node scripts) and this guard refuses to skip: a machine that "
                + "cannot run node cannot run its own gates, and allowedSkips is not a quarantine list", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await TestWait.Until(
                process.WaitForExitAsync(),
                $"{Interpreter} {Path.GetFileName(script)} to exit",
                window: RunWindow);
            return new ToolRun(process.ExitCode, await stdout, await stderr);
        }
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}); the intra-citation gate refuses to skip");
    }

    [GeneratedRegex(@"^(?<neg>not )?ok (?<n>\d+) - (?<rest>.*)$")]
    private static partial Regex TapResultLine();

    [GeneratedRegex(@"^1\.\.(?<n>\d+)\s*$")]
    private static partial Regex TapPlanLine();

    [GeneratedRegex(@"^# (?<key>[a-z_]+) (?<value>\d+)$")]
    private static partial Regex TapCounterLine();

    [GeneratedRegex(@"^(?<id>I\d+[a-z]?):")]
    private static partial Regex FactIdPrefix();
}
