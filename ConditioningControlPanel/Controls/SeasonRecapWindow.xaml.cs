using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.ViewModels;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The season-rollover surface. WRAPS the existing reset notice (it does not replace the
    /// actual reset, which the server/SkillTreeService still perform): it presents the recap
    /// card plus the share actions and a "continue to next season" button.
    ///
    /// Also reused as the secondary re-view surface from the profile/stats screen.
    ///
    /// Share constraint: neither X nor Reddit's share-intent URL can attach an image, so the
    /// mechanism is text prefill + clipboard (X) / saved file (Reddit). We never try to attach
    /// the image via the URL.
    /// </summary>
    public partial class SeasonRecapWindow : Window
    {
        private readonly SeasonRecapViewModel _vm;
        private readonly SeasonRecapCard _card;
        private CardExportResult? _export; // rendered lazily, reused across share actions
        private DispatcherTimer? _statusTimer;

        public SeasonRecapWindow(SeasonRecapViewModel vm)
        {
            InitializeComponent();
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            _card = new SeasonRecapCard { AnimateReveal = true };
            _card.SetViewModel(vm);
            PART_CardHost.Child = _card;

            BtnCopy.Content = Loc.Get("recap_btn_copy");
            BtnSave.Content = Loc.Get("recap_btn_save");
            BtnShareX.Content = Loc.Get("recap_btn_share_x");
            BtnShareReddit.Content = Loc.Get("recap_btn_share_reddit");
            BtnContinue.Content = Loc.GetF("recap_btn_continue", _vm.NextSeasonNumber.ToString("00"));
            PART_Note.Text = Loc.Get("recap_share_note");
        }

        private CardExportResult Export() => _export ??= CardExporter.Render(_vm);

        // ---------- share actions ----------
        private void OnCopy(object sender, RoutedEventArgs e)
        {
            if (CardExporter.CopyToClipboard(Export()))
                ShowStatus(Loc.Get("recap_toast_copied"));
            else
                ShowStatus(Loc.Get("recap_toast_error"));
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var path = CardExporter.SaveToPictures(Export(), _vm.SuggestedFileName);
            ShowStatus(path != null ? Loc.GetF("recap_toast_saved", path) : Loc.Get("recap_toast_error"));
        }

        private void OnShareX(object sender, RoutedEventArgs e)
        {
            CardExporter.CopyToClipboard(Export());
            var url = "https://x.com/intent/post?text=" + Uri.EscapeDataString(_vm.SharePrefillText);
            if (OpenUrl(url))
                ShowStatus(Loc.Get("recap_toast_x"));
        }

        private void OnShareReddit(object sender, RoutedEventArgs e)
        {
            var path = CardExporter.SaveToPictures(Export(), _vm.SuggestedFileName);
            var url = "https://www.reddit.com/submit?title=" + Uri.EscapeDataString(_vm.SharePrefillText);
            if (OpenUrl(url))
                ShowStatus(path != null ? Loc.GetF("recap_toast_reddit", path) : Loc.Get("recap_toast_x"));
        }

        private void OnContinue(object sender, RoutedEventArgs e) => Close();

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
                App.Logger?.Warning(ex, "SeasonRecap: failed to open URL {Url}", url);
                return false;
            }
        }

        private void ShowStatus(string message)
        {
            PART_Status.Text = message;
            PART_Status.Visibility = Visibility.Visible;

            _statusTimer?.Stop();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer?.Stop();
                PART_Status.Visibility = Visibility.Collapsed;
            };
            _statusTimer.Start();
        }
    }
}
