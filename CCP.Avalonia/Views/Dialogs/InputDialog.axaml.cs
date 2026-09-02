using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/InputDialog.xaml.cs. WPF's DialogResult
    /// becomes Close(bool): Avalonia hands the result back through ShowDialog&lt;bool?&gt;.
    /// </summary>
    public partial class InputDialog : Window
    {
        private readonly TextBox _txtInput;

        public string ResultText { get; private set; } = "";

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        public InputDialog() : this("Sample title", "Enter a sample value:", "default") { }

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            AvaloniaXamlLoader.Load(this);

            _txtInput = this.FindControl<TextBox>("TxtInput")!;
            this.FindControl<TextBlock>("TxtTitle")!.Text = title;
            this.FindControl<TextBlock>("TxtPrompt")!.Text = prompt;
            _txtInput.Text = defaultValue;

            _txtInput.KeyDown += TxtInput_KeyDown;
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnOK")!.Click += (_, _) => Accept();

            Loaded += (_, _) =>
            {
                _txtInput.Focus();
                _txtInput.SelectAll();
            };
        }

        private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Accept();
            else if (e.Key == Key.Escape)
                Close(false);
        }

        private void Accept()
        {
            ResultText = _txtInput.Text ?? "";
            Close(true);
        }
    }
}
