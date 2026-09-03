using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/OfflineUsernameDialog.xaml.cs. DialogResult
    /// becomes Close(bool). The WPF MessageBox in Accept() guarded a path the UI already blocks
    /// (Confirm is disabled and Enter ignored below two characters), so here it is a plain return.
    /// </summary>
    public partial class OfflineUsernameDialog : Window
    {
        private readonly TextBox _txtUsername;
        private readonly Button _btnConfirm;

        public string Username { get; private set; } = "";

        public OfflineUsernameDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _txtUsername = this.FindControl<TextBox>("TxtUsername")!;
            _btnConfirm = this.FindControl<Button>("BtnConfirm")!;
            var txtCharCount = this.FindControl<TextBlock>("TxtCharCount")!;

            _txtUsername.TextChanged += (_, _) =>
            {
                var length = (_txtUsername.Text ?? "").Trim().Length;
                txtCharCount.Text = Loc.GetF("label_char_count_of_max", length, 30);
                _btnConfirm.IsEnabled = length >= 2;
            };

            _txtUsername.KeyDown += TxtUsername_KeyDown;
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            _btnConfirm.Click += (_, _) => Accept();

            Loaded += (_, _) => _txtUsername.Focus();
        }

        private void TxtUsername_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _btnConfirm.IsEnabled)
                Accept();
            else if (e.Key == Key.Escape)
                Close(false);
        }

        private void Accept()
        {
            var name = (_txtUsername.Text ?? "").Trim();
            if (name.Length < 2)
                return;

            Username = name;
            Close(true);
        }
    }
}
