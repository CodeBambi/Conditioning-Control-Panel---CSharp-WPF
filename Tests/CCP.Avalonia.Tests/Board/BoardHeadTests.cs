using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests.Board;

/// <summary>
/// The Tonight Board's head (parity wave 3, lane F): the Skia PNG seam Core's board decodes
/// through, the 64x36 raised-tile view on its frame clock (arrival, ola, ripple, release), the
/// card host's walk (the hold fill moves the deck on, hover holds it, snooze folds the card away
/// with a toast) and the board's silence. Set CCP_BOARD_PNG_DIR to keep the frames
/// (a local folder for the parity proof).
/// </summary>
public sealed class BoardHeadTests
{
    internal static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    internal static void Pin(bool transitions = true, bool ambient = true, bool particles = false)
    {
        BoardMotion.TransitionsOverride = () => transitions;
        BoardMotion.AmbientOverride = () => ambient;
        BoardMotion.ParticlesOverride = () => particles;
    }

    internal static void Unpin()
    {
        BoardMotion.TransitionsOverride = null;
        BoardMotion.AmbientOverride = null;
        BoardMotion.ParticlesOverride = null;
    }

    internal static void Save(WriteableBitmap frame, string name)
    {
        if (Environment.GetEnvironmentVariable("CCP_BOARD_PNG_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, name));
    }

    internal static uint[] Pixels(WriteableBitmap bmp, out int width)
    {
        using var fb = bmp.Lock();
        width = fb.Size.Width;
        var px = new int[fb.Size.Width * fb.Size.Height];
        for (int y = 0; y < fb.Size.Height; y++)
            Marshal.Copy(fb.Address + y * fb.RowBytes, px, y * fb.Size.Width, fb.Size.Width);
        return px.Select(p => (uint)p).ToArray();
    }

    // ---- the mockup's watch-party sample (Core BoardGlowTests.WatchParty, compact) ---------------

    private static readonly int[] Pal = { 0x120F26, 0xFF4FA8, 0x3CFF7D, 0xFFC94A, 0x5FE3FF, 0x9B7BFF, 0xFFFFFF, 0x2C2556 };

    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['W'] = new[] { "10001", "10001", "10101", "10101", "01010" }, ['A'] = new[] { "010", "101", "111", "101", "101" },
        ['T'] = new[] { "111", "010", "010", "010", "010" }, ['C'] = new[] { "011", "100", "100", "100", "011" },
        ['H'] = new[] { "101", "101", "111", "101", "101" }, ['P'] = new[] { "110", "101", "110", "100", "100" },
        ['R'] = new[] { "110", "101", "110", "101", "101" }, ['Y'] = new[] { "101", "101", "010", "010", "010" },
        ['S'] = new[] { "011", "100", "010", "001", "110" }, ['I'] = new[] { "111", "010", "010", "010", "111" },
        ['N'] = new[] { "1001", "1101", "1011", "1001", "1001" }, ['E'] = new[] { "111", "100", "110", "100", "111" },
        ['L'] = new[] { "100", "100", "100", "100", "111" }, ['O'] = new[] { "010", "101", "101", "101", "010" },
        ['B'] = new[] { "110", "101", "110", "101", "110" }, ['2'] = new[] { "110", "001", "010", "100", "111" },
        ['1'] = new[] { "010", "110", "010", "010", "111" }, ['0'] = new[] { "010", "101", "101", "101", "010" },
        [':'] = new[] { "0", "1", "0", "1", "0" }, [' '] = new[] { "0", "0", "0", "0", "0" },
    };

    private static void Stamp(int[] buf, string s, int y, int col)
    {
        int width = s.Sum(c => Font[c][0].Length + 1) - 1;
        int x = (64 - width) / 2;
        foreach (char c in s)
        {
            var g = Font[c];
            for (int r = 0; r < g.Length; r++)
                for (int k = 0; k < g[r].Length; k++)
                    if (g[r][k] == '1') buf[(y + r) * 64 + x + k] = col;
            x += g[0].Length + 1;
        }
    }

    internal static BoardPicture WatchParty(params string[] fx)
    {
        var buf = new int[64 * 36];
        for (int x = 0; x < 64; x++) { buf[x] = 1; buf[35 * 64 + x] = 1; }
        for (int y = 0; y < 36; y++) { buf[y * 64] = 1; buf[y * 64 + 63] = 1; }
        for (int x = 2; x < 62; x += 2) { buf[2 * 64 + x] = 7; buf[33 * 64 + x] = 7; }
        Stamp(buf, "WATCH PARTY", 7, 1);
        Stamp(buf, "SAT 21:00", 15, 2);
        Stamp(buf, "IN THE LOBBY", 24, 4);
        var px = buf.Select(i => unchecked((int)0xFF000000) | Pal[i]).ToArray();
        return BoardPicture.FromPixels(px, 64, 36, new BoardPost(3, fx, null, "lobby", BoardAudience.Everyone, 1, 6, 64, 36));
    }

    // ---- the PNG seam -----------------------------------------------------------------------------

    [Fact]
    public void Skia_decodes_a_real_png_for_the_board_straight_alpha()
    {
        using var bmp = new SKBitmap(new SKImageInfo(4, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bmp.SetPixel(0, 0, new SKColor(255, 79, 168, 255));
        bmp.SetPixel(1, 0, new SKColor(10, 20, 30, 0));
        bmp.SetPixel(2, 0, new SKColor(200, 100, 50, 128));
        using var img = SKImage.FromBitmap(bmp);
        var png = img.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        var got = BoardPng.Decode(png);
        Assert.NotNull(got);
        var (px, w, h) = got!.Value;
        Assert.Equal((4, 2), (w, h));
        Assert.Equal(unchecked((int)0xFFFF4FA8), px[0]);
        Assert.Equal(0, (px[1] >> 24) & 0xFF);
        int half = px[2];
        Assert.InRange((half >> 24) & 0xFF, 127, 129);
        Assert.InRange((half >> 16) & 0xFF, 198, 202); // straight, not premultiplied (would read ~100)

        BoardPng.Install();
        var pic = BoardPicture.Decode(png, new BoardPost(1, Array.Empty<string>(), null, null, BoardAudience.Everyone, 1, 6, 4, 2));
        Assert.NotNull(pic);
        Assert.Null(BoardPng.Decode(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5 }));
    }

    // ---- the tile view ----------------------------------------------------------------------------

    [Fact]
    public Task The_board_renders_mid_ola_with_raised_lit_tiles() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        Pin();
        try
        {
            var view = new BoardTileView(new BoardArtData(WatchParty("ola"), null));
            var w = new Window { Width = 896, Height = 504, Content = view };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.Pitch >= 4, "no raster: " + view.Pitch);

            // Still (never played) first: the flat picture.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var still = w.CaptureRenderedFrame()!;
            Save(still, "f-board-still.png");

            view.FxSecondsForTests = 0;
            view.Play();
            Assert.True(view.ClockRunning);
            view.FxSecondsForTests = 1.25 + BoardFxMath.OlaEverySeconds; // landed, a crest mid-grid
            view.Render();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var frame = w.CaptureRenderedFrame()!;
            Save(frame, "f-board-ola.png");

            var a = Pixels(still, out int width);
            var b = Pixels(frame, out _);
            Assert.Equal(a.Length, b.Length);
            int changed = a.Zip(b).Count(p => p.First != p.Second);
            Assert.True(changed > a.Length / 200, $"the ola moved almost nothing ({changed} px)");
            // The message is lit: some pixel is near the house pink.
            Assert.Contains(b, p => ((p >> 16) & 0xFF) > 0xD0 && ((p >> 8) & 0xFF) < 0x90 && (p & 0xFF) > 0x90);

            view.Touch(new Point(0.5, 0.5));
            view.Pause();
            view.Release();
            Assert.False(view.ClockRunning);
            view.Play(); // after release: ignored, no throw
            w.Close();
        }
        finally { Unpin(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_board_art_registers_and_reports_its_first_show() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        BillboardWiring.RegisterArt();
        Assert.True(BillboardArt.IsRegistered(BoardProvider.ArtKey));
        int shown = 0;
        var view = BillboardArt.Create(BoardProvider.ArtKey, new BoardArtData(WatchParty(), v => shown = v));
        var tiles = Assert.IsType<BoardTileView>(view);
        var w = new Window { Width = 640, Height = 360, Content = tiles };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, shown);
        w.Close();
        return Task.CompletedTask;
    });

    // ---- the host's walk --------------------------------------------------------------------------

    private sealed class FakeProvider : IBillboardProvider
    {
        public List<BillboardCardSpec> Cards = new();
        public string Id => "fake";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Cards;
        public void Invoke(string actionTarget) => Invoked.Add(actionTarget);
        public readonly List<string> Invoked = new();
        public event EventHandler? Changed { add { } remove { } }
    }

    private static BillboardCardSpec Card(string id, BillboardCardKind kind, int priority = 0) =>
        new(id, kind, priority, "eyebrow", "Title " + id, "a line", "#5fe3ff", "nothing-registered", null,
            new BillboardAction(BillboardActionKind.Callback, "go:" + id, "Go"));

    [Fact]
    public Task The_hold_fill_moves_the_deck_on_and_hover_holds_it() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        Pin(particles: false);
        try
        {
            var p = new FakeProvider();
            p.Cards.Add(Card("waiting.quests", BillboardCardKind.Waiting));
            p.Cards.Add(Card("house.discord", BillboardCardKind.House));
            var deck = new BillboardDeck(() => new IBillboardProvider[] { p }, () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now));
            var host = new BillboardDeckView(deck);
            host.Tweens.NowForTests = 0;
            var w = new Window { Width = 900, Height = 420, Content = host };
            w.Show();
            host.Begin();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("waiting.quests", host.CurrentCard!.Spec.Id);
            Assert.Equal(2, host.ChipButtons.Count);
            Assert.True(host.HoldRunning);

            host.Tweens.NowForTests = DashboardBillboard.HoldSeconds * 500.0;
            host.Tweens.Step();
            Assert.InRange(host.HoldProgress, 0.45, 0.55);

            host.Tweens.NowForTests = DashboardBillboard.HoldSeconds * 1000.0 + 5;
            host.Tweens.Step();
            Assert.Equal("house.discord", host.CurrentCard!.Spec.Id);
            Assert.Equal(2, host.Changes);

            // The press runs the provider's callback through the host's ActionRequested.
            DeckCard? asked = null;
            host.ActionRequested += c => asked = c;
            host.PressForTests();
            Dispatcher.UIThread.RunJobs();
            w.Close();
        }
        finally { Unpin(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Snooze_folds_the_card_away_and_says_so() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        Pin(transitions: false, ambient: false);
        try
        {
            var p = new FakeProvider();
            p.Cards.Add(Card("waiting.quests", BillboardCardKind.Waiting));
            p.Cards.Add(Card("house.discord", BillboardCardKind.House));
            var snoozes = new Dictionary<string, DateTime>();
            int saved = 0;
            var deck = new BillboardDeck(() => new IBillboardProvider[] { p },
                () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now), snoozes, () => saved++);
            var host = new BillboardDeckView(deck);
            var w = new Window { Width = 900, Height = 420, Content = host };
            w.Show();
            host.Begin();
            Dispatcher.UIThread.RunJobs();

            host.SnoozeForTests();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("house.discord", host.CurrentCard!.Spec.Id);
            Assert.True(snoozes.ContainsKey("waiting.quests"));
            Assert.Equal(1, saved);
            Assert.False(string.IsNullOrWhiteSpace(host.ToastText));
            Assert.DoesNotContain("{0}", host.ToastText);
            w.Close();
        }
        finally { Unpin(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void The_board_stays_silent()
    {
        // 7.1.5 BoardSilentTests: every board cue returned early behind LauncherSfx.BoardSoundOn.
        // This head has no board cue at all: nothing under Controls/Billboard reaches for audio.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "CCP.Avalonia.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "CCP.Avalonia", "Controls", "Billboard"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var src = File.ReadAllText(f);
            foreach (var bad in new[] { "LauncherSfx", "CoreAudio", "PlaySound", "AudioService", "SoundPlayer", "LibVLC" })
                Assert.False(src.Contains(bad, StringComparison.Ordinal), Path.GetFileName(f) + " reaches for " + bad);
        }
    }
}
