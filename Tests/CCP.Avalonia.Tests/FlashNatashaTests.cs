using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HB18: Natasha's favourite on flashes (the dodge), luminance sampling and the picture
/// override. Swaps the process-wide Chaster service and the flash seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FlashNatashaTests
{
    private sealed class Quiet : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = r });
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    /// <summary>A decoded (immutable) picture, as a flash holds it.</summary>
    private static Bitmap Solid(byte b, byte g, byte r, byte a = 255)
    {
        using var bmp = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var fb = bmp.Lock();
        var px = new byte[fb.RowBytes * fb.Size.Height];
        for (var i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a; }
        Marshal.Copy(px, 0, fb.Address, px.Length);
        fb.Dispose();
        var png = new MemoryStream();
        bmp.Save(png);
        png.Position = 0;
        return new Bitmap(png);
    }

    [Fact]
    public Task A_red_flash_books_only_when_its_ring_runs_out() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var dir = Directory.CreateTempSubdirectory("ccp-flash-natasha-").FullName;
        SecretStore.Seed();
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
        var chaster = new ChasterService(new ChasterClient(new Quiet()), new SecretChasterTokenStore(), Path.Combine(dir, "chaster_tab.json"),
            () => new ChasterOptions(true, "l1", new HashSet<string>(new[] { NatashasFavourite.EventId }, StringComparer.Ordinal)));
        var booked = new List<(string Id, int Seconds, bool Unprompted)>();
        chaster.Booked += (id, b) => booked.Add((id, b.AppliedSeconds, b.Unprompted));
        var (service, idle, timer) = (ChasterHead.Service, FlashOverlay.IdleSeconds, FlashOverlay.DodgeTimer);
        var s = CoreSettings.Current;
        var dodge = s.ChasterFlashDodge;
        ChasterHead.Service = chaster;
        var rings = new List<Action>();
        FlashOverlay.DodgeTimer = (run, after) =>
        {
            Assert.Equal(NatashasFavourite.DodgeMs, after.TotalMilliseconds);
            rings.Add(run);
        };
        var windows = new List<FlashOverlayWindow>();
        try
        {
            // ---- the roll (WPF FlashService.cs:1860-1864) ----
            var rng = new Random(7);
            int Dealt(bool clickable = true, int generation = 0) =>
                Enumerable.Range(0, 400).Count(_ => FlashOverlay.RollNatasha(s, clickable, generation, rng));
            FlashOverlay.IdleSeconds = () => 5;
            s.ChasterFlashDodge = false;
            Assert.Equal(0, Dealt());                       // the setting is off: flashes are never red
            s.ChasterFlashDodge = true;
            Assert.InRange(Dealt(), 10, 90);                // about one in ten
            Assert.Equal(0, Dealt(clickable: false));       // cannot be dodged: never dealt
            Assert.Equal(0, Dealt(generation: 1));          // hydra children never
            FlashOverlay.IdleSeconds = () => NatashasFavourite.DodgeIdleSec + 1;
            Assert.Equal(0, Dealt());                       // walked away
            FlashOverlay.IdleSeconds = () => -1;
            Assert.Equal(0, Dealt());                       // nobody can tell: never dealt

            // ---- the dodge (WPF :2772-2800) ----
            FlashOverlayWindow Red()
            {
                var w = new FlashOverlayWindow(Solid(0, 0, 255)) { IsNatasha = true };
                windows.Add(w);
                FlashOverlay.Active.Add((w, new PixelRect(100, 200, 400, 300)));
                FlashOverlay.StartNatashaDodge(w);
                Assert.True(w.DodgeRingShown);
                return w;
            }

            var ignored = Red();
            Assert.Empty(booked);                           // nothing books while the ring runs
            rings[^1]();
            Assert.Equal((NatashasFavourite.EventId, NatashasFavourite.Seconds, true), Assert.Single(booked));   // +5:00, unprompted
            Assert.False(ignored.DodgeRingShown);
            booked.Clear();

            var flung = Red();
            flung.RaiseFlung();
            Assert.True(flung.NatashaDodged);
            Assert.False(flung.DodgeRingShown);
            rings[^1]();
            Assert.Empty(booked);

            var cleared = Red();                            // panic / stop took it down: not up, books nothing
            FlashOverlay.Active.RemoveAll(e => e.Window == cleared);
            rings[^1]();
            Assert.Empty(booked);

            // Under the ten-minute safety hold no flash is dealt at all.
            FlashOverlay.IdleSeconds = () => 5;
            chaster.NoteSafetyExit();
            Assert.Equal(0, Dealt());
            var late = Red();                               // one already up when the hold armed: refused
            rings[^1]();
            Assert.Empty(booked);
            Assert.Equal(0, chaster.BalanceSeconds - NatashasFavourite.Seconds);
        }
        finally
        {
            foreach (var w in windows) { FlashOverlay.Active.RemoveAll(e => e.Window == w); w.Close(); }
            ChasterHead.Service = service; FlashOverlay.IdleSeconds = idle; FlashOverlay.DodgeTimer = timer;
            s.ChasterFlashDodge = dodge;
            chaster.Dispose();
            new SecretChasterTokenStore().Clear();
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Luminance_is_the_pictures_own_brightness() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var white = Solid(255, 255, 255);
        using var black = Solid(0, 0, 0);
        using var red = Solid(0, 0, 255);
        using var clear = Solid(0, 0, 0, 0);
        Assert.InRange(FlashOverlay.SampleLuminanceCore(white), 0.98, 1.0);
        Assert.InRange(FlashOverlay.SampleLuminance(black), 0.0, 0.02);
        Assert.InRange(FlashOverlay.SampleLuminance(red), 0.27, 0.33);   // Rec. 601: 0.299 for pure red
        Assert.Equal(0, FlashOverlay.SampleLuminance(clear));            // a transparent picture is not bright
        Assert.Equal(-1, FlashOverlay.SampleLuminance(null));
        // With no haptics service (or the switch off) nothing is sampled and nothing is pushed.
        if (CoreHaptics.Service == null)
        {
            Assert.Equal(-1, FlashOverlay.LuminanceFor(white, "white.png"));
            FlashOverlay.PushLuminance(-1, TimeSpan.FromSeconds(3));
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void A_pinned_picture_resolves_under_the_images_folder_or_falls_back()
    {
        var root = Directory.CreateTempSubdirectory("ccp-flash-pin-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images", "set"));
            var file = Path.Combine(root, "images", "set", "a.png");
            File.WriteAllBytes(file, new byte[] { 1 });
            Assert.Equal(file, FlashOverlay.ResolveOverride(Path.Combine("set", "a.png"), root));   // rooted under assets/images
            Assert.Equal(file, FlashOverlay.ResolveOverride(file, root));                           // absolute as given
            Assert.Null(FlashOverlay.ResolveOverride(Path.Combine("set", "gone.png"), root));       // missing: random instead
            Assert.Null(FlashOverlay.ResolveOverride(null, root));
            Assert.Null(FlashOverlay.ResolveOverride("  ", root));
            Assert.Null(FlashOverlay.ResolveOverride("https://example.invalid/a.png", root));       // never fetched from here
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
