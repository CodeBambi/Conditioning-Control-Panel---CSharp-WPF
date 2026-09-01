using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Where the guard tests look for product source.
///
/// <para><b>Why this exists.</b> A dozen tests in this suite are source-scanning guards: they walk
/// the product's <c>.cs</c>/<c>.xaml</c> files on disk and grep them to assert a project-wide
/// invariant ("every awareness line has a consent gate", "every EMI moment id is wired up"). Each
/// had computed its own scan root as the <c>ConditioningControlPanel/</c> directory. The app is
/// being split so a platform-agnostic <c>CCP.Core</c> can be shared with future Linux/VR heads, and
/// <c>CCP.Core/</c> is a SIBLING of that directory rather than a child — so every file that moves
/// to Core silently drops out of those scans. The guard then greps zero relevant files, finds zero
/// violations, and passes green while checking nothing. That is a safety net lost without a single
/// red test, which is the worst way to lose one.</para>
///
/// <para>So the roots live here, once, and every guard reads them from here. When the next head
/// lands (<c>CCP.Avalonia</c>), nothing below needs editing — see
/// <see cref="ProductDirectories"/>.</para>
/// </summary>
internal static class SourceRoots
{
    /// <summary>Directories that are never product source, matched per path SEGMENT.
    ///
    /// <para><c>.claude</c> is in here because agent worktrees check the whole repo out under
    /// <c>&lt;repo&gt;/.claude/worktrees/&lt;name&gt;/</c> — those are other branches' copies of
    /// this same tree, and asserting against them fails on whatever anyone happens to have on
    /// disk.</para></summary>
    private static readonly string[] SkipSegments = { "bin", "obj", ".claude", "node_modules" };

    private static string? _repoRoot;

    /// <summary>The repository root, found by walking up from the test binary to the solution file.
    ///
    /// <para>Anchoring on the <c>.sln</c> rather than on <c>ConditioningControlPanel/</c> is the
    /// point: the repo root is what stays put while projects are added and files move between
    /// them. Inside an agent worktree the nearest <c>.sln</c> is that worktree's own, which is the
    /// tree under test — correct, and the same root the old per-class walkers found.</para></summary>
    internal static string RepoRoot
    {
        get
        {
            if (_repoRoot != null) return _repoRoot;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.EnumerateFiles(dir.FullName, "*.sln").Any())
                dir = dir.Parent;

            Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
            return _repoRoot = dir!.FullName;
        }
    }

    /// <summary>Every product project root: today <c>ConditioningControlPanel/</c> and
    /// <c>CCP.Core/</c>, tomorrow <c>CCP.Avalonia/</c> too, with no edit here.
    ///
    /// <para>Discovered as the repo-root directories holding a <c>.csproj</c> DIRECTLY. That one
    /// rule also excludes the things it should: this test project lives at
    /// <c>Tests/ConditioningControlPanel.Tests/</c> and the generators at
    /// <c>Tools/&lt;name&gt;/</c>, both a level deeper, so neither <c>Tests/</c> nor <c>Tools/</c>
    /// has a <c>.csproj</c> of its own. A new head dropped at the repo root joins automatically; a
    /// new head nested under a folder would not, so put heads at the root.</para></summary>
    internal static IEnumerable<string> ProductDirectories
    {
        get
        {
            var roots = Directory.EnumerateDirectories(RepoRoot)
                                 .Where(d => Directory.EnumerateFiles(d, "*.csproj").Any())
                                 .OrderBy(d => d, StringComparer.Ordinal)
                                 .ToList();

            // The whole reason this class exists. If discovery ever quietly finds only the WPF head,
            // a *.cs scan still returns ~1500 files and every "found no violations" assertion below
            // it passes — while no longer covering a single file that has moved to Core.
            Assert.True(roots.Count >= 2,
                $"expected at least the WPF head and CCP.Core under {RepoRoot}, found: " +
                (roots.Count == 0 ? "(none)" : string.Join(", ", roots.Select(Path.GetFileName))));

            return roots;
        }
    }

    /// <summary>Product source files matching <paramref name="searchPattern"/> (e.g. <c>"*.cs"</c>)
    /// across every product root, build output and nested worktrees excluded.
    ///
    /// <para>Exclusions are matched on each file's path RELATIVE to its own product root, because
    /// they are about directories INSIDE the tree under test. Matching the absolute path — as
    /// several of these guards used to — silently empties the entire walk inside an agent worktree,
    /// where the checkout itself sits under a <c>.claude</c> segment.</para></summary>
    internal static IReadOnlyList<string> EnumerateProductSources(string searchPattern)
    {
        var files = ProductDirectories
            .SelectMany(root => Directory
                .EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(root, f)
                                 .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 .Any(segment => SkipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))))
            .ToList();

        // A walk that finds nothing makes every assertion built on it vacuously true.
        Assert.True(files.Count > 0,
            $"the product source walk found no {searchPattern} files under {RepoRoot}");

        return files;
    }
}
