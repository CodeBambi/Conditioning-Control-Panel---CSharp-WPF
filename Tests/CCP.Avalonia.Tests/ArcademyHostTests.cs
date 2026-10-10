using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Arcademy host, one test per frame family (WPF ArcademyHostService): page frame in,
/// expected frame or saved state out. Offline mode keeps every mirror (cards, wallet, presence) shut,
/// so the local till is what answers, and the save goes to a file the test names.</summary>
[Collection(RunsAloneCollection.Name)]   // names the process-wide meta path and flips OfflineMode
public sealed class ArcademyHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>Opens the campus on a throwaway save, runs the body, and puts every touched setting back.</summary>
    private static Task Campus(Action<Func<GameWindow>, List<JObject>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = CoreSettings.Current;
        bool offline = s.OfflineMode;
        double intensity = s.ArcademyMasterIntensity;
        string bag = s.ArcademySettingsJson, binds = s.ArcademyKeybindsJson;
        var open = new List<GameWindow>();
        var posted = new List<JObject>();
        GameWindow.ArcademyMetaPathOverride = Path.Combine(dir, "arcademy_meta.json");
        s.OfflineMode = true;
        try
        {
            body(() =>
            {
                var w = new GameWindow(GameWindow.Games["arcademy"]);
                w.Posted += json => posted.Add(JObject.Parse(json));
                w.Show();
                open.Add(w);
                w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
                return w;
            }, posted);
        }
        finally
        {
            foreach (var w in open) { try { w.Close(); } catch { } }
            GameWindow.ArcademyMetaPathOverride = null;
            s.OfflineMode = offline;
            s.ArcademyMasterIntensity = intensity;
            s.ArcademySettingsJson = bag;
            s.ArcademyKeybindsJson = binds;
            CoreSettings.SaveImmediate();
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });

    private static JObject Last(List<JObject> posted, string type) => posted.Last(p => (string?)p["type"] == type);

    [Fact]
    public Task Ready_GetsOneInitWithTheContractFields_ThenTheFullscreenState() => Campus((open, posted) =>
    {
        open();
        var init = posted.Single(p => (string?)p["type"] == "init");
        Assert.Equal(1, (int)init["protocol"]!);
        Assert.Equal("desktop", (string?)init["platform"]!["host"]);
        Assert.Equal("The Arcademy", (string?)init["lexicon"]!["arcademy"]);
        Assert.Equal("#FF69B4", (string?)init["palette"]!["pink"]);
        Assert.True(((JArray)init["economy"]!["catalog"]!).Count > 0);
        Assert.NotNull(init["economy"]!["payday"]);
        Assert.Equal(JTokenType.Object, init["meta"]!.Type);
        Assert.Equal(JTokenType.Object, init["settings"]!.Type);
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", (string?)init["subject"]!["code"]);
        Assert.False((bool)init["devDoor"]!);
        // The page's EMI keeps her own name in the page-owned meta: nothing host-side projects one.
        Assert.Null(init["emiName"]);
        var after = posted.SkipWhile(p => (string?)p["type"] != "init").Skip(1).First();
        Assert.Equal("fullscreen", (string?)after["type"]);
    });

    [Fact]
    public Task MetaCommand_SetsAndGets_AndRefusesAHostOwnedKey() => Campus((open, posted) =>
    {
        var w = open();
        w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"emiName\",\"value\":\"Dot\"}");
        Assert.Equal("Dot", (string?)Last(posted, "meta")["value"]);
        w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"get\",\"key\":\"emiName\"}");
        Assert.Equal("Dot", (string?)Last(posted, "meta")["value"]);
        // The wallet is host-owned: a page write is refused and the reply is what is stored.
        w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"wallet\",\"value\":{\"t\":99999,\"k\":99}}");
        Assert.NotEqual(99999, (int?)Last(posted, "meta")["value"]?["t"] ?? 0);
        Assert.NotEqual(99999, (int?)w.ArcademyMeta!.WalletSnapshot()["t"] ?? 0);
    });

    [Fact]
    public Task ClassEnded_PaysOncePerDay_RecordsAttendance_AndARetakeIsFree() => Campus((open, posted) =>
    {
        var w = open();
        w.HandleMessage("{\"type\":\"class-started\",\"gameKey\":\"echo\",\"gradeTier\":1,\"lever\":\"honors\"}");
        Assert.True(w.InRun);
        // S+ is claimed, but the Honors lever is not unlocked on a fresh save: graded S, never trusted.
        w.HandleMessage("{\"type\":\"class-ended\",\"gameKey\":\"echo\",\"gradeTier\":1,\"grade\":\"S+\",\"flavorXp\":999}");
        Assert.False(w.InRun);
        var pay = Last(posted, "payout-result");
        Assert.Equal("S", (string?)pay["grade"]);
        Assert.Equal(40 * 1.5 + 15, (double)pay["xp"]!);   // tier 1 x S, flavour bonus capped at 15
        Assert.False((bool)pay["retake"]!);
        Assert.Equal(1, (int)pay["streak"]!);
        Assert.Equal(1, (int)pay["classesToday"]!);
        Assert.Equal("standard", (string?)pay["lever"]);
        int tickets = (int)pay["tickets"]!;
        Assert.True(tickets > 0);
        Assert.Equal(tickets, (int)pay["wallet"]!["t"]!);
        Assert.True((bool)pay["tokenMinted"]!);   // the first S of the day

        w.HandleMessage("{\"type\":\"class-ended\",\"gameKey\":\"echo\",\"gradeTier\":1,\"grade\":\"S\"}");
        var again = Last(posted, "payout-result");
        Assert.True((bool)again["retake"]!);
        Assert.Equal(0, (double)again["xp"]!);
        Assert.False((bool)again["tokenMinted"]!);

        // A garbled frame still records the class (grade degrades to C, tier to 1).
        w.HandleMessage("{\"type\":\"class-ended\",\"gameKey\":\"sort\",\"gradeTier\":{},\"grade\":7}");
        Assert.Equal("C", (string?)Last(posted, "payout-result")["grade"]);
        Assert.Equal(2, (int)Last(posted, "payout-result")["classesToday"]!);
    });

    [Fact]
    public Task Progress_SurvivesARestart_InTheWpfFile() => Campus((open, posted) =>
    {
        var w = open();
        w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"lockerOutfit\",\"value\":\"varsity\"}");
        w.HandleMessage("{\"type\":\"class-ended\",\"gameKey\":\"echo\",\"gradeTier\":2,\"grade\":\"A\"}");
        int tickets = (int)Last(posted, "payout-result")["wallet"]!["t"]!;
        w.Close();   // the close flushes the debounced save

        var saved = JObject.Parse(File.ReadAllText(GameWindow.ArcademyMetaPathOverride!));
        Assert.Equal(1, (int)saved["streak"]!);
        Assert.Equal(tickets, (int)saved["wallet"]!["t"]!);

        posted.Clear();
        open();
        var meta = posted.Single(p => (string?)p["type"] == "init")["meta"]!;
        Assert.Equal("varsity", (string?)meta["lockerOutfit"]);
        Assert.Equal(1, (int)meta["streak"]!);
        Assert.Equal(tickets, (int)meta["wallet"]!["t"]!);
    });

    [Fact]
    public Task PrizeBuy_WithAnEmptyWallet_IsRefused_AndNothingIsOwned() => Campus((open, posted) =>
    {
        var w = open();
        w.HandleMessage("{\"type\":\"prize-buy\",\"sku\":\"tube_midnight\"}");
        var r = Last(posted, "wallet-result");
        Assert.False((bool)r["ok"]!);
        Assert.Equal("tube_midnight", (string?)r["sku"]);
        Assert.False(w.ArcademyMeta!.WalletOwns("tube_midnight"));
        w.HandleMessage("{\"type\":\"prize-buy\",\"sku\":\"no_such_prize\"}");
        Assert.False((bool)Last(posted, "wallet-result")["ok"]!);
    });

    [Fact]
    public Task EnrollmentDone_MintsThePunchCard_Once() => Campus((open, posted) =>
    {
        var w = open();
        w.HandleMessage("{\"type\":\"enrollment-done\",\"gameKey\":\"echo\"}");
        var card = Last(posted, "punchcard-result");
        Assert.Equal("enrollment", (string?)card["reason"]);
        Assert.Equal("echo", (string?)card["gameKey"]);
        Assert.Contains("echo", w.ArcademyMeta!.EnrolledGameKeys());
    });

    [Fact]
    public Task SetSetting_EchoesWhatIsStored_AndThePrizeGateHoldsTheWideBoard() => Campus((open, posted) =>
    {
        var w = open();
        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"masterIntensity\",\"value\":0.4}");
        var echo = Last(posted, "setting");
        Assert.Equal("masterIntensity", (string?)echo["key"]);
        Assert.Equal(CoreSettings.Current.ArcademyMasterIntensity, (double)echo["value"]!);

        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"echo_speed\",\"value\":\"fast\"}");
        Assert.Equal("fast", (string?)Last(posted, "setting")["value"]);
        Assert.Contains("echo_speed", CoreSettings.Current.ArcademySettingsJson);

        // The wide board is a prize nobody has bought here: refused, and the echo is not the asked value.
        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"de_board_size\",\"value\":\"5x5\"}");
        Assert.NotEqual("5x5", (string?)Last(posted, "setting")["value"]);
        Assert.DoesNotContain("5x5", CoreSettings.Current.ArcademySettingsJson);

        // Keybinds must be an object: a string is refused and the stored binds are kept.
        string before = CoreSettings.Current.ArcademyKeybindsJson;
        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"keybinds\",\"value\":\"oops\"}");
        Assert.Equal(before, CoreSettings.Current.ArcademyKeybindsJson);
    });

    [Fact]
    public Task FullscreenRequest_AndTheUnportedMediaFrames_AlwaysGetOneReply() => Campus((open, posted) =>
    {
        var w = open();
        posted.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-request\",\"on\":false}");
        Assert.Equal("fullscreen", (string?)posted.Single()["type"]);

        w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"r1\",\"count\":4,\"kind\":\"still\"}");
        var assets = Last(posted, "assets");
        Assert.Equal("r1", (string?)assets["reqId"]);
        Assert.True((bool)assets["done"]!);
        Assert.Empty((JArray)assets["urls"]!);

        w.HandleMessage("{\"type\":\"share-image\",\"png\":\"x\"}");
        Assert.False((bool)Last(posted, "share-image-result")["ok"]!);

        // Offline: the registry link answers with body null, never silence.
        w.HandleMessage("{\"type\":\"annex-stats\"}");
        Assert.Equal(JTokenType.Null, Last(posted, "annex-stats")["body"]!.Type);
    });
}
