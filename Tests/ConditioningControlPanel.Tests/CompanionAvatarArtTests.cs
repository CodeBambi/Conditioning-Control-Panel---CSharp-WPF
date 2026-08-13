using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Companion hero's portrait: it must sit centred and whole inside the ring, and it must
/// follow the active mod.
///
/// <para><b>The bug this suite exists to prevent.</b> The ring painted its bust with
/// <c>Stretch=UniformToFill</c>. The shipped poses are full-body art on a tall canvas — set 1 is
/// 540x960 — so "fill" scaled by WIDTH to 132x235 inside a 132x132 circle and centre-cropped 51px
/// off each end: her head off the top, her heels off the bottom. On top of that the PNG's ink is
/// not centred on its own canvas (opaque bounds 158..417 across, 97..801 down), so the surviving
/// crop read as shoved up and to the left. Both halves are silent — no exception, no layout
/// warning, just a portrait that looks wrong — which is why the assertions below are geometric and
/// run against the REAL shipped asset rather than a stand-in.</para>
///
/// <para>The mod half is source-level for the usual reason: realizing the tab against a switched
/// mod is not something this suite can afford, and the contract that actually breaks is a wiring
/// one — <c>MainWindow.ApplyActiveModChange</c> never reaches the Companion room, so if the card
/// stops hooking <c>ModChanged</c> the ring silently keeps the previous mod's bust.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionAvatarArtTests
{
    /// <summary>The mod-contract path for the active avatar set's pose 1. Never renamed.</summary>
    private const string Bust = "avatar_pose1.png";

    /// <summary>The ring's hole, in DIP — Margin=3 inside the 138x138 portrait grid.</summary>
    private const int Hole = 132;

    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    // =====================================================================================
    //  geometry
    // =====================================================================================

    /// <summary>Renders <paramref name="brush"/> across a <see cref="Hole"/>-square and returns it.</summary>
    private static RenderTargetBitmap PaintTheHole(Brush brush)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(brush, null, new Rect(0, 0, Hole, Hole));

        var rtb = new RenderTargetBitmap(Hole, Hole, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }

    /// <summary>Bounds of everything painted, in pixels. Throws if nothing was.</summary>
    private static (int X0, int Y0, int X1, int Y1) PaintedBounds(BitmapSource bmp)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bmp.CopyPixels(px, stride, 0);

        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * stride + x * 4 + 3] > 8)
                {
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }

        Assert.True(x1 >= x0 && y1 >= y0, "nothing was painted at all");
        return (x0, y0, x1, y1);
    }

    private static ImageBrush RingBrush(ImageSource art, bool fixedUp) => fixedUp
        ? new ImageBrush(art)
        {
            // exactly what CompanionHeroCard.xaml declares plus what CentrePortrait writes
            Stretch = Stretch.Uniform,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = CompanionHeroCard.InkViewbox(art),
        }
        : new ImageBrush(art) { Stretch = Stretch.UniformToFill };

    [Fact]
    public void TheShippedBustLandsWholeAndCentredInTheRing()
    {
        OnStaThread(() =>
        {
            var art = ModResourceResolver.ResolveImage(Bust);
            Assert.NotNull(art);

            var (x0, y0, x1, y1) = PaintedBounds(PaintTheHole(RingBrush(art!, fixedUp: true)));

            // Nothing reaches an edge => nothing was cropped. This is the head-and-heels assertion.
            Assert.True(x0 >= 1 && y0 >= 1 && x1 <= Hole - 2 && y1 <= Hole - 2,
                $"the bust is touching the ring's edge (ink {x0}..{x1} x {y0}..{y1} in a {Hole}px hole) — it is being cropped again");

            // ...and it is centred on BOTH axes, which the canvas's lopsided padding used to break.
            double cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0;
            Assert.InRange(cx, Hole / 2.0 - 2.5, Hole / 2.0 + 2.5);
            Assert.InRange(cy, Hole / 2.0 - 2.5, Hole / 2.0 + 2.5);

            // A centred speck would satisfy both of the above. It has to fill the frame as well.
            Assert.InRange((y1 - y0 + 1) / (double)Hole, 0.80, 0.92);
        });
    }

    [Fact]
    public void UniformToFillIsWhatCroppedHer()
    {
        // The guard rail's other half: this is the state the assertions above reject, spelled out
        // so a future reader can see that the fix is load-bearing rather than cosmetic.
        OnStaThread(() =>
        {
            var art = ModResourceResolver.ResolveImage(Bust);
            Assert.NotNull(art);

            var (_, y0, _, y1) = PaintedBounds(PaintTheHole(RingBrush(art!, fixedUp: false)));

            Assert.True(y0 == 0 && y1 == Hole - 1,
                "UniformToFill no longer crops the shipped bust top and bottom — re-derive the fix before trusting it");
        });
    }

    [Fact]
    public void TheViewboxIsMeasuredFromTheInkNotTheCanvas()
    {
        OnStaThread(() =>
        {
            // Deliberately lopsided: a 40x100 opaque block whose centre (40, 90) is nowhere near
            // the 200x400 canvas's own centre (100, 200).
            var art = OpaqueBlock(200, 400, new Int32Rect(20, 40, 40, 100));
            var vb = CompanionHeroCard.InkViewbox(art);

            // The viewbox is expressed as fractions of the source; put it back on the pixel grid.
            double cx = (vb.X + vb.Width / 2) * 200, cy = (vb.Y + vb.Height / 2) * 400;
            Assert.InRange(cx, 37, 43);
            Assert.InRange(cy, 85, 95);

            // Square in PIXELS (which is what a square viewport needs), not in fractions.
            Assert.InRange(vb.Width * 200 - vb.Height * 400, -3, 3);

            // ...and sized so the ink's long side is ~86% of it: the margin that keeps art off the
            // circle's edge. Anything near 1.0 means the fill constant was lost.
            Assert.InRange(100 / (vb.Height * 400), 0.82, 0.90);
        });
    }

    [Fact]
    public void ArtThatCannotBeMeasuredFallsBackToTheWholeImage()
    {
        OnStaThread(() =>
        {
            var whole = new Rect(0, 0, 1, 1);
            Assert.Equal(whole, CompanionHeroCard.InkViewbox(null));
            // Fully transparent: there is no ink to centre on, and inventing one would put the
            // (empty) art somewhere arbitrary instead of leaving it as authored.
            Assert.Equal(whole, CompanionHeroCard.InkViewbox(OpaqueBlock(64, 64, Int32Rect.Empty)));
        });
    }

    private static BitmapSource OpaqueBlock(int w, int h, Int32Rect block)
    {
        int stride = w * 4;
        var px = new byte[stride * h];
        for (int y = block.Y; y < block.Y + block.Height; y++)
            for (int x = block.X; x < block.X + block.Width; x++)
            {
                int i = y * stride + x * 4;
                px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 255;
            }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        bmp.Freeze();
        return bmp;
    }

    // =====================================================================================
    //  mod awareness (source-level — see the class remarks for why)
    // =====================================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine("ConditioningControlPanel", Path.Combine(parts))));

    private static string HeroCardXaml() => AppFile("Views", "Controls", "Companion", "CompanionHeroCard.xaml");
    private static string HeroCardCode() => AppFile("Views", "Controls", "Companion", "CompanionHeroCard.xaml.cs");
    private static string HeroVm() => AppFile("Views", "Controls", "Companion", "Runtime", "CompanionHeroRuntimeVm.cs");

    [Fact]
    public void TheBrushIsNamedAndFitsRatherThanFills()
    {
        var xaml = HeroCardXaml();

        Assert.Contains("x:Name=\"PortraitBrush\"", xaml, StringComparison.Ordinal);
        // The attribute, not the word: the comment above the brush names the old mode on purpose.
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardRepaintsOnModChangedAndLetsGoOfTheHook()
    {
        var code = HeroCardCode();

        Assert.Contains("ModChanged += OnModChanged", code, StringComparison.Ordinal);
        Assert.Contains("ModChanged -= OnModChanged", code, StringComparison.Ordinal);
        // Double-hook guard: Loaded fires again on every re-parent.
        Assert.Contains("_modHooked", code, StringComparison.Ordinal);
        // ModChanged can be raised off the UI thread.
        Assert.Contains("Dispatcher.BeginInvoke(new Action(ApplyAvatarArt))", code, StringComparison.Ordinal);
        Assert.Contains("internal void ApplyAvatarArt()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBustResolvesThroughTheDecodedResolverAndNeverBlanksTheRing()
    {
        var vm = HeroVm();

        Assert.Contains("ModResourceResolver.ResolveImageDecoded(resourceName, PortraitDecodeWidth)", vm,
            StringComparison.Ordinal);
        // The "keep what is painted on a null resolve" rule the whole sweep is written to.
        Assert.Contains("Portrait = LoadPortrait() ?? Portrait;", vm, StringComparison.Ordinal);
        // The art path itself is a mod contract - every .ccpmod on disk targets this filename.
        Assert.Contains("\"avatar_pose\"", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryAvatarSetsPoseOneIsOnDisk()
    {
        // A missing bust is not a crash, it is a blank ring - which the null-resolve rule above
        // deliberately hides. This is the assertion that would not hide it.
        var resources = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");
        Assert.True(File.Exists(Path.Combine(resources, Bust)), $"{Bust} is missing from Resources/");

        for (int set = 2; set <= 5; set++)
        {
            var file = $"avatar{set}_pose1.png";
            Assert.True(File.Exists(Path.Combine(resources, file)), $"{file} is missing from Resources/");
        }
    }
}
