using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>THE HARNESS MUST NOT BE THE REASON THE MACHINE FAILS, AND THE SWEEP MUST NOT GO STALE.</b>
///
/// <para>Two defects from 2026-08-25, both of them the evidence system lying for reasons that have
/// nothing to do with the product. (1) Every capture that opened an embedded browser left a
/// WebView2 profile under <c>%APPDATA%\CcpClient\dtrh</c> and nothing ever removed it; a 14-capture
/// survey filled a 952 GB volume to zero, and with the volume full a real file picker reported
/// "wrote 0 bytes" — a FALSE HARNESS FAILURE that reads exactly like a storage defect in the app.
/// (2) <c>self-test.ps1</c> drove two surfaces of nineteen, which is how a surface that had stopped
/// capturing entirely sat unnoticed for a day.</para>
///
/// <para><b>Measured rather than argued</b>, on this machine: <c>goon-page -State first-run</c> left
/// 18,382 KB behind on every run; after the fix the tree is ABSENT at exit, removed in 0.1 s (a
/// bounded retry, because a <c>msedgewebview2</c> child outlives its host holding
/// <c>EBWebView\lockfile</c>). The free-space floor refused at exit 1 with 414.06 GB free against a
/// temporarily raised 900 GB floor, and passed against the shipped 5 GB one.</para>
///
/// <para><b>What this is and is not.</b> Lexical guards over two PowerShell files. Nothing here
/// runs a capture, frees a byte, or proves that a removal succeeds — the harness is PowerShell,
/// nothing about it is reflectable, and its text is the only mechanical grip available (the same
/// grip and the same lineage as <see cref="HarnessLeaseGuardTests"/>). "Does the file DO this" is
/// asked of CODE LINES ONLY, dropping any line whose first non-whitespace character is <c>#</c>,
/// because a guard satisfied by PROSE ABOUT a mechanism is worse than no guard: it reports the
/// mechanism present. The third fact is not lexical about its subject at all — it compares two
/// lists the harness itself declares, so a surface added to one and not the other is a red.</para>
/// </summary>
public class HarnessDiskHygieneGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    private static string HarnessFile(string name) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "client", "tools", "verify", name));

    /// <summary>The file with every comment line removed — what it DOES, never what it says.</summary>
    private static string CodeLines(string script) =>
        string.Join('\n', script.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    /// <summary>
    /// The profile tree is cleared at START, at EXIT, and on the FAILURE path — and the thing
    /// cleared is the ROOT, not one named child.
    ///
    /// <para>The start clear existed before this and was not enough: it removed only
    /// <c>wv2-profile-goon</c>, and only for the goon-page capture, so the base profile, the intake
    /// profile and the loom profile were cleared by nothing, ever. The exit clear is the half that
    /// was missing outright, and the failure path needs its own because that is the path a lane hits
    /// repeatedly while chasing a refusal.</para>
    /// </summary>
    [Fact]
    public void TheCaptureHarnessClearsItsWebView2ProfileTreeAtStartAndAtEveryExit()
    {
        var code = CodeLines(HarnessFile("capture.ps1"));

        (string Needle, string Why)[] required =
        [
            ("$dtrhRoot = Join-Path $env:APPDATA 'CcpClient\\dtrh'",
                "the clear must target the profile ROOT. Scoped to one named child, the base wv2-profile, "
                + "wv2-profile-intake and wv2-profile-loom accumulate forever - which is what filled the volume"),
            ("Remove-Item $dtrhRoot -Recurse -Force -ErrorAction Stop",
                "the removal itself, on the root, refusing silently-partial deletes"),
            ("Clear-DtrhProfiles 'deterministic start'",
                "the START clear, unconditional rather than scoped to one surface"),
            ("Clear-DtrhProfiles 'exit'",
                "the EXIT clear - the half that did not exist, and the reason profiles accumulated one run at a time"),
            ("Clear-DtrhProfiles 'exit (failed)'",
                "the FAILURE path clears too; it leaks exactly as much as the success path and is hit more often"),
        ];

        foreach (var (needle, why) in required)
        {
            Assert.True(code.Contains(needle, StringComparison.Ordinal),
                $"client/tools/verify/capture.ps1 no longer carries `{needle}` on a CODE line. {why}");
        }

        // The start clear must be reachable for EVERY surface. Scoping it back to goon-page is the
        // exact regression this fact exists to catch, and it would still satisfy the needle above.
        var startClear = code.IndexOf("Clear-DtrhProfiles 'deterministic start'", StringComparison.Ordinal);
        var lineStart = code.LastIndexOf('\n', startClear) + 1;
        var startLine = code[lineStart..startClear];
        Assert.True(string.IsNullOrWhiteSpace(startLine),
            "the START clear is no longer an unconditional statement - it is guarded by "
            + $"`{startLine.Trim()}`. A clear that runs for one surface is what left every other "
            + "surface's profile on disk forever.");
    }

    /// <summary>
    /// The free-space refusal happens BEFORE the machine-wide desktop lease is taken.
    ///
    /// <para>Order is the whole point twice over. Past a full volume every failure this harness
    /// produces is a lie about the product, so the refusal must come before anything runs; and a run
    /// that cannot honestly produce evidence must not make every other lane queue five minutes
    /// behind its lease to find that out.</para>
    /// </summary>
    [Fact]
    public void TheCaptureHarnessRefusesAThinVolumeBeforeItTakesTheDesktopLease()
    {
        var code = CodeLines(HarnessFile("capture.ps1"));

        Assert.Contains("$script:freeSpaceFloorBytes = 5GB", code, StringComparison.Ordinal);
        Assert.Contains("AvailableFreeSpace", code, StringComparison.Ordinal);

        var assertFreeSpace = code.IndexOf("\nAssert-FreeSpace", StringComparison.Ordinal);
        var takeLease = code.IndexOf("\nTake-Lease", StringComparison.Ordinal);
        Assert.True(assertFreeSpace >= 0,
            "client/tools/verify/capture.ps1 no longer CALLS Assert-FreeSpace at script scope - "
            + "declaring the function without calling it is the shape a guard must never accept");
        Assert.True(takeLease >= 0, "client/tools/verify/capture.ps1 no longer calls Take-Lease at script scope");
        Assert.True(assertFreeSpace < takeLease,
            "client/tools/verify/capture.ps1 takes the machine-wide desktop lease BEFORE it checks free "
            + "space. A run that is going to refuse must refuse before it makes every other lane wait for it.");
    }

    /// <summary>
    /// <b>Every surface capture.ps1 can bind is in the table the sweep drives.</b>
    ///
    /// <para><c>self-test.ps1 -Sweep</c> iterates <c>capture.ps1</c>'s own <c>$statesFor</c> table,
    /// discovered through the PowerShell parser rather than copied — a duplicated list is precisely
    /// the rot vector the sweep exists to close. That leaves ONE hole a lexical check cannot see: a
    /// surface added to the <c>ValidateSet</c> and not to <c>$statesFor</c>. <c>capture.ps1</c> fails
    /// closed on it when driven BY NAME, but the sweep would simply never drive it, and the surface
    /// would be exactly as unswept as <c>companion-transcript</c> was. So the two lists are compared
    /// here, mechanically, in both directions.</para>
    /// </summary>
    [Fact]
    public void TheSweepDiscoversEverySurfaceCaptureCanBind_AndNeverCarriesItsOwnList()
    {
        var capture = HarnessFile("capture.ps1");
        var selfTest = CodeLines(HarnessFile("self-test.ps1"));

        // Discovery, not duplication: the sweep must read capture.ps1's table through the parser.
        foreach (var needle in new[] { "Parser]::ParseFile", "'$statesFor'", "SafeGetValue()" })
        {
            Assert.True(selfTest.Contains(needle, StringComparison.Ordinal),
                $"client/tools/verify/self-test.ps1 no longer carries `{needle}` on a code line - the sweep "
                + "is no longer discovering its surfaces from capture.ps1's own table, and a copied list goes "
                + "stale the way the two-surface coverage it replaced did");
        }

        var surfaceParam = capture.Split('\n').FirstOrDefault(l => l.Contains("[string]$Surface", StringComparison.Ordinal));
        Assert.NotNull(surfaceParam);
        var validateSet = Regex.Match(surfaceParam, @"ValidateSet\((?<body>[^)]*)\)");
        Assert.True(validateSet.Success, "capture.ps1's -Surface parameter no longer declares a ValidateSet");
        var bindable = Regex.Matches(validateSet.Groups["body"].Value, @"'(?<name>[^']+)'")
            .Select(m => m.Groups["name"].Value).Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(bindable);

        // The $statesFor table's keys, read from the assignment block only so a surface name written
        // in a comment somewhere else in the file cannot stand in for a real row.
        var tableStart = capture.IndexOf("$statesFor = @{", StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "capture.ps1 no longer declares a $statesFor table");
        var tableEnd = capture.IndexOf("\n}", tableStart, StringComparison.Ordinal);
        Assert.True(tableEnd > tableStart, "capture.ps1's $statesFor table is not closed by a line-anchored brace");
        var tabled = Regex.Matches(CodeLines(capture[tableStart..tableEnd]), @"(?m)^\s*'(?<name>[^']+)'\s*=\s*@\(")
            .Select(m => m.Groups["name"].Value).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(bindable, tabled);
    }
}
