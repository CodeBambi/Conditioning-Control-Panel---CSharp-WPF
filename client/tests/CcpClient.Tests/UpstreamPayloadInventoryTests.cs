using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The guard the v6.6.3 → v6.7.4 sync exposed as missing
/// (client/docs/upstream-sync.md §D): an entire 184-file upstream payload tree
/// (Resources/web/goon/) appeared and the suite stayed green because every parity
/// test only covers trees the client already ships. This guard compares the REAL
/// top-level trees under ConditioningControlPanel/Resources/web/ against the
/// committed inventory (client/docs/upstream-payload-inventory.json):
/// an unlisted tree FAILS (naming the tree, its file count, and the required
/// action); a listed tree that no longer exists upstream FAILS (stale entry);
/// a listed tree whose SHAPE changed FAILS (drift, naming the tree).
///
/// <para><b>The drift pin (schemaVersion 2), and the hole it closes.</b> The
/// v6.8.3 → v6.8.4 sync moved <c>Resources/web/arcademy/</c> from 88 to 91 files
/// and this guard stayed green at 19/19, because <c>fileCountAtBaseline</c> was
/// recorded as "record data, not an assertion" and the per-tree manifest tests
/// that DO pin counts exist only for trees the client actually serves. So the
/// guard saw a tree ARRIVE and a tree DISAPPEAR but never a tree CHANGE — which
/// is the permanent state of every not-ported tree until its row lands, and was
/// the fourth time a payload guard was the only thing between this port and a
/// silently-arrived upstream surface. Every entry now carries
/// <c>fileCountAtBaseline</c> + <c>listSha256</c> (SHA-256 over the tree's
/// ordinal-sorted, '/'-normalized relative file paths, one per line, UTF-8) and
/// BOTH are asserted, for served and not-ported trees alike — one rule, no
/// exception to keep honest.</para>
///
/// <para><b>Why the FILE LIST and not the file CONTENT.</b> A count alone misses
/// a rename and an equal add/remove swap; a content hash would also catch an EDIT
/// inside an unchanged path, and that is a real hole this pin does not close. It
/// is left open deliberately, for two measured reasons. First, this repository
/// checks out with <c>core.autocrlf=true</c>, so the on-disk BYTES of every text
/// file differ between a Windows and a Linux checkout: a content hash would be
/// red on one of the port's two acceptance targets for a reason no syncer could
/// fix, and a guard that cries wolf gets holes drilled in it. Second, the trees
/// total ~530 MB and the pin runs on every floor run. The upgrade path, if an
/// edit-inside-a-file ever needs catching, is git's own normalized blob ids
/// (<c>git ls-files -s</c>), which cost no file reads and are platform-neutral —
/// at the price of a subprocess dependency inside a unit test.</para>
///
/// <para><b>A bump must carry its reason, mechanically.</b> Each entry also
/// carries <c>pinnedAt</c> (the upstream version whose tree the pin was measured
/// against) and <c>pinReason</c> (the prose). The parser REFUSES a
/// <c>pinReason</c> that does not contain the literal phrase
/// "<c>{fileCountAtBaseline} files</c>" and the literal <c>pinnedAt</c> version,
/// so a syncer cannot bump 91 to 94 and leave the sentence untouched: the number
/// lives in two places, one of them prose. What it still cannot catch is a
/// rewritten phrase attached to a stale explanation — prose is prose. What it
/// does force is that the explanation be TOUCHED, which is precisely what a
/// silent bump does not do.</para>
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
public sealed partial class UpstreamPayloadInventoryTests
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

    private sealed record GuardOutcome(
        GuardBranch Branch,
        int InventoryTreeCount,
        int ActualTreeCount,
        int VerifiedShapeCount);

    private enum ViolationKind
    {
        UnknownTree,
        StaleEntry,
        Drift,
    }

    private sealed record Violation(ViolationKind Kind, string Tree, int FileCount, string Message);

    /// <summary>The measured shape of one real tree: how many files, and the digest of
    /// their sorted relative paths.</summary>
    private sealed record TreeShape(int FileCount, string ListSha256);

    private sealed record InventoryEntry(
        string Name,
        string Disposition,
        int FileCountAtBaseline,
        string ListSha256,
        string PinnedAt,
        string PinReason,
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
            return new GuardOutcome(GuardBranch.Unreachable, inventory.Trees.Count, 0, 0);
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

        // Every inventory entry was matched to a real tree AND its pinned shape held.
        // Reported so the caller can refuse a run that compared nothing.
        var verified = inventory.Trees.Count(t => actual.ContainsKey(t.Name));
        return new GuardOutcome(GuardBranch.Reachable, inventory.Trees.Count, actual.Count, verified);
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
    /// under the given web root, each mapped to its measured shape (recursive file
    /// count + file-list digest). Case-insensitive keys (Windows/macOS file
    /// systems), actual on-disk names preserved for messages.</summary>
    private static IReadOnlyDictionary<string, TreeShape> EnumerateTrees(string webRoot)
    {
        var trees = new Dictionary<string, TreeShape>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(webRoot))
        {
            var relatives = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                .ToList();
            trees[Path.GetFileName(dir)] = new TreeShape(relatives.Count, ListDigest(relatives));
        }

        return trees;
    }

    /// <summary>SHA-256, lowercase hex, over the ordinal-sorted relative paths joined
    /// by '\n' in UTF-8. Separator normalization ('\' → '/') happens at the call site
    /// and ordinal ordering is used rather than a culture one, so a Windows checkout
    /// and a Linux checkout of the same commit produce the SAME digest — the property
    /// that lets this be a committed pin rather than a machine-local one.</summary>
    internal static string ListDigest(IEnumerable<string> relativePaths)
    {
        var builder = new StringBuilder();
        foreach (var relative in relativePaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            builder.Append(relative).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    /// <summary>The pure comparer: inventory vs actual trees → typed violations.
    /// Unknown trees name the tree, its file count, and the required action; stale
    /// entries name the entry and the required action; a known tree whose measured
    /// SHAPE differs from its pin is drift and names the tree, the delta, and the
    /// exact JSON edit that resolves it.</summary>
    private static IReadOnlyList<Violation> Compare(
        Inventory inventory,
        IReadOnlyDictionary<string, TreeShape> actualTrees)
    {
        var violations = new List<Violation>();
        var known = inventory.Trees.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, shape) in actualTrees.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var count = shape.FileCount;
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
            if (!actualTrees.TryGetValue(entry.Name, out var actual))
            {
                violations.Add(new Violation(
                    ViolationKind.StaleEntry,
                    entry.Name,
                    0,
                    $"inventory entry '{entry.Name}' has no corresponding tree under " +
                    $"{string.Join('/', WebTreeParts)} — the tree was removed or renamed upstream and the entry is stale. " +
                    $"Action: remove or correct the entry in {InventoryRelativePath} and cite the change in client/docs/upstream-sync.md."));
                continue;
            }

            if (actual.FileCount == entry.FileCountAtBaseline &&
                string.Equals(actual.ListSha256, entry.ListSha256, StringComparison.Ordinal))
            {
                continue;
            }

            var what = actual.FileCount != entry.FileCountAtBaseline
                ? $"pinned {entry.FileCountAtBaseline} files at {entry.PinnedAt}, found {actual.FileCount} " +
                  $"({actual.FileCount - entry.FileCountAtBaseline:+#;-#;0})"
                : $"{actual.FileCount} files as pinned at {entry.PinnedAt}, but the file LIST changed — " +
                  "a rename, or an equal number of files added and removed";

            violations.Add(new Violation(
                ViolationKind.Drift,
                entry.Name,
                actual.FileCount,
                $"upstream payload tree '{entry.Name}' DRIFTED: {what}. " +
                "A tree that CHANGES is how the next upstream surface arrives, and it is the state every " +
                "not-ported tree is permanently in: web/arcademy/ went 88 → 91 files at the v6.8.3 → v6.8.4 " +
                "sync with this whole suite green, because fileCountAtBaseline was record data rather than an " +
                "assertion. It is an assertion now, and this drift is NOT closed by editing the number alone. " +
                $"Action: (1) run `git diff --stat <previous sync merge>..HEAD -- {string.Join('/', WebTreeParts)}/{entry.Name}` " +
                "to see WHAT changed and decide what it obliges the port to do; " +
                $"(2) set, in {InventoryRelativePath} entry '{entry.Name}': " +
                $"\"fileCountAtBaseline\": {actual.FileCount}, \"listSha256\": \"{actual.ListSha256}\", " +
                "\"pinnedAt\": \"<the upstream version that changed it>\"; " +
                "(3) REWRITE \"pinReason\" to say WHY it changed — the parser rejects a reason that does not " +
                $"contain the literal phrase \"{CountPhrase(actual.FileCount)}\" and the literal pinnedAt version, " +
                "so the number cannot be bumped while the sentence explaining it stays untouched; " +
                "(4) cite the change in client/docs/upstream-sync.md and open or renew the tree's row in " +
                "client/docs/task-board.md."));
        }

        return violations;
    }

    /// <summary>The phrase a pinReason must contain for its own file count. The count
    /// therefore lives in two places — a number and a sentence — and only a syncer who
    /// touches the sentence can move the number.</summary>
    private static string CountPhrase(int fileCount) => $"{fileCount} files";

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

            // Bumped 1 → 2 when listSha256/pinnedAt/pinReason became required: an entry
            // written against the old schema carries no pin and must fail loudly rather
            // than default to "unpinned".
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 2)
            {
                throw new InventoryFormatException("inventory schemaVersion must be 2");
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

                var fileCount = countNode.GetInt32();
                var listSha256 = RequiredString(node, "listSha256", $"tree '{name}'");
                if (!ListDigestPattern().IsMatch(listSha256))
                {
                    throw new InventoryFormatException(
                        $"tree '{name}' listSha256 must be 64 lowercase hex characters (got '{listSha256}')");
                }

                var pinnedAt = RequiredString(node, "pinnedAt", $"tree '{name}'");
                if (!VersionPattern().IsMatch(pinnedAt))
                {
                    throw new InventoryFormatException(
                        $"tree '{name}' pinnedAt must name the upstream version the pin was measured against " +
                        $"(e.g. 'v6.8.4'), got '{pinnedAt}'");
                }

                // THE REASON IS LOAD-BEARING, not decoration. The count lives in the data
                // AND in the prose, and the version does too, so bumping either without
                // rewriting the sentence that explains it is a parse failure. A syncer can
                // still attach a rewritten phrase to a stale explanation — prose is prose —
                // but cannot leave the explanation untouched, which is what a SILENT bump is.
                var pinReason = RequiredString(node, "pinReason", $"tree '{name}'");
                if (!pinReason.Contains(CountPhrase(fileCount), StringComparison.Ordinal))
                {
                    throw new InventoryFormatException(
                        $"tree '{name}' pinReason must state its own file count as the literal phrase " +
                        $"'{CountPhrase(fileCount)}' — a count bumped without its reason rewritten is the silent " +
                        "sync this pin exists to stop");
                }

                if (!pinReason.Contains(pinnedAt, StringComparison.Ordinal))
                {
                    throw new InventoryFormatException(
                        $"tree '{name}' pinReason must name its own pinnedAt version '{pinnedAt}' — " +
                        "the version and the sentence explaining it move together or neither is trustworthy");
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

                entries.Add(new InventoryEntry(
                    name, disposition, fileCount, listSha256, pinnedAt, pinReason, evidence, boardRow));
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

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex ListDigestPattern();

    [GeneratedRegex(@"^v\d+(\.\d+)+$")]
    private static partial Regex VersionPattern();

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
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "listSha256": "%%beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "boardRow": "r" } ] }
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
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "evidence": "x" },
                    { "name": "ghost", "disposition": "not-ported", "fileCountAtBaseline": 5, "listSha256": "%%ghost%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 5 files", "boardRow": "r" } ] }
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
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "evidence": "x" },
                    { "name": "Beta", "disposition": "not-ported", "fileCountAtBaseline": 2, "listSha256": "%%Beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 2 files", "boardRow": "r" } ] }
                """,
            // On-disk casing differs from the inventory — matching is case-insensitive,
            // the actual on-disk name is what messages report.
            webTrees: new Dictionary<string, int> { ["alpha"] = 1, ["BETA"] = 2 },
            act: (root, log) =>
            {
                var outcome = RunGuard(root, log.Add);
                Assert.Equal(GuardBranch.Reachable, outcome.Branch);
                Assert.Equal(2, outcome.ActualTreeCount);
                // THE PASS DIRECTION, non-vacuously: both pinned shapes were actually
                // compared and held. A guard that always fires is as broken as one that
                // never does, and a "0 compared, 0 violations" pass is the same as no guard.
                Assert.Equal(2, outcome.VerifiedShapeCount);
                Assert.Contains(log, line => line.Contains("full-compare branch", StringComparison.Ordinal));
            });
    }

    /// <summary>THE HOLE THIS ROW CLOSED. A listed tree that GROWS is drift, and drift is
    /// a failure that names the tree and the delta — before this pin, the same shape was a
    /// green suite (web/arcademy/ 88 → 91 at v6.8.4).</summary>
    [Fact]
    public void Fixture_ListedTreeGrew_Fails_NamingTreeDeltaAndTheExactJsonEdit()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 3, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 3 files", "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "listSha256": "%%beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "boardRow": "r" } ] }
                """,
            webTrees: new Dictionary<string, int> { ["alpha"] = 3, ["beta"] = 1 },
            act: (root, _) =>
            {
                // The sync: one file arrives in a tree the inventory already knows about.
                File.WriteAllText(
                    Path.Combine([root, .. WebTreeParts, "beta", "arrived.js"]), "// new upstream surface");

                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex);
                Assert.Contains("'beta' DRIFTED", ex.Message);
                Assert.Contains("pinned 1 files at v9.9.9, found 2 (+1)", ex.Message);
                Assert.Contains("\"fileCountAtBaseline\": 2", ex.Message);   // the exact edit
                Assert.Contains("\"listSha256\": \"", ex.Message);           // ...including the new digest
                Assert.Contains("REWRITE \"pinReason\"", ex.Message);        // ...and its reason
                Assert.Contains("\"2 files\"", ex.Message);                  // the phrase the parser will demand
                Assert.Contains("task-board.md", ex.Message);
                Assert.Contains("upstream-sync.md", ex.Message);
                // 'alpha' did not move, so it is NOT named — a guard that reds on everything
                // teaches a syncer to skim.
                Assert.DoesNotContain("'alpha' DRIFTED", ex.Message);
            });
    }

    /// <summary>What the COUNT alone would miss and the file-list digest catches: the same
    /// number of files under different names (a rename, or an equal add/remove swap).</summary>
    [Fact]
    public void Fixture_ListedTreeRenamedAtEqualCount_Fails_BecauseTheDigestIsOverTheLIST()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 2, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 2 files", "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 2, "listSha256": "%%beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 2 files", "boardRow": "r" } ] }
                """,
            webTrees: new Dictionary<string, int> { ["alpha"] = 2, ["beta"] = 2 },
            act: (root, _) =>
            {
                var beta = Path.Combine([root, .. WebTreeParts, "beta"]);
                File.Move(Path.Combine(beta, "f1.js"), Path.Combine(beta, "renamed.js"));

                var ex = Record.Exception(() => RunGuard(root, _ => { }));
                Assert.NotNull(ex);
                Assert.Contains("'beta' DRIFTED", ex.Message);
                Assert.Contains("2 files as pinned at v9.9.9, but the file LIST changed", ex.Message);
                Assert.DoesNotContain("'alpha' DRIFTED", ex.Message);
            });
    }

    [Fact]
    public void Fixture_MissingWpfTree_UnreachableBranch_StillAssertsWellFormedness()
    {
        WithFixtureRepo(
            inventory: """
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "listSha256": "%%beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "boardRow": "r" } ] }
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
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "listSha256": "%%beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "boardRow": "r" } ] }
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
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "listSha256": "%%beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "boardRow": "r" } ] }
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
                { "schemaVersion": 2,
                  "baseline": { "upstreamVersion": "v9.9.9", "merge": "deadbeef" },
                  "trees": [
                    { "name": "alpha", "disposition": "served", "fileCountAtBaseline": 1, "listSha256": "%%alpha%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "evidence": "x" },
                    { "name": "beta", "disposition": "not-ported", "fileCountAtBaseline": 1, "listSha256": "%%beta%%", "pinnedAt": "v9.9.9", "pinReason": "v9.9.9 fixture: 1 files", "boardRow": "r" } ] }
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

    // The parser rows below build one-entry inventories out of these pieces, so a row
    // reads as "the ONE thing that is wrong with it" rather than as 300 characters of JSON.
    private const string ZeroDigest = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string Head = "{ \"schemaVersion\": 2, \"baseline\": { \"upstreamVersion\": \"v1.0.0\", \"merge\": \"x\" }, \"trees\": [ ";
    private const string Tail = " ] }";
    private const string Pin = "\"listSha256\": \"" + ZeroDigest + "\", \"pinnedAt\": \"v1.0.0\", \"pinReason\": \"v1.0.0 first pin: 5 files\"";
    private const string ServedFive = "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, " + Pin + ", \"evidence\": \"x\" }";

    [Theory]
    [InlineData("not json at all", "not valid JSON")]
    // schemaVersion 1 is the PRE-PIN schema: an entry written against it carries no
    // listSha256/pinnedAt/pinReason, and must be refused rather than read as "unpinned".
    [InlineData("{ \"schemaVersion\": 1, \"baseline\": { \"upstreamVersion\": \"v1.0.0\", \"merge\": \"x\" }, \"trees\": [] }", "schemaVersion must be 2")]
    [InlineData("{ \"schemaVersion\": 2, \"trees\": [] }", "baseline object is required")]
    [InlineData("{ \"schemaVersion\": 2, \"baseline\": { \"upstreamVersion\": \"v1.0.0\", \"merge\": \"x\" } }", "trees array is required")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"shipped\", \"fileCountAtBaseline\": 5 }" + Tail, "unknown disposition")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 0 }" + Tail, "positive fileCountAtBaseline")]
    // The pin itself: present, well-shaped, and explained.
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, \"evidence\": \"x\" }" + Tail, "requires a non-empty string 'listSha256'")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, \"listSha256\": \"NOTAHASH\", \"evidence\": \"x\" }" + Tail, "64 lowercase hex")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, \"listSha256\": \"" + ZeroDigest + "\", \"evidence\": \"x\" }" + Tail, "requires a non-empty string 'pinnedAt'")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, \"listSha256\": \"" + ZeroDigest + "\", \"pinnedAt\": \"6.8.4\", \"evidence\": \"x\" }" + Tail, "must name the upstream version")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, \"listSha256\": \"" + ZeroDigest + "\", \"pinnedAt\": \"v1.0.0\", \"evidence\": \"x\" }" + Tail, "requires a non-empty string 'pinReason'")]
    // THE ANTI-SILENT-BUMP ROWS: the count was moved and the sentence was not, and the
    // version was moved and the sentence was not. Both are the defect this row closed.
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, \"listSha256\": \"" + ZeroDigest + "\", \"pinnedAt\": \"v1.0.0\", \"pinReason\": \"v1.0.0 first pin: 4 files\", \"evidence\": \"x\" }" + Tail, "literal phrase '5 files'")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, \"listSha256\": \"" + ZeroDigest + "\", \"pinnedAt\": \"v2.0.0\", \"pinReason\": \"v1.0.0 first pin: 5 files\", \"evidence\": \"x\" }" + Tail, "must name its own pinnedAt version 'v2.0.0'")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"served\", \"fileCountAtBaseline\": 5, " + Pin + " }" + Tail, "names no serving code path")]
    [InlineData(Head + "{ \"name\": \"a\", \"disposition\": \"not-ported\", \"fileCountAtBaseline\": 5, " + Pin + " }" + Tail, "names no owning board row")]
    [InlineData(Head + ServedFive + ", { \"name\": \"A\", \"disposition\": \"not-ported\", \"fileCountAtBaseline\": 5, " + Pin + ", \"boardRow\": \"r\" }" + Tail, "duplicate tree entry")]
    public void Parser_MalformedInventory_ThrowsReadable(string json, string expectedMessagePart)
    {
        var ex = Assert.Throws<InventoryFormatException>(() => ParseInventory(json));
        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Fact]
    public void Parser_ValidInventory_RoundTripsAllFields()
    {
        var inventory = ParseInventory("""
            { "schemaVersion": 2,
              "baseline": { "upstreamVersion": "v6.7.4", "merge": "42286638" },
              "trees": [
                { "name": "dtrh", "disposition": "served", "fileCountAtBaseline": 1542, "listSha256": "6848646844a1edd6f8e5076d6a0f154611130d561e0f0b76ac50855e0df236f6", "pinnedAt": "v6.7.4", "pinReason": "v6.7.4: 1542 files", "evidence": "glob" },
                { "name": "goon", "disposition": "not-ported", "fileCountAtBaseline": 184, "listSha256": "a88b98788041176951d054315b489117e8658a45d0241220f86cb8f11ff456bd", "pinnedAt": "v6.7.4", "pinReason": "v6.7.4: 184 files", "boardRow": "row", "note": "n" } ] }
            """);
        Assert.Equal(2, inventory.SchemaVersion);
        Assert.Equal("v6.7.4", inventory.UpstreamVersion);
        Assert.Equal("42286638", inventory.Merge);
        Assert.Equal(2, inventory.Trees.Count);
        Assert.Equal("goon", inventory.Trees[1].Name);
        Assert.Equal("row", inventory.Trees[1].BoardRow);
        Assert.Equal("a88b98788041176951d054315b489117e8658a45d0241220f86cb8f11ff456bd", inventory.Trees[1].ListSha256);
        Assert.Equal("v6.7.4", inventory.Trees[1].PinnedAt);
        Assert.Equal("v6.7.4: 184 files", inventory.Trees[1].PinReason);
    }

    /// <summary>The digest is over the SORTED list, so enumeration order cannot change it,
    /// and it is separator-normalized, so a Windows checkout and a Linux checkout of the same
    /// commit agree. Both are why a committed digest is a pin rather than a machine-local fact.
    /// The third row is the property that makes the digest worth having at all: the count is
    /// equal and the digest is not.</summary>
    [Fact]
    public void ListDigest_IsOrderIndependent_SeparatorNormalized_AndSensitiveToARename()
    {
        var pinned = ListDigest(["a.js", "sub/b.js", "sub/c.js"]);

        Assert.Equal(pinned, ListDigest(["sub/c.js", "a.js", "sub/b.js"]));
        Assert.Equal(pinned, ListDigest(["a.js", "sub/b.js", "sub/c.js"]));
        Assert.NotEqual(pinned, ListDigest(["a.js", "sub/b.js", "sub/renamed.js"]));
        Assert.NotEqual(pinned, ListDigest(["a.js", "sub/b.js", "sub/c.js", "sub/d.js"]));
        Assert.Matches("^[0-9a-f]{64}$", pinned);
    }

    /// <summary>Builds a temp-dir repo: the sln anchor, an optional inventory file,
    /// and optional web trees (name → file count). null inventory = file absent;
    /// null webTrees = no ConditioningControlPanel/ at all; empty dict = the dir
    /// exists but Resources/web does not. Cleans up after the callback.
    ///
    /// <para>The trees are built FIRST, then every <c>%%treeName%%</c> token in the
    /// fixture inventory is replaced by that tree's real list digest — a fixture that
    /// hard-coded a digest would have to be recomputed by hand every time the helper
    /// changed a file name, which is the hand-maintenance this whole row is removing.
    /// A token naming no tree becomes <see cref="ZeroDigest"/>, so a fixture that is
    /// about something else (a stale entry, an unreachable tree) still parses and
    /// still fails for its own reason.</para></summary>
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

            if (inventory is not null)
            {
                var path = Path.Combine([root, .. InventoryParts]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, ResolveDigestTokens(inventory, root, webTrees));
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

    private static string ResolveDigestTokens(string inventory, string root, Dictionary<string, int>? webTrees)
    {
        if (webTrees is { Count: > 0 })
        {
            var web = Path.Combine([root, .. WebTreeParts]);
            foreach (var name in webTrees.Keys)
            {
                var dir = Path.Combine(web, name);
                var digest = ListDigest(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')));
                inventory = inventory.Replace($"%%{name}%%", digest, StringComparison.OrdinalIgnoreCase);
            }
        }

        return DigestTokenPattern().Replace(inventory, ZeroDigest);
    }

    [GeneratedRegex("%%[A-Za-z0-9_]+%%")]
    private static partial Regex DigestTokenPattern();
}
