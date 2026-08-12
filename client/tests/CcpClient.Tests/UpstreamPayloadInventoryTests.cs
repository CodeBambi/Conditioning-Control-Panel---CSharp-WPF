using System.Text.Json;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-056 — the guard the v6.6.3 → v6.7.4 sync exposed as missing
/// (client/docs/upstream-sync.md §D): an entire 184-file upstream payload tree
/// (Resources/web/goon/) appeared and the suite stayed green because every parity
/// test only covers trees the client already ships. This guard compares the REAL
/// top-level trees under ConditioningControlPanel/Resources/web/ against the
/// committed inventory (client/docs/upstream-payload-inventory.json):
/// an unlisted tree FAILS (naming the tree, its file count, and the required
/// action); a listed tree that no longer exists upstream FAILS (stale entry).
///
/// Non-vacuous design (pre-approach consult, 2026-08-11):
///  * the repo root is anchored on client/CcpClient.sln (exists in every checkout
///    AND in worktrees); root-not-found and missing-inventory are hard FAILURES,
///    never skips;
///  * "unreachable reference tree" means exactly one thing: the repo has no
///    ConditioningControlPanel/ directory at all (a client-only/sparse context).
///    A half-present reference (ConditioningControlPanel/ without Resources/web)
///    is a corrupt checkout and FAILS;
///  * the unreachable branch still asserts the inventory parses, is non-empty,
///    is fully well-formed, and holds at least one 'served' AND one 'not-ported'
///    entry — a gutted inventory cannot pass either branch;
///  * the branch taken is written to the test output, so a permanently-skipping
///    guard is visible in the TRX/output transcript.
///
/// The inventory is the DATA, this file is the LOGIC — no upstream tree name is
/// hard-coded here. All guard machinery lives in the test assembly because the
/// task's file scope forbids client/src changes; the fixture tests below pin the
/// parser/comparer/branch behavior against temp-dir repos, not today's tree list.
/// </summary>
public sealed class UpstreamPayloadInventoryTests
{
    private const string InventoryRelativePath = "client/docs/upstream-payload-inventory.json";

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] InventoryParts = ["client", "docs", "upstream-payload-inventory.json"];
    private static readonly string[] WpfRootParts = ["ConditioningControlPanel"];
    private static readonly string[] WebTreeParts = ["ConditioningControlPanel", "Resources", "web"];

    private readonly ITestOutputHelper _output;

    public UpstreamPayloadInventoryTests(ITestOutputHelper output) => _output = output;

    // ------------------------------------------------------------------
    // The guard itself, against the real repository this test runs from.
    // ------------------------------------------------------------------

    [Fact]
    public void RealRepo_InventoryCoversEveryUpstreamPayloadTree()
    {
        var root = FindRepoRoot(); // throws (fails) if unresolvable — never a skip
        RunGuard(root, line => _output.WriteLine(line));
    }

    // ------------------------------------------------------------------
    // Guard machinery (pinned by the fixture tests below).
    // ------------------------------------------------------------------

    private enum GuardBranch
    {
        Reachable,
        Unreachable,
    }

    private sealed record GuardOutcome(GuardBranch Branch, int InventoryTreeCount, int ActualTreeCount);

    private enum ViolationKind
    {
        UnknownTree,
        StaleEntry,
    }

    private sealed record Violation(ViolationKind Kind, string Tree, int FileCount, string Message);

    private sealed record InventoryEntry(
        string Name,
        string Disposition,
        int FileCountAtBaseline,
        string? Evidence,
        string? BoardRow);

    private sealed record Inventory(
        int SchemaVersion,
        string UpstreamVersion,
        string Merge,
        IReadOnlyList<InventoryEntry> Trees);

    private sealed class InventoryFormatException(string message) : Exception(message);

    /// <summary>Walks up from the test assembly's base directory to the directory
    /// containing client/CcpClient.sln. The anchor exists in every checkout and in
    /// worktrees (whereas .git is a FILE in worktrees). Failure to resolve throws —
    /// a guard that cannot find its repo must fail, not skip.</summary>
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
            $"repo root not found walking up from {AppContext.BaseDirectory} " +
            $"(anchor: {string.Join('/', RepoAnchorParts)}) — the upstream-tree guard refuses to skip");
    }

    /// <summary>The guard body, parameterized on the repo root so fixture tests can
    /// pin every branch against a temp-dir repo. Asserts (fails) on any violation;
    /// returns the branch taken so callers can observe it.</summary>
    private static GuardOutcome RunGuard(string repoRoot, Action<string> log)
    {
        var inventoryPath = Path.Combine([repoRoot, .. InventoryParts]);
        Assert.True(
            File.Exists(inventoryPath),
            $"inventory missing at {inventoryPath} — {InventoryRelativePath} is the guard's data file and must be committed; " +
            "its absence is a failure, not a skip");

        var inventory = ParseInventory(File.ReadAllText(inventoryPath));
        AssertInventoryWellFormed(inventory);

        var wpfRoot = Path.Combine([repoRoot, .. WpfRootParts]);
        if (!Directory.Exists(wpfRoot))
        {
            // The ONLY legal non-compare branch: no WPF reference tree at all
            // (client-only / sparse context). Well-formedness above already ran;
            // the branch is observable in the output so a permanently-skipping
            // guard is visible.
            log($"upstream reference tree UNREACHABLE under {repoRoot} (no {WpfRootParts[0]}/ directory) — " +
                $"well-formedness-only branch; inventory holds {inventory.Trees.Count} trees " +
                $"({inventory.Trees.Count(t => t.Disposition == "served")} served, " +
                $"{inventory.Trees.Count(t => t.Disposition == "not-ported")} not-ported)");
            return new GuardOutcome(GuardBranch.Unreachable, inventory.Trees.Count, 0);
        }

        var webRoot = Path.Combine([repoRoot, .. WebTreeParts]);
        Assert.True(
            Directory.Exists(webRoot),
            $"{WpfRootParts[0]}/ exists under {repoRoot} but Resources/web is missing — " +
            "a corrupt or partial reference checkout; refusing to skip the guard");

        var actual = EnumerateTrees(webRoot);
        log($"upstream reference tree reachable at {webRoot} — full-compare branch " +
            $"({actual.Count} trees on disk: {string.Join(", ", actual.Keys.Order(StringComparer.OrdinalIgnoreCase))})");
        Assert.NotEmpty(actual); // an empty web/ tree is a broken checkout, not a pass

        var violations = Compare(inventory, actual);
        Assert.True(
            violations.Count == 0,
            "upstream payload-tree guard violations:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => "  - " + v.Message)));
        return new GuardOutcome(GuardBranch.Reachable, inventory.Trees.Count, actual.Count);
    }

    /// <summary>Well-formedness invariants that hold on BOTH branches, so neither
    /// branch can pass vacuously: non-empty inventory, both dispositions present
    /// (a gutted or single-sided inventory fails even when the reference tree is
    /// unreachable), unique names, positive counts, valid baseline shape.</summary>
    private static void AssertInventoryWellFormed(Inventory inventory)
    {
        Assert.NotEmpty(inventory.Trees);
        Assert.Contains(inventory.Trees, t => t.Disposition == "served");
        Assert.Contains(inventory.Trees, t => t.Disposition == "not-ported");
        Assert.Matches(@"^v\d+(\.\d+)+$", inventory.UpstreamVersion);
        Assert.False(string.IsNullOrWhiteSpace(inventory.Merge));
    }

    /// <summary>Enumerates the REAL top-level payload trees: direct directories
    /// under the given web root, each mapped to its recursive file count.
    /// Case-insensitive keys (Windows/macOS file systems), actual on-disk names
    /// preserved for messages.</summary>
    private static IReadOnlyDictionary<string, int> EnumerateTrees(string webRoot)
    {
        var trees = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(webRoot))
        {
            trees[Path.GetFileName(dir)] = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();
        }

        return trees;
    }

    /// <summary>The pure comparer: inventory vs actual trees → typed violations.
    /// Unknown trees name the tree, its file count, and the required action; stale
    /// entries name the entry and the required action. Known-tree count drift is
    /// NOT a violation (per-tree manifest tests own count pinning).</summary>
    private static IReadOnlyList<Violation> Compare(
        Inventory inventory,
        IReadOnlyDictionary<string, int> actualTrees)
    {
        var violations = new List<Violation>();
        var known = inventory.Trees.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, count) in actualTrees.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!known.Contains(name))
            {
                violations.Add(new Violation(
                    ViolationKind.UnknownTree,
                    name,
                    count,
                    $"upstream payload tree '{name}' ({count} files) exists under " +
                    $"{string.Join('/', WebTreeParts)} but is not listed in {InventoryRelativePath} — " +
                    "a new upstream product surface must not slip past the port silently " +
                    "(the v6.6.3 → v6.7.4 sync added web/goon/, 184 files, with the suite green). " +
                    "Action: file a row in client/docs/task-board.md, add the tree to the inventory " +
                    "(disposition 'served' naming the serving code path, or 'not-ported' with the " +
                    "board-row reference), and cite it in client/docs/upstream-sync.md."));
            }
        }

        foreach (var entry in inventory.Trees.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!actualTrees.ContainsKey(entry.Name))
            {
                violations.Add(new Violation(
                    ViolationKind.StaleEntry,
                    entry.Name,
                    0,
                    $"inventory entry '{entry.Name}' has no corresponding tree under " +
                    $"{string.Join('/', WebTreeParts)} — the tree was removed or renamed upstream and the entry is stale. " +
                    $"Action: remove or correct the entry in {InventoryRelativePath} and cite the change in client/docs/upstream-sync.md."));
            }
        }

        return violations;
    }

    /// <summary>Strict parser: any structural deviation is an InventoryFormatException
    /// (test failure with a readable message), never a tolerated default.</summary>
    private static Inventory ParseInventory(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InventoryFormatException($"inventory is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InventoryFormatException("inventory root must be a JSON object");
            }

            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
            {
                throw new InventoryFormatException("inventory schemaVersion must be 1");
            }

            if (!root.TryGetProperty("baseline", out var baseline) || baseline.ValueKind != JsonValueKind.Object)
            {
                throw new InventoryFormatException("inventory baseline object is required");
            }

            var version = RequiredString(baseline, "upstreamVersion", "baseline");
            var merge = RequiredString(baseline, "merge", "baseline");

            if (!root.TryGetProperty("trees", out var trees) || trees.ValueKind != JsonValueKind.Array)
            {
                throw new InventoryFormatException("inventory trees array is required");
            }

            var entries = new List<InventoryEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in trees.EnumerateArray())
            {
                var name = RequiredString(node, "name", "tree entry");
                var disposition = RequiredString(node, "disposition", $"tree '{name}'");
                if (disposition is not ("served" or "not-ported"))
                {
                    throw new InventoryFormatException(
                        $"tree '{name}' has unknown disposition '{disposition}' (expected 'served' or 'not-ported')");
                }

                if (!node.TryGetProperty("fileCountAtBaseline", out var countNode) || countNode.GetInt32() <= 0)
                {
                    throw new InventoryFormatException($"tree '{name}' must declare a positive fileCountAtBaseline");
                }

                var evidence = OptionalString(node, "evidence");
                var boardRow = OptionalString(node, "boardRow");
                if (disposition == "served" && string.IsNullOrWhiteSpace(evidence))
                {
                    throw new InventoryFormatException(
                        $"tree '{name}' is 'served' but names no serving code path (evidence) — dispositions are honest, not aspirational");
                }

                if (disposition == "not-ported" && string.IsNullOrWhiteSpace(boardRow))
                {
                    throw new InventoryFormatException(
                        $"tree '{name}' is 'not-ported' but names no owning board row (boardRow) — an unowned tree is a failure, not a warning");
                }

                if (!names.Add(name))
                {
                    throw new InventoryFormatException($"duplicate tree entry '{name}'");
                }

                entries.Add(new InventoryEntry(name, disposition, countNode.GetInt32(), evidence, boardRow));
            }

            return new Inventory(schema.GetInt32(), version, merge, entries);
        }
    }

    private static string RequiredString(JsonElement node, string property, string context)
    {
        if (!node.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InventoryFormatException($"{context} requires a non-empty string '{property}'");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ------------------------------------------------------------------
    // Fixture tests — the guard's own logic pinned against temp-dir repos.
    // ------------------------------------------------------------------

    [Fact]
    public void Fixture_UnknownUpstreamTree_Fails_NamingTreeCountAndAction()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 1,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "boardRow": "r" } ] }
                """,
            webTrees: new Dictionary<string, int> { ["alpha"] = 1, ["beta"] = 1, ["mystery"] = 3 },
            act: (root, _) =>
            {
                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex);
                Assert.Contains("'mystery' (3 files)", ex.Message);
                Assert.Contains("task-board.md", ex.Message); // the action a future sync must take
                Assert.Contains("upstream-sync.md", ex.Message);
            });
    }

    [Fact]
    public void Fixture_StaleInventoryEntry_Fails_NamingEntry()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 1,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "evidence": "x" },
                    { "name": "ghost", "disposition": "not-ported", "fileCountAtBaseline": 5, "boardRow": "r" } ] }
                """,
            webTrees: new Dictionary<string, int> { ["alpha"] = 1 },
            act: (root, _) =>
            {
                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex);
                Assert.Contains("'ghost'", ex.Message);
                Assert.Contains("stale", ex.Message);
            });
    }

    [Fact]
    public void Fixture_MatchingSet_Passes_ReachableBranchObservable()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 1,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "evidence": "x" },
                    { "name": "Beta", "disposition": "not-ported", "fileCountAtBaseline": 2, "boardRow": "r" } ] }
                """,
            // On-disk casing differs from the inventory — matching is case-insensitive,
            // the actual on-disk name is what messages report.
            webTrees: new Dictionary<string, int> { ["alpha"] = 1, ["BETA"] = 2 },
            act: (root, log) =>
            {
                var outcome = RunGuard(root, log.Add);
                Assert.Equal(GuardBranch.Reachable, outcome.Branch);
                Assert.Equal(2, outcome.ActualTreeCount);
                Assert.Contains(log, line => line.Contains("full-compare branch", StringComparison.Ordinal));
            });
    }

    [Fact]
    public void Fixture_MissingWpfTree_UnreachableBranch_StillAssertsWellFormedness()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 1,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "boardRow": "r" } ] }
                """,
            webTrees: null, // no ConditioningControlPanel/ at all — the ONLY legal skip-compare shape
            act: (root, log) =>
            {
                var outcome = RunGuard(root, log.Add);
                Assert.Equal(GuardBranch.Unreachable, outcome.Branch);
                Assert.Contains(log, line => line.Contains("UNREACHABLE", StringComparison.Ordinal));
            });
    }

    [Fact]
    public void Fixture_UnreachableBranch_GuttedInventory_Fails()
    {
        WithFixtureRepo(
            // Single-sided inventory (no 'served' entry) — must fail even with no
            // reference tree to compare against: both branches cannot pass vacuously.
            inventory: """
                { "schemaVersion": 1,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "boardRow": "r" } ] }
                """,
            webTrees: null,
            act: (root, _) =>
            {
                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex);
            });
    }

    [Fact]
    public void Fixture_HalfPresentWpfTree_Fails_RefusesToSkip()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 1,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "boardRow": "r" } ] }
                """,
            webTrees: new Dictionary<string, int>(), // ConditioningControlPanel/ exists, Resources/web does NOT
            act: (root, _) =>
            {
                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex);
                Assert.Contains("Resources/web is missing", ex.Message);
            });
    }

    [Fact]
    public void Fixture_MissingInventory_Fails_NeverASkip()
    {
        WithFixtureRepo(
            inventory: null, // repo root + web trees present, the data file deleted
            webTrees: new Dictionary<string, int> { ["alpha"] = 1 },
            act: (root, _) =>
            {
                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex);
                Assert.Contains("inventory missing", ex.Message);
            });
    }

    [Fact]
    public void Fixture_ReachableBranch_EmptyWebTree_Fails_NotAPass()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 1,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "boardRow": "r" } ] }
                """,
            webTrees: new Dictionary<string, int> { ["__empty__"] = 0 }, // creates web/ with zero real trees
            act: (root, _) =>
            {
                // __empty__ creates the directory then we remove it, leaving web/ empty
                var web = Path.Combine(root, "ConditioningControlPanel", "Resources", "web");
                Directory.Delete(Path.Combine(web, "__empty__"));
                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex); // Assert.NotEmpty(actual) — an empty web/ is a broken checkout
            });
    }

    [Theory]
    [InlineData("not json at all", "not valid JSON")]
    [InlineData("""{ "schemaVersion": 2, "baseline": { "upstreamVersion": "v1.0.0", "merge": "x" }, "trees": [] }""", "schemaVersion must be 1")]
    [InlineData("""{ "schemaVersion": 1, "trees": [] }""", "baseline object is required")]
    [InlineData("""{ "schemaVersion": 1, "baseline": { "upstreamVersion": "v1.0.0", "merge": "x" } }""", "trees array is required")]
    [InlineData("""{ "schemaVersion": 1, "baseline": { "upstreamVersion": "v1.0.0", "merge": "x" }, "trees": [ { "name": "a", "disposition": "shipped", "fileCountAtBaseline": 1 } ] }""", "unknown disposition")]
    [InlineData("""{ "schemaVersion": 1, "baseline": { "upstreamVersion": "v1.0.0", "merge": "x" }, "trees": [ { "name": "a", "disposition": "served", "fileCountAtBaseline": 1 } ] }""", "names no serving code path")]
    [InlineData("""{ "schemaVersion": 1, "baseline": { "upstreamVersion": "v1.0.0", "merge": "x" }, "trees": [ { "name": "a", "disposition": "not-ported", "fileCountAtBaseline": 1 } ] }""", "names no owning board row")]
    [InlineData("""{ "schemaVersion": 1, "baseline": { "upstreamVersion": "v1.0.0", "merge": "x" }, "trees": [ { "name": "a", "disposition": "served", "fileCountAtBaseline": 0, "evidence": "x" } ] }""", "positive fileCountAtBaseline")]
    [InlineData("""{ "schemaVersion": 1, "baseline": { "upstreamVersion": "v1.0.0", "merge": "x" }, "trees": [ { "name": "a", "disposition": "served", "fileCountAtBaseline": 1, "evidence": "x" }, { "name": "A", "disposition": "not-ported", "fileCountAtBaseline": 1, "boardRow": "r" } ] }""", "duplicate tree entry")]
    public void Parser_MalformedInventory_ThrowsReadable(string json, string expectedMessagePart)
    {
        var ex = Assert.Throws<InventoryFormatException>(() => ParseInventory(json));
        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Fact]
    public void Parser_ValidInventory_RoundTripsAllFields()
    {
        var inventory = ParseInventory("""
            { "schemaVersion": 1,
              "baseline": { "upstreamVersion": "v6.7.4", "merge": "42286638" },
              "trees": [
                { "name": "dtrh", "disposition": "served", "fileCountAtBaseline": 1542, "evidence": "glob" },
                { "name": "goon", "disposition": "not-ported", "fileCountAtBaseline": 184, "boardRow": "row", "note": "n" } ] }
            """);
        Assert.Equal(1, inventory.SchemaVersion);
        Assert.Equal("v6.7.4", inventory.UpstreamVersion);
        Assert.Equal("42286638", inventory.Merge);
        Assert.Equal(2, inventory.Trees.Count);
        Assert.Equal("goon", inventory.Trees[1].Name);
        Assert.Equal("row", inventory.Trees[1].BoardRow);
    }

    /// <summary>Builds a temp-dir repo: the sln anchor, an optional inventory file,
    /// and optional web trees (name → file count). null inventory = file absent;
    /// null webTrees = no ConditioningControlPanel/ at all; empty dict = the dir
    /// exists but Resources/web does not. Cleans up after the callback.</summary>
    private static void WithFixtureRepo(
        string? inventory,
        Dictionary<string, int>? webTrees,
        Action<string, List<string>> act)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-sp056-" + Guid.NewGuid().ToString("N"));
        var log = new List<string>();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "client"));
            File.WriteAllText(Path.Combine([root, .. RepoAnchorParts]), "<!-- anchor -->");

            if (inventory is not null)
            {
                var path = Path.Combine([root, .. InventoryParts]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, inventory);
            }

            if (webTrees is not null)
            {
                Directory.CreateDirectory(Path.Combine([root, .. WpfRootParts]));
                if (webTrees.Count > 0)
                {
                    var web = Path.Combine([root, .. WebTreeParts]);
                    foreach (var (name, count) in webTrees)
                    {
                        var dir = Path.Combine(web, name);
                        Directory.CreateDirectory(dir);
                        for (var i = 0; i < count; i++)
                        {
                            File.WriteAllText(Path.Combine(dir, $"f{i}.js"), "//");
                        }
                    }
                }
            }

            act(root, log);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
