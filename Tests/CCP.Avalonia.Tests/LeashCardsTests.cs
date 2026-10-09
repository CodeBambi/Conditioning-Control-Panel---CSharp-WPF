using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Ledger social#1 (wave B6): the Core LeashService on this head draws real cards on
/// Social &gt; Leash, the launcher gate reads it, and the cut runs the safety steps first.</summary>
public sealed class LeashCardsTests
{
    private const string Block = """
    {"me":{"holder":{"id":"h1","name":"Vex"},"intensity":"standard","since":"2026-10-01T00:00:00Z","day":3,
      "pending":[{"pid":"p1","kind":"lines","size":5,"from":{"id":"h1","name":"Vex"},"at":"2026-10-09T00:00:00Z","expires_at":"2099-10-12T00:00:00Z"}],
      "pardons":1,"stickers":[]},
     "holding":[],
     "offers":[{"id":"o1","from":{"id":"f2","name":"Ash"},"at":"2026-10-09T00:00:00Z","expires_at":"2099-10-12T00:00:00Z"}]}
    """;

    private sealed class Api : ILeashApi
    {
        public string? LastOp;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            LastOp = op;
            return Task.FromResult<JObject?>(new JObject { ["ok"] = true });
        }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task LeashedAccount_GetsOfferSelfCardAndGate_AndTheCutRunsSafetyFirst()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            int safety = 0;
            var api = new Api();
            var svc = new LeashService(api, () => "me", cutSafety: () => safety++);
            svc.ApplyBlock(JObject.Load(new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(Block)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None }));   // as FriendsApi.Read: dates stay strings
            Assert.NotNull(svc.Snapshot.Me);
            Assert.Equal("p1", svc.GateDue?.Pid);

            var page = new LeashTabView { Resolve = () => svc };
            page.Rebuild();
            Assert.False(page.ShowingEmpty);
            // WPF: the page hosts a LeashDrawerSection (offer row, own card); the gate stand-in follows.
            var tags = page.SectionHost.Children.Select(c => c.Tag as string).ToList();
            Assert.Equal(new[] { "leash-section", "leash-gate-standin" }, tags);
            Assert.Equal(new[] { "leash-offer-row:f2", "leash-self-card" }, page.Section.Children.Select(c => c.Tag as string).ToList());
            var buttons = page.SectionHost.GetLogicalDescendants().OfType<Button>().Select(b => b.Tag as string).ToList();
            Assert.Contains("leash-cut", buttons);
            Assert.Contains("leash-gate-pardon", buttons);
            Assert.Contains("leash-offer-look", buttons);   // Look opens the ask card (Put it on lives there)
            Assert.Contains("leash-help:leashed", buttons);

            var (oldDue, oldPresent, oldSvc) = (MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider, LeashHead.Service);
            try
            {
                LeashHead.Service = svc;
                MainShellWindow.LeashGateDueProvider = () => LeashHead.Service?.GateDue != null;
                Assert.True(MainShellWindow.LeashBlocksGames);
                Assert.True(LeashHead.IsLeashed);

                await svc.CutAsync();
                Assert.Equal(1, safety);
                Assert.Null(svc.Snapshot.Me);
                Assert.Equal("cut", api.LastOp);
                Assert.False(MainShellWindow.LeashBlocksGames);
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(page.Section.Children, c => (c.Tag as string) == "leash-self-card");
                Assert.DoesNotContain(page.SectionHost.Children, c => (c.Tag as string) == "leash-gate-standin");
            }
            finally
            {
                (MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider, LeashHead.Service) = (oldDue, oldPresent, oldSvc);
            }
        });
    }

    [Fact]
    public void CutSafety_OnThisHead_EndsLockdownAndRemoteBeforeItReleasesTheSettings()
    {
        var order = new System.Collections.Generic.List<string>();
        var fake = new Targets(order);
        var done = LeashCutSafety.Apply(fake);
        Assert.Equal(new[] { "lockdown", "remote", "video", "settings" }, done);
        Assert.Equal(new[] { "EndLockdown", "Discard", "EndRemote", "StopVideo", "Release" }, order);

        LeashHead.Seed();
        Assert.Same(LeashHead.Targets.Instance, LeashCutSafety.LiveTargets);
    }

    private sealed class Targets(System.Collections.Generic.List<string> log) : LeashCutSafety.ITargets
    {
        public bool LockdownActive => true;
        public void EndLockdown() => log.Add("EndLockdown");
        public void DiscardLockdownRecovery() => log.Add("Discard");
        public bool RemoteActive => true;
        public void EndRemote() => log.Add("EndRemote");
        public bool StrictVideoRunning => true;
        public void StopVideo() => log.Add("StopVideo");
        public void ReleaseSafetySettings() => log.Add("Release");
        public bool DropUnpushedLeashBookings() => false;
    }
}
