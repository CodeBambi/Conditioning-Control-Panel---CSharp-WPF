using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The floor's hold on <c>client/tools/citations/self-test.mjs</c>, which is the only
/// fixtured guard on what <c>detect.mjs</c> CLASSIFIES.
///
/// <para><b>Why this file exists.</b> F1-F14 were fixtured first and F15-F24 added later (moved /
/// gone / ambiguous / out-of-range, and the coverage arithmetic), and <b>no standing gate ran any
/// of them</b>: <c>check-floor.mjs</c> discovers csproj entries under <c>client/tests/</c>, so a
/// node file under <c>client/tools/</c> is invisible to it and the classifier's green held only as
/// of the last time a human ran the script by hand (<c>task-board.md:49</c> and <c>:330</c>). This
/// class is the bridge. What <see cref="CitationNeedleTests"/> already pins is the tool's
/// CONTRACT (both modes exit 0, both print the coverage block) and its DATA (needle schema,
/// one-line resolution at a frozen SHA); neither touches the mechanism, and this does.</para>
///
/// <para><b>The transcript is the interface, and it is a real contract.</b> The script is run as
/// <c>node --test-reporter=tap client/tools/citations/self-test.mjs</c>. TAP rather than node's
/// default spec reporter because TAP is a machine format: one column-0 <c>ok N - name</c> per fact,
/// a <c>1..N</c> plan line, and a per-line <c># tests</c> / <c># pass</c> / <c># fail</c> summary.
/// The spec reporter emits no <c>ok N -</c> line at all, so a node that ignored the flag lands in
/// <see cref="Transcript.Problems"/>'s named "no TAP result lines" failure instead of passing over
/// an empty result set.</para>
///
/// <para><b>Nothing here hard-codes how many facts the script has.</b>
/// <see cref="TheTranscriptTotals_ComeFromTheScript_AndAgreeWithItsOwnResults"/> takes the number
/// from the script's own plan line and its own summary and cross-checks both against the result
/// lines actually parsed. What IS written down is a list of fact IDS (<see cref="AnchoredFactIds"/>)
/// matched on the <c>Fn:</c> PREFIX only, never on the title, so a retitled fact survives while a
/// deleted or renamed one reds. That asymmetry is the whole point: a pinned total reds when a fact
/// is ADDED, which is friction in the wrong direction and is the class of number this wave has
/// already spent two packets repairing. An added-but-failing fact is still caught by
/// <see cref="TheCitationSelfTest_RunsClean_EveryFactPassing"/>.</para>
///
/// <para><b>This guard never skips.</b> A missing <c>node</c>, a missing script, an unexpected exit
/// code and an unrecognised transcript are each a FAILURE carrying the reason. A skip here would be
/// a silent hole in the only guard the classifier has, and <c>allowedSkips</c> is not a quarantine
/// list.</para>
///
/// <para><b>What this does NOT prove.</b> Not that the classifier is CORRECT: only that its
/// fixtured facts execute and hold, on this machine's <c>node</c>, on every floor run. It does not
/// widen what the detector can see (the corpus and token-class limits recorded at
/// <c>wpf-surface-reachability.md</c> D260 are untouched), and it does not turn the review list into
/// a red test on the real tree (<c>detect.mjs:13-14</c>). No UI is involved, so it proves nothing
/// about rendering, interaction, focus, audio or animation. It is observed on Windows only.</para>
/// </summary>
public sealed partial class CitationSelfTestGateTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SelfTestParts = ["client", "tools", "citations", "self-test.mjs"];

    private const string Interpreter = "node";
    private const string TapReporterFlag = "--test-reporter=tap";

    /// <summary>The NAME anchor: every fact ID the self-test carries, matched on the <c>Fn:</c>
    /// prefix. Deleting or renaming a fact reds <see cref="EveryAnchoredFact_IsPresent_AndPassing"/>;
    /// adding one does not, and that direction is deliberate. F15-F24 are the classification half
    /// (moved / gone / ambiguous / out-of-range plus the coverage arithmetic) this packet exists to
    /// gate; F1-F14 are here because the board's acceptance is F1-F24 on the floor and they carried
    /// the identical unguarded limit. F25-F29 are the REGENERATE mode — the only one that writes the
    /// inventory — and they are anchored for the sharper reason: a deleted write fact leaves a
    /// writer loose on a tracked document with nothing watching what it moves. Updating this list IS
    /// the review friction when a fact is legitimately retired.</summary>
    private static readonly string[] AnchoredFactIds =
    [
        "F1", "F2", "F2b", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14",
        "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
        "F25", "F26", "F27", "F28", "F29",
    ];

    /// <summary>The subject the negative-path facts mutate. F16 is the moved class, whose reason
    /// string carries the signed shift that is the entire actionable output of that class
    /// (<c>detect.mjs:1058</c>, <c>:1061-1062</c>).</summary>
    private const string MutationSubjectId = "F16";

    /// <summary>
    /// The wait window, and it is WIDER than <see cref="TestWait.DefaultWindow"/> for a reason that
    /// is not the banned one. <c>TestWait</c> says "never widen a per-test window to buy green",
    /// which forbids widening to paper over an INTERMITTENT pass. This subject's honest duration is
    /// 23.6 s measured (25 fixture repositories, each running <c>git</c> several times and the
    /// detector CLI as a child process; node v24.5.0, 2026-08-21), so the 20 s default would be a
    /// DETERMINISTIC false red on a healthy tree rather than a flake being silenced. Three minutes
    /// is ~7.6x the measurement, sized for a lane running through the 3-slot gate limiter, and stays
    /// finite so a genuinely wedged node fails loudly instead of hanging the host.
    /// <para>Re-measured at 20.4 s over 30 fixture repositories (node v24.5.0, 2026-08-23) when
    /// F25-F29 added the regenerate mode's five. The window is unchanged: adding facts moved the
    /// honest duration DOWN on this machine, so nothing here was widened to buy room.</para>
    /// </summary>
    private static readonly TimeSpan SelfTestWindow = TimeSpan.FromMinutes(3);

    /// <summary>The self-test costs ~23.6 s, so it runs ONCE per assembly and every fact asserts
    /// over that one transcript. It is deliberately not given a per-test cancellation token: a
    /// shared run must not be tied to whichever fact happened to touch it first.</summary>
    private static readonly Lazy<Task<ToolRun>> SharedRun = new(() => RunSelfTestAsync(Interpreter, SelfTestPath()));

    private readonly ITestOutputHelper _output;

    public CitationSelfTestGateTests(ITestOutputHelper output) => _output = output;

    // ======================================================================================
    // The real run.
    // ======================================================================================

    [Fact]
    public async Task TheCitationSelfTest_RunsClean_EveryFactPassing()
    {
        var run = await SharedRun.Value;
        var transcript = Transcript.Parse(run.ExitCode, run.StdOut, run.StdErr);
        var problems = transcript.Problems();

        _output.WriteLine(transcript.Headline());
        Assert.True(problems.Count == 0,
            "client/tools/citations/self-test.mjs did not run clean, so detect.mjs's classification is "
            + "unguarded until this is fixed. Reproduce with `node --test-reporter=tap "
            + "client/tools/citations/self-test.mjs`." + Environment.NewLine
            + string.Join(Environment.NewLine, problems) + transcript.FailureEvidence());
    }

    [Fact]
    public async Task EveryAnchoredFact_IsPresent_AndPassing()
    {
        var run = await SharedRun.Value;
        var transcript = Transcript.Parse(run.ExitCode, run.StdOut, run.StdErr);
        var byId = transcript.ById();

        var missing = AnchoredFactIds.Where(id => !byId.ContainsKey(id)).ToList();
        var failing = AnchoredFactIds.Where(id => byId.TryGetValue(id, out var found) && !found.Ok).ToList();

        Assert.True(missing.Count == 0,
            "the self-test no longer reports these anchored fact(s): " + string.Join(", ", missing)
            + ". A deleted or renamed fact silently shrinks the only coverage detect.mjs's classifier has, "
            + "so the anchor reds and the list in AnchoredFactIds is the thing to update: deliberately, "
            + "and only when the fact was genuinely retired.");
        Assert.True(failing.Count == 0,
            "anchored fact(s) present but not passing: " + string.Join(", ", failing) + transcript.FailureEvidence());
    }

    /// <summary>
    /// The count is DERIVED, never typed: the plan line, the summary and the parsed result lines are
    /// three numbers the script itself emits, and they must agree. The non-empty assertion closes the
    /// vacuous corner where a transcript with zero facts would satisfy every equality.
    /// </summary>
    [Fact]
    public async Task TheTranscriptTotals_ComeFromTheScript_AndAgreeWithItsOwnResults()
    {
        var run = await SharedRun.Value;
        var transcript = Transcript.Parse(run.ExitCode, run.StdOut, run.StdErr);
        var listed = transcript.Results.Count;

        _output.WriteLine(transcript.Headline());
        Assert.True(listed > 0,
            "the transcript carried no TAP result line at all: nothing was measured, and a count taken "
            + "from an empty run is the vacuous green this bridge exists to prevent." + transcript.FailureEvidence());
        Assert.True(transcript.Plan == listed,
            $"the script planned {transcript.Plan?.ToString() ?? "no"} fact(s) but listed {listed} result line(s)");
        Assert.True(transcript.Counter("tests") == listed,
            $"the script's own summary claims {transcript.Counter("tests")} test(s) but listed {listed} result line(s)");
        Assert.True(transcript.Counter("pass") + transcript.Counter("fail") == transcript.Counter("tests"),
            $"the summary does not add up: pass {transcript.Counter("pass")} + fail {transcript.Counter("fail")} "
            + $"!= tests {transcript.Counter("tests")}");
    }

    // ======================================================================================
    // The negative paths: a bridge that cannot go red is worse than no bridge.
    // ======================================================================================

    /// <summary>
    /// A failing fact is red EVEN AT EXIT 0, so the bridge cannot be passing merely because it reads
    /// the exit code. The mutation is applied to the REAL transcript and reproduces the exact shape
    /// node emits for a failure (verified against a genuinely broken detect.mjs), so it cannot drift
    /// from the reporter the way a hand-written fixture would.
    /// </summary>
    [Fact]
    public async Task AFailingFact_IsReadAsRed_EvenAtExitZero()
    {
        var run = await SharedRun.Value;
        var mutated = MutateOneFactToFailing(run.StdOut, MutationSubjectId);
        var problems = Transcript.Parse(0, mutated, string.Empty).Problems();

        // Belt, not braces, and near-tautological on its own: MutateOneFactToFailing rejoins with
        // "\n" while the captured stdout carries "\r\n", so these two differ even on a no-op edit.
        // THE REAL ANTI-VACUITY GUARD IS THAT HELPER THROWING when it finds no passing result line
        // for the subject, which is what fired under mutation A. Do not read this line as the check.
        Assert.True(mutated != run.StdOut, "the mutation changed nothing, so this fact would prove nothing");
        Assert.True(problems.Count > 0,
            $"a transcript reporting {MutationSubjectId} as FAILED was read as clean at exit 0: the bridge would "
            + "pass a broken classifier whenever the script's exit code lied");
        Assert.Contains(problems, p => p.Contains(MutationSubjectId, StringComparison.Ordinal)
            && p.Contains("did not pass", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other half: a non-zero exit is red even when the transcript that came with it looks
    /// perfect. A script that crashed after printing its summary must never read as green.
    /// </summary>
    [Fact]
    public async Task ANonZeroExit_IsReadAsRed_EvenWithACleanTranscript()
    {
        var run = await SharedRun.Value;
        var problems = Transcript.Parse(1, run.StdOut, "boom").Problems();

        Assert.True(problems.Count > 0, "a non-zero exit was read as clean because the transcript looked fine");
        Assert.Contains(problems, p => p.Contains("exited 1", StringComparison.Ordinal));
    }

    /// <summary>
    /// A result line removed while the summary still claims every fact ran. A bridge that trusted the
    /// summary counters alone would call this green, which is how a truncated or partially written
    /// transcript becomes a false pass.
    /// </summary>
    [Fact]
    public async Task ATruncatedTranscript_IsReadAsRed_TheSummaryIsNeverTrustedAlone()
    {
        var run = await SharedRun.Value;
        var truncated = DropOneResultLine(run.StdOut, MutationSubjectId);
        var problems = Transcript.Parse(0, truncated, string.Empty).Problems();

        // Same caveat as AFailingFact_IsReadAsRed_EvenAtExitZero: the line-ending rejoin makes this
        // comparison near-tautological. DropOneResultLine THROWING when it removes nothing is the
        // guard that actually stops this fact from passing vacuously.
        Assert.True(truncated != run.StdOut, "the truncation changed nothing, so this fact would prove nothing");
        Assert.True(problems.Count > 0, "a transcript missing a result line was read as clean on the summary's word alone");
        Assert.Contains(problems, p => p.Contains("but its own summary claims", StringComparison.Ordinal));
    }

    // ======================================================================================
    // Fail, never skip.
    // ======================================================================================

    [Fact]
    public async Task AnAbsentInterpreter_FailsNamingIt_NeverSkips()
    {
        var absent = "ccp-node-that-is-not-installed";
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunSelfTestAsync(absent, SelfTestPath()));

        Assert.Contains(absent, failure.Message, StringComparison.Ordinal);
        Assert.Contains("refuses to skip", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAbsentScript_FailsNamingIt_NeverSkips()
    {
        var absent = Path.Combine(FindRepoRoot(), "client", "tools", "citations", "self-test-that-is-not-here.mjs");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunSelfTestAsync(Interpreter, absent));

        Assert.Contains("self-test-that-is-not-here.mjs", failure.Message, StringComparison.Ordinal);
        Assert.Contains("refuses to skip", failure.Message, StringComparison.Ordinal);
    }

    // ======================================================================================
    // Machinery.
    // ======================================================================================

    private sealed record ToolRun(int ExitCode, string StdOut, string StdErr);

    private sealed record Result(int Number, string Id, string Name, bool Ok, string Directive, string Detail);

    /// <summary>
    /// One parsed TAP transcript, and the single place a verdict is formed. Parsing and judging are
    /// kept apart from the spawn so both can be exercised without a process: the negative-path facts
    /// feed this mutations of the REAL transcript.
    /// </summary>
    private sealed record Transcript(
        int ExitCode, IReadOnlyList<Result> Results, int? Plan, IReadOnlyDictionary<string, int> Counters, string StdErr)
    {
        private static readonly string[] RequiredCounters = ["tests", "pass", "fail", "cancelled", "skipped", "todo"];

        private static readonly string[] MustBeZeroCounters = ["fail", "cancelled", "skipped", "todo"];

        public static Transcript Parse(int exitCode, string stdout, string stderr)
        {
            var lines = SplitLines(stdout);
            var results = new List<Result>();
            var counters = new Dictionary<string, int>(StringComparer.Ordinal);
            int? plan = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var planLine = TapPlanLine().Match(line);
                if (planLine.Success)
                {
                    plan = int.Parse(planLine.Groups["n"].Value);
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
                var directive = TapDirective().Match(rest);
                var name = directive.Success ? rest[..directive.Index] : rest;
                var id = FactIdPrefix().Match(name);
                results.Add(new Result(
                    int.Parse(result.Groups["n"].Value),
                    id.Success ? id.Groups["id"].Value : string.Empty,
                    name,
                    !result.Groups["neg"].Success,
                    directive.Success ? directive.Groups["directive"].Value.ToUpperInvariant() : string.Empty,
                    YamlBlockAfter(lines, i)));
            }

            return new Transcript(exitCode, results, plan, counters, stderr);
        }

        /// <summary>Every named reason this run is not clean; empty means clean. A parser that read
        /// only the exit code, or only the summary counters, would return empty for two of the
        /// transcripts the negative-path facts feed it.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();

            if (ExitCode != 0)
            {
                problems.Add($"the self-test exited {ExitCode}; exit 0 means every fact holds, and node names the "
                    + "ones that did not");
            }

            if (Results.Count == 0)
            {
                problems.Add("no TAP result lines in the output. The run must be `node --test-reporter=tap "
                    + "<script>`: node's default spec reporter emits no `ok N -` line at all, so an ignored flag "
                    + "lands here rather than reading as an empty green run");
            }

            foreach (var result in Results.Where(r => !r.Ok))
            {
                problems.Add($"self-test fact {Label(result)} did not pass");
            }

            foreach (var result in Results.Where(r => r.Directive.Length > 0))
            {
                problems.Add($"self-test fact {Label(result)} carries a {result.Directive} directive: a fact on this "
                    + "floor is never skipped or deferred, it holds or it fails");
            }

            foreach (var required in RequiredCounters.Where(c => !Counters.ContainsKey(c)))
            {
                problems.Add($"the transcript carries no `# {required}` summary line, so its own totals cannot be "
                    + "cross-checked against the results it listed");
            }

            foreach (var zero in MustBeZeroCounters.Where(c => Counters.GetValueOrDefault(c) != 0))
            {
                problems.Add($"the script's summary reports `# {zero} {Counters[zero]}`, and every one of those must "
                    + "be zero for the classifier to count as guarded");
            }

            if (Counters.TryGetValue("tests", out var tests))
            {
                if (Results.Count != tests)
                {
                    problems.Add($"the transcript lists {Results.Count} result line(s) but its own summary claims "
                        + $"{tests} test(s): the two disagree, so neither can be trusted");
                }

                if (Plan is not null && Plan != tests)
                {
                    problems.Add($"the plan line says 1..{Plan} but the summary claims {tests} test(s)");
                }

                if (Counters.TryGetValue("pass", out var pass) && Counters.TryGetValue("fail", out var fail)
                    && pass + fail != tests)
                {
                    problems.Add($"the summary does not add up: pass {pass} + fail {fail} != tests {tests}");
                }
            }

            if (Plan is null)
            {
                problems.Add("the transcript carries no `1..N` plan line, so the script never said how many facts it "
                    + "intended to run");
            }

            return problems;
        }

        public Dictionary<string, Result> ById() =>
            Results.Where(r => r.Id.Length > 0).GroupBy(r => r.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        public int Counter(string name) => Counters.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException(
                $"the self-test transcript carries no `# {name}` summary line; this guard refuses to invent one");

        /// <summary>The derived totals, written to test output so the number is REPORTED by the
        /// script rather than typed by this file.</summary>
        public string Headline() =>
            $"self-test.mjs: {Results.Count} result line(s), plan 1..{Plan?.ToString() ?? "?"}, "
            + $"# tests {Counters.GetValueOrDefault("tests")}, # pass {Counters.GetValueOrDefault("pass")}, "
            + $"# fail {Counters.GetValueOrDefault("fail")}, # skipped {Counters.GetValueOrDefault("skipped")}, "
            + $"exit {ExitCode}";

        /// <summary>The failing facts with the assertion detail node printed under each, so a red
        /// here names the broken classification instead of only counting it.</summary>
        public string FailureEvidence()
        {
            var evidence = new StringBuilder();
            foreach (var result in Results.Where(r => !r.Ok))
            {
                evidence.Append(Environment.NewLine).Append("  ").Append(Label(result))
                    .Append(Environment.NewLine).Append(result.Detail);
            }

            if (StdErr.Trim().Length > 0)
            {
                evidence.Append(Environment.NewLine).Append("  stderr: ").Append(Tail(StdErr, 800));
            }

            return evidence.ToString();
        }

        private static string Label(Result result) =>
            result.Id.Length > 0 ? $"{result.Id} ({result.Name})" : $"#{result.Number} ({result.Name})";
    }

    /// <summary>
    /// The exact red form node emits, applied to the transcript the real run produced: the result
    /// line gains its `not `, and the summary's pass/fail move with it. Verified against a genuinely
    /// broken detect.mjs, which produced `not ok 17 - F16: ...` with `# pass 24` / `# fail 1`.
    /// Throws rather than returning the input unchanged, so the fact using it can never be vacuous.
    /// </summary>
    private static string MutateOneFactToFailing(string stdout, string id)
    {
        var lines = SplitLines(stdout);
        var flipped = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var result = TapResultLine().Match(lines[i]);
            if (result.Success && !result.Groups["neg"].Success && IdOf(result.Groups["rest"].Value) == id)
            {
                lines[i] = "not " + lines[i];
                flipped = true;
                continue;
            }

            var counter = TapCounterLine().Match(lines[i]);
            if (!counter.Success)
            {
                continue;
            }

            var value = int.Parse(counter.Groups["value"].Value);
            lines[i] = counter.Groups["key"].Value switch
            {
                "pass" => $"# pass {value - 1}",
                "fail" => $"# fail {value + 1}",
                _ => lines[i],
            };
        }

        if (!flipped)
        {
            throw new InvalidOperationException(
                $"no passing TAP result line for {id} to mutate; the negative-path fact would have proven nothing");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Removes one result line and leaves the summary still claiming it ran. Throws when it
    /// removed nothing, for the same reason.</summary>
    private static string DropOneResultLine(string stdout, string id)
    {
        var kept = new List<string>();
        var dropped = false;

        foreach (var line in SplitLines(stdout))
        {
            var result = TapResultLine().Match(line);
            if (!dropped && result.Success && IdOf(result.Groups["rest"].Value) == id)
            {
                dropped = true;
                continue;
            }

            kept.Add(line);
        }

        if (!dropped)
        {
            throw new InvalidOperationException(
                $"no TAP result line for {id} to remove; the truncation fact would have proven nothing");
        }

        return string.Join("\n", kept);
    }

    private static string IdOf(string rest)
    {
        var directive = TapDirective().Match(rest);
        var name = directive.Success ? rest[..directive.Index] : rest;
        var id = FactIdPrefix().Match(name);
        return id.Success ? id.Groups["id"].Value : string.Empty;
    }

    /// <summary>The indented YAML block node prints under a result, which is where the failing
    /// assertion's own text lives.</summary>
    private static string YamlBlockAfter(string[] lines, int resultIndex)
    {
        var block = new StringBuilder();
        for (var i = resultIndex + 1; i < lines.Length && i <= resultIndex + 40; i++)
        {
            if (lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0]))
            {
                break;
            }

            block.Append(lines[i]).Append(Environment.NewLine);
        }

        return block.ToString();
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static string Tail(string text, int max) => text.Length <= max ? text : text[^max..];

    private static string SelfTestPath() => Path.Combine([FindRepoRoot(), .. SelfTestParts]);

    /// <summary>
    /// Runs the self-test to completion and returns its exit code and streams. Both streams are read
    /// concurrently with the wait: the transcript is larger than a pipe buffer and reading after the
    /// exit would deadlock. The wait goes through the approved helper; there is no sleep, no bare
    /// delay and no clock poll here. A missing interpreter and a missing script are FAILURES with
    /// their reason, never skips. Shape borrowed from <c>ExecutionCensusTests.cs:620-685</c>.
    /// </summary>
    private static async Task<ToolRun> RunSelfTestAsync(string interpreter, string script)
    {
        if (!File.Exists(script))
        {
            throw new InvalidOperationException(
                $"the citation self-test is missing at {script}. It is the only fixtured guard on detect.mjs's "
                + "classification, so its absence is exactly the state this gate exists to detect, and this guard "
                + "refuses to skip when its subject is gone");
        }

        var start = new ProcessStartInfo(interpreter)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepoRoot(),
            // node writes UTF-8. Without these the redirected readers use the console code page on
            // Windows and mangle the fact titles this class quotes back in its failure messages.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(TapReporterFlag);
        start.ArgumentList.Add(script);

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"could not start `{interpreter}` to run the citation self-test. node is a hard requirement of this "
                + "tree (both tier-1 gates are node scripts) and this guard refuses to skip: a machine that cannot "
                + "run node cannot run its own gates, and allowedSkips is not a quarantine list", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await TestWait.Until(
                    process.WaitForExitAsync(),
                    $"{interpreter} {TapReporterFlag} {script} to exit",
                    window: SelfTestWindow);
            }
            catch
            {
                // A wedged node must not outlive the fact that started it. The expired window is
                // already the failure and stays one; this only stops a survivor from holding the
                // assembly open and confusing the next run.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the window expiring and the kill; nothing to clean up.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // The OS refused the kill. Reporting that instead of the timeout would bury the
                    // real failure, so the original exception wins.
                }

                throw;
            }

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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}); the citation self-test gate refuses to skip");
    }

    [GeneratedRegex(@"^(?<neg>not )?ok (?<n>\d+) - (?<rest>.*)$")]
    private static partial Regex TapResultLine();

    [GeneratedRegex(@"^1\.\.(?<n>\d+)\s*$")]
    private static partial Regex TapPlanLine();

    [GeneratedRegex(@"^# (?<key>[a-z_]+) (?<value>\d+)$")]
    private static partial Regex TapCounterLine();

    [GeneratedRegex(@"^(?<id>F\d+[a-z]?):")]
    private static partial Regex FactIdPrefix();

    [GeneratedRegex(@"\s+# (?<directive>SKIP|TODO)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex TapDirective();
}
