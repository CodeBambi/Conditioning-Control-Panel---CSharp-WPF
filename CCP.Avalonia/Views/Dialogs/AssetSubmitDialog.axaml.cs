using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Affirmation + metadata modal for Preset/Session catalogue submissions.
    /// Unlike <see cref="CatalogueSubmitDialog"/> (enhancements carry their own
    /// creator/tags in the bundle), preset/session native files have neither, so
    /// this dialog collects a creator name + tags before the POST. Submit is gated
    /// on a non-empty creator AND the affirmation checkbox.
    ///
    /// Usage:
    ///   var d = new AssetSubmitDialog(assetName, defaultCreator);
    ///   if (await d.ShowDialog&lt;bool&gt;(owner)) {
    ///       // d.Creator, d.Tags ready for SubmitCatalogueAssetAsync
    ///   }
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/AssetSubmitDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> property becomes <c>Close(bool)</c>; Avalonia carries the
    ///    result through <c>ShowDialog&lt;bool&gt;</c>.
    ///  - <c>LinkGuidelines_RequestNavigate</c> is gone: <c>HyperlinkButton.NavigateUri</c> opens
    ///    the browser itself, so there is no <c>Process.Start</c> to port.
    ///  - The mod-body swap REBINDS TxtBody rather than assigning <c>.Text</c>. The control carries
    ///    a <c>{loc:Str}</c> binding; a local value would be overwritten on the next language
    ///    change (see the porting notes in CLAUDE.md). Same key, chosen in code.
    ///  - Checked/Unchecked collapse into <c>IsCheckedChanged</c>; <c>TextChanged</c> keeps its name.
    /// </summary>
    public partial class AssetSubmitDialog : Window
    {
        public bool Confirmed { get; private set; }
        public string Creator { get; private set; } = "";
        public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();
        public string DownloadUrl { get; private set; } = "";

        private readonly bool _requireDownloadUrl;
        private readonly TextBox _txtCreator;
        private readonly TextBox _txtTags;
        private readonly TextBox _txtDownloadUrl;
        private readonly CheckBox _chkAffirm;
        private readonly Button _btnSubmit;

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.
        /// It asks for a download URL, which is the branch that draws the most - the MEGA panel
        /// and the mod explainer are otherwise invisible in the render proof.</summary>
        internal AssetSubmitDialog() : this("Deep Trance Session", "bambi", requireDownloadUrl: true, defaultTags: "gentle, 30min") { }

        public AssetSubmitDialog(string assetName, string? defaultCreator = null,
            bool requireDownloadUrl = false, string? defaultTags = null)
        {
            AvaloniaXamlLoader.Load(this);
            _requireDownloadUrl = requireDownloadUrl;

            _txtCreator = this.FindControl<TextBox>("TxtCreator")!;
            _txtTags = this.FindControl<TextBox>("TxtTags")!;
            _txtDownloadUrl = this.FindControl<TextBox>("TxtDownloadUrl")!;
            _chkAffirm = this.FindControl<CheckBox>("ChkAffirm")!;
            _btnSubmit = this.FindControl<Button>("BtnSubmit")!;

            this.FindControl<TextBlock>("TxtSubtitle")!.Text = string.IsNullOrWhiteSpace(assetName)
                ? string.Empty
                : Loc.GetF("dialog_catalogue_submit_subtitle_fmt", assetName);

            if (!string.IsNullOrWhiteSpace(defaultCreator))
                _txtCreator.Text = defaultCreator.Trim();
            if (!string.IsNullOrWhiteSpace(defaultTags))
                _txtTags.Text = defaultTags.Trim();

            if (requireDownloadUrl)
            {
                this.FindControl<StackPanel>("PnlDownloadUrl")!.IsVisible = true;
                _txtDownloadUrl.TextChanged += (_, _) => UpdateSubmitEnabled();
                // Mods get their own explainer: they are user-created and stay
                // hosted on the creator's MEGA - the catalogue lists only the
                // link, so the flow (export -> MEGA -> paste link) needs
                // spelling out where presets/sessions don't.
                this.FindControl<TextBlock>("TxtBody")!.Bind(TextBlock.TextProperty,
                    new Binding("[dialog_catalogue_submit_mod_body]")
                    {
                        Source = LocalizationManager.Instance,
                        Mode = BindingMode.OneWay,
                    });
            }

            _chkAffirm.IsCheckedChanged += (_, _) => UpdateSubmitEnabled();
            _txtCreator.TextChanged += (_, _) => UpdateSubmitEnabled();

            this.FindControl<Button>("BtnCancel")!.Click += (_, _) =>
            {
                Confirmed = false;
                Close(false);
            };
            _btnSubmit.Click += (_, _) => Submit();

            UpdateSubmitEnabled();
        }

        private void UpdateSubmitEnabled()
        {
            _btnSubmit.IsEnabled = _chkAffirm.IsChecked == true
                && !string.IsNullOrWhiteSpace(_txtCreator.Text)
                && (!_requireDownloadUrl || IsValidMegaUrl(_txtDownloadUrl.Text));
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

        private void Submit()
        {
            if (_chkAffirm.IsChecked != true || string.IsNullOrWhiteSpace(_txtCreator.Text)) return;
            if (_requireDownloadUrl && !IsValidMegaUrl(_txtDownloadUrl.Text)) return;

            Creator = _txtCreator.Text!.Trim();
            DownloadUrl = _requireDownloadUrl ? _txtDownloadUrl.Text!.Trim() : "";
            // Accept comma- or whitespace-separated tags; dedup, lowercase-trim, cap length.
            Tags = (_txtTags.Text ?? string.Empty)
                .Split(new[] { ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            Confirmed = true;
            Close(true);
        }
    }
}
