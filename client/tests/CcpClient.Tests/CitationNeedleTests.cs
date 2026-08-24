using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The floor's hold on <c>client/tools/citations/detect.mjs --needles</c> and on the
/// needle records in <c>client/docs/upstream-citation-inventory.json</c>.
///
/// <para><b>What this guards, and what it deliberately refuses to guard.</b> The detector is a
/// REVIEW LIST, not a red test (<c>detect.mjs:13-14</c>): <i>"a changed upstream file is not
/// automatically a defect, and a guard that cries wolf gets disabled."</i> So nothing here asserts
/// anything about the CONTENT of the review list. What it pins is the tool's own contract — that
/// BOTH modes exit 0 whatever the list says, and that both print the coverage gap — plus the shape
/// of the needle data, which is this repository's own and is ours to keep honest.</para>
///
/// <para><b>The one assertion about needles against real bytes is pinned at a FROZEN SHA.</b>
/// <see cref="EveryNeedle_ResolvesAtExactlyOneLine_InTheFrozenBaselineSnapshot"/> resolves each
/// needle in <c>git show &lt;baseline.merge&gt;:&lt;path&gt;</c>, never in the working tree. A
/// working-tree check would red the day upstream edits the cited file — which is the day the tool
/// is most needed, and is the cry-wolf shape the detector's header forbids by name. A snapshot at a
/// recorded commit cannot move, so this catches a garbage or later-mangled needle and can never be
/// reddened by an upstream edit. A needle that has genuinely gone stale is a
/// <c>NEEDLE-GONE</c>/<c>NEEDLE-AMBIGUOUS</c> ROW in the report, not a failure here.</para>
///
/// <para><b>And the SHA those two comparisons stand on is itself checked.</b> Every fact here that
/// reads real bytes derives them from <c>baseline.merge</c> or <c>baseline.previous.merge</c>, so an
/// endpoint that is not a state of the read-only WPF tree quietly poisons all of them —
/// <see cref="BothBaselineEndpoints_NameAnUpstreamStateOfTheReadOnlyTree"/> is why that cannot
/// happen silently. It is not a theoretical guard: the value this file carried until 2026-08-25
/// (the v6.8.0 SYNC MERGE, on the port branch) fails it, because that merge's
/// <c>ConditioningControlPanel/</c> still held the abandoned first-attempt refactor. Regenerating
/// the inventory's deltas from there contaminated 21 of 297 entries and invented a tier-1 review
/// row for a file upstream never touched.</para>
///
/// <para><b>What is NOT on the floor, stated rather than implied.</b> The needle mode's MECHANISM
/// — moved/gone/ambiguous/out-of-range classification and the coverage arithmetic — is fixtured in
/// <c>client/tools/citations/self-test.mjs</c> (F15-F24), and <b>no standing gate in this
/// repository runs that file</b> (<c>self-test.mjs:5-12</c>). Wiring it into one is a separate
/// board row with its own acceptance and was out of this packet's scope. These facts therefore prove the
/// contract and the data, not the classification.</para>
/// </summary>
public sealed class CitationNeedleTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] InventoryParts = ["client", "docs", "upstream-citation-inventory.json"];
    private static readonly string[] DetectorParts = ["client", "tools", "citations", "detect.mjs"];

    /// <summary>The read-only zone, as a repo-relative git path — the same constant and the same
    /// spelling <c>ReadOnlyWpfTreeGuardTests</c> uses.</summary>
    private const string ReadOnlyTree = "ConditioningControlPanel";

    /// <summary>The only two keys a needle record may carry. `id` names the subject for the report;
    /// `needle` is the literal. Anything else — and <c>line</c> above all — is rejected by name.</summary>
    private static readonly HashSet<string> AllowedNeedleKeys = new(StringComparer.Ordinal) { "id", "needle" };

    /// <summary>Key names that would be a stored line number wearing a different hat. A stored line
    /// rots exactly as fast as the citation it describes, which is the whole reason the schema
    /// carries a needle instead.</summary>
    private static readonly string[] ForbiddenNeedleKeys = ["line", "lines", "lineNumber", "at", "span", "range"];

    private static readonly Regex NeedleIdShape = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    /// <summary>A needle that is itself a citation: `Foo.cs:12`, `Foo.cs:12-20`, or a bare `:12`.
    /// Storing one of those as the needle is the trap this packet exists to close.</summary>
    private static readonly Regex NeedleLooksLikeACitation = new(
        @"[A-Za-z0-9_.\-]+\.(?:cs|xaml|axaml):\d+|(?<![A-Za-z0-9_./\\-]):\d+(?:-\d+)?(?!\d)",
        RegexOptions.Compiled);

    private readonly ITestOutputHelper _output;

    public CitationNeedleTests(ITestOutputHelper output) => _output = output;

    // ======================================================================================
    // The data: the needle records in the inventory.
    // ======================================================================================

    [Fact]
    public void EveryNeedleRecord_HasExactlyAnIdAndANeedle()
    {
        var needled = NeedledEntries();
        Assert.NotEmpty(needled); // never vacuous: a file with no needles would pass everything below

        var wrong = new List<string>();
        foreach (var (path, needles) in needled)
        {
            foreach (var record in needles)
            {
                if (record.ValueKind != JsonValueKind.Object)
                {
                    wrong.Add($"{path}: a needle record is {record.ValueKind}, not an object");
                    continue;
                }

                var keys = record.EnumerateObject().Select(p => p.Name).ToList();
                foreach (var extra in keys.Where(k => !AllowedNeedleKeys.Contains(k)))
                {
                    wrong.Add($"{path}: needle record carries an unexpected key \"{extra}\"");
                }

                foreach (var required in AllowedNeedleKeys.Where(k => !keys.Contains(k, StringComparer.Ordinal)))
                {
                    wrong.Add($"{path}: needle record is missing \"{required}\"");
                }
            }
        }

        Assert.True(wrong.Count == 0,
            "a needle record is not {id, needle}:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// THE ANTI-TRAP GUARD. The schema must never grow a line-number field, in any spelling, and a
    /// needle must never BE a line number. Both endpoints of the tool's comparison are derived —
    /// one from <c>git show</c>, one from the working tree — precisely so there is nothing stored
    /// that can go stale, and that property is only worth anything if it is enforced.
    /// </summary>
    [Fact]
    public void NoNeedleRecord_StoresALineNumber()
    {
        var needled = NeedledEntries();
        Assert.NotEmpty(needled);

        var wrong = new List<string>();
        foreach (var (path, needles) in needled)
        {
            foreach (var record in needles)
            {
                if (record.ValueKind != JsonValueKind.Object) continue;

                foreach (var property in record.EnumerateObject())
                {
                    if (ForbiddenNeedleKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        wrong.Add($"{path}: needle record carries \"{property.Name}\" — a stored line rots exactly "
                            + "as fast as the citation it describes; the mode re-greps instead");
                    }

                    if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        wrong.Add($"{path}: needle record's \"{property.Name}\" is a number");
                    }
                }

                if (record.TryGetProperty("needle", out var needle)
                    && needle.ValueKind == JsonValueKind.String
                    && NeedleLooksLikeACitation.IsMatch(needle.GetString() ?? string.Empty))
                {
                    wrong.Add($"{path}: the needle {JsonSerializer.Serialize(needle.GetString())} is itself a citation");
                }
            }
        }

        Assert.True(wrong.Count == 0,
            "the needle schema has grown a line number:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// The shape rules, checked against the bound the INVENTORY ITSELF states. The number lives in
    /// <c>needleContract</c> and is read out of it rather than duplicated here, so loosening the
    /// prose without the needles conforming reds, and a needle over the stated bound reds too. The
    /// document is the data and this is the logic — <c>GoonGameCensusTests</c>'s own rule, followed
    /// rather than re-invented.
    /// </summary>
    [Fact]
    public void EveryNeedle_ObeysTheShapeAndTheBoundTheInventoryItselfStates()
    {
        using var inventory = ReadInventory();
        var contract = inventory.RootElement.GetProperty("needleContract").GetString();
        Assert.False(string.IsNullOrWhiteSpace(contract), "the inventory must state its needle contract");

        var stated = Regex.Match(contract!, @"at most (?<max>\d+) characters");
        Assert.True(stated.Success, "needleContract must state the length bound as a number: \"at most N characters\"");
        var max = int.Parse(stated.Groups["max"].Value);

        var needled = NeedledEntries();
        Assert.NotEmpty(needled);

        var wrong = new List<string>();
        foreach (var (path, needles) in needled)
        {
            var ids = new List<string>();
            foreach (var record in needles)
            {
                if (record.ValueKind != JsonValueKind.Object) continue;

                var id = record.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
                ids.Add(id);
                if (!NeedleIdShape.IsMatch(id)) wrong.Add($"{path}: id \"{id}\" is not lower-case-kebab");

                var text = record.TryGetProperty("needle", out var value) ? value.GetString() : null;
                if (string.IsNullOrEmpty(text)) { wrong.Add($"{path}#{id}: the needle is empty"); continue; }
                if (text.Length > max) wrong.Add($"{path}#{id}: the needle is {text.Length} characters, over the stated {max}");
                if (text.Trim() != text) wrong.Add($"{path}#{id}: the needle has leading/trailing whitespace");
                if (text.Contains('\n') || text.Contains('\r')) wrong.Add($"{path}#{id}: the needle spans lines");

                _output.WriteLine($"{path}#{id}: {text.Length} chars");
            }

            foreach (var duplicate in ids.GroupBy(i => i, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                wrong.Add($"{path}: id \"{duplicate.Key}\" appears {duplicate.Count()} times in one entry");
            }
        }

        Assert.True(wrong.Count == 0,
            $"a needle breaks the contract the inventory states (bound: {max}):" + Environment.NewLine
            + string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// Every needle resolves at EXACTLY ONE line of the file as it stood at the inventory's own
    /// recorded baseline. Pinned at that frozen commit, never at the working tree: see the class
    /// remarks for why a working-tree check would be the cry-wolf shape the detector forbids.
    /// </summary>
    [Fact]
    public async Task EveryNeedle_ResolvesAtExactlyOneLine_InTheFrozenBaselineSnapshot()
    {
        using var inventory = ReadInventory();
        var baseline = inventory.RootElement.GetProperty("baseline").GetProperty("merge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(baseline), "the inventory must record baseline.merge");

        var needled = NeedledEntries();
        Assert.NotEmpty(needled);

        var wrong = new List<string>();
        foreach (var (path, needles) in needled)
        {
            var snapshot = await RunAsync("git", ["show", $"{baseline}:{path}"]);
            Assert.True(snapshot.ExitCode == 0,
                $"git show {baseline}:{path} exited {snapshot.ExitCode} — the recorded baseline must resolve in "
                + $"this checkout, or the inventory is describing a tree that is not here: {snapshot.StdErr}");

            var lines = snapshot.StdOut.Split('\n');
            foreach (var record in needles)
            {
                var id = record.GetProperty("id").GetString();
                var text = record.GetProperty("needle").GetString()!;
                var hits = Enumerable.Range(0, lines.Length)
                    .Where(i => lines[i].Contains(text, StringComparison.Ordinal))
                    .Select(i => i + 1)
                    .ToList();

                _output.WriteLine($"{path}#{id} at {baseline}: {(hits.Count == 0 ? "-" : string.Join(",", hits))}");
                if (hits.Count != 1)
                {
                    wrong.Add($"{path}#{id}: matches {hits.Count} lines at {baseline} "
                        + $"({(hits.Count == 0 ? "none" : string.Join(", ", hits))}); a needle must anchor exactly one");
                }
            }
        }

        Assert.True(wrong.Count == 0,
            "a needle does not anchor exactly one line in the recorded baseline snapshot — it was wrong when it "
            + "was written, or it was mangled since:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// BOTH ends of the inventory's window must name a state the READ-ONLY WPF TREE actually had.
    ///
    /// <para><b>The rule, and why a port-branch merge is not automatically one.</b> The inventory
    /// measures what UPSTREAM changed, so both endpoints have to be upstream trees. A merge commit
    /// on the port branch usually is one — its <c>ConditioningControlPanel/</c> is whatever it just
    /// merged — but it is one only by convention, and the convention was broken for real: at the
    /// v6.8.0 sync merge that tree still carried the first-attempt <c>CCP.Core/</c> extraction, so a
    /// window starting there measures the port's own later restore as though upstream had done it.
    /// Four entries were unmeasurable, seventeen mixed the restore into their add/del, and one
    /// tier-1 file regenerated to <c>M +121/-1</c> having not been touched upstream at all.</para>
    ///
    /// <para><b>The check is subtree identity against the merge base, not an ancestry test.</b>
    /// A commit ON main passes trivially (it is its own merge base). A port merge passes when its
    /// WPF subtree equals that of the main commit it merged. A port commit carrying local edits to
    /// that tree fails, which is the whole point. Every resolvable main ref is tried and ANY match
    /// passes, so a stale <c>origin/main</c> mirror cannot manufacture a red — the same
    /// authority-first candidate list <c>ReadOnlyWpfTreeGuardTests</c> uses, for the same reason.</para>
    ///
    /// <para><b>This is a red test, unlike the detector it protects.</b> The review list is
    /// deliberately never a failure (<c>detect.mjs:13-14</c>); a broken BASELINE is different in
    /// kind, because it makes every row of that list — and both needle comparisons above — describe
    /// a tree nobody shipped. There is nothing to cry wolf about: the value is written by hand, once
    /// per sync, and it is either an upstream tree or it is not.</para>
    /// </summary>
    [Fact]
    public async Task BothBaselineEndpoints_NameAnUpstreamStateOfTheReadOnlyTree()
    {
        using var inventory = ReadInventory();
        var baseline = inventory.RootElement.GetProperty("baseline");
        var endpoints = new[]
        {
            ("baseline.merge", baseline.GetProperty("merge").GetString()),
            ("baseline.previous.merge", baseline.GetProperty("previous").GetProperty("merge").GetString()),
        };

        var mainRefs = new List<string>();
        foreach (var candidate in new[] { "origin/main", "main" })
        {
            var probe = await RunAsync("git", ["rev-parse", "--verify", "--quiet", $"{candidate}^{{commit}}"]);
            if (probe.ExitCode == 0) mainRefs.Add(candidate);
        }

        Assert.True(mainRefs.Count > 0,
            "neither origin/main nor main resolves in this clone, so the inventory's endpoints cannot be checked "
            + "against the upstream tree. This guard refuses to skip. Fix with: git fetch origin main:main");

        var wrong = new List<string>();
        foreach (var (field, sha) in endpoints)
        {
            Assert.False(string.IsNullOrWhiteSpace(sha), $"the inventory must record {field}");

            var subtree = await RunAsync("git", ["rev-parse", $"{sha}:{ReadOnlyTree}"]);
            Assert.True(subtree.ExitCode == 0,
                $"{field} = {sha} does not resolve to a {ReadOnlyTree}/ tree in this checkout, so the inventory "
                + $"describes a repository that is not here: {subtree.StdErr}");
            var atEndpoint = subtree.StdOut.Trim();

            var seen = new List<string>();
            var matched = false;
            foreach (var reference in mainRefs)
            {
                var mergeBase = await RunAsync("git", ["merge-base", sha!, reference]);
                if (mergeBase.ExitCode != 0) continue;

                var baseCommit = mergeBase.StdOut.Trim();
                var baseTree = await RunAsync("git", ["rev-parse", $"{baseCommit}:{ReadOnlyTree}"]);
                if (baseTree.ExitCode != 0) continue;

                seen.Add($"{reference} (merge base {baseCommit[..9]}) -> {baseTree.StdOut.Trim()}");
                if (string.Equals(atEndpoint, baseTree.StdOut.Trim(), StringComparison.Ordinal)) matched = true;
            }

            _output.WriteLine($"{field} = {sha}: {ReadOnlyTree}/ = {atEndpoint} — {(matched ? "upstream state" : "NOT an upstream state")}");
            if (!matched)
            {
                wrong.Add($"{field} = {sha}: its {ReadOnlyTree}/ is {atEndpoint}, which no main merge base has "
                    + $"[{string.Join("; ", seen)}] — the tree at that commit carries port-local edits, so a window "
                    + "starting or ending there attributes them to upstream");
            }
        }

        Assert.True(wrong.Count == 0,
            "an inventory baseline endpoint does not name a state of the read-only WPF tree, so every delta measured "
            + "over that window mixes the port's own edits into upstream's:" + Environment.NewLine
            + string.Join(Environment.NewLine, wrong));
    }

    // ======================================================================================
    // The contract: neither mode may ever fail a build, and both must print the gap.
    // ======================================================================================

    /// <summary>
    /// The needle mode exits 0 with a NON-EMPTY review list. The endpoint is the inventory's own
    /// recorded PREVIOUS baseline, read out of the file rather than hard-coded, because at the
    /// current baseline the mode is silent by construction — and a fact watching an empty list
    /// would pass with <c>return outcome.rows.length ? 1 : 0</c> applied, i.e. it would not guard
    /// the thing it names. At the previous baseline the list has rows, so the revert reds it.
    /// </summary>
    [Fact]
    public async Task TheNeedleMode_ExitsZero_WithANonEmptyReviewList()
    {
        using var inventory = ReadInventory();
        var previous = inventory.RootElement.GetProperty("baseline").GetProperty("previous").GetProperty("merge").GetString();
        Assert.False(string.IsNullOrWhiteSpace(previous), "the inventory must record baseline.previous.merge");

        var run = await RunDetectorAsync(["--needles", "--since", previous!]);

        Assert.True(run.ExitCode == 0,
            "the needle mode exited " + run.ExitCode + " on a NON-EMPTY review list. A review list with rows in it "
            + "is the tool working, not the tool failing (detect.mjs:13-14), and a detector that can fail a build "
            + "is a detector that gets disabled: " + run.StdErr);

        var total = Regex.Match(run.StdOut, @"TOTAL ROWS: (?<n>\d+)");
        Assert.True(total.Success, "the needle report must print a row total");
        var rows = int.Parse(total.Groups["n"].Value);
        _output.WriteLine($"needle mode at {previous}: {rows} row(s), exit {run.ExitCode}");

        Assert.True(rows > 0,
            $"this fact is only a guard while the list is non-empty, and at {previous} it held {rows} rows — "
            + "with an empty list the exit-code revert would pass here unseen");
        Assert.Contains("This is a REVIEW LIST, not a failure.", run.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNeedleMode_PrintsTheCoverageGapEveryRun()
    {
        var run = await RunDetectorAsync(["--needles"]);
        Assert.True(run.ExitCode == 0, "the needle mode must exit 0: " + run.StdErr);
        AssertCoverageBlock(run.StdOut, "--needles");
    }

    /// <summary>
    /// The DEFAULT mode — the one that already had users — keeps its exit contract and gains the
    /// same coverage block, so its report can never read as complete: it answers a FILE-level
    /// question and now says so, with numbers.
    /// </summary>
    [Fact]
    public async Task TheDefaultMode_StillExitsZero_AndAlsoPrintsTheCoverageGap()
    {
        var run = await RunDetectorAsync([]);

        Assert.True(run.ExitCode == 0,
            "the default mode exited " + run.ExitCode + "; it emits a long review list on this tree and every row "
            + "of it is a REVIEW row, never a failure: " + run.StdErr);

        var total = Regex.Match(run.StdOut, @"TOTAL ROWS: (?<n>\d+)");
        Assert.True(total.Success, "the default report must print a row total");
        _output.WriteLine($"default mode: {total.Groups["n"].Value} row(s), exit {run.ExitCode}");
        Assert.True(int.Parse(total.Groups["n"].Value) > 0,
            "the default list is non-empty on this tree, which is what makes the exit-code revert visible here");

        AssertCoverageBlock(run.StdOut, "the default mode");
    }

    // ======================================================================================
    // Machinery.
    // ======================================================================================

    private void AssertCoverageBlock(string stdout, string mode)
    {
        Assert.Contains("## NEEDLE COVERAGE", stdout, StringComparison.Ordinal);

        // Numbers, not prose. An unstated coverage gap is how a review list becomes a false
        // reassurance, so each half of the split must be PRINTED and must be a figure.
        var entries = Regex.Match(stdout, @"inventory entries: (?<total>\d+) — (?<needled>\d+) carry needle\(s\), (?<bare>\d+) do not");
        Assert.True(entries.Success, $"{mode} must print how many entries carry a needle and how many do not");

        var bare = Regex.Match(stdout, @"bare :NNN continuations: (?<n>\d+) in the port");
        Assert.True(bare.Success, $"{mode} must print the bare-continuation count — the largest gap, stated as a number");
        Assert.Contains("NOT CHECKED", stdout, StringComparison.Ordinal);

        var citations = Regex.Match(stdout, @"(?<into>\d+) into a needled path — (?<confirmed>\d+) confirmed");
        Assert.True(citations.Success, $"{mode} must print how many citations a needle covers");

        _output.WriteLine($"{mode}: {entries.Groups["needled"].Value}/{entries.Groups["total"].Value} entries needled, "
            + $"{citations.Groups["into"].Value} citations into them ({citations.Groups["confirmed"].Value} confirmed), "
            + $"{bare.Groups["n"].Value} bare continuations unchecked");
    }

    private sealed record ToolRun(int ExitCode, string StdOut, string StdErr);

    private static Task<ToolRun> RunDetectorAsync(string[] args) =>
        RunAsync("node", [Path.Combine([FindRepoRoot(), .. DetectorParts]), .. args]);

    /// <summary>
    /// Runs a tool to completion inside the repository. A missing <c>node</c> or <c>git</c> is a
    /// hard FAILURE and never a skip: both tier-1 gates are node scripts and this tree is a git
    /// checkout, so a machine that cannot run them cannot run its own gates, and
    /// <c>allowedSkips</c> is not a quarantine list. The wait is <see cref="TestWait.Until"/> —
    /// there is no <c>Thread.Sleep</c>, no bare <c>Task.Delay</c> and no clock poll here.
    /// </summary>
    private static async Task<ToolRun> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepoRoot(),
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"could not start `{executable}` — it is a hard requirement of this tree (the tier-1 gates are node "
                + "scripts and the citation detector reads git history), and this guard refuses to skip", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await TestWait.Until(process.WaitForExitAsync(), $"{executable} {string.Join(' ', arguments)} to exit");
            }
            catch
            {
                // A wedged child must not outlive the fact that started it: the expired window is
                // already the failure and stays one, but a surviving process would hold the
                // assembly open and confuse the next run.
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
                    // The OS refused the kill; reporting that instead of the timeout would bury
                    // the real failure, so the original exception wins.
                }

                throw;
            }

            return new ToolRun(process.ExitCode, await stdout, await stderr);
        }
    }

    private static JsonDocument ReadInventory()
    {
        var path = Path.Combine([FindRepoRoot(), .. InventoryParts]);
        Assert.True(File.Exists(path), $"{string.Join('/', InventoryParts)} is missing; this guard never skips");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Every entry carrying needles, as (path, records). The JsonDocument is cloned out so
    /// callers are not tied to its lifetime.</summary>
    private static List<(string Path, List<JsonElement> Needles)> NeedledEntries()
    {
        using var inventory = ReadInventory();
        var needled = new List<(string, List<JsonElement>)>();

        foreach (var entry in inventory.RootElement.GetProperty("entries").EnumerateArray())
        {
            if (!entry.TryGetProperty("needles", out var needles) || needles.ValueKind != JsonValueKind.Array) continue;
            needled.Add((entry.GetProperty("path").GetString()!, needles.EnumerateArray().Select(n => n.Clone()).ToList()));
        }

        return needled;
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the citation-needle guard refuses to skip");
    }
}
