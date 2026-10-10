using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// PvP stakes on the Goon page (ui/stake.js) speak to the shared Core bridge
/// (Services/Stakes/StakeBridge) through the Goon window (WPF GoonStakeWireTests). The two halves
/// are in different languages, so this holds the seam: every stake frame the page sends is one the
/// bridge handles, and the host routes those frames to the bridge before its own switch.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GoonStakeWireTests
{
    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void EveryStakeFrameThePageSendsIsOneTheBridgeHandles()
    {
        var js = Read("Assets", "web", "goon", "ui", "stake.js");
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
        var host = Read("CCP.Avalonia", "Views", "Games", "GameWindow.Goon.cs");
        var onPage = host.IndexOf("private bool HandleGoon", StringComparison.Ordinal);
        Assert.True(onPage > 0);
        var body = host.Substring(onPage, 700);
        Assert.Contains("StakeBridge.Handles(", body);
        Assert.Contains("GoonStakes.Handle(", body);
        Assert.Contains("StakeBridge.ForApp(\"goon\"", host);
    }

    [Fact]
    public void ThePageSettlesOnlyOffTheLedgerClaimAndLocksAtCountdown()
    {
        var boot = Read("Assets", "web", "goon", "boot.js");
        Assert.Contains("submitClaim(", boot);
        Assert.Contains("stake.lock(code)", boot);
        Assert.Contains("GoonMatchPhase.Countdown", boot);
        // Practice never carries a stake: the eligibility check reads the practice pair.
        Assert.Contains("practice: !!soloPair", boot);
    }

    private sealed class NoAccountStakes : IStakeApi
    {
        public string? Account() => null;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default) => Task.FromResult<JObject?>(null);
    }

    [Fact]
    public async Task AStakeFrameIsAnsweredByTheSharedBridge_AsGameGoon()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            GoonHostService.DetachWindow();
            var w = new GameWindow(GameWindow.Games["goon"]);
            var posted = new List<JObject>();
            w.Posted += json => { lock (posted) posted.Add(JObject.Parse(json)); };
            w.Show();
            List<JObject> Stakes() { lock (posted) return posted.Where(p => (string?)p["type"] == "stake").ToList(); }
            var settlement = new StakeSettlement((_, _) => 0, null, () => null);
            GameWindow.GoonStakes = new StakeBridge("goon", o => w.Post(o), new NoAccountStakes(), settlement, () => true, () => { });
            try
            {
                w.HandleMessage("{\"type\":\"stake-limits\"}");
                for (int i = 0; i < 200 && Stakes().Count < 1; i++) await Task.Delay(10);
                var limits = Assert.Single(Stakes());
                Assert.Equal("limits", (string?)limits["op"]);
                Assert.Equal("goon", (string?)limits["game"]);
                Assert.False((bool)limits["ok"]!);
                Assert.Equal("signin", (string?)limits["reason"]);

                // An offer with no room is refused on the host side and never sent anywhere.
                w.HandleMessage("{\"type\":\"stake-offer\",\"kind\":\"sp\",\"amount\":50}");
                for (int i = 0; i < 200 && Stakes().Count < 2; i++) await Task.Delay(10);
                Assert.Equal("no_match", (string?)Stakes().Last()["reason"]);
            }
            finally { w.Close(); GameWindow.GoonStakes = null!; }
        });
    }
}
