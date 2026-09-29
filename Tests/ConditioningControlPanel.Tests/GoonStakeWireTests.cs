using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Stakes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// PvP stakes on the Goon page (ui/stake.js) speak to the shared C# bridge
/// (Services/Stakes/StakeBridge) through GoonHostService. The two halves are in different
/// languages, so this holds the seam: every stake frame the page sends is one the bridge
/// handles, and the host routes those frames to the bridge before its own switch.
/// </summary>
public class GoonStakeWireTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { AppDir() }.Concat(parts).ToArray()));

    [Fact]
    public void EveryStakeFrameThePageSendsIsOneTheBridgeHandles()
    {
        var js = File.ReadAllText(Path.Combine(SourceRoots.RepoRoot, "Assets", "web", "goon", "ui", "stake.js"));
        var sent = Regex.Matches(js, @"type:\s*'(stake-[a-z]+)'")
            .Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.Contains("stake-limits", sent);
        Assert.Contains("stake-offer", sent);
        Assert.Contains("stake-state", sent);
        Assert.Contains("stake-settle", sent);
        foreach (var type in sent)
            Assert.True(StakeBridge.Handles(type), $"ui/stake.js sends '{type}' but StakeBridge does not handle it");
    }

    [Fact]
    public void TheGoonHostRoutesStakeFramesToTheSharedBridge()
    {
        var host = Read("Services", "GoonGame", "GoonHostService.cs");
        var onPage = host.IndexOf("private static void OnPageMessage", System.StringComparison.Ordinal);
        Assert.True(onPage > 0);
        var body = host.Substring(onPage, 600);
        Assert.Contains("StakeBridge.Handles(", body);
        Assert.Contains("StakeBridge.ForApp(\"goon\"", body);
    }

    [Fact]
    public void ThePageSettlesOnlyOffTheLedgerClaimAndLocksAtCountdown()
    {
        var boot = File.ReadAllText(Path.Combine(SourceRoots.RepoRoot, "Assets", "web", "goon", "boot.js"));
        Assert.Contains("submitClaim(", boot);
        Assert.Contains("stake.lock(code)", boot);
        Assert.Contains("GoonMatchPhase.Countdown", boot);
        // Practice never carries a stake: the eligibility check reads the practice pair.
        Assert.Contains("practice: !!soloPair", boot);
    }
}
