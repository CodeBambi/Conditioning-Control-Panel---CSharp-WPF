using System.Text.Json;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-066 (board row 49 part 1): the vacuous-SHAPE guard. Runs the committed detector
/// surface (<see cref="VacuousShapeDetector"/>) over both test projects and pins every
/// site against the committed ledger (<c>client/tests/floor/vacuous-shape-ledger.json</c>).
/// A NEW unclassified silencing shape fails HERE with file:line; a stale ledger entry
/// (renamed/deleted test, changed shape set) fails too — the ledger edit is the review
/// friction that keeps the class non-recurring (four recurrences in the timing lineage
/// is the evidence; TestTimingGuardTests / FloorWrapperGuardTests are the precedent:
/// repo-root walk, never skips, file:line violations).
///
/// HONESTY (framing b): this is a SHAPE guard. It is lexical, and lexical detection of
/// vacuity is provably incomplete. It CANNOT see: assertions hoisted into called helpers
/// (reads them as absent — a false positive the ledger dispositions by naming the helper),
/// and loops over possibly-empty collections (reads them as asserting — a false negative;
/// the framing-(c) Assert.NotEmpty pins are the mitigation, not this guard). It binds only
/// the shapes it enumerates: a NEW way to silence an assertion is invisible until someone
/// adds it to the detector surface.
/// </summary>
public class VacuousShapeGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] LedgerParts = ["client", "tests", "floor", "vacuous-shape-ledger.json"];

    private sealed record LedgerEntry(
        string Key, string Path, int Line, string Method,
        string[] Shapes, bool ExpectDetected, string Verdict, string Reason);

    [Fact]
    public void EverySilencingShapeSite_IsDispositionedInTheLedger()
    {
        var ledgerPath = Path.Combine([FindRepoRoot(), .. LedgerParts]);
        Assert.True(File.Exists(ledgerPath),
            $"vacuous-shape ledger not found at {ledgerPath} — the guard refuses to skip (SP-066)");

        List<LedgerEntry> entries;
        using (var doc = JsonDocument.Parse(File.ReadAllText(ledgerPath)))
        {
            entries = doc.RootElement.GetProperty("entries").Deserialize<List<LedgerEntry>>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }

        Assert.NotEmpty(entries); // an empty ledger on this suite is a broken ledger, not a clean sweep

        var violations = new List<string>();
        var byKey = new Dictionary<string, LedgerEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!byKey.TryAdd(entry.Key, entry))
            {
                violations.Add($"vacuous-shape-ledger.json: duplicate key {entry.Key} — name-anchored coverage requires unique keys");
            }
        }

        var detected = VacuousShapeDetector.Scan();
        var detectedByKey = detected.ToDictionary(s => s.Key, StringComparer.Ordinal);

        // (1) Every detected site must be ledgered with the exact shape set.
        foreach (var site in detected)
        {
            if (!byKey.TryGetValue(site.Key, out var entry))
            {
                violations.Add($"{site.Path}:{site.Line}: {site.Key} carries silencing shape(s) " +
                    $"[{string.Join(", ", site.Shapes)}] with NO ledger entry — disposition it in " +
                    "client/tests/floor/vacuous-shape-ledger.json (SP-066: a silenced assertion must never pass silently again)");
                continue;
            }

            if (!entry.ExpectDetected)
            {
                violations.Add($"{site.Path}:{site.Line}: {site.Key} is ledgered expectDetected=false ({entry.Verdict}) " +
                    $"but the detector sees it with shape(s) [{string.Join(", ", site.Shapes)}] — a mitigated or deleted " +
                    "site reappeared; either restore the mitigation or re-disposition the entry (SP-066 resurrection guard)");
                continue;
            }

            if (!entry.Shapes.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(site.Shapes.OrderBy(s => s, StringComparer.Ordinal)))
            {
                violations.Add($"{site.Path}:{site.Line}: {site.Key} shape drift — ledger has " +
                    $"[{string.Join(", ", entry.Shapes)}], detector sees [{string.Join(", ", site.Shapes)}] — " +
                    "a shape change forces a ledger edit (that edit is the review gate)");
            }
        }

        // (2) Every live ledger entry must still be detected (stale entries cannot rot).
        foreach (var entry in entries)
        {
            if (entry.ExpectDetected && !detectedByKey.ContainsKey(entry.Key))
            {
                violations.Add($"vacuous-shape-ledger.json: stale entry {entry.Key} ({entry.Verdict}) is no longer detected — " +
                    "the test was renamed, deleted, or its shapes resolved; update or re-disposition the entry (SP-066)");
            }
        }

        Assert.True(violations.Count == 0,
            "vacuous-shape guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the vacuous-shape guard refuses to skip");
    }
}
