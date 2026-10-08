using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Input.Platform;
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
    ///  - Its CLIPBOARD half is <c>IClipboard.SetBitmapAsync</c> (Avalonia 12). Share on X copies
    ///    and opens the composer like WPF; if the copy fails it falls back to the Reddit route
    ///    (save the PNG, name the path) instead of toasting "copied" over an empty clipboard.
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

        private async void OnCopy(object? sender, RoutedEventArgs e) =>
            ShowStatus(Loc.Get(await CopyToClipboardAsync() ? "recap_toast_copied" : "recap_toast_error"));

        /// <summary>CardExporter.CopyToClipboard: the exported PNG as a clipboard bitmap.</summary>
        internal async Task<bool> CopyToClipboardAsync()
        {
            var png = ExportPng();
            var clip = GetTopLevel(this)?.Clipboard;
            if (png == null || clip == null) return false;
            try
            {
                using var ms = new MemoryStream(png);
                await clip.SetBitmapAsync(new Bitmap(ms)); // the clipboard owns it from here
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SeasonRecap: failed to copy the card to the clipboard");
                return false;
            }
        }

        private void OnSave(object? sender, RoutedEventArgs e)
        {
            var png = ExportPng();
            var path = png == null ? null : SaveToPictures(png, _vm.SuggestedFileName);
            ShowStatus(path != null ? Loc.GetF("recap_toast_saved", path) : Loc.Get("recap_toast_error"));
        }

        private async void OnShareX(object? sender, RoutedEventArgs e)
        {
            const string x = "https://x.com/intent/post?text=";
            if (!await CopyToClipboardAsync()) { ShareVia(x); return; }
            if (OpenUrl(x + Uri.EscapeDataString(_vm.SharePrefillText)))
                ShowStatus(Loc.Get("recap_toast_x"));
        }

        private void OnShareReddit(object? sender, RoutedEventArgs e) =>
            ShareVia("https://www.reddit.com/submit?title=");

        /// <summary>Save the card, open the composer, and name the file to attach (WPF's Reddit
        /// route; also X's fallback when the clipboard copy fails).</summary>
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

                // The stage is NOT optional. Avalonia applies styling on logical-tree attach, so an
                // unparented control gets no Theme= setters at all and renders as an empty backdrop
                // - measured with a probe on this branch, not assumed. Parent it, render, detach.
                var stage = this.FindControl<Panel>("PART_ExportStage")!;
                stage.Children.Add(host);
                try
                {
                    // Explicit layout pass so the visual has real geometry now, rather than after
                    // the stage's own deferred pass. The card has a fixed Width but auto Height, so
                    // measure against unbounded height.
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
                finally
                {
                    stage.Children.Remove(host);
                }
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
                return Platform.ExternalOpener.Open(url);
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
