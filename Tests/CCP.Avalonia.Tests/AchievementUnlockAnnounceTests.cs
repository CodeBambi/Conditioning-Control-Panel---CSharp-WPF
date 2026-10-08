using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF App.OnAchievementUnlocked's tail (App.xaml.cs:4180-4205) on this head: a real unlock through the
/// startup wiring plays the achievement sound and, only when DiscordShareAchievements is on, posts the community
/// webhook from Core DiscordAccount; plus the toasts' passive corner placement (WPF PositionWindow).</summary>
public sealed class AchievementUnlockAnnounceTests
{
    private sealed class Proxy : HttpMessageHandler
    {
        public readonly List<(string Path, string Body, string? Token)> Posts = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Posts.Add((r.RequestUri!.AbsolutePath, await r.Content!.ReadAsStringAsync(ct),
                r.Headers.TryGetValues("X-Auth-Token", out var t) ? string.Join(",", t) : null));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALevelUnlockChimesAndPostsOnlyWhenSharingIsOn(bool share)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (oldShare, oldSecret, oldId, oldPlay) = (s.DiscordShareAchievements, CoreSecrets.RetrieveProvider, CoreAccount.UnifiedUserId, CoreAudio.PlayOneShotProvider);
            var dir = Directory.CreateTempSubdirectory("ccp-ach-announce-").FullName;
            var proxy = new Proxy();
            var played = new List<string>();
            try
            {
                s.DiscordShareAchievements = share;
                CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null; // AppSettings.AuthToken reads secrets
                CoreAccount.UnifiedUserId = "u-1";
                CoreAudio.PlayOneShotProvider = (p, _, tag, _, done) => { played.Add(Path.GetFileName(p) + " " + tag); done?.Invoke(); };
                using var discord = new DiscordAccount(null, () => new AppSettings(), proxy) { CustomDisplayName = "Dolly" };

                var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
                AvApp.WireAchievementUnlocks(engine, discord);
                engine.CheckLevelAchievements(10); // plastic_initiation, the first level milestone
                Dispatcher.UIThread.RunJobs();
                await Task.Yield();

                Assert.Equal(new[] { "chime2.mp3 achievement" }, played);
                if (!share) { Assert.Empty(proxy.Posts); return; }
                var post = Assert.Single(proxy.Posts);
                Assert.Equal("/discord/community-webhook", post.Path);
                Assert.Equal("tok", post.Token);
                Assert.Contains("\"display_name\":\"Dolly\"", post.Body);
                Assert.Contains("\"unified_id\":\"u-1\"", post.Body);
                Assert.Contains("\"achievement_id\":\"plastic_initiation\"", post.Body);
                Assert.Contains("\"mod_id\":\"" + DiscordAccount.ModThemeId(CoreMods.ActiveModId) + "\"", post.Body);
            }
            finally
            {
                (s.DiscordShareAchievements, CoreSecrets.RetrieveProvider, CoreAccount.UnifiedUserId, CoreAudio.PlayOneShotProvider) = (oldShare, oldSecret, oldId, oldPlay);
                Directory.Delete(dir, true);
            }
        });
    }

    [Fact]
    public void ToastsSitInTheCornerInDevicePixelsWithItemToastsStackedAbove()
    {
        // 1.79 is this desktop's scaling: WorkingArea is pixels, Width/Height DIPs (WPF Left = Right - Width - 20).
        var wa = new PixelRect(0, 0, 3440, 1400);
        var ach = AchievementPopup.CornerRect(wa, 1.79, 400, 200, 20);
        Near(wa.Right - (int)(20 * 1.79), ach.Right);
        Near(wa.Bottom - (int)(20 * 1.79), ach.Bottom);
        // Item toast #0 and #1 (WPF ItemUnlockedPopup.PositionWindow): above the 200-DIP achievement popup, 12 + 8 gaps.
        Assert.Equal(200 + 12, ItemUnlockedPopup.BottomDip(0));
        Assert.Equal(200 + 12 + 2 * (104 + 8), ItemUnlockedPopup.BottomDip(2));
        var item0 = AchievementPopup.CornerRect(wa, 1.79, 340, ItemUnlockedPopup.ToastHeight, ItemUnlockedPopup.BottomDip(0));
        var item1 = AchievementPopup.CornerRect(wa, 1.79, 340, ItemUnlockedPopup.ToastHeight, ItemUnlockedPopup.BottomDip(1));
        Assert.True(item0.Bottom <= wa.Bottom - (int)(200 * 1.79));
        Near(item0.Y - (int)Math.Round(8 * 1.79), item1.Bottom);
        Near(ach.Right, item0.Right);
    }

    private static void Near(int expected, int actual) => Assert.InRange(actual, expected - 1, expected + 1);
}
