using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Back Room media feed (CONTRACT section 5). The deal is the one place a user's local files
/// meet a web page, so the first test is the privacy one: no absolute path in the deal, none in the
/// log. The rest pin the dealing rules - full-path dedupe, shortfall fill, empty and remote-only
/// pools, and that one seed always deals the same hand.
/// </summary>
public sealed class BackRoomMediaTests : IDisposable
{
    private sealed class Capture : ILogEventSink
    {
        public readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private readonly string _root;
    private readonly Capture _capture = new();
    private readonly ILogger _logger;

    public BackRoomMediaTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-br-media-" + Guid.NewGuid().ToString("N"), "PrivateUserName", "assets");
        Directory.CreateDirectory(Path.Combine(_root, "images"));
        _logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_capture).CreateLogger();
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(_root))!, true); } catch { }
    }

    private string Gif(string rel, int w = 320, int h = 240)
    {
        var path = Path.Combine(_root, "images", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var b = new byte[32];
        "GIF89a"u8.CopyTo(b);
        b[6] = (byte)w; b[7] = (byte)(w >> 8); b[8] = (byte)h; b[9] = (byte)(h >> 8);
        File.WriteAllBytes(path, b);
        return path;
    }

    private string Webp(string rel, bool animated, int w = 480, int h = 270)
    {
        var path = Path.Combine(_root, "images", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var b = new byte[40];
        "RIFF"u8.CopyTo(b); "WEBPVP8X"u8.CopyTo(b.AsSpan(8));
        b[20] = (byte)(animated ? 0x12 : 0x10);
        int w1 = w - 1, h1 = h - 1;
        b[24] = (byte)w1; b[25] = (byte)(w1 >> 8); b[26] = (byte)(w1 >> 16);
        b[27] = (byte)h1; b[28] = (byte)(h1 >> 8); b[29] = (byte)(h1 >> 16);
        File.WriteAllBytes(path, b);
        return path;
    }

    private BackRoomMedia Media(IEnumerable<string> images, IEnumerable<string>? words = null,
        Func<string, string, string>? lex = null)
    {
        var imgs = images.ToList();
        var wds = (words ?? Array.Empty<string>()).ToList();
        return new BackRoomMedia(() => imgs, () => wds, () => _root, lex, () => _logger);
    }

    private string LogText() => string.Join("\n", _capture.Events.Select(e =>
        e.RenderMessage() + " " + string.Join(" ", e.Properties.Values.Select(v => v.ToString()))
        + " " + e.Exception));

    [Fact]
    public void Privacy_DealAndLogCarryNoLocalPaths()
    {
        var files = new[]
        {
            Gif("secret folder/my private.gif"), Webp("nested/deeper/loop.webp", animated: true),
            Gif("a.gif"), Gif("b#c.gif"), Gif("z.gif"),
        };
        var media = Media(files.Append(Path.Combine(Path.GetTempPath(), "outside.gif")),
            new[] { "Secret phrase one", "Two" });

        var deal = media.Deal("slot", 918273);
        var json = JsonConvert.SerializeObject(deal);

        Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", json);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\'), json, StringComparison.OrdinalIgnoreCase);
        Assert.False(Regex.IsMatch(json, @"\b[A-Za-z]:(\\\\|/)"), "drive-letter path in deal: " + json);
        Assert.DoesNotContain("\\\\", json);
        Assert.DoesNotContain("file:", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(deal.Gifs, g => Assert.True(
            g.Url.StartsWith("https://ccp.assets/images/", StringComparison.Ordinal)
            || g.Url.StartsWith(BackRoomMedia.FallbackBase, StringComparison.Ordinal), g.Url));
        // Encoded exactly like the Arcademy manifest, so a space or a # can never break out of the url.
        Assert.All(deal.Gifs.Where(g => g.Src == "pool"), g => Assert.DoesNotContain(" ", g.Url));

        var log = LogText();
        Assert.NotEmpty(_capture.Events);
        Assert.DoesNotContain(_root, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", log);
        Assert.DoesNotContain("private", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ccp.assets", log);
        Assert.DoesNotContain("Secret phrase", log);
        Assert.False(Regex.IsMatch(log, @"\b[A-Za-z]:\\"), "drive-letter path in log: " + log);
    }

    [Fact]
    public void Privacy_AFailingListingLogsNoMessage()
    {
        var media = new BackRoomMedia(
            () => throw new IOException("Access denied: " + _root),
            () => throw new IOException("Access denied: " + _root),
            () => _root, null, () => _logger);

        var deal = media.Deal("slot", 1);

        Assert.All(deal.Gifs, g => Assert.Equal("fallback", g.Src));
        Assert.DoesNotContain(_root, LogText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateUserName", LogText());
    }

    [Fact]
    public void Dedupe_IsByFullPath_NotByName()
    {
        var one = Gif("x/same.gif");
        var two = Gif("y/same.gif");
        var aliasOfOne = Path.Combine(_root, "images", "x", "..", "X", "SAME.gif");

        var deal = Media(new[] { one, aliasOfOne, one.ToUpperInvariant(), two }).Deal("slot", 5);

        var pool = deal.Gifs.Where(g => g.Src == "pool").Select(g => g.Url).ToList();
        Assert.Equal(2, pool.Count);
        Assert.Equal(pool.Count, pool.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("https://ccp.assets/images/x/same.gif", pool, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("https://ccp.assets/images/y/same.gif", pool, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shortfall_FillsFallbackArtAndPresetsInOrder()
    {
        var deal = Media(new[] { Gif("a.gif", 64, 48), Webp("still.webp", animated: false), Gif("b.gif") },
                new[] { "Relax", "Obey" })
            .Deal("slot", 42);

        Assert.Equal(new[] { "g0", "g1", "g2", "g3" }, deal.Gifs.Select(g => g.Key));
        Assert.Equal(new[] { "pool", "pool", "fallback", "fallback" }, deal.Gifs.Select(g => g.Src));
        Assert.Equal(BackRoomMedia.FallbackBase + "gif2.webp", deal.Gifs[2].Url);
        Assert.Equal(BackRoomMedia.FallbackBase + "gif3.webp", deal.Gifs[3].Url);
        Assert.Contains(deal.Gifs, g => g.W == 64 && g.H == 48);

        // Pool words first (shuffled), then presets in contract order, skipping one already dealt.
        Assert.Equal(new[] { "s0", "s1", "s2", "s3" }, deal.Words.Select(w => w.Key));
        Assert.Equal(new[] { "Obey", "Relax" }, deal.Words.Take(2).Select(w => w.Text).OrderBy(t => t));
        Assert.Equal(new[] { "Drop", "Let Go" }, deal.Words.Skip(2).Select(w => w.Text));
        Assert.All(deal.Words.Skip(2), w => Assert.Equal("preset", w.Src));
    }

    [Fact]
    public void EmptyPools_DealAllFallbacksAndLexiconPresets()
    {
        var deal = Media(Array.Empty<string>(), Array.Empty<string>(),
                (key, fallback) => key == "br_word_sink" ? "Melt" : fallback)
            .Deal("slot", 0);

        Assert.All(deal.Gifs, g => Assert.Equal("fallback", g.Src));
        Assert.Equal(new[] { "Drop", "Relax", "Let Go", "Melt" }, deal.Words.Select(w => w.Text));
        Assert.All(deal.Words, w => Assert.Equal("preset", w.Src));
    }

    [Fact]
    public void RemoteOnlyPool_NeverDealsAUrlItDidNotMint()
    {
        var remote = new[] { "https://cdn.example.com/a.gif", "http://cdn.example.com/b.webp" };
        var deal = Media(remote).Deal("slot", 7);

        Assert.All(deal.Gifs, g => Assert.Equal("fallback", g.Src));
        Assert.DoesNotContain("example.com", JsonConvert.SerializeObject(deal));
    }

    [Fact]
    public void OnlyAnimatedFilesUnderTheRootAreDealt()
    {
        var outsideDir = Path.Combine(Path.GetDirectoryName(_root)!, "elsewhere");
        Directory.CreateDirectory(outsideDir);
        var outside = Path.Combine(outsideDir, "o.gif");
        File.Copy(Gif("seed.gif"), outside);
        var png = Path.Combine(_root, "images", "p.png");
        File.WriteAllBytes(png, "GIF89a0000"u8.ToArray());
        var fakeGif = Path.Combine(_root, "images", "fake.gif");
        File.WriteAllText(fakeGif, "not an image at all, just text");

        var deal = Media(new[] { outside, png, fakeGif, Webp("anim.webp", animated: true, 481, 271),
            Path.Combine(_root, "images", "missing.gif") }).Deal("slot", 3);

        var pool = deal.Gifs.Where(g => g.Src == "pool").ToList();
        var only = Assert.Single(pool);
        Assert.StartsWith("https://ccp.assets/images/anim.webp", only.Url);
        Assert.Equal((481, 271), (only.W, only.H));
    }

    [Fact]
    public void Seed_DealsTheSameHandWhateverTheListingOrder()
    {
        var files = Enumerable.Range(0, 20).Select(i => Gif($"f{i:D2}.gif")).ToList();
        var words = Enumerable.Range(0, 10).Select(i => "word" + i).ToList();

        var a = Media(files, words).Deal("slot", 918273);
        var b = Media(Enumerable.Reverse(files), Enumerable.Reverse(words)).Deal("slot", 918273);

        Assert.Equal(JsonConvert.SerializeObject(a), JsonConvert.SerializeObject(b));
        Assert.Equal(918273, a.Seed);
        Assert.Equal(4, a.Gifs.Select(g => g.Url).Distinct().Count());

        // And the seed actually matters.
        var hands = Enumerable.Range(1, 8)
            .Select(s => string.Join("|", Media(files, words).Deal("slot", s).Gifs.Select(g => g.Url)))
            .Distinct().Count();
        Assert.True(hands > 1);
    }
}
