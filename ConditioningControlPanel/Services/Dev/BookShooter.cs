using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Services.Dev;

/// <summary>
/// Offscreen screenshot rig for HER BOOK (launched via <c>--shoot-book [outDir]</c>).
///
/// <para><b>Why this exists.</b> The book is a drawn object: an 8-bit loop on a pixel panel, at an
/// integer scale, in a font loaded from a base URI. Every one of those fails visibly and none of
/// them fails loudly - a font that did not resolve renders in the default UI face and looks merely
/// wrong, a stage at a non-integer scale renders with seams, and a demo that reads as noise still
/// paints. A design review needs pixels, and the same rule <see cref="DoorShooter"/> was built for
/// applies here: screen capture returns a stale frame whenever the display is asleep, which is
/// precisely when the owner is reviewing remotely. <see cref="RenderTargetBitmap"/> rasterizes in
/// process and never asks DWM for anything.</para>
///
/// <para><b>Determinate frames.</b> A capture taken while the 30 fps clock runs lands wherever the
/// scheduler happened to be, so the rig stops the clock and asks for exact times through
/// <c>EmiBookWindow.ShootFrame</c>. Each card gets its reduced-motion still (the frame the painter
/// itself nominates as most legible) plus a five-frame walk across its loop, which is how you tell
/// a loop that reads from one that only reads at the moment you happened to catch it.</para>
///
/// <para><b>It runs in the real app</b>, against the real EMI, the real settings and the real mod.
/// Dead code in every normal launch.</para>
/// </summary>
internal static class BookShooter
{
    /// <summary>Frames per card, as fractions of the loop. Endpoints included: the seam between the
    /// last frame and the first is where a loop that does not actually loop shows itself.</summary>
    private static readonly double[] Walk = { 0.0, 0.2, 0.4, 0.6, 0.8 };

    public static void Run(Window window, string outDir, bool narrow = false)
    {
        // Set before the book is ever placed: PlaceWindow reads it on the way in.
        EmiBookWindow.ForceNarrow = narrow;

        // Deferred to Loaded for the same reason DoorShooter defers: rendering an unarranged visual
        // yields an empty bitmap that reads exactly like a broken panel.
        if (window.IsLoaded) _ = Shoot(outDir);
        else window.Loaded += (_, _) => _ = Shoot(outDir);
    }

    private static async Task Shoot(string outDir)
    {
        int written = 0;
        var notes = new List<string>();
        try
        {
            Directory.CreateDirectory(outDir);
            App.Logger?.Information("BookShooter: writing to {Dir}", outDir);

            // Startup settle: services wire, the mod's art resolves, first-run popups come and go.
            await Task.Delay(TimeSpan.FromSeconds(6));

            var desk = App.EmiDesk;
            if (desk == null) { notes.Add("no EmiDesk service - nothing to shoot"); return; }

            // The book is anchored to her body and refuses to open while she is away, so she has to
            // be out first. This is the shipped summon, not a private show.
            if (!desk.IsOut) desk.Summon("bookShot");
            for (int i = 0; i < 40 && (!desk.IsOut || desk.Window == null); i++)
                await Task.Delay(TimeSpan.FromMilliseconds(250));

            if (!desk.IsOut || desk.Window == null) { notes.Add("she never came out - nothing to shoot"); return; }
            await Task.Delay(TimeSpan.FromMilliseconds(900));

            EmiBook.Open(EmiBookCards.All.Count > 0 ? EmiBookCards.All[0].Id : null);
            await Task.Delay(TimeSpan.FromMilliseconds(900));   // the unfurl, and then some

            var book = EmiBook.Live;
            if (book == null) { notes.Add("the book did not open"); return; }

            for (int c = 0; c < EmiBookCards.All.Count; c++)
            {
                var card = EmiBookCards.All[c];
                var painter = EmiBookDemos.For(card.Id);
                string stem = string.Format(CultureInfo.InvariantCulture, "{0:00}-{1}", c + 1, card.Id);

                // The still first: this is the card as somebody with reduced motion sees it, and it
                // is the shot a copy review should be read off.
                book.ShootFrame(card.Id, painter?.StillMs ?? 0);
                await Task.Delay(TimeSpan.FromMilliseconds(220));
                if (Capture(book, Path.Combine(outDir, stem + "-still.png"))) written++;

                if (painter == null) { notes.Add($"{card.Id}: no painter"); continue; }

                foreach (var f in Walk)
                {
                    double t = painter.LoopMs * f;
                    book.ShootFrame(card.Id, t);
                    await Task.Delay(TimeSpan.FromMilliseconds(120));
                    var name = string.Format(CultureInfo.InvariantCulture, "{0}-t{1:0000}.png", stem, t);
                    if (Capture(book, Path.Combine(outDir, name))) written++;
                }
            }

            App.Logger?.Information("BookShooter: {Count} shots written", written);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BookShooter failed after {Count} shots", written);
            notes.Add("threw: " + ex.Message);
        }
        finally
        {
            // A sentinel, so the driving script can tell "finished" from "still going" and from
            // "died early" without guessing at file counts.
            try
            {
                File.WriteAllText(Path.Combine(outDir, "_done.txt"),
                    string.Format(CultureInfo.InvariantCulture, "{0} shots at {1:o}{2}{3}",
                                  written, DateTime.Now, Environment.NewLine,
                                  string.Join(Environment.NewLine, notes)));
            }
            catch { /* the shots are the point, not the sentinel */ }

            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => Application.Current.Shutdown()));
        }
    }

    private static bool Capture(Window window, string path)
    {
        try
        {
            var size = window.Content is FrameworkElement fe && fe.ActualWidth > 0
                ? new Size(fe.ActualWidth, fe.ActualHeight)
                : new Size(window.ActualWidth, window.ActualHeight);

            if (size.Width < 4 || size.Height < 4)
            {
                App.Logger?.Warning("BookShooter: {Path} skipped - degenerate size {W}x{H}",
                                    path, size.Width, size.Height);
                return false;
            }

            // 96 DPI pinned, so shots are the same pixel size on any machine and can be compared
            // side by side. It also means the 288 wide stage measures 288 wide in the PNG, which is
            // what makes an integer-scale regression countable rather than a matter of opinion.
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height),
                                             96, 96, PixelFormats.Pbgra32);

            // The window is layered and its own Background is Transparent, so a flat ground is
            // drawn under the content first - otherwise the PNG's alpha reads as a black panel in
            // most viewers and the drop shadow looks like a bug.
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x1E)), null,
                                 new Rect(size));
                dc.DrawRectangle(new VisualBrush((Visual)window.Content) { Stretch = Stretch.None },
                                 null, new Rect(size));
            }
            rtb.Render(dv);

            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            png.Save(fs);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BookShooter: capture failed for {Path}", path);
            return false;
        }
    }
}
