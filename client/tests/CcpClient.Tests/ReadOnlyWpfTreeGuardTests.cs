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
/// branch (repaired at SP-141), and then drifted AGAIN two commits later: a de-vendoring sweep
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
/// is one comparison over the whole tree and cannot miss a path.</para>
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

    [Fact]
    public async Task TheWpfTreeIsByteIdenticalToItsMainBaseline()
    {
        var mainRef = await ResolveMainRefAsync();
        var mergeBase = (await GitAsync("merge-base", "HEAD", mainRef)).Trim();
        Assert.False(mergeBase.Length == 0,
            $"git merge-base HEAD {mainRef} produced nothing — the read-only-tree guard refuses to skip");

        var atHead = (await GitAsync("rev-parse", $"HEAD:{ReadOnlyTree}")).Trim();
        var atBaseline = (await GitAsync("rev-parse", $"{mergeBase}:{ReadOnlyTree}")).Trim();

        if (!string.Equals(atHead, atBaseline, StringComparison.Ordinal))
        {
            var drifted = await GitAsync("diff", "--name-only", mergeBase, "HEAD", "--", ReadOnlyTree);
            Assert.Fail(
                $"{ReadOnlyTree}/ has DRIFTED from its {mainRef} baseline — it is read-only behavioural "
                + "evidence and every port citation into it becomes ambiguous the moment it stops matching."
                + Environment.NewLine
                + $"  HEAD:{ReadOnlyTree}     = {atHead}" + Environment.NewLine
                + $"  {mergeBase[..Math.Min(9, mergeBase.Length)]}:{ReadOnlyTree} = {atBaseline}" + Environment.NewLine
                + "  drifted paths:" + Environment.NewLine
                + string.Join(Environment.NewLine, drifted.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => "    " + p.Trim()))
                + Environment.NewLine
                + $"  repair: git checkout {mergeBase} -- {ReadOnlyTree}/"
                + Environment.NewLine
                + "  (port work never edits this tree; upstream changes arrive only through a merge of main)");
        }
    }

    /// <summary>Prefers a local <c>main</c>, falls back to <c>origin/main</c>; failing both is a
    /// failure with the one command that fixes it, not a skip.</summary>
    private static async Task<string> ResolveMainRefAsync()
    {
        foreach (var candidate in new[] { "main", "origin/main" })
        {
            var probe = await TryGitAsync("rev-parse", "--verify", "--quiet", $"{candidate}^{{commit}}");
            if (probe.ExitCode == 0 && probe.Output.Trim().Length > 0)
            {
                return candidate;
            }
        }

        Assert.Fail(
            "neither 'main' nor 'origin/main' resolves in this clone, so the read-only WPF tree cannot be "
            + "compared against its baseline. This guard refuses to skip — a boundary nothing checks is a "
            + "boundary that drifts. Fix with: git fetch origin main:main");
        return string.Empty; // unreachable; Assert.Fail throws
    }

    private static async Task<string> GitAsync(params string[] arguments)
    {
        var run = await TryGitAsync(arguments);
        Assert.True(run.ExitCode == 0,
            $"git {string.Join(' ', arguments)} exited {run.ExitCode} — the read-only-tree guard refuses to skip."
            + Environment.NewLine + run.Output);
        return run.Output;
    }

    private static async Task<(int ExitCode, string Output)> TryGitAsync(params string[] arguments)
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
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "git could not be started, and this tree is a git repository by construction — "
                + "the read-only-tree guard refuses to skip", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, (await stdout) + (await stderr));
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
