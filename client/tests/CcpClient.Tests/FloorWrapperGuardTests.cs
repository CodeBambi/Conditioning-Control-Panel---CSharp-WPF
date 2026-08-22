using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-065 half-install guard (board row 49 part 2, framing f): the test-floor wrapper
/// (<c>client/tests/floor/check-floor.mjs</c>) binds only invocations routed through it.
/// Holding that line is correct, so the omission must be impossible to miss at the point
/// of harm: every packet with task ID &gt;= SP-065 whose <c>| testCommand |</c> contract
/// cell invokes <c>dotnet test</c> WITHOUT routing through the wrapper fails HERE with
/// file:line. Grandfathering is by explicit ID rule only — never a suppression list.
/// Mirrors DataRootChokePointGuardTests / HarnessEntryPointGuardTests: repo-root walk,
/// never skips (a missing spine-tasks/ directory is a failure, not a pass), file:line
/// violations.
///
/// <para>Engine note (2026-08-14): pi-spine is retired and the packet TEMPLATE it applied
/// from <c>.spine/patches/manifest.json</c> no longer exists. Under Claude Code the packet
/// is authored directly by the orchestrator, which REMOVES the template as a place to
/// encode a rule — so the rule has to live here, in a guard, or nowhere.</para>
///
/// <para>Multi-lane chokepoint guard (2026-08-14): with lanes running concurrently,
/// <c>client/tests/floor/floor.json</c> is the file every test-adding packet would edit,
/// so at eight lanes it collides every wave. Resolving that at merge time by setting
/// <c>total</c> to the OBSERVED count would recreate exactly the vacuous-green class the
/// pin exists to prevent. The mechanism is therefore inverted: a lane never touches the
/// shared pin, it declares its delta in its own packet folder, and the land sums the
/// deltas in one commit naming every contributing packet. That is only real if it is
/// mechanically enforced, which is what <see cref="PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin"/>
/// does.</para>
///
/// <para><b>SP-136 — THIS GUARD NO LONGER PARSES A PACKET CONTRACT, AND THAT IS THE POINT.</b>
/// It used to hold its own copy of the contract-row regexes and its own chokepoint predicate,
/// with <c>client/tools/wave/validate-wave.mjs</c> holding a second copy and a header comment
/// between them saying "do not let these two drift". They drifted, on the shared floor pin: the
/// validator asked glob-aware coverage and this file asked <c>String.Contains</c> against a
/// literal, so <c>client/tests/floor/**</c> printed <c>WAVE OK</c> at the pre-launch gate and
/// reddened the suite. Both wave-60 packets were authored that way and the base was red from the
/// authoring commit onward. The rows, the wrapper-routing verdict and the chokepoint coverage
/// verdict are now READ from that script's <c>--emit-packet-scopes</c> projection. There is no
/// second implementation here to drift from.</para>
///
/// <para><b>WHAT THIS GUARD STILL OWNS, deliberately.</b> Its own packet enumeration — so it can
/// refuse to go blind, and so the two enumerations can be cross-checked against each other rather
/// than assumed equal; its own two grandfather IDs, which are genuinely NOT shared (the validator
/// binds its chokepoint check with no packet-number condition at all, and 60 of the 128 packets on
/// disk violate it there while being correctly invisible here); and its own violation text. The
/// grandfather re-application is pinned by
/// <c>WaveGuardConvergenceTests.PacketsBelowSp073_StayGrandfathered_EvenWhenTheirScopeCoversNothing</c>,
/// because dropping it would newly red sixty packets and look like a corpus problem.</para>
///
/// <para><b>node is a hard requirement here and never a skip</b>, the same disposition
/// <c>CitationNeedleTests</c> takes: the tier-1 gates are node scripts, so a machine that cannot
/// run node cannot run its own floor.</para>
/// </summary>
public partial class FloorWrapperGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SpineTasksParts = ["spine-tasks"];

    // The first packet the guard binds: this one. Older packets are grandfathered by this
    // explicit ID rule and nothing else.
    private const int FirstBoundPacketNumber = 65;

    // The first packet authored under the multi-lane floor-delta mechanism. Same explicit
    // ID rule, same reason: grandfathering by rule is auditable, a suppression list is not.
    private const int FirstDeltaBoundPacketNumber = 73;

    private const string WrapperToken = "check-floor.mjs";

    // SP-136: this is no longer a PREDICATE. Nothing here asks whether a scope cell contains this
    // string — that question is answered once, by validate-wave.mjs, glob-aware. The constant
    // survives as a CROSS-CHECK: if the projection ever protects a different path from the one
    // this guard believes it binds, the two are guarding different files and that is reported.
    private const string SharedFloorPin = "client/tests/floor/floor.json";

    [GeneratedRegex(@"^SP-(\d+)-", RegexOptions.IgnoreCase)]
    private static partial Regex PacketNumber();

    [GeneratedRegex(@"\bdotnet\s+test\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetTest();

    /// <summary>
    /// One packet as the guards see it. Enumerating packets is shared rather than copied:
    /// two divergent walks would let a packet be bound by one guard and invisible to the
    /// other, which is the failure mode both guards exist to prevent.
    /// </summary>
    internal sealed record PacketFile(string NormalizedPath, string PacketDir, int Number);

    /// <param name="spineTasks">
    /// The packet root, already existence-checked BY THE CALLING TEST. That check deliberately
    /// stays in each test body rather than moving in here: "this guard refuses to skip" is a
    /// property of each test, and the vacuous-shape detector is lexical, so a never-skip
    /// checkpoint hidden inside a shared helper stops being visible to the ledger that exists
    /// to track exactly these shapes.
    /// </param>
    internal static List<PacketFile> EnumeratePackets(string spineTasks, List<string> violations)
    {
        var packets = new List<PacketFile>();
        foreach (var promptPath in Directory.EnumerateFiles(spineTasks, "PROMPT.md", SearchOption.AllDirectories))
        {
            var normalized = promptPath.Replace('\\', '/');
            // Only packet-root PROMPT.md files: spine-tasks/<packet>/PROMPT.md exactly.
            var relative = normalized[(normalized.IndexOf("spine-tasks/", StringComparison.Ordinal) + "spine-tasks/".Length)..];
            var segments = relative.Split('/');
            if (segments.Length != 2)
            {
                continue; // e.g. evidence or review artifacts nested under a packet
            }

            var numberMatch = PacketNumber().Match(segments[0]);
            if (!numberMatch.Success)
            {
                // A directory holding a PROMPT.md whose name does not parse as SP-<n>-... would
                // silently escape the ID rule — fail closed, same refusal-to-go-blind principle
                // as the missing-contract-row checks.
                violations.Add($"{normalized}:1: packet directory '{segments[0]}' does not parse as SP-<number>-... — " +
                    "the packet guards refuse to go blind on an unparseable packet ID (SP-065)");
                continue;
            }

            packets.Add(new PacketFile(
                normalized,
                segments[0],
                int.Parse(numberMatch.Groups[1].Value)));
        }

        return packets;
    }

    /// <summary>
    /// Index the projection by packet directory and report ANY asymmetry with this guard's own
    /// enumeration, in both directions. This is the other half of what used to be a comment: a
    /// packet bound by one walk and invisible to the other is the drift class, so the two walks
    /// check each other instead of being assumed equal.
    /// </summary>
    internal static Dictionary<string, WaveScopePacket> IndexAndCrossCheckEnumeration(
        List<PacketFile> packets, WaveScopeProjection projection, List<string> violations)
    {
        var byDir = new Dictionary<string, WaveScopePacket>(StringComparer.OrdinalIgnoreCase);
        foreach (var emitted in projection.Packets)
        {
            byDir[emitted.Dir] = emitted;
        }

        var mine = new HashSet<string>(packets.Select(p => p.PacketDir), StringComparer.OrdinalIgnoreCase);
        foreach (var packet in packets)
        {
            if (!byDir.ContainsKey(packet.PacketDir))
            {
                violations.Add($"{packet.NormalizedPath}:1: packet {packet.PacketDir} is enumerated by this guard but is " +
                    "ABSENT from `validate-wave.mjs --emit-packet-scopes` — the two packet walks have diverged, and this " +
                    "guard refuses to judge a packet on a verdict nobody produced (SP-136)");
            }
        }

        foreach (var emitted in projection.Packets)
        {
            if (!mine.Contains(emitted.Dir))
            {
                violations.Add($"{emitted.PromptPath}:1: packet {emitted.Dir} is projected by validate-wave.mjs but is " +
                    "INVISIBLE to this guard's own enumeration — a packet bound by one walk and not the other is exactly " +
                    "the drift SP-136 closed (SP-065 refusal to go blind)");
            }
        }

        return byDir;
    }

    /// <summary>
    /// The SP-065 rule, applied to the projection's verdicts. Callable with an arbitrary
    /// projection so the convergence facts can drive it with a mutated one.
    /// </summary>
    internal static List<string> ComputeWrapperViolations(string spineTasks, WaveScopeProjection projection)
    {
        var violations = new List<string>();
        var packets = EnumeratePackets(spineTasks, violations);
        var byDir = IndexAndCrossCheckEnumeration(packets, projection, violations);

        if (projection.FirstBoundPacketNumber != FirstBoundPacketNumber)
        {
            violations.Add($"client/tools/wave/validate-wave.mjs:1: the projection binds the wrapper rule from " +
                $"SP-{projection.FirstBoundPacketNumber:D3} but this guard binds it from SP-{FirstBoundPacketNumber:D3} — " +
                "the two would grandfather different packets, which is the SP-136 drift class");
        }

        if (!string.Equals(projection.WrapperToken, WrapperToken, StringComparison.Ordinal))
        {
            violations.Add($"client/tools/wave/validate-wave.mjs:1: the projection routes through " +
                $"'{projection.WrapperToken}' but this guard binds '{WrapperToken}' — the two would accept different " +
                "test commands");
        }

        foreach (var packet in packets)
        {
            if (packet.Number < FirstBoundPacketNumber)
            {
                continue; // grandfathered by the explicit ID rule (never a suppression list)
            }

            if (!byDir.TryGetValue(packet.PacketDir, out var emitted))
            {
                continue; // already reported by the enumeration cross-check
            }

            foreach (var row in emitted.TestCommandRows)
            {
                if (row.InvokesDotnetTest && !row.RoutesThroughWrapper)
                {
                    violations.Add($"{packet.NormalizedPath}:{row.Line}: packet {packet.PacketDir} invokes `dotnet test` without routing " +
                        $"through {WrapperToken} — every packet >= SP-065 must run the suite through the floor wrapper " +
                        "(the SP-065 half-install guard; an unexpected skip must fail the contract, not read green)");
                }
            }

            if (emitted.TestCommandRows.Length == 0)
            {
                // A bound packet whose contract row is missing or no longer parses blinds the
                // guard at exactly the point of harm — fail closed, not silent pass.
                violations.Add($"{packet.NormalizedPath}:1: packet {packet.PacketDir} has no parseable `| testCommand | `...` |` row — " +
                    "the floor-wrapper guard refuses to go blind on a packet it binds (SP-065)");
            }
        }

        return violations;
    }

    /// <summary>
    /// The SP-073 rule, applied to the projection's verdicts. The chokepoint half is now
    /// entirely the projection's answer: this method never asks whether a cell contains a string.
    /// Callable with an arbitrary projection so the convergence facts can drive it with a mutated
    /// one and watch this guard's verdict change.
    /// </summary>
    internal static List<string> ComputeChokepointViolations(string spineTasks, WaveScopeProjection projection)
    {
        var violations = new List<string>();
        var packets = EnumeratePackets(spineTasks, violations);
        var byDir = IndexAndCrossCheckEnumeration(packets, projection, violations);

        if (!string.Equals(projection.FloorPinPath, SharedFloorPin, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"client/tools/wave/validate-wave.mjs:1: the projection protects '{projection.FloorPinPath}' " +
                $"but this guard binds '{SharedFloorPin}' — the two would be guarding different files, which is the " +
                "SP-136 drift in its most direct form");
        }

        foreach (var packet in packets)
        {
            if (packet.Number < FirstDeltaBoundPacketNumber)
            {
                continue; // grandfathered by the explicit ID rule (never a suppression list)
            }

            if (!byDir.TryGetValue(packet.PacketDir, out var emitted))
            {
                continue; // already reported by the enumeration cross-check
            }

            var expectedDelta = $"spine-tasks/{packet.PacketDir}/floor-delta.json";
            var deltaRowFound = false;
            foreach (var row in emitted.FloorDeltaRows)
            {
                if (row.Values.Length != 1)
                {
                    // Reproduces exactly what the old single-value regex here did with such a
                    // cell: it did not parse, so the row does not count as found and the
                    // missing-row violation below fires. Measured across all 128 packets on
                    // disk: no packet carries a floorDelta cell with anything but one value.
                    continue;
                }

                deltaRowFound = true;
                var declared = row.Values[0].Trim().Replace('\\', '/');
                if (!string.Equals(declared, expectedDelta, StringComparison.OrdinalIgnoreCase))
                {
                    // Pointing at another packet's delta file is a copy-paste away, and it would
                    // make two packets claim one delta — the land would sum one of them twice and
                    // lose the other, with a green suite either side of the mistake.
                    violations.Add($"{packet.NormalizedPath}:{row.Line}: packet {packet.PacketDir} declares floorDelta " +
                        $"`{declared}` but its own delta file is `{expectedDelta}` — a packet may only ever declare " +
                        "its own delta, or the land sums the wrong packet's count");
                }
            }

            if (!deltaRowFound)
            {
                violations.Add($"{packet.NormalizedPath}:1: packet {packet.PacketDir} has no parseable " +
                    $"`| floorDelta | `spine-tasks/<packet>/floor-delta.json` |` row — every packet >= SP-{FirstDeltaBoundPacketNumber:D3} " +
                    "declares its floor delta in its own folder so concurrent lanes never edit the shared pin " +
                    "(declare 0/0 when the packet adds no tests; omitting the row is not the same as declaring zero)");
            }

            if (!emitted.FloorPin.RowFound)
            {
                violations.Add($"{packet.NormalizedPath}:1: packet {packet.PacketDir} has no parseable " +
                    "`| fileScopeMustNotChange | ... |` row — the chokepoint guard refuses to go blind on a packet it binds");
            }
            else if (!emitted.FloorPin.Covered)
            {
                violations.Add($"{packet.NormalizedPath}:{emitted.FloorPin.RowLine}: packet {packet.PacketDir} does not " +
                    $"cover `{SharedFloorPin}` in fileScopeMustNotChange — a lane that edits the shared pin collides with " +
                    "every other lane in the wave, and resolving that at merge time by trusting the observed count " +
                    "is the vacuous-green class (SP-065/SP-066). A covering glob such as `client/tests/floor/**` " +
                    "satisfies this; the literal path is not required. Declared: " +
                    (emitted.FloorPin.Declared.Length > 0 ? string.Join(", ", emitted.FloorPin.Declared) : "(nothing)"));
            }
        }

        return violations;
    }

    [Fact]
    public async Task PacketsAtOrAboveSp065_RouteDotnetTestThroughTheFloorWrapper()
    {
        var spineTasks = Path.Combine([FindRepoRoot(), .. SpineTasksParts]);
        Assert.True(Directory.Exists(spineTasks),
            $"spine-tasks not found at {spineTasks} — the floor-wrapper guard refuses to skip");

        var violations = ComputeWrapperViolations(spineTasks, await WaveScopeOracle.DefaultAsync());

        Assert.True(violations.Count == 0,
            "floor-wrapper routing guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin()
    {
        // The multi-lane chokepoint. Every packet that adds a test moves the floor count, and
        // under the single-lane engine each one edited client/tests/floor/floor.json directly.
        // Concurrent lanes cannot do that: they collide on one line of one file every wave, and
        // the tempting resolution (set `total` to whatever the merged tree observes) is the
        // vacuous-green failure the pin was built to catch. So a bound packet must (1) declare
        // its delta in ITS OWN folder and (2) explicitly disclaim the shared pin. Both halves
        // are required: a delta file nobody forbade overwriting the pin would not prevent the
        // collision, and a disclaimer with no delta would lose the count entirely.
        var spineTasks = Path.Combine([FindRepoRoot(), .. SpineTasksParts]);
        Assert.True(Directory.Exists(spineTasks),
            $"spine-tasks not found at {spineTasks} — the floor-delta chokepoint guard refuses to skip");

        var violations = ComputeChokepointViolations(spineTasks, await WaveScopeOracle.DefaultAsync());

        Assert.True(violations.Count == 0,
            "floor-delta chokepoint guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AuditorPrompt_InvokesTheFloorWrapper_NeverBareDotnetTest()
    {
        // T-17 (SP-066 framing i): the blind auditor is the one check meant to catch a lying
        // land; a bare `dotnet test` in its prompt would retain the exact detection path this
        // wrapper replaced (an unexpected skip reads green there). The prompt must invoke the
        // wrapper, carry the CCP_DATA_ROOT warning, and contain NO bare dotnet test.
        var promptPath = Path.Combine(FindRepoRoot(), "client", "tools", "port-audit-prompt.md");
        Assert.True(File.Exists(promptPath),
            $"port-audit-prompt.md not found at {promptPath} — the auditor pin refuses to skip");
        var prompt = File.ReadAllText(promptPath);
        Assert.Contains("node client/tests/floor/check-floor.mjs", prompt);
        Assert.Contains("CCP_DATA_ROOT", prompt); // the wrapper must never be given one (:204)
        Assert.False(DotnetTest().IsMatch(prompt),
            "port-audit-prompt.md contains a bare `dotnet test` invocation — the auditor must run the suite "
            + "through node client/tests/floor/check-floor.mjs (T-17); a bare invocation keeps the vacuous-green "
            + "detection path SP-065 replaced");
    }

    internal static string FindRepoRoot()
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the floor-wrapper guard refuses to skip");
    }
}

// ---------------------------------------------------------------------------------------------
// The projection, and the reader that fails closed on it
// ---------------------------------------------------------------------------------------------

/// <summary>One fixture case, with the verdict the shared decision pins and the one it produced.</summary>
internal sealed record WaveScopeCaseVerdict(bool RowFound, bool Covered);

internal sealed record WaveScopeCase(
    string Id, string Why, string Chokepoint,
    WaveScopeCaseVerdict Expect, WaveScopeCaseVerdict Actual, string[] Declared);

internal sealed record WaveScopeTestCommandRow(int Line, string Command, bool InvokesDotnetTest, bool RoutesThroughWrapper);

internal sealed record WaveScopeRow(int Line, string[] Values);

internal sealed record WaveScopeChokepoint(bool RowFound, int RowLine, string[] Declared, bool Covered);

internal sealed record WaveScopePacket(
    string Dir, int Number, string PromptPath,
    WaveScopeTestCommandRow[] TestCommandRows,
    WaveScopeRow[] FloorDeltaRows,
    WaveScopeRow[] MustNotChangeRows,
    WaveScopeChokepoint FloorPin,
    WaveScopeChokepoint TaskBoard,
    string[] Violations);

/// <summary>One wrapper-routing fixture case: the SP-065 rule's verdict for one testCommand.</summary>
internal sealed record WaveScopeWrapperVerdict(bool InvokesDotnetTest, bool RoutesThroughWrapper);

internal sealed record WaveScopeWrapperCase(
    string Id, string Why, string Command,
    WaveScopeWrapperVerdict Expect, WaveScopeWrapperVerdict Actual);

internal sealed record WaveScopeProjection(
    string Schema, string SpineTasksDir, int WaveSize,
    string FloorPinPath, string TaskBoardPath, string WrapperToken, int FirstBoundPacketNumber,
    string[] StrayPromptDirs, WaveScopeCase[] Cases, WaveScopeWrapperCase[] WrapperCases,
    WaveScopePacket[] Packets);

/// <summary>
/// SP-136. Runs <c>client/tools/wave/validate-wave.mjs --emit-packet-scopes &lt;dir&gt;</c> and
/// returns its projection. This is the ONLY route by which a packet contract reaches the C# side,
/// which is what makes the wave validator and the floor guard one implementation rather than two
/// that agree today.
///
/// <para>FAILS CLOSED, never skips, and the distinction matters: an oracle that returned an empty
/// projection on a missing tool would make every guard consuming it vacuously green. A non-zero
/// exit, an unstartable <c>node</c>, empty stdout, or unparseable JSON all THROW. The exit-code
/// contract is the script's own (<c>validate-wave.mjs</c> header): 0 means a projection was
/// produced, even for a corpus riddled with violations, because violations travel as data for the
/// consumer to bind with its own rule; 2 means no projection exists.</para>
/// </summary>
internal static class WaveScopeOracle
{
    private static readonly string[] ValidatorParts = ["client", "tools", "wave", "validate-wave.mjs"];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // One spawn per test assembly for the default projection: both facts above and several
    // convergence facts consume it.
    private static readonly Lazy<Task<WaveScopeProjection>> DefaultProjection =
        new(() => LoadAsync(ValidatorPath(), Path.Combine(FloorWrapperGuardTests.FindRepoRoot(), "spine-tasks")));

    internal static string ValidatorPath() => Path.Combine([FloorWrapperGuardTests.FindRepoRoot(), .. ValidatorParts]);

    internal static Task<WaveScopeProjection> DefaultAsync() => DefaultProjection.Value;

    internal static async Task<WaveScopeProjection> LoadAsync(string validatorScriptPath, string spineTasksDir)
    {
        var run = await RunAsync(validatorScriptPath, "--emit-packet-scopes", spineTasksDir);

        if (run.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`node {validatorScriptPath} --emit-packet-scopes {spineTasksDir}` exited {run.ExitCode} — the packet-scope "
                + "projection is the only route by which a packet contract reaches this guard, so a failed projection is a "
                + "hard failure and never an empty corpus (SP-136). stderr:" + Environment.NewLine + run.StdErr);
        }

        if (string.IsNullOrWhiteSpace(run.StdOut))
        {
            throw new InvalidOperationException(
                $"`node {validatorScriptPath} --emit-packet-scopes {spineTasksDir}` exited 0 but wrote nothing to stdout — "
                + "an empty projection would make every guard consuming it vacuously green, so it fails here instead (SP-136)");
        }

        WaveScopeProjection? projection;
        try
        {
            projection = JsonSerializer.Deserialize<WaveScopeProjection>(run.StdOut, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"the packet-scope projection from {validatorScriptPath} is not parseable JSON — a projection this guard "
                + "cannot read is a projection it must not judge packets on (SP-136)", ex);
        }

        if (projection is null || projection.Packets is null || projection.Cases is null || projection.WrapperCases is null)
        {
            throw new InvalidOperationException(
                $"the packet-scope projection from {validatorScriptPath} parsed to null or is missing its "
                + "`packets`/`cases`/`wrapperCases` arrays — the schema this guard consumes is "
                + "`ccp.wave.packet-scopes.v1` (SP-136)");
        }

        if (!string.Equals(projection.Schema, "ccp.wave.packet-scopes.v1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"the packet-scope projection declares schema '{projection.Schema}', not 'ccp.wave.packet-scopes.v1' — "
                + "this guard refuses to read a payload shape it was not written against (SP-136)");
        }

        return projection;
    }

    internal sealed record ToolRun(int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// Runs the validator to completion. A missing <c>node</c> is a hard FAILURE and never a skip:
    /// the tier-1 gates are node scripts, so a machine that cannot run them cannot run its own
    /// floor. The wait is <see cref="TestWait.Until"/> — no <c>Thread.Sleep</c>, no bare
    /// <c>Task.Delay</c>, no clock poll.
    /// </summary>
    internal static async Task<ToolRun> RunAsync(string validatorScriptPath, params string[] arguments)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FloorWrapperGuardTests.FindRepoRoot(),
        };
        start.ArgumentList.Add(validatorScriptPath);
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
                "could not start `node` — it is a hard requirement of this tree (the tier-1 gates are node scripts and "
                + "the packet contract projection is one of them), and this guard refuses to skip", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await TestWait.Until(process.WaitForExitAsync(), $"node {validatorScriptPath} {string.Join(' ', arguments)} to exit");
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

                throw;
            }

            return new ToolRun(process.ExitCode, await stdout, await stderr);
        }
    }
}
