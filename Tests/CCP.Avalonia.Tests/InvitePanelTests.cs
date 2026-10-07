using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Exclusives invites card (WPF Controls/Invites/InvitePanel) over the real Core wire with a stubbed server.</summary>
public sealed class InvitePanelTests
{
    private const string Mine = """
        {"ok":true,"converted_total":2,"resets_at":"2026-11-01T00:00:00Z",
         "codes":[{"code":"BAMBI-AAAA","state":"open"},{"code":"BAMBI-BBBB","state":"converted","invitee_name":"Mia"}]}
        """;

    private sealed class Wire(string mine, string redeem) : HttpMessageHandler
    {
        public readonly List<string> Ops = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            lock (Ops) Ops.Add(op);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(op == "mine" ? mine : redeem) });
        }
    }

    private static InvitePanel Panel(Wire wire, bool signedIn = true, bool premium = false)
    {
        EnsurePlatform();
        return new(() => new InviteApi(new HttpClient(wire), () => ("u_me", "tok"), "http://127.0.0.1:9"), () => signedIn, () => premium);
    }

    /// <summary>The tiles carry a hand cursor, which needs a platform (as ChasterTab2Tests sets up).</summary>
    private static void EnsurePlatform()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static IEnumerable<T> All<T>(Control root) => root.GetLogicalDescendants().OfType<T>();
    private static T? Tagged<T>(Control root, string tag) where T : Control => All<T>(root).FirstOrDefault(c => c.Tag as string == tag);

    [Fact]
    public Task ASubscriberSeesTheirCodesAndTheLadder() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (oldUnlocked, oldUnlock) = (InviteRewards.UnlockedProvider, InviteRewards.TryUnlockProvider);
        var unlocked = new List<string>();
        try
        {
            InviteRewards.UnlockedProvider = () => new HashSet<string>();
            InviteRewards.TryUnlockProvider = id => { unlocked.Add(id); return true; };
            var wire = new Wire(Mine, "{}");
            var panel = Panel(wire);
            await panel.RefreshAsync(force: true);
            Assert.Equal(new[] { "invite_first" }, unlocked); // two converted friends earn the first badge only
            Body(panel);
        }
        finally { (InviteRewards.UnlockedProvider, InviteRewards.TryUnlockProvider) = (oldUnlocked, oldUnlock); }
    });

    private static void Body(InvitePanel panel)
    {
        Assert.True(panel.IsVisible);
        var texts = All<TextBlock>(panel).Select(t => t.Text).ToList();
        Assert.Contains("BAMBI-AAAA", texts);
        Assert.Contains(Loc.GetF("invites_slot_converted", "Mia"), texts);
        Assert.Contains(Loc.GetF("invites_left", 1, 2), texts);
        Assert.Equal(InviteRewards.Ladder.Count, All<Border>(panel).Count(b => b.Tag as string == "invite-rung"));
        Assert.Contains(Loc.GetF("invites_ladder_progress", 2, 3), texts); // the next rung (3 friends) carries the bar
    }

    [Fact]
    public Task SignedOutOrPremiumWithoutCodesShowsNothing() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var wire = new Wire("""{"ok":false,"reason":"not_subscribed"}""", "{}");
        var signedOut = Panel(wire, signedIn: false);
        await signedOut.RefreshAsync(force: true);
        Assert.False(signedOut.IsVisible);
        Assert.Empty(wire.Ops); // nothing is sent without an account

        var premium = Panel(wire, premium: true);
        await premium.RefreshAsync(force: true);
        Assert.False(premium.IsVisible);
    });

    [Fact]
    public Task RedeemWordsARefusalThroughTheButton() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var wire = new Wire("""{"ok":false,"reason":"not_subscribed"}""", """{"ok":false,"reason":"used"}""");
        var panel = Panel(wire);
        await panel.RefreshAsync(force: true);
        Assert.True(panel.IsVisible);
        Tagged<TextBox>(panel, "invite-code")!.Text = "bambi-cccc";
        Tagged<Button>(panel, "invite-redeem")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await panel.Redeeming!;
        Assert.Contains("redeem", wire.Ops);
        Assert.Equal(Loc.Get("invites_err_used"), Tagged<TextBlock>(panel, "invite-result")!.Text);
    });

    [Fact]
    public Task AnAcceptedCodeOpensTheInviteWeekOnThisAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsurePlatform();
        var s = CoreSettings.Current;
        var until = s.InviteGrantUntil;
        try
        {
            s.InviteGrantUntil = null;
            var grant = DateTime.UtcNow.AddDays(7);
            var wire = new Wire("""{"ok":false,"reason":"not_subscribed"}""", $$"""{"ok":true,"grant_until":"{{grant:O}}"}""");
            var panel = Panel(wire);
            await panel.RefreshAsync(force: true);
            Tagged<TextBox>(panel, "invite-code")!.Text = "BAMBI-CCCC";
            Tagged<Button>(panel, "invite-redeem")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await panel.Redeeming!;
            Assert.True(s.HasInviteGrant);
            Assert.True(InvitePanel.ExpiryArmed); // the end of the week will repaint the gates
            Assert.Equal(Loc.GetF("invites_redeem_ok", s.InviteGrantUntil!.Value.ToLocalTime().ToString("d MMM, HH:mm")),
                Tagged<TextBlock>(panel, "invite-result")!.Text);
        }
        finally { s.InviteGrantUntil = until; InvitePanel.CancelExpiry(); }
    });

    [Fact]
    public Task TheHeaderTicketFollowsAnUnusedCodeAndItsClickRefusesUnderLockdown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsurePlatform();
        var (oldId, oldLd) = (InviteApi.DefaultIdentity, LockdownService.Current);
        var shell = new MainShellWindow();
        try
        {
            InviteApi.DefaultIdentity = () => ("u_me", "tok");
            var ticket = shell.Named<Button>("BtnInviteTicket")!;
            string reply = Mine;
            shell.InviteTicketApi = () => new InviteApi(new HttpClient(new Wire(reply, "{}")), () => ("u_me", "tok"), "http://127.0.0.1:9");
            await shell.RefreshInviteTicketAsync();
            Assert.True(ticket.IsVisible); // BAMBI-AAAA is unused
            reply = """{"ok":true,"codes":[{"code":"BAMBI-BBBB","state":"trying","invitee_name":"Mia","day":2}]}""";
            await shell.RefreshInviteTicketAsync();
            Assert.False(ticket.IsVisible);

            var vault = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.ExclusivesTabView>("ExclusivesTab")!;
            shell.ShowTab("achievements");
            var ld = LockdownService.Current = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            shell.BtnInviteTicket_Click(ticket, new RoutedEventArgs(Button.ClickEvent));
            Assert.False(vault.IsVisible);
            ld.Deactivate();
            shell.BtnInviteTicket_Click(ticket, new RoutedEventArgs(Button.ClickEvent));
            Assert.True(vault.IsVisible);
        }
        finally
        {
            (InviteApi.DefaultIdentity, LockdownService.Current) = (oldId, oldLd);
            shell.Close();
        }
    });

    [Fact]
    public Task TheShellsTicketFollowsTheCardFromStartupAndWobblesOnlyWhileShown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsurePlatform();
        var oldId = InviteApi.DefaultIdentity;
        var shell = new MainShellWindow(); // the ctor wires the ticket (InitializeInviteTicket)
        try
        {
            InviteApi.DefaultIdentity = () => ("u_me", "tok");
            shell.Show();
            var ticket = shell.Named<Button>("BtnInviteTicket")!;
            var card = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.ExclusivesTabView>("ExclusivesTab")!.FindControl<InvitePanel>("InvitesHost")!;
            string reply = Mine;
            card.Api = () => new InviteApi(new HttpClient(new Wire(reply, "{}")), () => ("u_me", "tok"), "http://127.0.0.1:9");
            await card.RefreshAsync(force: true); // the card's read repaints the ticket
            Assert.True(ticket.IsVisible);
            Assert.True(shell.InviteWobbleRunning);
            shell.Hide();
            Assert.False(shell.InviteWobbleRunning); // no ticks while the panel is hidden
            shell.Show();
            Assert.True(shell.InviteWobbleRunning);
            reply = """{"ok":true,"codes":[{"code":"BAMBI-BBBB","state":"converted","invitee_name":"Mia"}]}""";
            await card.RefreshAsync(force: true);
            Assert.False(ticket.IsVisible);
            Assert.False(shell.InviteWobbleRunning);
        }
        finally
        {
            InviteApi.DefaultIdentity = oldId;
            shell.Close();
        }
    });

    [Fact]
    public async Task ASandboxWithoutAnInvitesAddressSendsNothing()
    {
        var (oldId, oldUrl) = (InviteApi.DefaultIdentity, InviteApi.DefaultBaseUrl);
        try
        {
            ConditioningControlPanel.Avalonia.Platform.FriendsHead.SeedInvites("/tmp/sandbox", null);
            InviteApi.DefaultIdentity = () => ("u_me", "tok"); // signed in: only the address is missing
            var wire = new Wire(Mine, """{"ok":true}""");
            var api = new InviteApi(new HttpClient(wire));
            Assert.False((await api.MineAsync()).Reachable);
            Assert.False((await api.RedeemAsync("BAMBI-CCCC")).Ok);
            Assert.Empty(wire.Ops);
        }
        finally { (InviteApi.DefaultIdentity, InviteApi.DefaultBaseUrl) = (oldId, oldUrl); }
    }

    [Fact]
    public Task ClosingTheShellCancelsTheEndOfWeekTimer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsurePlatform();
        var s = CoreSettings.Current;
        var until = s.InviteGrantUntil;
        var shell = new MainShellWindow();
        try
        {
            s.InviteGrantUntil = DateTime.UtcNow.AddDays(3);
            InvitePanel.ArmExpiry();
            Assert.True(InvitePanel.ExpiryArmed);
            shell.Show();
            shell.Close();
            Assert.False(InvitePanel.ExpiryArmed);
        }
        finally { s.InviteGrantUntil = until; InvitePanel.CancelExpiry(); shell.Close(); }
        return Task.CompletedTask;
    });
}
