using System.Text.RegularExpressions;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-064 unclassified-literal guard: every <c>"--..."</c> string literal under
/// client/src must be classified in <see cref="HarnessEntryPoints"/> — a new drive/demo
/// flag that nobody classified fails HERE with its file:line, so the refusal cannot be
/// forgotten by the next feature slice. Mirrors DataRootChokePointGuardTests (repo-root
/// walk, never-skip, file:line violations). One fact, three assertion groups:
/// (1) every literal classified; (2) no stale registry entries (a classified flag whose
/// literal left the tree); (3) the gate's wiring in Program.cs — HarnessFlagsIn AFTER the
/// SP-057 override read, BEFORE the composition root — so the pin cannot be satisfied by a
/// helper the real launch never calls while Program.Main stays ungated.
/// </summary>
public partial class HarnessEntryPointGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SrcParts = ["client", "src"];

    [GeneratedRegex("\"--[^\"\r\n]*")]
    private static partial Regex FlagLiteral();

    [Fact]
    public void EveryStartupFlagLiteral_IsClassified_AndTheGateRidesTheRealEntryPoint()
    {
        var srcRoot = Path.Combine([FindRepoRoot(), .. SrcParts]);
        Assert.True(Directory.Exists(srcRoot), $"client/src not found at {srcRoot} — the harness-entry-point guard refuses to skip");

        // (1) Unclassified literals.
        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in FlagLiteral().Matches(lines[i]))
                {
                    // Group 0 includes the opening quote; the literal content starts at --.
                    var literal = match.Value[1..];
                    seen.Add(literal);
                    if (!HarnessEntryPoints.All.ContainsKey(literal))
                    {
                        violations.Add($"{normalized}:{i + 1}: \"{literal}\" is not classified in " +
                            "Lifecycle/HarnessEntryPoints.cs — every startup flag literal needs a " +
                            "disposition (Harness / Demo / SelfCheck / Modifier / NotAStartupFlag), SP-064");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "unclassified \"--...\" literals:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // (2) Stale registry entries: a classified literal no longer present in the tree
        // means the registry drifted from reality (flag renamed/deleted without updating it).
        var stale = HarnessEntryPoints.All.Keys.Where(k => !seen.Contains(k)).ToArray();
        Assert.True(stale.Length == 0,
            "registry entries with no literal in client/src (stale classification): " + string.Join(", ", stale));

        // (3) Wiring: the gate call in Program.cs must sit between the SP-057 override
        // read and the composition-root construction — the real entry point, in the order
        // that fires before anything can write (framing b). Index-order assertion on the
        // real source, the choke-point-guard pattern applied to call ORDER.
        var programPath = Path.Combine(srcRoot, "CcpClient.Desktop", "Program.cs");
        Assert.True(File.Exists(programPath), $"Program.cs not found at {programPath} — the guard refuses to skip");
        var program = File.ReadAllText(programPath);
        var overrideRead = program.IndexOf("CompositionRoot.ActiveDataRootOverride()", StringComparison.Ordinal);
        var gateCall = program.IndexOf("HarnessEntryPoints.HarnessFlagsIn", StringComparison.Ordinal);
        var rootConstruction = program.IndexOf("new CompositionRoot", StringComparison.Ordinal);
        Assert.True(overrideRead >= 0 && gateCall >= 0 && rootConstruction >= 0,
            $"Program.cs wiring tokens missing (overrideRead={overrideRead}, gateCall={gateCall}, rootConstruction={rootConstruction}) — " +
            "the SP-064 gate must call HarnessEntryPoints.HarnessFlagsIn in Program.Main");
        Assert.True(overrideRead < gateCall && gateCall < rootConstruction,
            $"gate order broken (overrideRead={overrideRead}, gateCall={gateCall}, rootConstruction={rootConstruction}) — " +
            "the refusal must fire AFTER the SP-057 override read and BEFORE the composition root exists");
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
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the harness-entry-point guard refuses to skip");
    }
}
