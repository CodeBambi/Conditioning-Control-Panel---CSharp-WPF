using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-057 choke-point guard: <c>Environment.SpecialFolder.ApplicationData</c> and
/// <c>SpecialFolder.UserProfile</c> may appear ONLY in
/// <c>Lifecycle/CompositionRoot.cs</c> (the single data-root authority). Any other use
/// is a data-root bypass — the exact false-isolation failure mode this task closes —
/// and fails this test with the offending file:line. FindRepoRoot precedent:
/// UpstreamPayloadInventoryTests.cs:98 (a guard never skips; unresolvable root fails).
/// </summary>
public class DataRootChokePointGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SrcParts = ["client", "src"];

    private static readonly string[] ForbiddenTokens =
    [
        "SpecialFolder.ApplicationData",
        "SpecialFolder.UserProfile",
    ];

    private const string AllowedSuffix = "Lifecycle/CompositionRoot.cs";

    [Fact]
    public void NoDataRootSpecialFolderUseOutsideTheChokePoint()
    {
        var srcRoot = Path.Combine([FindRepoRoot(), .. SrcParts]);
        Assert.True(Directory.Exists(srcRoot), $"client/src not found at {srcRoot} — the choke-point guard refuses to skip");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.EndsWith(AllowedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue; // the choke point itself
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var token in ForbiddenTokens)
                {
                    if (lines[i].Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add($"{normalized}:{i + 1}: {token} outside {AllowedSuffix} — "
                            + "route the data root through CompositionRoot.DefaultSettingsPath() (SP-057)");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "data-root choke-point guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // SP-062: the pin's class MUST stay inside ProcessEnvCollection. The collection
        // membership is the env-leak isolation (probe-proven intra-collection sequentiality);
        // a future refactor dropping the attribute silently reintroduces the cross-collection
        // race against the pin's two checkpoints. This deterministic reflection assertion backs
        // the pin's probabilistic skip tripwire, and lives inside THIS fact so the suite count
        // stays pinned (892) — the packet pins the floor exactly.
        Assert.True(
            typeof(DataRootOverrideTests)
                .GetCustomAttributes(typeof(Xunit.CollectionAttribute), inherit: false)
                .Cast<Xunit.CollectionAttribute>()
                .Any(a => a.Name == nameof(ProcessEnvCollection)),
            "DataRootOverrideTests must carry [Collection(nameof(ProcessEnvCollection))] — " +
            "the SP-062 env-leak isolation; removing it revives the cross-collection race");
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the choke-point guard refuses to skip");
    }
}
