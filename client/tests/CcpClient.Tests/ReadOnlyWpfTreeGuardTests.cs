using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The read-only WPF tree must stay byte-identical to its <c>main</c> baseline, and until now
/// that rule had NO ENFORCEMENT anywhere in the repository.
///
/// <para><b>Why this exists.</b> <c>docs/constitution.md</c> makes
/// <c>ConditioningControlPanel/</c> read-only behavioural evidence, and the upstream-sync
/// procedure resolves every content conflict in that tree to <c>main</c> precisely so the
/// port's archaeology citations (<c>File.cs:line</c>) stay unambiguous. The rule was stated in
/// three documents and checked by nothing. It drifted 1180 files unnoticed for the life of the
/// branch (repaired once), and then drifted AGAIN two commits later: a de-vendoring sweep
/// that stripped agent names and worktree paths out of documentation applied itself to eight
/// files under <c>ConditioningControlPanel/docs/</c> and <c>openspec/</c> as well as the port's
/// own docs. That edit moved <c>VN_MAX_MS</c>/<c>VN_MAX_BYTES</c> in
/// <c>docs/GOON_VOICE_PLAN.md</c> from lines 61-62 to 59-60 and shortened the census's
/// design-doc line total from 1072 to 1070, reddening two <see cref="GoonGameCensusTests"/>
/// assertions. The guard that noticed was a census pinning citations into those documents by
/// line number — an indirect, downstream detector that reports a citation defect when the
/// actual defect is a boundary violation. This test reports the violation itself.</para>
///
/// <para><b>The invariant, and why this exact form holds in both states.</b> The subtree object
/// id of <c>ConditioningControlPanel/</c> at HEAD must equal its object id at the merge base
/// with <c>main</c>. Before a sync that is the shared baseline; after a completed sync the
/// merge base IS <c>main</c>, so the same comparison reduces to the upstream-sync procedure's
/// own strongest resolution check. Object identity is used rather than a file diff because it
/// is one comparison over the whole tree, cannot miss a path, and — unlike a content diff —
/// never engages the <c>diff=lfs</c> driver that <c>.gitattributes</c> binds to the seventeen
/// tracked <c>*.mp4</c>/<c>*.mov</c>/<c>*.ccpmod</c> files inside this tree.</para>
///
/// <para><b>Both the committed tree and the working tree are checked, and the working tree is
/// the one that matters most.</b> The sweep described above was a WORKING-TREE event before it
/// was a commit, and the ritual is edit → floor → land. A guard that only reads <c>HEAD</c>
/// hands a green floor to the very sweep it exists to catch, and the worker then reads their own
/// shifted line numbers and writes an ambiguous citation with the guard's blessing.</para>
///
/// <para><b>The baseline is DERIVED, and it is anchored on <c>origin/main</c> first.</b> A
/// recorded baseline SHA rots — the citation inventory's own recorded merge predates the latest
/// sync — so the merge base is computed instead. Order matters and is not cosmetic: local
/// <c>main</c> is a convenience mirror that a bare <c>git fetch origin</c> leaves stale, and a
/// stale anchor yields an OLDER merge base, a false red, and then a "repair" instruction that
/// would revert this tree to its pre-sync state and turn the false red green. That is the exact
/// 1180-file regression this guard exists to prevent, arrived at by obeying the guard. So both
/// refs are resolved and the LATER merge base wins, which is correct whichever ref is stale.</para>
///
/// <para>A missing <c>git</c> or an unresolvable <c>main</c> is a hard FAILURE and never a skip:
/// this is a git repository by construction, and a guard that skips when it cannot look is the
/// same as no guard, which is the condition this test was written to end.</para>
/// </summary>
public class ReadOnlyWpfTreeGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    /// <summary>The read-only zone, as a repo-relative git path.</summary>
    private const string ReadOnlyTree = "ConditioningControlPanel";

    /// <summary>Candidate refs for the upstream baseline, authority first. See the class remarks:
    /// preferring the local mirror is what makes a stale anchor dangerous.</summary>
    private static readonly string[] MainRefs = ["origin/main", "main"];

    /// <summary>The failure text lists drifted paths, and the floor's reporter truncates a failure
    /// message to 600 characters with no marker. A 1180-file drift would arrive as one clipped
    /// blob, so the list is capped here and the full command is printed instead.</summary>
    private const int MaxListedPaths = 20;

    [Fact]
    public async Task TheWpfTreeIsByteIdenticalToItsMainBaseline()
    {
        var (mainRef, mergeBase) = await ResolveBaselineAsync();

        // rev-parse takes several revisions and prints one line each, so this is one process.
        var trees = (await GitAsync("rev-parse", $"HEAD:{ReadOnlyTree}", $"{mergeBase}:{ReadOnlyTree}"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToArray();
        Assert.True(trees.Length == 2,
            $"git rev-parse returned {trees.Length} object id(s), expected 2 — the read-only-tree guard refuses to skip");

        var (atHead, atBaseline) = (trees[0], trees[1]);
        var drifted = string.Equals(atHead, atBaseline, StringComparison.Ordinal)
            ? string.Empty
            : await GitAsync("diff", "--name-only", mergeBase, "HEAD", "--", ReadOnlyTree);

        Assert.True(string.Equals(atHead, atBaseline, StringComparison.Ordinal), string.Join(Environment.NewLine,
            $"{ReadOnlyTree}/ has DRIFTED from its {mainRef} baseline — it is read-only behavioural evidence "
                + "and every port citation into it becomes ambiguous the moment it stops matching.",
            $"  HEAD:{ReadOnlyTree}         = {atHead}",
            $"  {mergeBase}:{ReadOnlyTree} = {atBaseline}",
            "  drifted paths:",
            Describe(drifted),
            $"  full list: git diff --name-only {mergeBase} HEAD -- {ReadOnlyTree}",
            $"  repair: git checkout {mergeBase} -- {ReadOnlyTree}/  (ONLY if {mergeBase} is the baseline you "
                + "actually merged — if it looks older than your last sync, run git fetch origin main:main first)",
            "  (port work never edits this tree; upstream changes arrive only through a merge of main)"));
    }

    /// <summary>
    /// The committed tree can be identical while the tree on disk is not, and the disk is what a
    /// worker reads line numbers out of. Uncommitted edits are their own failure with their own
    /// message, because the repair differs: nothing has been recorded yet, so it is a discard.
    /// </summary>
    [Fact]
    public async Task TheWpfTreeHasNoUncommittedEdits()
    {
        var dirty = (await GitAsync("status", "--porcelain", "--", ReadOnlyTree)).Trim();

        Assert.True(dirty.Length == 0, string.Join(Environment.NewLine,
            $"{ReadOnlyTree}/ has UNCOMMITTED edits. It is read-only behavioural evidence, and a citation "
                + "read out of an edited working tree is ambiguous before it is ever committed.",
            Describe(dirty),
            $"  discard them: git checkout HEAD -- {ReadOnlyTree}/",
            "  (if these arrived from a merge of main, commit the merge — this guard reads the working tree "
                + "on purpose, and an in-progress merge is not a state the gates are run in)"));
    }

    /// <summary>Resolves every candidate ref and returns the LATEST merge base, so neither a stale
    /// local <c>main</c> nor a stale <c>origin/main</c> can anchor the comparison too far back.</summary>
    private static async Task<(string Ref, string MergeBase)> ResolveBaselineAsync()
    {
        var found = new List<(string Ref, string MergeBase)>();
        foreach (var candidate in MainRefs)
        {
            var probe = await TryGitAsync("rev-parse", "--verify", "--quiet", $"{candidate}^{{commit}}");
            if (probe.ExitCode != 0)
            {
                continue;
            }

            var mergeBase = (await GitAsync("merge-base", "HEAD", candidate)).Trim();
            found.Add((candidate, mergeBase));
        }

        Assert.True(found.Count > 0,
            $"none of [{string.Join(", ", MainRefs)}] resolves in this clone, so the read-only WPF tree cannot be "
            + "compared against its baseline. This guard refuses to skip — a boundary nothing checks is a boundary "
            + "that drifts. Fix with: git fetch origin main:main");

        var best = found[0];
        foreach (var candidate in found.Skip(1))
        {
            // --is-ancestor A B exits 0 when A is reachable from B, i.e. B is the later baseline.
            var later = await TryGitAsync("merge-base", "--is-ancestor", best.MergeBase, candidate.MergeBase);
            if (later.ExitCode == 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Indents a path list and caps it, so the floor's 600-character truncation cannot
    /// silently eat the evidence on a large drift.</summary>
    private static string Describe(string paths)
    {
        var lines = paths.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        var shown = lines.Take(MaxListedPaths).Select(p => "    " + p);
        return lines.Length > MaxListedPaths
            ? string.Join(Environment.NewLine, shown) + Environment.NewLine + $"    (+{lines.Length - MaxListedPaths} more)"
            : string.Join(Environment.NewLine, shown);
    }

    private static async Task<string> GitAsync(params string[] arguments)
    {
        var run = await TryGitAsync(arguments);
        Assert.True(run.ExitCode == 0,
            $"git {string.Join(' ', arguments)} exited {run.ExitCode} — the read-only-tree guard refuses to skip."
            + Environment.NewLine + run.StdErr);
        return run.StdOut;
    }

    /// <summary>Runs git and returns its streams SEPARATELY. Merging them would let an exit-0
    /// warning (<c>safe.directory</c>, CRLF advice) land in the value, and
    /// <see cref="ResolveBaselineAsync"/> would then read that warning as a resolved ref.</summary>
    private static async Task<(int ExitCode, string StdOut, string StdErr)> TryGitAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
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
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "git could not be started, and this tree is a git repository by construction — "
                + "the read-only-tree guard refuses to skip", ex);
        }

        using (process)
        {
            // Drain both pipes before waiting, or a full buffer deadlocks the child.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            // The bounded wait, not WaitForExitAsync: this suite has no per-test timeout, so a
            // wedged git (index.lock, a credential prompt, an AV stall) would hang the whole host.
            try
            {
                await TestWait.Until(process.WaitForExitAsync(), $"git {string.Join(' ', arguments)} to exit");
            }
            catch
            {
                // A survivor holds the assembly open and confuses the next run, so the window
                // expiring means kill the tree — then let the timing verdict travel.
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }

            return (process.ExitCode, await stdout, await stderr);
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the read-only-tree guard refuses to skip");
    }
}
