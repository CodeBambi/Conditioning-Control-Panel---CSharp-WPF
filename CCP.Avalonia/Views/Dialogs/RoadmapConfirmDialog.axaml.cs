using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Themed dialog for confirming photo submission.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/RoadmapConfirmDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> becomes <c>Close(bool)</c>; Avalonia carries the result
    ///    through <c>ShowDialog&lt;bool?&gt;</c>. <c>Confirmed</c> is kept as-is.
    ///  - The Click handlers are wired in the constructor rather than in markup.
    /// </summary>
    public partial class RoadmapConfirmDialog : Window
    {
        public bool Confirmed { get; private set; }

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        internal RoadmapConfirmDialog()
            : this("The Blank Slate", "A photo of your empty desk, taken from above.") { }

        public RoadmapConfirmDialog(string stepTitle, string photoRequirement)
        {
            AvaloniaXamlLoader.Load(this);

            this.FindControl<TextBlock>("TxtStepTitle")!.Text = $"\"{stepTitle}\"";
            this.FindControl<TextBlock>("TxtRequirement")!.Text = photoRequirement;

            this.FindControl<Button>("BtnNo")!.Click += (_, _) => { Confirmed = false; Close(false); };
            this.FindControl<Button>("BtnYes")!.Click += (_, _) => { Confirmed = true; Close(true); };
        }
    }
}
