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
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Profile pictures (owner, 2026-10-09: the Profile disc and the rail foot pill were empty, and the
/// drawer said "Friends need an account" while signed in). Your picture follows ShareProfilePicture +
/// Discord; a friend's only exists when the server sent one (their consent); pictures are fetched
/// through a stub here, never the network. Same class as the other cloud tests: they share the
/// CoreSettings / CoreAccount statics.
/// </summary>
public sealed partial class AccountSeedTests
{
    private const string OwnPhoto = "https://cdn.discordapp.com/avatars/1/me.png?size=128";

    private const string PhotoState = """
        {"ok":true,"code":"CCP-ABCDE","me":{"activity":"panel","lock_day":null,"shared":true},
         "friends":[{"id":"u_pic","name":"Mia","avatar":"/v2/friends/avatar/u_pic","tier":0,"online":true,"activity":"panel","lock_day":null,"last_seen":null,"squelched":false},
                    {"id":"u_nopic","name":"Zed","avatar":null,"tier":0,"online":false,"activity":"online","lock_day":null,"last_seen":"2026-09-01T00:00:00Z","squelched":false}],
         "incoming":[],"outgoing":[],"blocked":[]}
        """;

    private sealed class PhotoWire : HttpMessageHandler
    {
        public string Lookup = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/state") ? PhotoState
                : path.EndsWith("/poll") ? """{"ok":true,"online":[],"inbox":[],"receipts":[]}"""
                : path == "/v3/leaderboard" ? Board
                : path == "/user/lookup" ? Lookup
                : """{"ok":true}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static byte[] Png()
    {
        using var bmp = new SKBitmap(8, 8);
        bmp.Erase(SKColors.HotPink);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Stubs the fetch and your own url for the body, then puts them back.</summary>
    private static async Task WithPhotos(string? ownUrl, List<string> fetched, Func<Task> body)
    {
        var (oldFetch, oldOwn) = (AvatarPhotos.Fetch, AvatarPhotos.OwnUrl);
        var png = Png();
        AvatarPhotos.ClearForTests();
        AvatarPhotos.Fetch = url => { lock (fetched) fetched.Add(url); return Task.FromResult(png); };
        AvatarPhotos.OwnUrl = _ => ownUrl;
        try { await body(); }
        finally { (AvatarPhotos.Fetch, AvatarPhotos.OwnUrl) = (oldFetch, oldOwn); AvatarPhotos.ClearForTests(); }
    }

    private static async Task UntilTrue(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static bool HasPhoto(Control root) =>
        root.GetLogicalDescendants().OfType<Border>().Any(b => b.Tag as string == "avatar-photo")
        || (root is Border self && self.Tag as string == "avatar-photo");

    private static Border? Row(Control root, string tag) =>
        root.GetLogicalDescendants().OfType<Border>().FirstOrDefault(b => b.Tag as string == tag);

    private static FriendsService PhotoFriends(PhotoWire wire)
    {
        var shared = true;
        var api = new FriendsApi(new HttpClient(wire), () => ("u_me", "tok"), "http://127.0.0.1:9");
        return new FriendsService(api, () => "u_me", () => true, null, () => shared, v => shared = v);
    }

    [Fact]
    public Task FriendPillsShowAPictureOnlyWhenTheServerSentOne() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fetched = new List<string>();
        await WithPhotos(null, fetched, async () =>
        {
            var svc = PhotoFriends(new PhotoWire());
            var d = new FriendsDrawer(svc);
            await svc.RefreshAsync();
            d.Render();
            await UntilTrue(() => HasPhoto(Row(d, "friends-row:u_pic")!));
            Assert.True(HasPhoto(Row(d, "friends-row:u_pic")!), "Mia shares a picture and her row shows it");
            Assert.False(HasPhoto(Row(d, "friends-row:u_nopic")!), "Zed shares none: initials");
            Assert.Equal(new[] { AvatarPhotos.ProxyBase + "/v2/friends/avatar/u_pic" }, fetched.ToArray());
        });
    });

    [Fact]
    public Task YourPictureSitsOnTheDrawerHeadAndTheRailChip_OnlyWhenYouShareIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var fetched = new List<string>();
        await WithPhotos(OwnPhoto, fetched, async () =>
        {
            var chip = new FriendsRailChip(PhotoFriends(new PhotoWire()));
            await UntilTrue(() => HasPhoto(chip) && HasPhoto(chip.Drawer));
            Assert.True(HasPhoto(chip), "the rail foot pill shows your picture");
            Assert.True(HasPhoto(chip.Drawer), "the drawer head shows your picture");
            Assert.Contains(OwnPhoto, fetched);
        });
        await WithPhotos(null, new List<string>(), async () =>
        {
            var chip = new FriendsRailChip(PhotoFriends(new PhotoWire()));
            await UntilTrue(() => false);
            Assert.False(HasPhoto(chip), "sharing off: initials");
        });
    });

    [Fact]
    public Task TheTrainerCardShowsYourPictureAndALookedUpPlayersPicture() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var s = CoreSettings.Current;
        var old = (s.UserDisplayName, s.OfflineMode, LeaderboardTabView.NewClient);
        var wire = new PhotoWire { Lookup = """{ "display_name": "Nyx", "level": 29, "is_online": true, "avatar_url": "/v2/friends/avatar/u_nyx" }""" };
        var fetched = new List<string>();
        try
        {
            (s.UserDisplayName, s.OfflineMode) = ("me_here", false);
            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            await WithPhotos(OwnPhoto, fetched, async () =>
            {
                var tab = new DiscordTabView();
                var hero = tab.FindControl<AdornedAvatar>("ProfileHeroAvatar")!;
                tab.DisplayOwnProfile();
                await UntilTrue(() => hero.AvatarImage != null);
                Assert.NotNull(hero.AvatarImage);

                await tab.OpenProfileAsync("ny");
                await UntilTrue(() => fetched.Contains(AvatarPhotos.ProxyBase + "/v2/friends/avatar/u_nyx") && hero.AvatarImage != null);
                Assert.Equal("Nyx", tab.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
                Assert.NotNull(hero.AvatarImage);
                Assert.Contains(AvatarPhotos.ProxyBase + "/v2/friends/avatar/u_nyx", fetched);
            });
            await WithPhotos(null, new List<string>(), async () =>
            {
                var tab = new DiscordTabView();
                tab.DisplayOwnProfile();
                await UntilTrue(() => false);
                Assert.Null(tab.FindControl<AdornedAvatar>("ProfileHeroAvatar")!.AvatarImage);
            });
        }
        finally { (s.UserDisplayName, s.OfflineMode, LeaderboardTabView.NewClient) = old; }
    });

    [Theory]
    [InlineData("/tmp/sandbox", null, false, null)]
    [InlineData("/tmp/sandbox", null, true, "https://codebambi-proxy.vercel.app")]
    [InlineData("/tmp/sandbox", "http://127.0.0.1:3001/", true, "http://127.0.0.1:3001")]
    [InlineData(null, null, false, "https://codebambi-proxy.vercel.app")]
    public void TheOnlineDeskSandboxReachesTheRealFriendsServer(string? userData, string? url, bool online, string? expected) =>
        Assert.Equal(expected, FriendsHead.BaseUrl(userData, url, online));

    [Fact]
    public Task ASignedInOnlineSandboxDrawerShowsTheListNotTheSignInPlate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.AuthToken, s.OfflineMode, FriendsHead.SandboxOnline);
        try
        {
            (s.UnifiedId, s.AuthToken, s.OfflineMode) = ("u_me", "tok", false);
            FriendsHead.SandboxOnline = () => true;
            var svc = FriendsHead.Create("/tmp/sandbox", null);   // built, no request yet
            Assert.NotNull(svc);
            Assert.True(svc!.Available);
            var d = new FriendsDrawer(svc);
            Assert.Null(d.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Tag as string == "friends-sign-in"));
            Assert.Null(d.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == Loc.Get("friends_signed_out")));

            FriendsHead.SandboxOnline = () => false;
            Assert.Null(FriendsHead.Create("/tmp/sandbox", null));   // the offline sandbox still builds none
        }
        finally { (s.UnifiedId, s.AuthToken, s.OfflineMode, FriendsHead.SandboxOnline) = old; }
        return Task.CompletedTask;
    });
}
