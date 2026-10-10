using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// PORTED from Tests/ConditioningControlPanel.Tests/ArcademyAnimatedWebpHintTests.cs (7.1.5),
/// ccp-bugs#1086: an animated .webp is a loop wearing a still's extension. The host probes the
/// header and stamps <c>#.gif</c> on the url; every page-side budget must read that hint. The page
/// tree is Assets/web here and the host rules live in Core (ArcademyLocalMedia).
/// </summary>
public class ArcademyAnimatedWebpHintTests
{
    private static byte[] Vp8xHeader(bool animated)
    {
        var b = new byte[32];
        void Ascii(int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
        Ascii(0, "RIFF");
        b[4] = 24;
        Ascii(8, "WEBP");
        Ascii(12, "VP8X");
        b[16] = 10;
        b[20] = (byte)(animated ? 0x02 : 0x00);   // libwebp ANIMATION_FLAG
        return b;
    }

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
        var path = Path.Combine(Path.GetTempPath(), "ccp-1086-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, bytes);
        return path;
    }

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
            Assert.False(ArcademyHostService.IsAnimatedLocalImage(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".gif")]
    public void OnlyWebpIsEverProbed(string extension)
    {
        // The bytes ARE an animated webp header; the extension gate is what has to refuse it.
        var path = WriteTemp(extension, Vp8xHeader(animated: true));
        try { Assert.False(ArcademyHostService.IsAnimatedLocalImage(path), extension + " must be classed by name"); }
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

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "web", "arcademy")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    internal static string ReadWebSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Assets", "web", "arcademy" }.Concat(parts).ToArray()));

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

        var re = new Regex(decl.Groups["body"].Value, RegexOptions.IgnoreCase);
        // Both url shapes: WPF's virtual host and this head's loopback path.
        foreach (var bare in new[] { "https://ccp.assets/images/loop", "http://127.0.0.1:5123/ccp.assets/images/loop" })
        {
            Assert.True(re.IsMatch(bare + ".webp" + ArcademyHostService.AnimatedImageHint),
                file + "'s " + constant + " no longer reads the animated-webp hint (ccp-bugs#1086)");
            Assert.False(re.IsMatch(bare + ".webp"), "an UNHINTED webp must stay a still");
            Assert.True(re.IsMatch(bare + ".gif"), "a real gif must still read as animated");
        }
    }

    [Fact]
    public void TheHintIsAFragment()
    {
        Assert.StartsWith("#", ArcademyHostService.AnimatedImageHint, StringComparison.Ordinal);
        Assert.DoesNotContain("?", ArcademyHostService.AnimatedImageHint, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProviderResolvesTheHintAheadOfTheExtension()
    {
        var src = ReadWebSource("provider", "inventory.js");
        Assert.Contains("export function hintExtOf(", src, StringComparison.Ordinal);
        var kindOf = Regex.Match(src, @"export function kindOf\(entry\)\s*\{(?<body>[\s\S]*?)\n\}");
        Assert.True(kindOf.Success, "kindOf is no longer a plain function declaration");
        Assert.Contains("hintExtOf(", kindOf.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostAdmitsWebpToBothLocalLanes()
    {
        Assert.Contains(".webp", ArcademyLocalMedia.LocalLoopExts);
        Assert.Contains(".webp", ArcademyLocalMedia.LocalStillExts);
    }

    /// <summary>Port only: this head serves the library on a loopback path, and the page must count
    /// it as LOCAL (canvas-safe, in the local pools) exactly as it counts https://ccp.assets/.</summary>
    [Fact]
    public void ThePageCountsTheLoopbackHostsAsLocal()
    {
        var src = ReadWebSource("provider", "inventory.js");
        var fn = Regex.Match(src, @"export function isLocalUrl\(url\)\s*\{(?<body>[\s\S]*?)\n\}");
        Assert.True(fn.Success, "isLocalUrl is no longer a plain function declaration");
        Assert.Contains(@"127\.0\.0\.1:\d+\/ccp\.[a-z]+\/", fn.Groups["body"].Value, StringComparison.Ordinal);
        var shell = ReadWebSource("shell", "shell.js");
        Assert.Contains(@"http:\/\/127\.0\.0\.1:\d+\/)ccp\.spirals\/", shell, StringComparison.Ordinal);
    }
}
