using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// CCBill compliance — moderation escalation warning modal. Shown once per threshold-cross
    /// by the moderation counter; ShowDialog&lt;bool&gt; yields true on OK.
    /// </summary>
    public partial class ContentPolicyWarningDialog : Window
    {
        /// <summary>Render/design constructor: sample count so --render-view can draw the dialog.</summary>
        public ContentPolicyWarningDialog() : this(3) { }

        public ContentPolicyWarningDialog(int hitCount)
        {
            AvaloniaXamlLoader.Load(this);
            this.FindControl<TextBlock>("TxtBodyCount")!.Text = Loc.GetF("policy_warning_body_count", hitCount);
            this.FindControl<Button>("BtnOk")!.Click += (_, _) => Close(true);
        }
    }
}
