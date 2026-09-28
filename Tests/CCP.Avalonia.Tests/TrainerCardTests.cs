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
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Trainer Card (unit 6, part 3), read-only: own card from local settings, another trainer's card from the
/// transcribed board + GET /user/lookup, unknown and offline states, and nothing but GETs on the wire.
/// Same class as the other cloud tests: they share the CoreSettings / CoreAccount statics.
/// </summary>
public sealed partial class AccountSeedTests
{
    // Real catalogue ids plus one the catalogue does not know (dropped, as WPF drops a tile it cannot resolve).
    private const string CardLookup = """
    { "display_name": "Nyx", "level": 29, "xp": 90000, "achievements_count": 3,
      "achievements": ["plastic_initiation", "dumb_bimbo", "not_a_real_id"], "is_online": false,
      "is_whitelisted": true, "is_staff": true, "staff_role": "owner" }
    """;

    private sealed class CardWire : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add($"{r.Method} {r.RequestUri!.PathAndQuery}");
            var body = r.RequestUri.AbsolutePath switch { "/v3/leaderboard" => Board, "/user/lookup" => CardLookup, _ => null };
            return Task.FromResult(new HttpResponseMessage(body == null ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
                { Content = new StringContent(body ?? "{}") });
        }
    }

    [Fact]
    public async Task TrainerCard_OwnCard_LookedUpCard_Unknown_Offline_GetsOnly()
    {
        var s = CoreSettings.Current;
        var old = (s.OfflineMode, s.UserDisplayName, s.PlayerLevel, s.PlayerXP, s.IsSeason0Og, s.DescentEpoch,
                   LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId);
        var wire = new CardWire();
        try
        {
            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            CoreAccount.UnifiedUserId = "u9";
            (s.UserDisplayName, s.PlayerLevel, s.PlayerXP, s.IsSeason0Og, s.OfflineMode) = ("not_on_board", 5, 120, true, false);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                LocalizationManager.Instance.SetLanguage("en");

                var tab = new DiscordTabView();
                T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;
                string Text(string n) => F<TextBlock>(n).Text ?? "";
                Assert.False(F<Grid>("ProfileCardWrapper").IsVisible);          // no sample card
                Assert.Empty(wire.Seen);

                // Own card: you are not on the board, so the local card from settings.
                await tab.ViewMyProfileAsync();
                Assert.True(F<Grid>("ProfileCardWrapper").IsVisible);
                Assert.Equal("not_on_board", Text("TxtProfileViewerName"));
                Assert.Equal("5", Text("TxtProfileViewerLevel"));
                Assert.Equal("#4", Text("TxtProfileViewerRank"));             // server your_rank from the board
                Assert.Equal(TrainerCardText.Number(XpCurve.GetTotalXP(5, 120, s.DescentEpoch)), Text("TxtProfileViewerXp"));
                Assert.Equal(Loc.GetF("profile_xp_progress", "120", $"{XpCurve.GetXPForLevel(5, s.DescentEpoch):N0}"), Text("TxtProfileXpProgress"));
                Assert.Equal(Loc.Get("label_online"), Text("TxtProfileViewerOnline"));
                Assert.True(F<Border>("OgBannerBadge").IsVisible);
                Assert.Equal(Loc.Get("label_no_achievements_yet"), Text("TxtNoAchievements"));

                // Another trainer: partial name, board row now, lookup (badges, presence, tiles) after.
                await tab.OpenProfileAsync("ny");
                Assert.Equal("Nyx", Text("TxtProfileViewerName"));
                Assert.Equal(("29", "#3", "90.0k", "9 / "), (Text("TxtProfileViewerLevel"), Text("TxtProfileViewerRank"),
                    Text("TxtProfileViewerXp"), Text("TxtProfileViewerAchievements")[..4]));
                Assert.False(F<Border>("OgBannerBadge").IsVisible);
                for (var i = 0; i < 50 && !wire.Seen.Any(r => r.Contains("/user/lookup")); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                await Task.Delay(50); Dispatcher.UIThread.RunJobs();
                Assert.Contains("GET /user/lookup?display_name=Nyx", wire.Seen);
                Assert.Equal("Offline", Text("TxtProfileViewerOnline"));      // the lookup's fresh presence
                Assert.True(F<Border>("StaffBadge").IsVisible && F<Border>("WhitelistBadge").IsVisible);
                var tiles = ((IEnumerable<ProfileAchievementTile>)F<ItemsControl>("ProfileAchievementGrid").ItemsSource!).ToList();
                Assert.Equal(new[] { "plastic_initiation", "dumb_bimbo" }, tiles.Select(t => t.Id));

                // Unknown trainer: the "search for a user" plate, no card.
                await tab.OpenProfileAsync("nobody_by_that_name");
                Assert.False(F<Grid>("ProfileCardWrapper").IsVisible);
                Assert.True(F<Border>("NoProfileSelected").IsVisible);

                // Offline: nothing on the wire, a fresh tab finds nobody and your card is local with no rank.
                s.OfflineMode = true;
                var seen = wire.Seen.Count;
                var offline = new DiscordTabView();
                await offline.OpenProfileAsync("Nyx");
                Assert.True(offline.FindControl<Border>("NoProfileSelected")!.IsVisible);
                await offline.ViewMyProfileAsync();
                Assert.Equal("#-", offline.FindControl<TextBlock>("TxtProfileViewerRank")!.Text);
                Assert.Equal(seen, wire.Seen.Count);
            });
            Assert.All(wire.Seen, r => Assert.True(r.StartsWith("GET "), $"write endpoint called: {r}"));
        }
        finally
        {
            (s.OfflineMode, s.UserDisplayName, s.PlayerLevel, s.PlayerXP, s.IsSeason0Og, s.DescentEpoch,
             LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId) = old;
        }
    }

    [Fact]
    public async Task LeaderboardRowDoubleClick_OpensThatTrainersCard()
    {
        var s = CoreSettings.Current;
        var old = (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId);
        var wire = new CardWire();
        try
        {
            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            (s.OfflineMode, CoreAccount.UnifiedUserId) = (false, "u9");
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var w = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
                w.Show();
                w.ShowTab("leaderboard");
                var board = w.Named<LeaderboardTabView>("LeaderboardTab")!;
                await board.RefreshLeaderboardAsync();
                Dispatcher.UIThread.RunJobs();
                var list = board.FindControl<ListBox>("LstLeaderboard")!;
                var row = list.GetRealizedContainers().First(c => c.DataContext is LeaderboardRow);
                var at = row.TranslatePoint(new Point(40, row.Bounds.Height / 2), w)!.Value;
                for (var i = 0; i < 2; i++)
                {
                    w.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
                    w.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
                }
                for (var i = 0; i < 50 && !wire.Seen.Any(r => r.Contains("/user/lookup")); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }

                var card = w.ProfilePage!;
                var name = ((LeaderboardRow)row.DataContext!).DisplayName;
                Assert.True(card.IsEffectivelyVisible);                          // the Profile tab is up
                Assert.Equal(name, card.FindControl<TextBox>("TxtProfileSearch")!.Text);
                Assert.Equal(name, card.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
                w.Close();
            });
            Assert.All(wire.Seen, r => Assert.True(r.StartsWith("GET "), $"write endpoint called: {r}"));
        }
        finally
        {
            (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId) = old;
        }
    }

    [Fact]
    public void TrainerCardText_MatchesWpfRules()
    {
        Assert.Equal(("1.2M", "3.4k", "999"), (TrainerCardText.Number(1_234_567), TrainerCardText.Number(3_420), TrainerCardText.Number(999)));
        Assert.Equal(("1.3h", "59m"), (TrainerCardText.Video(75.5), TrainerCardText.Video(59)));
        Assert.Equal(("#12", "#-", "#-"), (TrainerCardText.Rank(12), TrainerCardText.Rank(0), TrainerCardText.Rank(null)));
    }
}
