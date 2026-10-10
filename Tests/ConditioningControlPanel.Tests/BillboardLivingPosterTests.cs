using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The living poster (owner, 2026-10-07: the still house cards get the dashboard logo's
/// treatment): every pose keeps the picture covering the card, the settled crop stays near 4
/// percent, the landing pop and the sheen come and go, and Pause / Release stop the clock.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BillboardLivingPosterTests
{
    [Theory]
    [InlineData(1600, 900)]
    [InlineData(1000, 420)]
    [InlineData(800, 600)]
    public void Every_pose_covers_the_card(double w, double h)
    {
        var rng = new Random(7);
        for (int i = 0; i < 4000; i++)
        {
            var pointer = new Vector(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
            double energy = rng.NextDouble(), phase = rng.NextDouble() * Math.Tau * 3;
            double land = PosterMotion.Land(rng.NextDouble() * PosterMotion.LandSeconds);
            var pose = PosterMotion.At(phase, pointer, energy, land, w, h);
            Assert.True(PosterMotion.Covers(pose, w, h), $"edge shows at {pose}");
        }
    }

    [Fact]
    public void The_settled_crop_stays_near_four_percent_on_a_poster_shaped_card()
    {
        var rng = new Random(11);
        double max = 0;
        for (int i = 0; i < 4000; i++)
        {
            var pose = PosterMotion.At(rng.NextDouble() * Math.Tau, new Vector(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1), rng.NextDouble(), 0, 1600, 900);
            max = Math.Max(max, pose.Scale);
        }
        Assert.InRange(max, PosterMotion.BaseScale, 1.0405);
        Assert.Equal(PosterMotion.BaseScale, PosterMotion.Still.Scale);
    }

    [Fact]
    public void The_landing_pop_kicks_dips_and_settles()
    {
        Assert.Equal(PosterMotion.LandKick, PosterMotion.Land(0), 6);
        Assert.Equal(0, PosterMotion.Land(PosterMotion.LandSeconds));
        Assert.Equal(0, PosterMotion.Land(-1));
        Assert.True(PosterMotion.Land(PosterMotion.LandSeconds * 0.66) < 0);
        Assert.True(PosterMotion.Land(PosterMotion.LandSeconds * 0.66) > -PosterMotion.LandKick * 0.2);
    }

    [Fact]
    public void The_sheen_sweeps_now_and_then()
    {
        Assert.Equal(0, PosterMotion.Sheen(0));
        Assert.InRange(PosterMotion.Sheen(PosterMotion.SweepSeconds / 2), 0.49, 0.51);
        Assert.Equal(-1, PosterMotion.Sheen(PosterMotion.SweepSeconds + 0.5));
        Assert.Equal(0, PosterMotion.Sheen(PosterMotion.SheenCycleSeconds), 6);
        Assert.Equal(-1, PosterMotion.Sheen(double.NaN));
    }

    [Fact]
    public void The_poster_view_steps_and_stops_its_clock() => WpfRenderHarness.OnStaThread(() =>
    {
        var view = new PosterArtView("billboard/discord.png");
        Realize(view, 960, 420);
        var still = view.CurrentMatrix;
        Assert.Equal(PosterMotion.BaseScale, still.M11, 3);

        view.Play();
        Assert.Equal(MotionFx.AllowAmbientLoops, view.IsAnimating);
        view.Step(0.4, hovered: true, new Point(0.9, 0.2));
        view.Step(0.4, hovered: true, new Point(0.9, 0.2));
        Assert.NotEqual(still, view.CurrentMatrix);

        view.Pause();
        Assert.False(view.IsAnimating);
        view.Play();
        view.Release();
        Assert.False(view.IsAnimating);
        view.Play(); // released for good
        Assert.False(view.IsAnimating);
    });

    /// <summary>Writes a few frames of a poster card (and the host with its button hovered) to
    /// CCP_POSTER_SHOTS when that folder is set. A look check, not a test of the look.</summary>
    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_POSTER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        foreach (var name in new[] { "discord", "webapp", "loom" })
        {
            var view = new PosterArtView($"billboard/{name}.png") { Accent = Color.FromRgb(0x9b, 0x7b, 0xff) };
            var host = Realize(view, 960, 420);
            Save(host, Path.Combine(dir, $"{name}-0-still.png"));
            view.Play();
            view.Step(0.05, false, default);
            Save(host, Path.Combine(dir, $"{name}-1-land.png"));
            for (int i = 0; i < 20; i++) view.Step(0.033, false, default);
            Save(host, Path.Combine(dir, $"{name}-2-rest.png"));
            for (int i = 0; i < 30; i++) view.Step(0.033, true, new Point(0.85, 0.25));
            Save(host, Path.Combine(dir, $"{name}-3-hover.png"));
            view.Release();
        }

        BuiltInArt.Register();
        var list = new List<BillboardCardSpec>
        {
            new("house:discord", BillboardCardKind.House, 0, "the server", "Come say hi", "events, streams and the lobby", "#9b7bff",
                BuiltInArtKeys.Poster, "billboard/discord.png", new BillboardAction(BillboardActionKind.Tab, "availablesubjects", "Join Discord")),
        };
        var deck = new BillboardDeck(() => new IBillboardProvider[] { new Fixed(list) },
            () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.UtcNow));
        var card = new BillboardCardHost(deck);
        var root = Realize(card, 1000, 420);
        card.Begin();
        root.UpdateLayout();
        Save(root, Path.Combine(dir, "host-0-rest.png"));
        typeof(BillboardCardHost).GetMethod("Lift", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, new object[] { true });
        root.UpdateLayout();
        Save(root, Path.Combine(dir, "host-1-hover.png"));
    });

    private sealed class Fixed : IBillboardProvider
    {
        private readonly List<BillboardCardSpec> _cards;
        public Fixed(List<BillboardCardSpec> cards) => _cards = cards;
        public string Id => "fixed";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => _cards;
        public void Invoke(string actionTarget) { }
        public event EventHandler? Changed { add { } remove { } }
    }

    private static Grid Realize(FrameworkElement element, double w, double h)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Width = w, Height = h };
        host.Children.Add(element);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        return host;
    }

    private static void Save(FrameworkElement e, string path)
    {
        var bmp = new RenderTargetBitmap((int)e.ActualWidth, (int)e.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
