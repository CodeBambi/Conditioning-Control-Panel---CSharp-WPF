using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Navigation;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Affirmation + metadata modal for Preset/Session catalogue submissions.
    /// Unlike <see cref="CatalogueSubmitDialog"/> (enhancements carry their own
    /// creator/tags in the bundle), preset/session native files have neither, so
    /// this dialog collects a creator name + tags before the POST. Submit is gated
    /// on a non-empty creator AND the affirmation checkbox.
    ///
    /// Usage:
    ///   var d = new AssetSubmitDialog(assetName, defaultCreator) { Owner = this };
    ///   if (d.ShowDialog() == true) {
    ///       // d.Creator, d.Tags ready for SubmitCatalogueAssetAsync
    ///   }
    /// </summary>
    public partial class AssetSubmitDialog : Window
    {
        public bool Confirmed { get; private set; }
        public string Creator { get; private set; } = "";
        public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();
        public string DownloadUrl { get; private set; } = "";

        private readonly bool _requireDownloadUrl;

        public AssetSubmitDialog(string assetName, string? defaultCreator = null,
            bool requireDownloadUrl = false, string? defaultTags = null)
        {
            InitializeComponent();
            _requireDownloadUrl = requireDownloadUrl;

            TxtSubtitle.Text = string.IsNullOrWhiteSpace(assetName)
                ? string.Empty
                : Loc.GetF("dialog_catalogue_submit_subtitle_fmt", assetName);

            if (!string.IsNullOrWhiteSpace(defaultCreator))
                TxtCreator.Text = defaultCreator.Trim();
            if (!string.IsNullOrWhiteSpace(defaultTags))
                TxtTags.Text = defaultTags.Trim();

            if (requireDownloadUrl)
            {
                PnlDownloadUrl.Visibility = Visibility.Visible;
                TxtDownloadUrl.TextChanged += (_, _) => UpdateSubmitEnabled();
                // Mods get their own explainer: they are user-created and stay
                // hosted on the creator's MEGA - the catalogue lists only the
                // link, so the flow (export -> MEGA -> paste link) needs
                // spelling out where presets/sessions don't.
                TxtBody.Text = Loc.Get("dialog_catalogue_submit_mod_body");
            }

            ChkAffirm.Checked += (_, _) => UpdateSubmitEnabled();
            ChkAffirm.Unchecked += (_, _) => UpdateSubmitEnabled();
            TxtCreator.TextChanged += (_, _) => UpdateSubmitEnabled();
            UpdateSubmitEnabled();
        }

        private void UpdateSubmitEnabled()
        {
            BtnSubmit.IsEnabled = ChkAffirm.IsChecked == true
                && !string.IsNullOrWhiteSpace(TxtCreator.Text)
                && (!_requireDownloadUrl || IsValidMegaUrl(TxtDownloadUrl.Text));
        }

        // The catalogue only stores a link to the creator-hosted binary; v1
        // restricts hosting to MEGA so the server-side allowlist stays simple.
        internal static bool IsValidMegaUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            var host = uri.Host;
            return host.Equals("mega.nz", StringComparison.OrdinalIgnoreCase)
                || host.Equals("www.mega.nz", StringComparison.OrdinalIgnoreCase)
                || host.Equals("mega.co.nz", StringComparison.OrdinalIgnoreCase)
                || host.Equals("www.mega.co.nz", StringComparison.OrdinalIgnoreCase);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (ChkAffirm.IsChecked != true || string.IsNullOrWhiteSpace(TxtCreator.Text)) return;
            if (_requireDownloadUrl && !IsValidMegaUrl(TxtDownloadUrl.Text)) return;

            Creator = TxtCreator.Text.Trim();
            DownloadUrl = _requireDownloadUrl ? TxtDownloadUrl.Text.Trim() : "";
            // Accept comma- or whitespace-separated tags; dedup, lowercase-trim, cap length.
            Tags = (TxtTags.Text ?? string.Empty)
                .Split(new[] { ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void LinkGuidelines_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true,
                });
                e.Handled = true;
            }
            catch
            {
                // Best-effort: failure to open the browser shouldn't crash the dialog.
            }
        }
    }
}
