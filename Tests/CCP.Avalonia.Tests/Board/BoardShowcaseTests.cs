using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Billboard.Showcase;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests.Board;

/// <summary>
/// Lane k16 (HA10): the Showcase clip view (poster first, one 260 ms fade of the player over it,
/// silent decoder made only on Play, nothing with motion off), the "clip" art key, the Showcase
/// provider staying out of a headless host (no network in tests), and the Waiting provider's
/// program-day card leading its quests. Process-wide seams are swapped here, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class BoardShowcaseTests
{
    private sealed class FakePlayer : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private static string TempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-k16-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePoster(string path)
    {
        using var bmp = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(new SKColor(255, 79, 168));
        using var img = SKImage.FromBitmap(bmp);
        File.WriteAllBytes(path, img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
    }

    private static WriteableBitmap Frame() =>
        new(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    [Fact]
    public void Clip_view_shows_its_poster_and_starts_no_player_with_motion_off() => AvaloniaTestDispatcher.Run(() =>
    {
        BoardHeadTests.EnsureApp();
        var root = TempRoot();
        var posterPath = Path.Combine(root, "poster.png");
        var videoPath = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(videoPath, new byte[] { 0 });
        WritePoster(posterPath);

        var oldMotion = ClipArtView.MotionAllowed;
        var oldStart = ClipArtView.StartPlayer;
        int started = 0;
        ClipArtView.MotionAllowed = () => false;
        ClipArtView.StartPlayer = (_, _) => { started++; return new FakePlayer(); };
        try
        {
            int played = 0;
            var view = new ClipArtView(new ShowcaseClipArt("dtrh", videoPath, posterPath, _ => played++));
            var poster = Assert.IsType<Image>(Assert.Single(view.Children));
            Assert.NotNull(poster.Source);

            view.Play();
            Assert.Single(view.Children); // no player surface made
            Assert.Equal(0, started);
            Assert.Equal(0, played);

            view.Release();
            Assert.Null(poster.Source);
            view.Play(); // after Release: a no-op, never a throw
            view.Touch(new Point(.5, .5));
            Assert.Equal(0, started);

            // The poster file is not held open.
            File.Delete(posterPath);
        }
        finally
        {
            ClipArtView.MotionAllowed = oldMotion;
            ClipArtView.StartPlayer = oldStart;
        }
    });

    [Fact]
    public void Clip_view_fades_the_player_in_over_the_poster_once_and_reports_played_once() => AvaloniaTestDispatcher.Run(() =>
    {
        BoardHeadTests.EnsureApp();
        var root = TempRoot();
        var videoPath = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(videoPath, new byte[] { 0 });

        var oldMotion = ClipArtView.MotionAllowed;
        var oldStart = ClipArtView.StartPlayer;
        var oldClock = FxTrack.ManualClock;
        FakePlayer? player = null;
        int started = 0;
        ClipArtView.MotionAllowed = () => true;
        ClipArtView.StartPlayer = (_, _) => { started++; return player = new FakePlayer(); };
        FxTrack.ManualClock = true;
        BoardHeadTests.Pin();
        try
        {
            int played = 0;
            var view = new ClipArtView(new ShowcaseClipArt("remote", videoPath, null, _ => played++));
            view.Play();
            Assert.Equal(1, started);
            Assert.True(view.HasPlayer);
            Assert.Equal(2, view.Children.Count);
            var screen = Assert.IsType<Image>(view.Children[1]);
            Assert.Equal(0, screen.Opacity);   // the poster shows until a frame lands
            Assert.False(screen.IsHitTestVisible);
            Assert.Equal(0, played);           // nothing ran yet

            view.OnFrame(Frame());
            Assert.Equal(1, played);
            Assert.InRange(screen.Opacity, 0, 0.01); // the fade starts at 0 and never passes 1

            view.OnFrame(Frame());
            view.Play();                        // already playing: no second decoder
            Assert.Equal(1, started);
            Assert.Equal(1, played);

            // Pause (another tab, a fold, motion off) closes the decoder and keeps the frame up.
            view.Pause();
            Assert.False(view.HasPlayer);
            Assert.True(player!.Disposed);
            Assert.NotNull(screen.Source);

            view.Play();
            Assert.Equal(2, started);
            Assert.Equal(1, played);            // one report per card

            view.Release();
            Assert.True(player!.Disposed);
            Assert.Single(view.Children);
            Assert.Equal(1, screen.Opacity);    // the fade landed on its end state before leaving
        }
        finally
        {
            BoardHeadTests.Unpin();
            FxTrack.ManualClock = oldClock;
            ClipArtView.MotionAllowed = oldMotion;
            ClipArtView.StartPlayer = oldStart;
        }
    });

    [Fact]
    public void A_clip_that_will_not_start_leaves_the_poster_up() => AvaloniaTestDispatcher.Run(() =>
    {
        BoardHeadTests.EnsureApp();
        var root = TempRoot();
        var videoPath = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(videoPath, new byte[] { 0 });

        var oldMotion = ClipArtView.MotionAllowed;
        var oldStart = ClipArtView.StartPlayer;
        ClipArtView.MotionAllowed = () => true;
        ClipArtView.StartPlayer = (_, _) => null; // no libvlc, or eight clips already run
        try
        {
            int played = 0;
            var view = new ClipArtView(new ShowcaseClipArt("goon", videoPath, null, _ => played++));
            view.Play();
            Assert.False(view.HasPlayer);
            Assert.Equal(0, played);
            Assert.Equal(0, ((Image)view.Children[1]).Opacity);

            // A missing file never reaches the player at all.
            int asked = 0;
            ClipArtView.StartPlayer = (_, _) => { asked++; return new FakePlayer(); };
            var gone = new ClipArtView(new ShowcaseClipArt("goon", Path.Combine(root, "nope.mp4"), null, null));
            gone.Play();
            Assert.Equal(0, asked);
            view.Release();
            gone.Release();
        }
        finally
        {
            ClipArtView.MotionAllowed = oldMotion;
            ClipArtView.StartPlayer = oldStart;
        }
    });

    [Fact]
    public void The_clip_key_registers_and_the_showcase_stays_out_of_a_headless_host() => AvaloniaTestDispatcher.Run(() =>
    {
        BoardHeadTests.EnsureApp();
        BillboardWiring.RegisterArt();
        Assert.True(BillboardArt.IsRegistered(ShowcaseRules.ArtKey));
        var view = BillboardArt.Create("clip", null);
        Assert.IsAssignableFrom<IBillboardArtView>(view);
        ((IBillboardArtView)view!).Release();

        // No controlled lifetime here: the provider that reaches the network must not join.
        Assert.False(BillboardWiring.ShowcaseLive());
        var oldHead = BillboardWiring.HeadProviders;
        BillboardWiring.ResetForTests();
        BillboardWiring.HeadProviders = null;
        try
        {
            BillboardWiring.Start();
            Assert.DoesNotContain(BillboardWiring.Providers, p => p is ShowcaseProvider);
        }
        finally
        {
            BillboardWiring.ResetForTests();
            BillboardWiring.HeadProviders = oldHead;
        }
    });

    [Fact]
    public void Waiting_leads_with_the_program_day_then_the_quests()
    {
        ProgramToday? today = new("Deep Dive", 3, 14, SessionDone: false, DayDone: false);
        var waiting = new WaitingProvider(new BillboardShellHooks(), () => (3, 1), () => today);
        var ctx = new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now);

        var cards = waiting.Current(ctx).ToList();
        Assert.Equal(WaitingCards.CardProgram, cards[0].Id);
        Assert.Equal(BillboardCardKind.Waiting, cards[0].Kind);
        Assert.Equal(BillboardActionKind.Tab, cards[0].Action.Kind);
        Assert.Equal("programs", cards[0].Action.Target);
        Assert.True(cards.Count >= 2);

        // A finished day, or no running program, has no card.
        today = today with { DayDone = true };
        Assert.DoesNotContain(waiting.Current(ctx), c => c.Id == WaitingCards.CardProgram);
        today = null;
        Assert.DoesNotContain(waiting.Current(ctx), c => c.Id == WaitingCards.CardProgram);
    }
}
