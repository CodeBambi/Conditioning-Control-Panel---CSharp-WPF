using System;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Deeper
{
    /// <summary>
    /// Tiny modal that prompts for a https://... URL pointing at a
    /// .ccpenh.json. Input validation is just "is it an http(s) URL"; the
    /// actual fetch + schema sniff happens in the caller via
    /// <see cref="Services.Deeper.EnhancementFetcher"/>.
    /// </summary>
    public partial class UrlPromptDialog : Window
    {
        public string? Result { get; private set; }

        public UrlPromptDialog(string? initial = null)
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(initial)) TxtUrl.Text = initial;
            Loaded += (_, _) => { TxtUrl.Focus(); TxtUrl.SelectAll(); };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var url = (TxtUrl.Text ?? "").Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                TxtError.Text = Loc.Get("deeper_url_prompt_invalid");
                TxtError.Visibility = Visibility.Visible;
                return;
            }
            Result = url;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnOk_Click(sender, new RoutedEventArgs());
        }
    }
}
