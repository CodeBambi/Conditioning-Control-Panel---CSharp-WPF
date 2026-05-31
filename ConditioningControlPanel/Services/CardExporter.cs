using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.ViewModels;

namespace ConditioningControlPanel.Services
{
    /// <summary>Result of rendering a recap card to PNG.</summary>
    public class CardExportResult
    {
        public BitmapSource Bitmap { get; init; } = null!;
        public byte[] PngBytes { get; init; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Renders a Season Recap card to a crisp PNG and handles clipboard / disk hand-off.
    ///
    /// The card is rendered onto a SOLID dark backdrop (not transparent) so the rounded corners
    /// composite cleanly on platforms that fill alpha. Export is 2x (≈836x1576 incl. the dark
    /// frame; the card itself is 382x752 at 1x) for sharpness in the X/Reddit composers.
    /// </summary>
    public static class CardExporter
    {
        private const double FramePadding = 18; // dark frame around the rounded card
        private const double ExportScale = 2.0;

        // Near-void backdrop so the rounded corners never read as transparency.
        private static readonly Color BackdropColor = Color.FromRgb(0x07, 0x03, 0x0F);

        /// <summary>
        /// Build a fresh, non-animated card for the given view model, freeze it to a clean still,
        /// and render it to a 2x PNG. Rendering a throwaway card (rather than the on-screen one)
        /// keeps the live card animating and guarantees the still is captured at a representative
        /// frame, never mid-sweep.
        /// </summary>
        public static CardExportResult Render(SeasonRecapViewModel vm)
        {
            var card = new SeasonRecapCard { AnimateReveal = false };
            card.SetViewModel(vm);

            // Host the card on a solid dark frame. The card has a fixed Width but auto Height,
            // so measure against unbounded height and let it size to its content.
            var host = new Border
            {
                Background = new SolidColorBrush(BackdropColor),
                Padding = new Thickness(FramePadding),
                Child = card,
            };

            // Off-tree layout pass so the visual has real geometry to render.
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();

            // Freeze animations AFTER layout so the spiral geometry exists.
            card.PrepareForStill();
            host.UpdateLayout();

            var bmp = RenderToBitmap(host, ExportScale, host.DesiredSize);
            var png = EncodePng(bmp);
            return new CardExportResult { Bitmap = bmp, PngBytes = png };
        }

        private static BitmapSource RenderToBitmap(FrameworkElement element, double scale, Size size)
        {
            int pxW = (int)Math.Ceiling(size.Width * scale);
            int pxH = (int)Math.Ceiling(size.Height * scale);
            var rtb = new RenderTargetBitmap(pxW, pxH, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(element);
            rtb.Freeze();
            return rtb;
        }

        public static byte[] EncodePng(BitmapSource bmp)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Place the PNG on the clipboard under multiple formats so it pastes reliably into the
        /// X web composer: raw PNG stream under a "PNG" format (browsers prefer this) AND a
        /// BitmapSource (CF_BITMAP/CF_DIB). Must run on the STA UI thread.
        /// </summary>
        public static bool CopyToClipboard(CardExportResult export)
        {
            try
            {
                var data = new DataObject();

                // Raw PNG stream — what Chromium/Firefox-based web composers read first.
                var pngStream = new MemoryStream(export.PngBytes);
                data.SetData("PNG", pngStream, false);

                // BitmapSource — registers CF_BITMAP; WPF also surfaces CF_DIB for native apps.
                data.SetImage(export.Bitmap);
                data.SetData(DataFormats.Bitmap, export.Bitmap, true);

                Clipboard.SetDataObject(data, true); // copy=true: survives app exit
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to copy card to clipboard");
                return false;
            }
        }

        /// <summary>
        /// Save the PNG into the user's Pictures\ConditioningControlPanel folder and return the
        /// full path (the Reddit flow surfaces this so the user can attach the file).
        /// </summary>
        public static string? SaveToPictures(CardExportResult export, string fileName)
        {
            try
            {
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                var dir = Path.Combine(pictures, "ConditioningControlPanel");
                Directory.CreateDirectory(dir);
                var safeName = MakeSafeFileName(fileName);
                var path = Path.Combine(dir, safeName);
                File.WriteAllBytes(path, export.PngBytes);
                App.Logger?.Information("SeasonRecap: saved card PNG to {Path}", path);
                return path;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to save card PNG");
                return null;
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return string.IsNullOrWhiteSpace(name) ? "cclabs-season.png" : name;
        }
    }
}
