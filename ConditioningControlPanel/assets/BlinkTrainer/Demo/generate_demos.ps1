# Demo asset generator for Blink Trainer stage preview.
#
# Renders 4 SFW abstract pink/purple gradient PNGs at 800x600. These ship as
# embedded Resources and are shown to non-premium users behind the premium
# gate, so they must be safe for any audience. No text, no logos, no
# recognizable imagery — just colored gradients and soft circular blobs.
#
# Rerun:  powershell.exe -ExecutionPolicy Bypass -File generate_demos.ps1

Add-Type -AssemblyName System.Drawing

$cs = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class BlinkTrainerDemoRenderer
{
    const int W = 800;
    const int H = 600;

    static Color ARGB(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }
    static Color Soft(Color c, int alpha) { return Color.FromArgb(alpha, c.R, c.G, c.B); }

    public class Blob { public int X; public int Y; public int R; public Color Center; }

    public static void Render(string path, Color gStart, Color gEnd, float gAngle, Blob[] blobs)
    {
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            var rect = new Rectangle(0, 0, W, H);
            using (var bg = new LinearGradientBrush(rect, gStart, gEnd, gAngle))
                g.FillRectangle(bg, rect);

            // Soft radial blobs via PathGradientBrush — center color fades to
            // transparent at the ellipse edge for a naturally blurred falloff
            // without needing a gaussian blur pass.
            var transparent = Color.FromArgb(0, 255, 255, 255);
            foreach (var b in blobs)
            {
                using (var p = new GraphicsPath())
                {
                    p.AddEllipse(b.X - b.R, b.Y - b.R, b.R * 2, b.R * 2);
                    using (var pg = new PathGradientBrush(p))
                    {
                        pg.CenterColor = b.Center;
                        pg.SurroundColors = new[] { transparent };
                        pg.CenterPoint = new PointF(b.X, b.Y);
                        g.FillPath(pg, p);
                    }
                }
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }

    public static Color Pink    { get { return ARGB(255, 255, 105, 180); } }   // #FF69B4
    public static Color Purple  { get { return ARGB(255, 180, 123, 255); } }   // #B47BFF
    public static Color Magenta { get { return ARGB(255, 255,  77, 204); } }   // #FF4DCC
    public static Color Rose    { get { return ARGB(255, 255, 174, 221); } }   // #FFAEDD
    public static Color Plum    { get { return ARGB(255, 110,  60, 160); } }   // #6E3CA0

    public static Blob B(int x, int y, int r, Color center, int alpha)
    {
        return new Blob { X = x, Y = y, R = r, Center = Soft(center, alpha) };
    }

    public static void RenderAll(string outDir)
    {
        // Demo 1: warm pink-dominant diagonal, central rose bloom + side magenta accents.
        Render(
            System.IO.Path.Combine(outDir, "demo_01.png"),
            Pink, Plum, 35.0f,
            new[] {
                B(260, 220, 280, Rose,    200),
                B(620, 460, 240, Magenta, 170),
                B(540, 120, 160, Rose,    140),
            });

        // Demo 2: cool purple-dominant horizontal sweep, magenta bloom off-center.
        Render(
            System.IO.Path.Combine(outDir, "demo_02.png"),
            Purple, Magenta, 165.0f,
            new[] {
                B(580, 240, 320, Rose,    190),
                B(180, 480, 220, Purple,  200),
                B(720, 540, 150, Pink,    170),
            });

        // Demo 3: deep plum-to-pink vertical, twin rose orbs.
        Render(
            System.IO.Path.Combine(outDir, "demo_03.png"),
            Plum, Pink, 90.0f,
            new[] {
                B(240, 330, 240, Rose,    180),
                B(580, 330, 240, Magenta, 180),
                B(400, 540, 200, Pink,    160),
            });

        // Demo 4: pink/purple split with central magenta nova.
        Render(
            System.IO.Path.Combine(outDir, "demo_04.png"),
            Pink, Purple, 0.0f,
            new[] {
                B(400, 300, 340, Rose,    220),
                B(120, 140, 180, Pink,    160),
                B(700, 480, 200, Purple,  180),
            });
    }
}
'@

Add-Type -TypeDefinition $cs -ReferencedAssemblies System.Drawing -Language CSharp

$OutDir = Split-Path -Parent $MyInvocation.MyCommand.Path
[BlinkTrainerDemoRenderer]::RenderAll($OutDir)

Get-ChildItem -Path $OutDir -Filter 'demo_*.png' | ForEach-Object {
    Write-Host ("Wrote {0} ({1} KB)" -f $_.Name, [math]::Round($_.Length / 1024, 1))
}
