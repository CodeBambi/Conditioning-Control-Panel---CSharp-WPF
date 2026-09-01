using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#1086 — Lost &amp; Found lagged so hard on a library of ANIMATED .webp images that a
/// click on the right tile registered as a miss.
///
/// <para>Not a decode failure: WebView2 plays animated webp perfectly well, which is precisely the
/// problem. The wall's whole media layer is built on a budget over DISTINCT ANIMATED URLS (one
/// Chromium decoder + one main-thread animation clock each, board.js §4), and every gate in it —
/// the live window, the sleeper draw, the frame governor's shed — asks the same question of a bare
/// url: <c>/\.gif(\?|#|$)/</c>. A webp's animation lives in its VP8X container flag, not its name,
/// so every animated webp answered "still", none were counted, none could be shed, and a dense
/// board dealt one decoder per seat instead of ~30.</para>
///
/// <para>The page cannot fix that — it never has the bytes. The DESKTOP host does, so it header-
/// probes a sampled local webp and stamps <see cref="ArcademyHostService.AnimatedImageHint"/> on
/// the url, riding the fragment-hint convention <c>provider/index.js hintedPileUrl()</c> already
/// uses for extension-less <c>blob:</c> rows. Two halves have to stay true for that to work, and
/// both are pinned here: the C# probe must be right, and the hint must be a shape the page-side
/// regexes actually read.</para>
/// </summary>
public class ArcademyAnimatedWebpHintTests
{
    // =====================================================================================
    //  fixtures — hand-built WebP container headers (the probe reads 21 bytes, no more)
    // =====================================================================================

    /// <summary>A RIFF/WEBP file whose VP8X extended header sets (or clears) the animation flag.
    /// Only the first 21 bytes carry meaning for the probe; the tail is padding.</summary>
    private static byte[] Vp8xHeader(bool animated)
    {
        var b = new byte[32];
        void Ascii(int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
        Ascii(0, "RIFF");
        b[4] = 24;                       // file size, little-endian — unread by the probe
        Ascii(8, "WEBP");
        Ascii(12, "VP8X");
        b[16] = 10;                      // chunk payload size
        b[20] = (byte)(animated ? 0x02 : 0x00);   // libwebp ANIMATION_FLAG
        return b;
    }

    /// <summary>A plain (simple-format) still webp: no VP8X chunk at all.</summary>
    private static byte[] Vp8Still()
    {
        var b = new byte[32];
        void Ascii(int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
        Ascii(0, "RIFF");
        Ascii(8, "WEBP");
        Ascii(12, "VP8 ");
        return b;
    }

    private static string WriteTemp(string extension, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(),
            "ccp-1086-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // =====================================================================================
    //  half one — the probe
    // =====================================================================================

    [Fact]
    public void AnAnimatedWebpIsRecognisedAsAnimated()
    {
        var path = WriteTemp(".webp", Vp8xHeader(animated: true));
        try
        {
            Assert.True(AnimatedWebp.IsAnimated(path), "VP8X with ANIMATION_FLAG set reads as still");
            Assert.True(ArcademyHostService.IsAnimatedLocalImage(path),
                "the Arcademy host must class an animated webp as a loop, not a still");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]   // VP8X, animation flag clear
    [InlineData(true)]    // simple VP8, no extended header
    public void AStillWebpStaysAStill(bool simpleFormat)
    {
        var path = WriteTemp(".webp", simpleFormat ? Vp8Still() : Vp8xHeader(animated: false));
        try
        {
            Assert.False(AnimatedWebp.IsAnimated(path));
            Assert.False(ArcademyHostService.IsAnimatedLocalImage(path),
                "a still webp must keep its seat on the sleeping side of the live window");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".gif")]
    public void OnlyWebpIsEverProbed(string extension)
    {
        // Every other extension already tells the truth by name — a .gif is dealt as a loop on
        // sight, a .png cannot animate — so the probe must not spend a file open on them. The
        // bytes here ARE an animated webp header; the extension gate is what has to refuse it.
        var path = WriteTemp(extension, Vp8xHeader(animated: true));
        try
        {
            Assert.False(ArcademyHostService.IsAnimatedLocalImage(path),
                extension + " must be classed by name, never by a header read");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMissingOrTruncatedFileIsNotAnimated()
    {
        Assert.False(AnimatedWebp.IsAnimated(Path.Combine(Path.GetTempPath(), "ccp-1086-nope.webp")));

        var stub = WriteTemp(".webp", new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        try { Assert.False(AnimatedWebp.IsAnimated(stub), "a 4-byte file must not read past its end"); }
        finally { File.Delete(stub); }
    }

    // =====================================================================================
    //  half two — the hint, as the page actually reads it
    // =====================================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadWebSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel", "Resources", "web", "arcademy" }
            .Concat(parts).ToArray()));

    /// <summary>Every page-side "does this url cost a decoder and a clock" test, by file. They are
    /// separate copies of one regex by design (a game may not reach into engine internals), which
    /// is exactly why a hint shape has to be checked against all of them rather than one.</summary>
    public static TheoryData<string, string> AnimatedUrlTests() => new()
    {
        { "games/lost-and-found/board.js", "GIF_RE" },
        { "games/instant-recall/montage.js", "GIF_RE" },
        { "engine/util.js", "GIF_URL_RE" },
    };

    [Theory]
    [MemberData(nameof(AnimatedUrlTests))]
    public void TheHintIsReadByEveryAnimatedUrlTest(string file, string constant)
    {
        var src = ReadWebSource(file.Split('/'));
        var decl = Regex.Match(src, @"\b" + Regex.Escape(constant) + @"\s*=\s*/(?<body>.+?)/[a-z]*\s*;");
        Assert.True(decl.Success, constant + " is no longer a regex literal in " + file);

        // The JS source is the single source of truth: the pattern is lifted out of it and run
        // here, so a future edit that tightens it (say, to /\.gif$/) fails this test instead of
        // silently un-budgeting every animated webp again.
        var re = new Regex(decl.Groups["body"].Value, RegexOptions.IgnoreCase);
        var hinted = "https://ccp.assets/images/loop.webp" + ArcademyHostService.AnimatedImageHint;

        Assert.True(re.IsMatch(hinted),
            file + "'s " + constant + " no longer reads the animated-webp hint (ccp-bugs#1086)");
        Assert.False(re.IsMatch("https://ccp.assets/images/loop.webp"),
            "an UNHINTED webp must stay a still — the url alone cannot know");
        Assert.True(re.IsMatch("https://ccp.assets/images/loop.gif"),
            "a real gif must still read as animated");
    }

    [Fact]
    public void TheHintIsAFragment()
    {
        // Load-bearing: the URL Standard drops the fragment before the fetch, so the hinted url
        // resolves to byte-for-byte the same file under the ccp.assets virtual-host mapping. A
        // query string would be part of the request and is NOT guaranteed to resolve there.
        Assert.StartsWith("#", ArcademyHostService.AnimatedImageHint, StringComparison.Ordinal);
        Assert.DoesNotContain("?", ArcademyHostService.AnimatedImageHint, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProviderResolvesTheHintAheadOfTheExtension()
    {
        // kindOf() buckets a url into the loop or still pool. extOf() strips the fragment, so
        // without a hint reader a stamped webp would land back in the still pool and the fix
        // would only half-work.
        var src = ReadWebSource("provider", "inventory.js");
        Assert.Contains("export function hintExtOf(", src, StringComparison.Ordinal);

        var kindOf = Regex.Match(src, @"export function kindOf\(entry\)\s*\{(?<body>[\s\S]*?)\n\}");
        Assert.True(kindOf.Success, "kindOf is no longer a plain function declaration");
        Assert.Contains("hintExtOf(", kindOf.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostAdmitsWebpToBothLocalLanes()
    {
        // The extension cannot say which lane a webp belongs to, so it is a candidate for both
        // and the probe decides. Dropping it back out of the loop lane would leave an
        // animated-webp-only library with an empty loop pool and a dead-looking wall.
        var host = SourceRoots.ReadProductFile("Services", "Arcademy", "ArcademyHostService.cs");
        var loop = Regex.Match(host, @"LocalLoopExts\s*=\s*\{(?<body>[^}]*)\}");
        Assert.True(loop.Success, "LocalLoopExts is no longer an array initialiser");
        Assert.Contains("\".webp\"", loop.Groups["body"].Value, StringComparison.Ordinal);
    }
}
