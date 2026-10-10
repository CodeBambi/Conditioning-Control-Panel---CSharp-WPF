using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.JustDrop;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane k9 (HA1, HC9): the For You, Just Drop and web app doors open what WPF 7.1.5
/// OpenExclusiveFeature / ShowTab open, from every door (Premium card, Play card, the Home mystery
/// tile's free day, the palette row, EMI's ring), and are refused with the WPF rule: For You asks
/// for Basic at the door, Just Drop exists only while the server's flag does and never asks for a tier.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PremiumDoorsTests
{
    private sealed class Probe
    {
        public bool Premium, FreeFyp;
        public readonly List<TierVerdict> Denied = new();
        public readonly List<(double Xp, string Source)> Xp = new();
        public int WebApp;
    }

    private static void Run(Action<MainShellWindow, Probe> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var probe = new Probe();
            var oldRing = EmiTargets.OpenWindowDoor;
            var old = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider,
                CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler, CoreProgression.AddXPProvider,
                MainShellWindow.JustDropKickoff, MainShellWindow.WebAppDoor);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasPremiumProvider = () => probe.Premium;
            CoreEntitlement.HasLabProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = key => probe.FreeFyp && key == "fyp";
            CoreEntitlement.ShowDeniedHandler = v => probe.Denied.Add(v);
            CoreProgression.AddXPProvider = (xp, source) => probe.Xp.Add((xp, source));
            MainShellWindow.JustDropKickoff = () => Task.CompletedTask;   // never the network
            MainShellWindow.WebAppDoor = _ => probe.WebApp++;
            JustDropService.SetServerEnabledForTests(false);
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                // The ring's Nav reaches the panel through the desktop lifetime, which a headless test has none of.
                EmiTargets.OpenWindowDoor = key => shell.ShowTab(key);
                body(shell, probe);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();
                JustDropService.SetServerEnabledForTests(false);
                EmiTargets.OpenWindowDoor = oldRing;
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider,
                    CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler, CoreProgression.AddXPProvider,
                    MainShellWindow.JustDropKickoff, MainShellWindow.WebAppDoor) = old;
                shell.Close();
                Dispatcher.UIThread.RunJobs();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static GameWindow[] OpenWindows()
    {
        var list = (List<GameWindow>)typeof(GameWindow)
            .GetField("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        lock (list) return list.ToArray();
    }

    private static string[] OpenIds() => OpenWindows().Select(w => w.Spec.Id).ToArray();

    // ---- For You ------------------------------------------------------------------------------

    [Fact]
    public void ForYou_PremiumCard_OpensTheFeed_AndTheTabStays() => Run((shell, probe) =>
    {
        probe.Premium = true;
        shell.ShowTab("premium");
        Dispatcher.UIThread.RunJobs();
        var before = shell.CurrentTab;
        Assert.True(ExclusivesTabView.IsOnThisBuild("fyp"));
        shell.OpenExclusiveFeature("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("fyp", OpenIds().Single());
        Assert.Equal(before, shell.CurrentTab);
        Assert.Empty(probe.Denied);
        Assert.True(FypHostService.IsActive);

        // A second click refocuses the live window, never a second feed.
        shell.ShowTab("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(OpenIds());
    });

    [Fact]
    public void ForYou_FreeAccount_IsRefusedOutLoud_FromEveryDoor() => Run((shell, probe) =>
    {
        shell.OpenExclusiveFeature("fyp");                       // the Premium card
        shell.ShowTab("fyp");                                    // the palette row / the mystery tile
        shell.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        var play = shell.GetVisualDescendants().OfType<PlayTabView>().First();
        play.FindControl<Button>("BtnPlayFyp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the Play card
        EmiTargets.Find("fyp")!.Open();                          // EMI's ring
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(OpenIds());
        Assert.Equal(4, probe.Denied.Count);                     // the WPF refusal, once per door
        Assert.All(probe.Denied, v => Assert.False(v.Allowed));
        Assert.All(probe.Denied, v => Assert.False(string.IsNullOrWhiteSpace(v.Reason)));
    });

    [Fact]
    public void ForYou_TodaysFreeDay_OpensForAFreeAccount() => Run((shell, probe) =>
    {
        probe.FreeFyp = true;                                    // the Home mystery tile rolled "fyp"
        Assert.Contains("fyp", SettingsTabView.MysteryKeys);
        shell.ShowTab("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("fyp", OpenIds().Single());
        Assert.Empty(probe.Denied);
    });

    [Fact]
    public void ForYou_PlayCardAndEmiRing_OpenTheFeed() => Run((shell, probe) =>
    {
        probe.Premium = true;
        shell.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        var play = shell.GetVisualDescendants().OfType<PlayTabView>().First();
        play.FindControl<Button>("BtnPlayFyp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("fyp", OpenIds().Single());
        GameWindow.CloseFyp();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(OpenIds());
        Assert.False(FypHostService.IsActive);

        var door = EmiTargets.Find("fyp");
        Assert.NotNull(door);
        Assert.True(door!.IsAvailable());
        Assert.False(door.IsLocked());
        door.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("fyp", OpenIds().Single());

        // The palette row is a plain tab key: ShowTab opens the window.
        Assert.Contains(SettingsPaletteIndex.All, e => e.TabKey == "fyp");
    });

    // ---- Just Drop ----------------------------------------------------------------------------

    [Fact]
    public void JustDrop_ShutDoor_OpensNothing_AndShowsNoCardRowOrRingDoor() => Run((shell, probe) =>
    {
        probe.Premium = true;                                    // a tier never opens this door
        shell.ShowTab("premium");
        Dispatcher.UIThread.RunJobs();
        var before = shell.CurrentTab;
        shell.ShowTab("justdrop");
        shell.OpenExclusiveFeature("justdrop");
        JustDropHostService.LaunchShop();                        // even the host entry refuses
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(OpenIds());
        Assert.Empty(probe.Denied);                              // a no-op, never a tier prompt
        Assert.Equal(before, shell.CurrentTab);

        Assert.False(ExclusiveFeature.All.Single(f => f.Key == "justdrop").Shown());
        Assert.False(SettingsPaletteIndex.All.Single(e => e.Id == "door.justdrop").Available);
        Assert.False(EmiTargets.Find("justdrop")!.IsAvailable());
    });

    [Fact]
    public void JustDrop_OpenDoor_OpensTheShopForAFreeAccount_NeverAPrimeCard() => Run((shell, probe) =>
    {
        JustDropService.SetServerEnabledForTests(true);
        Dispatcher.UIThread.RunJobs();

        // The card: on the Free shelf, tier 0, never locked, no badge of any tier.
        var feature = ExclusiveFeature.All.Single(f => f.Key == "justdrop");
        Assert.True(feature.Shown());
        Assert.Equal(0, feature.Tier);
        Assert.Equal(ConditioningControlPanel.Services.UI.PremiumGroup.Free,
            ConditioningControlPanel.Services.UI.PremiumShelfOrder.GroupOf(feature.Key, feature.Tier));
        Assert.NotEqual(ExclusiveGateState.Locked, feature.GateState());
        Assert.True(ExclusivesTabView.IsOnThisBuild("justdrop"));
        Assert.True(SettingsPaletteIndex.All.Single(e => e.Id == "door.justdrop").Available);
        var ring = EmiTargets.Find("justdrop")!;
        Assert.True(ring.IsAvailable());
        Assert.False(ring.IsLocked());

        shell.ShowTab("premium");
        Dispatcher.UIThread.RunJobs();
        var before = shell.CurrentTab;
        shell.OpenExclusiveFeature("justdrop");                  // the Premium card (a free account)
        Dispatcher.UIThread.RunJobs();
        var w = OpenWindows().Single();
        Assert.Equal("justdrop", w.Spec.Id);
        Assert.Empty(probe.Denied);
        Assert.Equal(before, shell.CurrentTab);
        Assert.True(JustDropHostService.IsActive);

        // The live site, on its own origin, and never a ccp.* host.
        Assert.Equal("app.cclabs.app", w.PageUrl!.Host);
        Assert.Equal(Uri.UriSchemeHttps, w.PageUrl.Scheme);
        Assert.True(w.Web.AllowNavigation!(new Uri("https://app.cclabs.app/express/play?order=A")));
        Assert.False(w.Web.AllowNavigation!(new Uri("https://evil.example/")));
        Assert.False(w.Web.AllowNavigation!(new Uri("https://ccp.game/fyp/index.html")));
        Assert.False(w.Web.AllowNavigation!(new Uri("http://app.cclabs.app/dashboard/express")));

        // The credential rule: nothing but the handoff request can carry a header.
        Assert.NotNull(w.Web.RequestHeader);
        Assert.Null(w.Web.RequestHeader!(new Uri("https://app.cclabs.app/dashboard/express")));
        Assert.Null(w.Web.RequestHeader!(new Uri("https://evil.example/api/auth/desktop-session")));

        // The tease tile, the palette row and EMI's ring all land on the same single window.
        shell.ShowTab("justdrop");
        ring.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(OpenIds());
    });

    [Fact]
    public void JustDrop_PageFramesGoToTheBridge_AndTheSiteNeverGetsTheGameInit() => Run((shell, probe) =>
    {
        JustDropService.SetServerEnabledForTests(true);
        var file = Path.Combine(Path.GetTempPath(), "k9-head-credited-" + Guid.NewGuid().ToString("N") + ".json");
        var prevFile = CreditedOrders.FilePathOverride;
        CreditedOrders.FilePathOverride = file;
        try
        {
            var w = GameWindow.LaunchJustDropShop()!;
            Dispatcher.UIThread.RunJobs();
            var posted = new List<string>();
            w.Posted += posted.Add;

            w.HandleMessage("{\"type\":\"ready\"}");                                   // the site's own traffic
            w.HandleMessage("{\"source\":\"justdrop\",\"v\":1,\"type\":\"ready\"}");
            Assert.Empty(posted);                                                       // no init, no manifest
            Assert.Empty(probe.Xp);

            w.HandleMessage("{\"source\":\"justdrop\",\"v\":1,\"type\":\"session-complete\",\"payload\":{\"orderCode\":\"K9-1\",\"sizeId\":\"XXL\",\"durationSec\":3600}}");
            Assert.Equal(JustDropService.QuickTasteXp, JustDropService.LastAwardedXp);   // no host clock: a taste
            Assert.Equal("Other", probe.Xp.Single().Source);

            w.HandleMessage("{\"source\":\"justdrop\",\"v\":1,\"type\":\"session-complete\",\"payload\":{\"orderCode\":\"K9-1\",\"sizeId\":\"XXL\"}}");
            Assert.Single(probe.Xp);                                                    // a replay pays nothing
            Assert.Empty(posted);
        }
        finally
        {
            CreditedOrders.FilePathOverride = prevFile;
            try { File.Delete(file); } catch { }
        }
    });

    // ---- web app ------------------------------------------------------------------------------

    [Fact]
    public void WebApp_Key_OpensTheWebAppDoor_AndTheTabStays() => Run((shell, probe) =>
    {
        shell.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        shell.ShowTab("webapp");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, probe.WebApp);
        Assert.Equal("play", shell.CurrentTab);
        Assert.Empty(OpenIds());
    });

    // ---- panic --------------------------------------------------------------------------------

    [Fact]
    public void Panic_ClosesTheFeedAndTheShop() => Run((shell, probe) =>
    {
        probe.Premium = true;
        JustDropService.SetServerEnabledForTests(true);
        shell.ShowTab("fyp");
        shell.ShowTab("justdrop");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "fyp", "justdrop" }, OpenIds().OrderBy(x => x));

        var games = PanicSurfaces.All.Single(s => s.Id == "games");
        Assert.True(games.OwnsTheScreen!());                     // a press that ends them is not an exit press
        games.Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(OpenIds());
        Assert.False(FypHostService.IsActive);
        Assert.False(JustDropHostService.IsActive);
    });

    // ---- the For You page's frames ---------------------------------------------------------------

    [Fact]
    public void ForYou_Frames_InitSettingsXpAndTheUnavailableAnswers() => Run((shell, probe) =>
    {
        probe.Premium = true;
        var stats = Path.Combine(Path.GetTempPath(), "k9-head-stats-" + Guid.NewGuid().ToString("N") + ".json");
        var prevStats = FypHostService.StatsFilePathOverride;
        FypHostService.StatsFilePathOverride = stats;
        FypHostService.ResetClipXpForTests();
        var s = CoreSettings.Current;
        try
        {
            var w = GameWindow.Launch(GameWindow.FypId)!;
            Dispatcher.UIThread.RunJobs();
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));

            w.HandleMessage("{\"type\":\"ready\"}");
            var init = posted.Single();
            Assert.Equal("init", (string?)init["type"]);
            Assert.NotNull(init["assets"]);
            Assert.NotNull(init["settings"]!["layout"]);
            Assert.False((bool)init["settings"]!["eyeControl"]!);      // nothing drives it on this head
            Assert.NotEmpty((JArray)init["online"]!["niches"]!);
            Assert.DoesNotContain(posted, p => (string?)p["type"] == "manifest");   // its own init, not the games'

            // A plain setting is stored.
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"layout\",\"value\":\"trio\"}");
            Assert.Equal("trio", s.FypLayout);

            // Ghost mode: WPF's own "could not compose" answer, so the toggle snaps back.
            posted.Clear();
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
            Assert.Equal(new[] { "clickThrough", "ghost-unavailable" }, posted.Select(p => (string?)p["type"]));
            Assert.False((bool)posted[0]["on"]!);

            // Eye control: refused with the WPF reason, and the setting goes back off.
            posted.Clear();
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
            Assert.Equal("eyeStatus", (string?)posted.Single()["type"]);
            Assert.Equal("no-camera", (string?)posted[0]["reason"]);
            Assert.False((bool)posted[0]["enabled"]!);
            Assert.False(s.FypEyeControl);

            // XP: 5 a clip, capped a minute; 15 an attention hit; always the Fyp source.
            for (int i = 0; i < FypHostService.MaxClipXpPerMinute + 5; i++)
                w.HandleMessage("{\"type\":\"clip-viewed\",\"segId\":\"videos/a.mp4#0\",\"dwellMs\":4000}");
            Assert.Equal(FypHostService.MaxClipXpPerMinute, probe.Xp.Count);
            Assert.All(probe.Xp, x => Assert.Equal((5.0, "Fyp"), x));
            w.HandleMessage("{\"type\":\"attention-hit\"}");
            Assert.Equal((15.0, "Fyp"), probe.Xp[^1]);

            // Stats are the page's blob, kept verbatim.
            w.HandleMessage("{\"type\":\"stats-save\",\"stats\":{\"clips\":7}}");
            Assert.Equal(7, (int)FypHostService.LoadStats()!["clips"]!);

            // The file menu never resolves a remote id, a traversal or a non-media file.
            foreach (var id in new[] { "scrolller/x/1", "videos/../../x.mp4", "videos/a.exe" })
            {
                w.HandleMessage("{\"type\":\"file-menu\",\"id\":\"" + id + "\"}");
                Assert.Null(w.FypLastMenuPath);
            }

            // close is the shell's: the window goes.
            w.HandleMessage("{\"type\":\"close\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(OpenIds());
        }
        finally
        {
            FypHostService.StatsFilePathOverride = prevStats;
            FypHostService.ResetClipXpForTests();
            try { File.Delete(stats); } catch { }
        }
    });

    [Fact]
    public void ForYou_RemoteFrames_NeedConsentAndANonLibrarySource()
    {
        GameWindow? w = null;
        var posted = new List<JObject>();
        int fetches = 0, probes = 0;
        Run((shell, probe) =>
        {
            probe.Premium = true;
            var s = CoreSettings.Current;
            Assert.False(s.HasRemoteMediaConsent);                     // a fresh profile
            w = GameWindow.Launch(GameWindow.FypId)!;
            Dispatcher.UIThread.RunJobs();
            w.Posted += json => posted.Add(JObject.Parse(json));
            w.FypFetchOverride = _ =>
            {
                fetches++;
                return Task.FromResult(new FypOnlineCoordinator.FeedBatch(
                    new() { new FypAssetManifest.Entry { Id = "scrolller/hypno/1", Url = "https://cdn.example/1.mp4" } }, null, false, 50));
            };
            w.FypProbeOverride = (_, _) => { probes++; return Task.FromResult(new SubProbe { Ok = true, VideoCount = 9 }); };

            // No consent: nothing is fetched, nothing is probed, whatever the page asks.
            w.ServeFypRemoteBatch().GetAwaiter().GetResult();
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"source\",\"value\":\"online\"}");
            Assert.Equal("library", s.FypSource);
            w.ServeFypRemoteBatch().GetAwaiter().GetResult();
            w.ProbeFypSub("hypno").GetAwaiter().GetResult();
            Assert.Equal(0, fetches);
            Assert.Equal(0, probes);
            Assert.Equal("consent", (string?)posted.Single(p => (string?)p["type"] == "sub-probe")["error"]);

            // A name that is not a subreddit is answered without a request.
            posted.Clear();
            w.ProbeFypSub("!!!").GetAwaiter().GetResult();
            Assert.Equal("invalid", (string?)posted.Single()["error"]);

            // Consent given but the source is still the library: still nothing.
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"onlineConsented\",\"value\":true}");
            Assert.True(s.HasRemoteMediaConsent);
            w.ServeFypRemoteBatch().GetAwaiter().GetResult();
            Assert.Equal(0, fetches);

            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"source\",\"value\":\"online\"}");
            Assert.Equal("online", s.FypSource);
            posted.Clear();
        });
        Assert.NotNull(w);
    }

    [Fact]
    public async Task ForYou_ConsentedRemoteBatch_AppendsThenReportsStatus()
    {
        var posted = new List<JObject>();
        int fetches = 0;
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.FypOnlineConsented = true;
            s.FypSource = "online";
            var w = new GameWindow(GameWindow.Games[GameWindow.FypId]);
            w.Posted += json => posted.Add(JObject.Parse(json));
            w.FypFetchOverride = _ =>
            {
                Interlocked.Increment(ref fetches);
                return Task.FromResult(new FypOnlineCoordinator.FeedBatch(
                    new() { new FypAssetManifest.Entry { Id = "scrolller/hypno/1", Url = "https://cdn.example/1.mp4" } }, null, false, 50));
            };
            w.Show();
            try
            {
                Assert.True(FypHostService.RemoteAllowed(s));
                await w.ServeFypRemoteBatch();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, fetches);
                Assert.Equal(new[] { "assets-append", "online-status" }, posted.Select(p => (string?)p["type"]));
                Assert.Equal("scrolller/hypno/1", (string?)posted[0]["assets"]![0]!["id"]);
                Assert.True((bool)posted[1]["ok"]!);
            }
            finally
            {
                w.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }
}
