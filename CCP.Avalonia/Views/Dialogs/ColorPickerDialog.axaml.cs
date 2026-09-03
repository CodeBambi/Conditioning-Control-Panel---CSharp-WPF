using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// The head's colour picker, the stand-in for the WPF head's
    /// <c>System.Windows.Forms.ColorDialog</c>. Every colour button on this head opens this one
    /// dialog rather than growing its own inline panel.
    ///
    /// Returns the chosen colour, or <c>null</c> when the user cancels or closes the window -
    /// the same "no change" answer WinForms' <c>DialogResult.Cancel</c> meant, so a caller keeps
    /// the colour it already had.
    ///
    /// Alpha is off: <c>ColorDialog</c> only ever returned opaque colours and the callers' hex
    /// round-trips assume it.
    /// </summary>
    public partial class ColorPickerDialog : Window
    {
        private readonly ColorView _picker;

        /// <summary>Render/design constructor. Internal, so no production caller can open the
        /// dialog without saying which colour it starts on.</summary>
        internal ColorPickerDialog() : this(Color.FromRgb(255, 0, 255)) { }

        public ColorPickerDialog(Color initial, string? title = null)
        {
            InitializeComponent();

            _picker = this.FindControl<ColorView>("Picker")!;
            _picker.Color = initial;

            title ??= Loc.Get("btn_choose_color");
            Title = title;
            this.FindControl<TextBlock>("TxtTitle")!.Text = title;

            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnOk")!.Click += (_, _) => Close(_picker.Color);
        }

        /// <summary>Opens the picker over <paramref name="owner"/> and returns the chosen colour,
        /// or null if the user cancelled. Avalonia's ShowDialog has no blocking form, so callers
        /// await where WPF's ColorDialog.ShowDialog() returned inline.</summary>
        public static async Task<Color?> PickAsync(Window owner, Color initial, string? title = null)
            => await new ColorPickerDialog(initial, title).ShowDialog<Color?>(owner);
    }
}
