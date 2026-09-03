using System.Threading.Tasks;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// The head's message box, standing in for the WPF head's <c>MessageBox.Show</c>. A title, a
    /// message, and either OK alone or OK/Cancel.
    ///
    /// Deliberately not a MessageBox clone: no icon set, no button-set enum, no builder. The
    /// ported call sites only ever needed "tell them" and "ask them", which is the two statics.
    /// A caller wanting the checkbox-gated double confirm still uses <see cref="WarningDialog"/>.
    ///
    /// Cancel and the window X both answer false, matching <c>MessageBoxResult.Cancel</c>.
    /// </summary>
    public partial class MessageDialog : Window
    {
        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog,
        /// with Cancel shown so the PNG proves both buttons. Internal, so no production caller
        /// can ship the sample.</summary>
        internal MessageDialog() : this(
            "Remove 3 phrases?",
            "The selected phrases will be removed from this companion. This cannot be undone.",
            showCancel: true)
        { }

        public MessageDialog(string title, string message, bool showCancel)
        {
            InitializeComponent();

            Title = title;
            this.FindControl<TextBlock>("TxtTitle")!.Text = title;
            this.FindControl<TextBlock>("TxtMessage")!.Text = message;

            var cancel = this.FindControl<Button>("BtnCancel")!;
            cancel.IsVisible = showCancel;
            cancel.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnOk")!.Click += (_, _) => Close(true);
        }

        /// <summary>Tells the user something. One OK button, so the result is true unless they
        /// dismissed the window with the X; a caller of this form normally ignores it.</summary>
        public static async Task<bool> ShowAsync(Window owner, string title, string message)
            => await new MessageDialog(title, message, showCancel: false).ShowDialog<bool?>(owner) == true;

        /// <summary>Asks the user something. True only on OK - Cancel and the X both answer false.</summary>
        public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
            => await new MessageDialog(title, message, showCancel: true).ShowDialog<bool?>(owner) == true;
    }
}
