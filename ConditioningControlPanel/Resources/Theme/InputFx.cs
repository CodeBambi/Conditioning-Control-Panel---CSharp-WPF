using System.Windows;

namespace ConditioningControlPanel.Resources.Theme
{
    /// <summary>
    /// Attached state for the Velvet Kit input chrome (Inputs.xaml).
    ///
    /// GlowLevel carries the focus-glow opacity for the implicit TextBox template.
    /// The template's storyboards animate THIS property on the templated TextBox
    /// (no Storyboard.TargetName) and the Border's DropShadowEffect binds to it,
    /// because a name-targeted storyboard resolves "Bd" through the template's
    /// name scope - and that scope is EMPTY until ApplyTemplate has run. A
    /// programmatic Focus() on a TextBox inside a just-uncollapsed panel fires
    /// GotKeyboardFocus before the first layout pass, so the old TargetName="Bd"
    /// storyboard threw InvalidOperationException ("'Bd' name cannot be found in
    /// the name scope of 'System.Windows.Controls.ControlTemplate'") straight
    /// through the caller - which is how a cosmetic glow aborted Patreon sign-in
    /// (LoginDialog.ShowUsernamePanel focuses a collapsed-at-birth TextBox).
    /// Animating the templated parent needs no name scope, so it cannot throw.
    /// </summary>
    public static class InputFx
    {
        public static readonly DependencyProperty GlowLevelProperty =
            DependencyProperty.RegisterAttached(
                "GlowLevel", typeof(double), typeof(InputFx),
                new FrameworkPropertyMetadata(0.0));

        public static double GetGlowLevel(DependencyObject obj) => (double)obj.GetValue(GlowLevelProperty);
        public static void SetGlowLevel(DependencyObject obj, double value) => obj.SetValue(GlowLevelProperty, value);
    }
}
