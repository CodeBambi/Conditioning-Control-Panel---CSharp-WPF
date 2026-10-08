using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Controls.Billboard.Scenes;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Xunit;

namespace CCP.Avalonia.Tests.Board;

/// <summary>
/// The Tonight Board's drawn scenes (WPF 7.1.5 HouseScenes A/B, TipScenes A/B, the built-in art and
/// the living poster), ported onto the WPF-shaped drawing shim: every house and tip scene and every
/// built-in key renders a non-blank card headless at a chosen moment, posters resolve to avares://
/// and the art payload helpers read the providers' data the way 7.1.5 did. Set CCP_BOARD_PNG_DIR to
/// keep the frames.
/// </summary>
public sealed class BoardScenesTests
{
    private static (uint[] Px, int W) Render(Control art, string? png, double seconds, double w = 900, double h = 420)
    {
        if (art is BillboardVectorArt v) v.SecondsForTests = seconds;
        var win = new Window { Width = w, Height = h, Content = art, Background = global::Avalonia.Media.Brushes.Black };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var frame = win.CaptureRenderedFrame()!;
        if (png != null) BoardHeadTests.Save(frame, png);
        var px = BoardHeadTests.Pixels(frame, out int width);
        win.Close();
        return (px, width);
    }

    private static int Distinct(uint[] px) => px.Select(p => p & 0xF0F0F0).Distinct().Count();

    public static IEnumerable<object[]> HouseStems() =>
        HouseProvider.Cards.Select(c => new object[] { c.Poster });

    [Theory]
    [MemberData(nameof(HouseStems))]
    public Task Every_house_scene_draws(string poster) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        try
        {
            var art = HouseScenes.Create(poster);
            Assert.IsNotType<PosterArtView>(art); // every house card has a drawn scene
            if (art is IAccentedArt a) a.Accent = global::Avalonia.Media.Color.Parse(HouseProvider.Cards.First(c => c.Poster == poster).Hue);
            var stem = System.IO.Path.GetFileNameWithoutExtension(poster);
            var (px, _) = Render(art, $"f-house-{stem}.png", 2.4);
            Assert.True(Distinct(px) > 40, $"{stem} drew almost nothing ({Distinct(px)} colours)");
        }
        finally { BoardHeadTests.Unpin(); }
        return Task.CompletedTask;
    });

    public static IEnumerable<object[]> TipIds() => TipCards.Table.Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(TipIds))]
    public Task Every_tip_scene_draws(string id) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        try
        {
            var art = TipScenes.Create(id);
            Assert.IsNotType<TipArtView>(art); // every tip has its own picture
            if (art is IAccentedArt a) a.Accent = global::Avalonia.Media.Color.Parse(CardHues.Tip);
            var (px, _) = Render(art, $"f-tip-{id}.png", 2.4);
            Assert.True(Distinct(px) > 40, $"{id} drew almost nothing ({Distinct(px)} colours)");
        }
        finally { BoardHeadTests.Unpin(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Every_built_in_key_registers_and_draws() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        try
        {
            BillboardWiring.RegisterArt();
            var payloads = new Dictionary<string, object?>
            {
                [BuiltInArtKeys.Poster] = "billboard/discord.png",
                [BuiltInArtKeys.Tables] = new Dictionary<string, int> { ["count"] = 3 },
                [BuiltInArtKeys.Wheel] = new Dictionary<string, int> { ["done"] = 2, ["total"] = 5 },
                [BuiltInArtKeys.Spiral] = null,
                [BuiltInArtKeys.Calendar] = new Dictionary<string, int> { ["counted"] = 6, ["need"] = 25, ["days"] = 31, ["today"] = 12 },
                [BuiltInArtKeys.Tip] = "quests",
                [BuiltInArtKeys.Invite] = null,
                [BuiltInArtKeys.Quests] = new Dictionary<string, int> { ["done"] = 1, ["total"] = 3 },
            };
            foreach (var key in BuiltInArtKeys.All)
            {
                Assert.True(BillboardArt.IsRegistered(key), key);
                var art = BillboardArt.Create(key, payloads[key])!;
                Assert.NotNull(art);
                var (px, _) = Render(art, $"f-art-{key}.png", 2.4);
                Assert.True(Distinct(px) > 20, $"{key} drew almost nothing ({Distinct(px)} colours)");
                (art as IBillboardArtView)?.Release();
            }
        }
        finally { BoardHeadTests.Unpin(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_living_poster_loads_its_picture_and_covers_the_card() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        try
        {
            var poster = new PosterArtView("features/deeper.png");
            Assert.True(poster.HasPicture);
            var (px, _) = Render(poster, "f-poster-deeper.png", 0);
            Assert.True(Distinct(px) > 40);
            poster.Play();
            poster.Step(0.5, true, new Point(0.9, 0.2));
            Assert.True(PosterMotion.Covers(PosterMotion.Still, 900, 420));
            poster.Release();
        }
        finally { BoardHeadTests.Unpin(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("billboard/loom.png", "avares://CCP.Avalonia/Resources/billboard/loom.png")]
    [InlineData("features/deeper.png", "avares://CCP.Avalonia/Resources/features/deeper.png")]
    [InlineData("../secrets.png", null)]
    [InlineData("/abs.png", null)]
    [InlineData("C:/x.png", null)]
    [InlineData("https://evil.example/a.png", null)]
    [InlineData("billboard/notes.txt", null)]
    [InlineData(null, null)]
    public void Poster_paths_stay_inside_the_resources(string? path, string? expected)
        => Assert.Equal(expected, BuiltInArt.PosterUri(path));

    [Fact]
    public void Art_reads_the_providers_payloads()
    {
        var now = new DateTime(2026, 10, 12);
        Assert.Equal(new CalendarInfo(6, 25, 31, 12), BuiltInArt.CalendarData(new Dictionary<string, int> { ["counted"] = 6, ["need"] = 25, ["days"] = 31, ["today"] = 12 }, now));
        Assert.Equal(new CalendarInfo(3, 14, 14, 4), BuiltInArt.CalendarData(new Dictionary<string, int> { ["day"] = 4, ["days"] = 14 }, now));
        Assert.Equal(new CalendarInfo(0, 0, 31, 12), BuiltInArt.CalendarData(null, now));
        Assert.Equal(new CalendarInfo(1, 0, 1, 1), BuiltInArt.CalendarData(new Dictionary<string, int> { ["counted"] = 90, ["need"] = -3, ["days"] = 0 }, now));
        Assert.Equal((2, 5), BuiltInArt.WheelData(new Dictionary<string, int> { ["done"] = 2, ["total"] = 5 }));
        Assert.Equal((5, 5), BuiltInArt.WheelData(new Dictionary<string, int> { ["done"] = 9, ["total"] = 5 }));
        Assert.Equal((0, 0), BuiltInArt.WheelData(null));
        Assert.Equal(2, BuiltInArt.Count(new Dictionary<string, int> { ["count"] = 2 }));
        Assert.Equal(3, BuiltInArt.Count(new Dictionary<string, int> { ["count"] = 40 }));
        Assert.Equal(3, BuiltInArt.Count(null));
    }

    [Fact]
    public void Every_house_poster_with_a_file_ships_in_the_head()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "CCP.Avalonia", "CCP.Avalonia.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var csproj = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "CCP.Avalonia", "CCP.Avalonia.csproj"));
        Assert.Contains(@"..\Assets\billboard\*.png", csproj);
        foreach (var c in HouseProvider.Cards)
        {
            if (HouseScenes.DrawnOnly(c.Poster)) continue;
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Assets", c.Poster)), c.Poster);
            Assert.NotNull(BuiltInArt.PosterUri(c.Poster));
        }
    }
}
