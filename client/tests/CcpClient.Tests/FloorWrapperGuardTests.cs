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

    private const string SharedFloorPin = "client/tests/floor/floor.json";

    [GeneratedRegex(@"^SP-(\d+)-", RegexOptions.IgnoreCase)]
    private static partial Regex PacketNumber();

    [GeneratedRegex(@"\|\s*testCommand\s*\|\s*`([^`]+)`\s*\|", RegexOptions.IgnoreCase)]
    private static partial Regex TestCommandRow();

    [GeneratedRegex(@"\bdotnet\s+test\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetTest();

    [GeneratedRegex(@"\|\s*floorDelta\s*\|\s*`([^`]+)`\s*\|", RegexOptions.IgnoreCase)]
    private static partial Regex FloorDeltaRow();

    [GeneratedRegex(@"\|\s*fileScopeMustNotChange\s*\|([^|]*)\|", RegexOptions.IgnoreCase)]
    private static partial Regex FileScopeMustNotChangeRow();

    /// <summary>
    /// One packet as the guards see it. Enumerating packets is shared rather than copied:
    /// two divergent walks would let a packet be bound by one guard and invisible to the
    /// other, which is the failure mode both guards exist to prevent.
    /// </summary>
    private sealed record PacketFile(string NormalizedPath, string PacketDir, int Number, string[] Lines);

    /// <param name="spineTasks">
    /// The packet root, already existence-checked BY THE CALLING TEST. That check deliberately
    /// stays in each test body rather than moving in here: "this guard refuses to skip" is a
    /// property of each test, and the vacuous-shape detector is lexical, so a never-skip
    /// checkpoint hidden inside a shared helper stops being visible to the ledger that exists
    /// to track exactly these shapes.
    /// </param>
    private static List<PacketFile> EnumeratePackets(string spineTasks, List<string> violations)
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
                int.Parse(numberMatch.Groups[1].Value),
                File.ReadAllLines(promptPath)));
        }

        return packets;
    }

    [Fact]
    public void PacketsAtOrAboveSp065_RouteDotnetTestThroughTheFloorWrapper()
    {
        var spineTasks = Path.Combine([FindRepoRoot(), .. SpineTasksParts]);
        Assert.True(Directory.Exists(spineTasks),
            $"spine-tasks not found at {spineTasks} — the floor-wrapper guard refuses to skip");

        var violations = new List<string>();
        foreach (var packet in EnumeratePackets(spineTasks, violations))
        {
            if (packet.Number < FirstBoundPacketNumber)
            {
                continue; // grandfathered by the explicit ID rule (never a suppression list)
            }

            var rowFound = false;
            for (var i = 0; i < packet.Lines.Length; i++)
            {
                var row = TestCommandRow().Match(packet.Lines[i]);
                if (!row.Success)
                {
                    continue;
                }

                rowFound = true;
                var command = row.Groups[1].Value;
                if (DotnetTest().IsMatch(command) && !command.Contains(WrapperToken, StringComparison.Ordinal))
                {
                    violations.Add($"{packet.NormalizedPath}:{i + 1}: packet {packet.PacketDir} invokes `dotnet test` without routing " +
                        $"through {WrapperToken} — every packet >= SP-065 must run the suite through the floor wrapper " +
                        "(the SP-065 half-install guard; an unexpected skip must fail the contract, not read green)");
                }
            }

            if (!rowFound)
            {
                // A bound packet whose contract row is missing or no longer parses blinds the
                // guard at exactly the point of harm — fail closed, not silent pass.
                violations.Add($"{packet.NormalizedPath}:1: packet {packet.PacketDir} has no parseable `| testCommand | `...` |` row — " +
                    "the floor-wrapper guard refuses to go blind on a packet it binds (SP-065)");
            }
        }

        Assert.True(violations.Count == 0,
            "floor-wrapper routing guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin()
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

        var violations = new List<string>();
        foreach (var packet in EnumeratePackets(spineTasks, violations))
        {
            if (packet.Number < FirstDeltaBoundPacketNumber)
            {
                continue; // grandfathered by the explicit ID rule (never a suppression list)
            }

            var expectedDelta = $"spine-tasks/{packet.PacketDir}/floor-delta.json";
            var deltaRowFound = false;
            for (var i = 0; i < packet.Lines.Length; i++)
            {
                var row = FloorDeltaRow().Match(packet.Lines[i]);
                if (!row.Success)
                {
                    continue;
                }

                deltaRowFound = true;
                var declared = row.Groups[1].Value.Trim().Replace('\\', '/');
                if (!string.Equals(declared, expectedDelta, StringComparison.OrdinalIgnoreCase))
                {
                    // Pointing at another packet's delta file is a copy-paste away, and it would
                    // make two packets claim one delta — the land would sum one of them twice and
                    // lose the other, with a green suite either side of the mistake.
                    violations.Add($"{packet.NormalizedPath}:{i + 1}: packet {packet.PacketDir} declares floorDelta " +
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

            var scopeRowFound = false;
            for (var i = 0; i < packet.Lines.Length; i++)
            {
                var row = FileScopeMustNotChangeRow().Match(packet.Lines[i]);
                if (!row.Success)
                {
                    continue;
                }

                scopeRowFound = true;
                if (!row.Groups[1].Value.Replace('\\', '/').Contains(SharedFloorPin, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{packet.NormalizedPath}:{i + 1}: packet {packet.PacketDir} does not list " +
                        $"`{SharedFloorPin}` in fileScopeMustNotChange — a lane that edits the shared pin collides with " +
                        "every other lane in the wave, and resolving that at merge time by trusting the observed count " +
                        "is the vacuous-green class (SP-065/SP-066)");
                }
            }

            if (!scopeRowFound)
            {
                violations.Add($"{packet.NormalizedPath}:1: packet {packet.PacketDir} has no parseable " +
                    "`| fileScopeMustNotChange | ... |` row — the chokepoint guard refuses to go blind on a packet it binds");
            }
        }

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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the floor-wrapper guard refuses to skip");
    }
}
