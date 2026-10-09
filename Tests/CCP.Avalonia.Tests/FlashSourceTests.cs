using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 7.1.5 flash sources on this head: the online clip pool beside the local folder,
/// gated on MediaSource + consent, with the same remote-share rule (FlashService.ShouldDrawRemote).</summary>
public sealed class FlashSourceTests
{
    private static AppSettings Settings(string source, bool consent, int ratio = 50)
        => new() { MediaSource = source, RemoteMediaConsented = consent, RemoteMediaRatio = ratio };

    [Fact]
    public void Remote_needs_a_non_local_source_and_consent()
    {
        Assert.False(FlashSourceRules.RemoteEnabled(Settings("local", true)));
        Assert.False(FlashSourceRules.RemoteEnabled(Settings("online", false)));
        Assert.True(FlashSourceRules.RemoteEnabled(Settings("online", true)));
        Assert.True(FlashSourceRules.RemoteEnabled(Settings("mixed", true)));
        // A For You consent counts (HasRemoteMediaConsent).
        Assert.True(FlashSourceRules.RemoteEnabled(new AppSettings { MediaSource = "online", FypOnlineConsented = true }));
        Assert.False(FlashSourceRules.RemoteEnabled(null));
    }

    [Fact]
    public void Online_with_an_empty_folder_draws_every_pick_remote()
    {
        var s = Settings("online", true);
        var rng = new Random(1);
        var plan = FlashSourceRules.Plan(6, s, haveLocal: false, remoteReady: 3, rng, () => true);
        Assert.Equal(6, plan.Count);
        Assert.All(plan, Assert.True);
    }

    [Fact]
    public void Online_draws_remote_even_when_the_folder_has_pictures()
    {
        var s = Settings("online", true);
        Assert.All(Enumerable.Range(0, 200), _ => Assert.True(FlashSourceRules.ShouldDrawRemote(s, haveLocal: true, new Random())));
    }

    [Fact]
    public void Mixed_follows_the_remote_share()
    {
        var s = Settings("mixed", true, ratio: 50);
        var rng = new Random(7);
        var remote = Enumerable.Range(0, 4000).Count(_ => FlashSourceRules.ShouldDrawRemote(s, haveLocal: true, rng));
        Assert.InRange(remote, 1800, 2200);
    }

    [Fact]
    public void Without_consent_every_pick_is_local_and_an_empty_folder_shows_nothing()
    {
        var s = Settings("online", false);
        var rng = new Random(3);
        Assert.All(FlashSourceRules.Plan(5, s, haveLocal: true, remoteReady: 9, rng, () => true), Assert.False);
        Assert.Empty(FlashSourceRules.Plan(5, s, haveLocal: false, remoteReady: 0, rng, () => true));
    }

    [Fact]
    public void A_cold_remote_pool_falls_back_to_local_or_stops_the_burst()
    {
        var s = Settings("online", true);
        var rng = new Random(5);
        var withLocal = FlashSourceRules.Plan(4, s, haveLocal: true, remoteReady: 2, rng, () => false);
        Assert.Equal(4, withLocal.Count);
        Assert.All(withLocal, Assert.False);
        Assert.Empty(FlashSourceRules.Plan(4, s, haveLocal: false, remoteReady: 2, rng, () => false));
    }

    [Fact]
    public void Remote_urls_are_told_apart_from_paths()
    {
        Assert.True(FlashSourceRules.IsRemotePath("https://cdn.example/x.mp4"));
        Assert.False(FlashSourceRules.IsRemotePath(@"C:\assets\images\x.png"));
        Assert.False(FlashSourceRules.IsRemotePath(null));
    }

    [Fact]
    public void Ready_clips_are_drawn_then_reused_least_recently_shown()
    {
        RemoteFlashSource.ResetForTests();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-flash-src-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = Path.Combine(dir, "a.jpg"); File.WriteAllBytes(a, new byte[] { 1 });
            var b = Path.Combine(dir, "b.jpg"); File.WriteAllBytes(b, new byte[] { 1 });
            Assert.True(RemoteFlashSource.AddReady(new RemoteFlashItem("https://x/a.mp4", a, "")));
            Assert.True(RemoteFlashSource.AddReady(new RemoteFlashItem("https://x/b.mp4", b, "")));
            Assert.Equal(2, RemoteFlashSource.ReadyCount);

            var rng = new Random(2);
            var first = RemoteFlashSource.TryTake(rng)!.Value;
            var second = RemoteFlashSource.TryTake(rng)!.Value;
            Assert.NotEqual(first.Url, second.Url);
            // Fresh ran dry: the least recently shown comes back instead of an empty burst.
            Assert.Equal(first.Url, RemoteFlashSource.TryTake(rng)!.Value.Url);

            // A poster that vanished from disk is never drawn.
            File.Delete(a); File.Delete(b);
            Assert.Null(RemoteFlashSource.TryTake(rng));
        }
        finally
        {
            RemoteFlashSource.ResetForTests();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
    /// <summary>WPF FlashClickable: a click pops THAT flash once (the tube hears it); other flashes live on.</summary>
    [Fact]
    public async Task A_clickable_flash_pops_once()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var w = new FlashOverlayWindow();
            var other = new FlashOverlayWindow();
            try
            {
                Assert.False(w.IsHitTestVisible);   // click-through until asked
                w.MakeClickable();
                Assert.True(w.IsHitTestVisible);
                var pops = 0;
                w.Popped += _ => pops++;
                w.Pop();
                w.Pop();
                Assert.Equal(1, pops);
                Assert.False(other.IsHitTestVisible);
            }
            finally { w.Close(); other.Close(); }
            return Task.CompletedTask;
        });
    }
}
