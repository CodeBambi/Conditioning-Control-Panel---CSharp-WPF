using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The "Get it" row against WPF Controls/V2GetItRow.xaml.cs: the row draws the rule's state, a press asks
/// first, "no" sends nothing, "yes" sends one buy at the price shown, and a signed-out press opens the way
/// in. The wallet write (<see cref="V2PurchaseService.AdoptIntoWallet"/>) keeps the wallet rule: only a
/// debited receipt lowers, a snapshot only raises, another account's reply is dropped.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class V2GetItRowTests
{
    private sealed class FakeRelay : IBackRoomRelay
    {
        private readonly object _gate = new();
        public readonly List<(string Op, string? Idem, JObject? Body)> Calls = new();
        public BackRoomStationResult Buy = new(true, 200, null, new JObject { ["ok"] = true, ["sp"] = 70 });
        public int Price = 30;

        public int CountOf(string op) { lock (_gate) return Calls.FindAll(c => c.Op == op).Count; }

        public Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body, CancellationToken ct = default)
        {
            lock (_gate) Calls.Add((op, idem, body));
            if (op == "buy") return Task.FromResult(Buy);
            return Task.FromResult(new BackRoomStationResult(true, 200, null, new JObject
            {
                ["ok"] = true, ["open"] = true, ["catalogVersion"] = 2, ["sp"] = 100,
                ["catalog"] = new JArray { new JObject { ["id"] = "flashes_v2", ["priceSp"] = Price, ["sale"] = "on" } },
            }));
        }
    }

    private static void Setup()
    {
        if (global::Avalonia.Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    [Fact]
    public Task APressAsksThenBuysOnceAtThePriceShown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var relay = new FakeRelay();
        string? account = "u_one";
        var owned = false;
        var sp = 100;
        var adopted = new List<(string Account, bool FromBuy, int Sp)>();
        var svc = new V2PurchaseService(relay, () => account, _ => owned, () => sp,
            (a, fromBuy, n) => { lock (adopted) adopted.Add((a, fromBuy, n)); });

        var questions = new List<string>();
        var toasts = new List<string>();
        var signIns = 0;
        var answer = false;
        var row = new V2GetItRow { ServiceOverride = svc };
        row.Ask = (_, title, message) => { questions.Add(title + "|" + message); return Task.FromResult(answer); };
        row.SignIn = _ => signIns++;
        row.Toast = toasts.Add;
        var changes = 0;
        row.RowChanged += (_, _) => changes++;
        row.Configure(V2PurchaseRule.FlashesPrizeId, "v2_get_flash_blurb", "section_flash_v2");

        var status = row.FindControl<TextBlock>("TxtStatus")!;
        var price = row.FindControl<TextBlock>("TxtPrice")!;
        var plate = row.FindControl<StackPanel>("PricePlate")!;
        var button = row.FindControl<Button>("BtnGet")!;
        var label = row.FindControl<TextBlock>("TxtGet")!;
        Assert.Equal(Loc.Get("v2_get_flash_blurb"), row.FindControl<TextBlock>("TxtBlurb")!.Text);

        // The counter has not answered: no price, no press.
        Assert.False(row.IsRowHidden);
        Assert.Equal(Loc.Get("v2_get_checking"), status.Text);
        Assert.False(plate.IsVisible);
        Assert.False(button.IsVisible);
        Assert.False(await row.PressAsync());
        Assert.Empty(questions);

        // On screen: the row reads the counter by itself, once, and listens to the service.
        var w = new Window { Width = 400, Height = 200, Content = row };
        w.Show();
        try
        {
        await Until(() => svc.RowFor(V2PurchaseRule.FlashesPrizeId).State == V2PurchaseRowState.Offer);
        row.Apply();
        Assert.Equal(1, relay.CountOf("state"));
        Assert.True(plate.IsVisible);
        Assert.Equal("30", price.Text);
        Assert.True(button.IsVisible && button.IsEnabled);
        Assert.Equal(Loc.Get("v2_get_button"), label.Text);
        Assert.Equal(string.Empty, status.Text);
        // A state read is a snapshot: handed on as "not a buy".
        Assert.Contains(("u_one", false, 100), adopted);

        // "No" sends nothing.
        Assert.False(await row.PressAsync());
        Assert.Single(questions);
        Assert.Equal(Loc.Get("v2_get_confirm_title") + "|" + Loc.GetF("v2_get_confirm_body", 30, Loc.Get("section_flash_v2")), questions[0]);
        Assert.Equal(0, relay.CountOf("buy"));

        // Short of the price: the row says by how much and the button is dead.
        sp = 12;
        row.Apply();
        Assert.Equal(Loc.GetF("v2_get_short", 18), status.Text);
        Assert.True(button.IsVisible);
        Assert.False(button.IsEnabled);
        Assert.False(await row.PressAsync());
        Assert.Single(questions);
        sp = 100;

        // "Yes" sends one buy, with the version the counter named, and the receipt is handed on as a buy.
        answer = true;
        Assert.True(await row.PressAsync());
        Assert.Equal(1, relay.CountOf("buy"));
        var buy = relay.Calls.Find(c => c.Op == "buy");
        Assert.Equal("flashes_v2", buy.Body!.Value<string>("prizeId"));
        Assert.Equal(2, buy.Body.Value<int>("catalogVersion"));
        Assert.False(string.IsNullOrEmpty(buy.Idem));
        Assert.Contains(("u_one", true, 70), adopted);
        await Until(() => toasts.Count == 1);
        Assert.Equal(Loc.GetF("v2_get_toast_done", Loc.Get("section_flash_v2")), toasts[0]);

        // Owned: the row hides and tells its box.
        owned = true;
        var before = changes;
        row.Apply();
        Assert.True(row.IsRowHidden);
        Assert.False(row.IsVisible);
        Assert.True(changes > before);
        Assert.False(await row.PressAsync());
        Assert.Equal(1, relay.CountOf("buy"));

        // Signed out: the button is the way in and spends nothing.
        owned = false;
        account = null;
        row.Apply();
        Assert.Equal(Loc.Get("v2_get_signin"), status.Text);
        Assert.Equal(Loc.Get("v2_get_signin_button"), label.Text);
        Assert.True(button.IsVisible && button.IsEnabled);
        Assert.False(plate.IsVisible);
        Assert.False(await row.PressAsync());
        Assert.Equal(1, signIns);
        Assert.Equal(1, relay.CountOf("buy"));
        }
        finally { w.Close(); }
    });

    [Fact]
    public Task ARepriceBetweenTheConfirmAndTheRequestRefuses() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var relay = new FakeRelay();
        var svc = new V2PurchaseService(relay, () => "u_one", _ => false, () => 100);
        var row = new V2GetItRow { ServiceOverride = svc };
        row.SignIn = _ => { };
        row.Toast = _ => { };
        // Access is re-checked on the press: the counter reprices while the question is up.
        row.Ask = async (_, _, _) =>
        {
            relay.Price = 45;
            svc.Refresh();
            await Until(() => svc.RowFor(V2PurchaseRule.FlashesPrizeId).PriceSp == 45);
            return true;
        };
        row.Configure(V2PurchaseRule.FlashesPrizeId, "v2_get_flash_blurb", "section_flash_v2");
        svc.EnsureState();
        await Until(() => svc.RowFor(V2PurchaseRule.FlashesPrizeId).State == V2PurchaseRowState.Offer);

        Assert.False(await row.PressAsync());
        Assert.Equal(0, relay.CountOf("buy"));
        row.Apply();
        Assert.Equal(Loc.Get("v2_get_error_changed"), row.FindControl<TextBlock>("TxtStatus")!.Text);
        Assert.Equal("45", row.FindControl<TextBlock>("TxtPrice")!.Text);
    });

    [Fact]
    public void TheWalletWriteKeepsTheWalletRule()
    {
        var s = new AppSettings { SkillPoints = 100 };
        // A snapshot below the wallet (a state read, or a refusal that carries sp) never lowers it.
        Assert.False(V2PurchaseService.AdoptIntoWallet(s, "u_one", "u_one", fromBuy: false, 60));
        Assert.Equal(100, s.SkillPoints);
        // A snapshot above it raises.
        Assert.True(V2PurchaseService.AdoptIntoWallet(s, "u_one", "u_one", fromBuy: false, 130));
        Assert.Equal(130, s.SkillPoints);
        // A debited receipt is the server's word and lowers.
        Assert.True(V2PurchaseService.AdoptIntoWallet(s, "u_one", "u_one", fromBuy: true, 70));
        Assert.Equal(70, s.SkillPoints);
        // Another account signed in by the time the write runs: dropped, receipt or not.
        Assert.False(V2PurchaseService.AdoptIntoWallet(s, "u_two", "u_one", fromBuy: true, 5));
        Assert.False(V2PurchaseService.AdoptIntoWallet(s, null, "u_one", fromBuy: true, 5));
        Assert.Equal(70, s.SkillPoints);
        // A negative balance is not a balance.
        Assert.False(V2PurchaseService.AdoptIntoWallet(s, "u_one", "u_one", fromBuy: true, -1));
        Assert.Equal(70, s.SkillPoints);
    }

    [Fact]
    public async Task ARefusalThatCarriesABalanceIsHandedOnAsASnapshot()
    {
        var relay = new FakeRelay
        {
            Buy = new BackRoomStationResult(false, 200, "insufficient", new JObject { ["ok"] = false, ["reason"] = "insufficient", ["sp"] = 4 }),
        };
        var adopted = new List<(string Account, bool FromBuy, int Sp)>();
        var svc = new V2PurchaseService(relay, () => "u_one", _ => false, () => 100,
            (a, fromBuy, n) => { lock (adopted) adopted.Add((a, fromBuy, n)); });
        svc.EnsureState();
        await Until(() => svc.RowFor(V2PurchaseRule.FlashesPrizeId).State == V2PurchaseRowState.Offer);

        Assert.False(await svc.BuyAsync(V2PurchaseRule.FlashesPrizeId, 30));
        Assert.Contains(("u_one", false, 4), adopted);       // never as a buy: it may only raise
        Assert.DoesNotContain(adopted, a => a.FromBuy);
        var row = svc.RowFor(V2PurchaseRule.FlashesPrizeId);
        Assert.Equal(V2PurchaseRowState.Failed, row.State);
        Assert.Equal("v2_get_error_sp", row.MessageKey);
    }
}
