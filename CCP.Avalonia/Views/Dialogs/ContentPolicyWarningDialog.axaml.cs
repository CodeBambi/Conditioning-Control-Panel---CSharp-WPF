using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// CCBill compliance — moderation escalation warning modal. Shown once per threshold-cross
    /// by the moderation counter; ShowDialog&lt;bool&gt; yields true on OK.
    ///
    /// <para>NO CALLER ON THIS HEAD. The WPF call site is
    /// <c>AvatarTube/AvatarTubeWindow.xaml.cs:WireModerationCounter</c> -&gt;
    /// <c>OnWarningTriggered</c>, which subscribes to <c>App.ModerationCounter.WarningTriggered</c>
    /// and shows this with the state's <c>HitsInLastTenMinutes</c>. The Avalonia AvatarTubeWindow
    /// is ported and its cooldown half is live, but the counter SINGLETON is not: ModerationCounter
    /// itself is in Core (CCP.Core/Services/Moderation/ModerationCounter.cs) while the app's one
    /// instance is built in ConditioningControlPanel/App.xaml.cs and has no seam. There is
    /// therefore no <c>WarningTriggered</c> to subscribe to, and constructing a second counter
    /// here to raise one would split the persisted sliding window - which is precisely how a
    /// cooldown gets dodged by opening the tube. Blocked on a seam for App.ModerationCounter, not
    /// on this dialog.</para>
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
