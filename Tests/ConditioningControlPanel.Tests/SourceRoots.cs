using System;
using System.Collections.Concurrent;
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
    private static IReadOnlyList<string>? _productDirectories;

    // The tree cannot change while the suite runs, so each walk is done once. Without this
    // the suite re-walks ~1500 files per call, and some guards call in a Theory loop.
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> SourcesByPattern = new(StringComparer.Ordinal);

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
    internal static IReadOnlyList<string> ProductDirectories
    {
        get
        {
            if (_productDirectories != null) return _productDirectories;

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

            return _productDirectories = roots;
        }
    }

    /// <summary>Product source files matching <paramref name="searchPattern"/> (e.g. <c>"*.cs"</c>)
    /// across every product root, build output and nested worktrees excluded.
    ///
    /// <para>Exclusions are matched on each file's path RELATIVE to its own product root, because
    /// they are about directories INSIDE the tree under test. Matching the absolute path — as
    /// several of these guards used to — silently empties the entire walk inside an agent worktree,
    /// where the checkout itself sits under a <c>.claude</c> segment.</para></summary>
    internal static IReadOnlyList<string> EnumerateProductSources(string searchPattern) =>
        SourcesByPattern.GetOrAdd(searchPattern, Walk);

    private static IReadOnlyList<string> Walk(string searchPattern)
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

    /// <summary>The absolute path to ONE product file, given its path relative to a product root
    /// (e.g. <c>FindProductFile("Services", "ChromeFxNav.cs")</c>), probed across every root in
    /// <see cref="ProductDirectories"/>.
    ///
    /// <para><b>Why this exists.</b> <see cref="EnumerateProductSources"/> fixed the guards that
    /// SCAN a directory. The ones that read ONE KNOWN FILE were left alone because they fail loudly
    /// rather than passing vacuously — true, but the cost landed on the wrong people: ~20 test
    /// classes pinned a file to <c>ConditioningControlPanel/</c> by name, so every unit of the Core
    /// migration tripped a fresh red test that had nothing to do with it, one at a time. One unit
    /// had to abandon a file it could otherwise have moved. Probing every root means a file's home
    /// stops being a test's business.</para></summary>
    internal static string FindProductFile(params string[] relativeParts)
    {
        var relative = Path.Combine(relativeParts);
        var hits = ProductDirectories.Select(root => Path.Combine(root, relative))
                                     .Where(File.Exists)
                                     .ToList();

        // Naming the roots is the point: "in none of CCP.Core, ConditioningControlPanel" says the
        // file was renamed or deleted. A bare absolute path says only that some path was wrong.
        Assert.True(hits.Count > 0,
            $"'{relative}' is in none of the product roots searched: " +
            string.Join(", ", ProductDirectories.Select(Path.GetFileName)));

        // Two roots holding one relative path means either a half-finished move (delete the stale
        // copy) or one real file per head (then the test has to say which head it means — e.g.
        // GlobalUsings.cs and Properties/AssemblyInfo.cs are already legitimately in both roots).
        // Taking the first silently would assert against the wrong copy and pass, so make the
        // author choose rather than guess for them.
        Assert.True(hits.Count == 1,
            $"'{relative}' exists in more than one product root, so nothing can say which copy this "
            + "test means — finish the move, or pin the test to one head: " + string.Join(", ", hits));

        return hits[0];
    }

    /// <summary>The directory holding the nine language JSON files, wherever it currently lives.
    ///
    /// <para><b>Why this exists.</b> Fourteen sites across twelve test classes each built this path
    /// as <c>&lt;repo&gt;/ConditioningControlPanel/Localization/Languages</c> by hand. The catalogue
    /// moved to <c>CCP.Core</c> and all fourteen went red in one commit — each one green on its own
    /// branch, red only once the move and the tests met on main. Probing the roots means the next
    /// move costs nothing here.</para></summary>
    internal static string LanguagesDirectory =>
        Path.GetDirectoryName(FindProductFile("Localization", "Languages", "en.json"))!;


    /// <summary>The text of ONE product file, located by <see cref="FindProductFile"/>.</summary>
    internal static string ReadProductFile(params string[] relativeParts) =>
        File.ReadAllText(FindProductFile(relativeParts));

    /// <summary>A product file's path relative to the repo root, forward-slashed, e.g.
    /// <c>ConditioningControlPanel/Models/AppSettings.cs</c>.
    ///
    /// <para>Use this — never <see cref="Path.GetFileName(string)"/> — to key an allow list or to
    /// print an offender. Now that the walk spans several roots a bare filename is no longer
    /// unique: a same-named file in another head would silently inherit the first one's
    /// exemption.</para></summary>
    internal static string Relative(string file) =>
        Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/');
}
