using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    public partial class UrlPromptDialog : Window
    {
        /// <summary>The entered URL, or null when the user cancelled.</summary>
        public string? Result { get; private set; }

        public UrlPromptDialog()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new UrlPromptViewModel();

            var url = this.FindControl<TextBox>("TxtUrl")!;
            var ok = this.FindControl<Button>("BtnOk")!;
            var cancel = this.FindControl<Button>("BtnCancel")!;

            // WPF wired KeyDown in markup; Avalonia's compiled bindings prefer code, and this
            // keeps Enter working identically to the original's IsDefault path.
            url.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(url); };
            ok.Click += (_, _) => Accept(url);
            cancel.Click += (_, _) => Close();
        }

        private void Accept(TextBox url)
        {
            var text = url.Text?.Trim();
            var error = this.FindControl<TextBlock>("TxtError")!;

            if (string.IsNullOrWhiteSpace(text))
            {
                error.Text = Loc.Get("deeper_url_prompt_invalid");
                error.IsVisible = true;
                return;
            }

            Result = text;
            Close();
        }
    }

    public sealed class UrlPromptViewModel
    {
        public string LocTitle => Loc.Get("deeper_url_prompt_title");
        public string LocLabel => Loc.Get("deeper_url_prompt_label");
        public string LocLoad => Loc.Get("deeper_url_prompt_load");
        public string LocCancel => Loc.Get("btn_cancel");
    }
}
