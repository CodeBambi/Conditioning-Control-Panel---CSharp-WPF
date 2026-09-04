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

        public MessageDialog(string title, string message, bool showCancel, bool defaultToCancel = false)
        {
            InitializeComponent();

            Title = title;
            this.FindControl<TextBlock>("TxtTitle")!.Text = title;
            this.FindControl<TextBlock>("TxtMessage")!.Text = message;

            var ok = this.FindControl<Button>("BtnOk")!;
            var cancel = this.FindControl<Button>("BtnCancel")!;
            cancel.IsVisible = showCancel;
            cancel.Click += (_, _) => Close(false);
            ok.Click += (_, _) => Close(true);

            // WPF's MessageBox.Show takes a defaultResult, and a guard prompt passes No so that
            // Enter keeps the guard. IsDefault lives on OK in the markup, so it has to MOVE, not
            // just lose focus: leaving it on OK would let Enter answer yes to the one question
            // where a stray keypress must not.
            if (showCancel && defaultToCancel)
            {
                ok.IsDefault = false;
                cancel.IsDefault = true;
                Opened += (_, _) => cancel.Focus();
            }
        }

        /// <summary>Tells the user something. One OK button, so the result is true unless they
        /// dismissed the window with the X; a caller of this form normally ignores it.</summary>
        public static async Task<bool> ShowAsync(Window owner, string title, string message)
            => await new MessageDialog(title, message, showCancel: false).ShowDialog<bool?>(owner) == true;

        /// <summary>Asks the user something. True only on OK - Cancel and the X both answer false.</summary>
        /// <param name="defaultToCancel">
        /// Puts Enter on Cancel instead of OK, for a question where the safe answer is "no": the
        /// port of a WPF <c>MessageBox.Show(..., MessageBoxResult.No)</c>. Leave it false for the
        /// rest, which defaulted to the first button.
        /// </param>
        public static async Task<bool> ConfirmAsync(Window owner, string title, string message,
                                                    bool defaultToCancel = false)
            => await new MessageDialog(title, message, showCancel: true, defaultToCancel)
                .ShowDialog<bool?>(owner) == true;
    }
}
