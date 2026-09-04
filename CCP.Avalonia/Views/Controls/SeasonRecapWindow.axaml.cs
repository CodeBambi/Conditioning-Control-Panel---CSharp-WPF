using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The season-rollover surface: the recap card plus the share actions and a "continue to next
    /// season" button. Also reused as the secondary re-view surface from the profile/stats screen.
    ///
    /// PORTED from ConditioningControlPanel/Controls/SeasonRecapWindow.xaml.cs. Deviations:
    ///  - <c>CardExporter</c> is not a shared service here; its render-and-save half is inlined
    ///    below against Avalonia's <c>RenderTargetBitmap</c>, which needs no visual tree - the
    ///    throwaway card is measured, arranged, frozen with <c>PrepareForStill</c> and rendered
    ///    off-tree exactly as WPF does it, so the live card keeps animating.
    ///  - Its CLIPBOARD half is not ported, so BtnCopy still reports <c>recap_toast_error</c>.
    ///    Avalonia 12 replaced <c>SetDataObjectAsync</c> with <c>DataFormat</c>/<c>IAsyncDataTransfer</c>
    ///    and no machine in this port's reach can prove an X11 selection actually serves
    ///    <c>image/png</c> to a browser composer. "card copied, paste it (Ctrl+V)" over an empty
    ///    clipboard is the kind of lie this port refuses; an unavailable button is not.
    ///  - Both SHARE buttons therefore take the Reddit route on this head: save the PNG, open the
    ///    composer, and tell you the path to attach. WPF's X route pastes from the clipboard.
    ///  - A parameterless constructor with sample data exists for the headless render.
    /// </summary>
    public partial class SeasonRecapWindow : Window
    {
        private readonly SeasonRecapCardViewModel _vm;
        private readonly SeasonRecapCard _card;
        private readonly TextBlock _status;
        private DispatcherTimer? _statusTimer;

        /// <summary>Render constructor: sample data, so --render-all can discover the window.</summary>
        public SeasonRecapWindow() : this(SeasonRecapCardViewModel.Sample()) { }

        public SeasonRecapWindow(SeasonRecapCardViewModel vm)
        {
            AvaloniaXamlLoader.Load(this);
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            _card = new SeasonRecapCard { AnimateReveal = true };
            _card.SetViewModel(vm);
            this.FindControl<Border>("PART_CardHost")!.Child = _card;
            _status = this.FindControl<TextBlock>("PART_Status")!;

            this.FindControl<Button>("BtnCopy")!.Click += OnCopy;
            this.FindControl<Button>("BtnSave")!.Click += OnSave;
            this.FindControl<Button>("BtnShareX")!.Click += OnShareX;
            this.FindControl<Button>("BtnShareReddit")!.Click += OnShareReddit;
            this.FindControl<Button>("BtnContinue")!.Click += OnContinue;
            this.FindControl<TextBlock>("TxtContinue")!.Text =
                Loc.GetF("recap_btn_continue", _vm.NextSeasonNumber.ToString("00"));
        }

        // ---------- share actions ----------

        /// ponytail: needs a clipboard image. Avalonia 12's IClipboard takes an IAsyncDataTransfer
        /// of DataFormats and nothing here can prove the X11 backend serves image/png to another
        /// process, so this stays refused rather than toasting "copied, paste it" over nothing.
        /// The two share buttons below save the file instead, which is provable.
        private void OnCopy(object? sender, RoutedEventArgs e) => ShowStatus(Loc.Get("recap_toast_error"));

        private void OnSave(object? sender, RoutedEventArgs e)
        {
            var png = ExportPng();
            var path = png == null ? null : SaveToPictures(png, _vm.SuggestedFileName);
            ShowStatus(path != null ? Loc.GetF("recap_toast_saved", path) : Loc.Get("recap_toast_error"));
        }

        private void OnShareX(object? sender, RoutedEventArgs e) =>
            ShareVia("https://x.com/intent/post?text=");

        private void OnShareReddit(object? sender, RoutedEventArgs e) =>
            ShareVia("https://www.reddit.com/submit?title=");

        /// <summary>Save the card, open the composer, and name the file to attach. WPF's X route
        /// pasted from the clipboard instead; see the class summary for why both take this one.</summary>
        private void ShareVia(string urlPrefix)
        {
            var png = ExportPng();
            var path = png == null ? null : SaveToPictures(png, _vm.SuggestedFileName);
            if (!OpenUrl(urlPrefix + Uri.EscapeDataString(_vm.SharePrefillText))) return;
            ShowStatus(path != null ? Loc.GetF("recap_toast_reddit", path) : Loc.Get("recap_toast_error"));
        }

        private void OnContinue(object? sender, RoutedEventArgs e) => Close();

        // ---------- the exporter ----------

        private const double FramePadding = 18;   // dark frame around the rounded card
        private const double ExportScale = 2.0;
        private byte[]? _png;

        /// <summary>
        /// Build a fresh, non-animated card, freeze it to a clean still and render it to a 2x PNG.
        /// Rendering a throwaway card rather than the on-screen one keeps the live card animating
        /// and guarantees the still is captured at a representative frame, never mid-sweep.
        /// Cached: the four buttons all want the same bytes.
        /// </summary>
        private byte[]? ExportPng()
        {
            if (_png != null) return _png;
            try
            {
                var card = new SeasonRecapCard { AnimateReveal = false };
                card.SetViewModel(_vm);

                // Near-void backdrop so the rounded corners never read as transparency.
                var host = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x07, 0x03, 0x0F)),
                    Padding = new Thickness(FramePadding),
                    Child = card,
                };

                // Off-tree layout pass so the visual has real geometry to render. The card has a
                // fixed Width but auto Height, so measure against unbounded height.
                host.Measure(Size.Infinity);
                host.Arrange(new Rect(host.DesiredSize));

                // Freeze AFTER layout, so the spiral geometry exists, then re-arrange.
                card.PrepareForStill();
                host.Measure(Size.Infinity);
                host.Arrange(new Rect(host.DesiredSize));

                var size = host.DesiredSize;
                var px = new PixelSize((int)Math.Ceiling(size.Width * ExportScale),
                                       (int)Math.Ceiling(size.Height * ExportScale));
                if (px.Width <= 0 || px.Height <= 0)
                {
                    Log.Warning("SeasonRecap: card measured to nothing, no PNG to write");
                    return null;
                }

                using var rtb = new RenderTargetBitmap(px, new Vector(96 * ExportScale, 96 * ExportScale));
                rtb.Render(host);
                using var ms = new MemoryStream();
                rtb.Save(ms);
                return _png = ms.ToArray();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to render the card");
                return null;
            }
        }

        /// <summary>Write the PNG into the user's Pictures/ConditioningControlPanel folder and
        /// return the full path, which the share toasts surface so the file can be attached.</summary>
        private static string? SaveToPictures(byte[] png, string fileName)
        {
            try
            {
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                // .NET answers $HOME when XDG_PICTURES_DIR is unset; do not dump PNGs in the home
                // directory over that.
                if (string.IsNullOrWhiteSpace(pictures) ||
                    pictures == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                {
                    pictures = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
                }

                var dir = Path.Combine(pictures, "ConditioningControlPanel");
                Directory.CreateDirectory(dir);

                var safe = fileName ?? "";
                foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '-');
                if (string.IsNullOrWhiteSpace(safe)) safe = "cclabs-season.png";

                var path = Path.Combine(dir, safe);
                File.WriteAllBytes(path, png);
                Log.Information("SeasonRecap: saved card PNG to {Path}", path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to save card PNG");
                return null;
            }
        }

        // ---------- helpers ----------
        private static bool OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to open URL {Url}", url);
                return false;
            }
        }

        private void ShowStatus(string message)
        {
            _status.Text = message;
            _status.IsVisible = true;

            _statusTimer?.Stop();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer?.Stop();
                _status.IsVisible = false;
            };
            _statusTimer.Start();
        }
    }
}
