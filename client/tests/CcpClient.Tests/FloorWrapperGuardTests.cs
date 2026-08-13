using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-065 half-install guard (board row 49 part 2, framing f): the test-floor wrapper
/// (<c>client/tests/floor/check-floor.mjs</c>) binds only invocations routed through it,
/// and the packet-TEMPLATE change that would make future packets inherit it lives in
/// <c>.spine/patches/manifest.json</c> — not worker-writable, applied by the orchestrator
/// at land (SP-059 precedent). Holding that line is correct, so the omission must be
/// impossible to miss at the point of harm: every packet with task ID &gt;= SP-065 whose
/// <c>| testCommand |</c> contract cell invokes <c>dotnet test</c> WITHOUT routing through
/// the wrapper fails HERE with file:line. Grandfathering is by explicit ID rule only —
/// never a suppression list. Mirrors DataRootChokePointGuardTests /
/// HarnessEntryPointGuardTests: repo-root walk, never skips (a missing spine-tasks/
/// directory is a failure, not a pass), file:line violations.
/// </summary>
public partial class FloorWrapperGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SpineTasksParts = ["spine-tasks"];

    // The first packet the guard binds: this one. Older packets are grandfathered by this
    // explicit ID rule and nothing else.
    private const int FirstBoundPacketNumber = 65;

    private const string WrapperToken = "check-floor.mjs";

    [GeneratedRegex(@"^SP-(\d+)-", RegexOptions.IgnoreCase)]
    private static partial Regex PacketNumber();

    [GeneratedRegex(@"\|\s*testCommand\s*\|\s*`([^`]+)`\s*\|", RegexOptions.IgnoreCase)]
    private static partial Regex TestCommandRow();

    [GeneratedRegex(@"\bdotnet\s+test\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetTest();

    [Fact]
    public void PacketsAtOrAboveSp065_RouteDotnetTestThroughTheFloorWrapper()
    {
        var spineTasks = Path.Combine([FindRepoRoot(), .. SpineTasksParts]);
        Assert.True(Directory.Exists(spineTasks),
            $"spine-tasks not found at {spineTasks} — the floor-wrapper guard refuses to skip");

        var violations = new List<string>();
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
                // as the missing-contract-row check below.
                violations.Add($"{normalized}:1: packet directory '{segments[0]}' does not parse as SP-<number>-... — " +
                    "the floor-wrapper guard refuses to go blind on an unparseable packet ID (SP-065)");
                continue;
            }

            if (int.Parse(numberMatch.Groups[1].Value) < FirstBoundPacketNumber)
            {
                continue; // grandfathered by the explicit ID rule (never a suppression list)
            }

            var lines = File.ReadAllLines(promptPath);
            var rowFound = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var row = TestCommandRow().Match(lines[i]);
                if (!row.Success)
                {
                    continue;
                }

                rowFound = true;
                var command = row.Groups[1].Value;
                if (DotnetTest().IsMatch(command) && !command.Contains(WrapperToken, StringComparison.Ordinal))
                {
                    violations.Add($"{normalized}:{i + 1}: packet {segments[0]} invokes `dotnet test` without routing " +
                        $"through {WrapperToken} — every packet >= SP-065 must run the suite through the floor wrapper " +
                        "(the SP-065 half-install guard; an unexpected skip must fail the contract, not read green)");
                }
            }

            if (!rowFound)
            {
                // A bound packet whose contract row is missing or no longer parses blinds the
                // guard at exactly the point of harm — fail closed, not silent pass.
                violations.Add($"{normalized}:1: packet {segments[0]} has no parseable `| testCommand | `...` |` row — " +
                    "the floor-wrapper guard refuses to go blind on a packet it binds (SP-065)");
            }
        }

        Assert.True(violations.Count == 0,
            "floor-wrapper routing guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
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
