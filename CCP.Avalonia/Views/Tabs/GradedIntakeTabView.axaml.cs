using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// "Graded Intake" — the banded-descent intake, promoted out of the Lab into its own
    /// Exclusives page. Nothing about the feature changed in the move: the same
    /// controls, handlers and settings are simply hosted here instead of on the Lab card.
    /// Pop Quiz rode along because it lived inside the same card, but it is NOT premium and
    /// deliberately sits outside <c>GradedIntakeGate</c>.
    ///
    /// The gate is no longer a plain t1 lock: free accounts get one run a week, so
    /// <c>GradedIntakeGate</c> (with its swappable copy) and <c>GradedIntakePassBanner</c> are
    /// painted together from <c>MainWindow.RefreshGradedIntakeGate</c>, which is the only thing
    /// that should ever touch their visibility. That host does not exist on this head, so both
    /// keep the authored starting state from the markup (hidden), exactly as WPF does before the
    /// host's first refresh pass.
    ///
    /// On WPF every handler below is a one-line hop to the identically named <c>MainWindow</c>
    /// method in <c>MainWindow.Lab.cs</c>. None of that is on this head, so all of them are stubs.
    /// </summary>
    public partial class GradedIntakeTabView : UserControl
    {
        public GradedIntakeTabView()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ponytail: needs MainWindow.Lab.cs (classic quiz launch, the intake window, the pop-quiz
        // settings writer and the gate unlock popup), wired when they move to Core.
        private void BtnStartQuiz_Click(object? sender, RoutedEventArgs e) { }
        private void BtnStartIntake_Click(object? sender, RoutedEventArgs e) { }
        private void BtnTestPopQuiz_Click(object? sender, RoutedEventArgs e) { }
        private void ChkPopQuizEnabled_Changed(object? sender, RoutedEventArgs e) { }
        private void SliderPopQuizFrequency_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { }

        /// <summary>Gate CTA. Serves both closed states: the shared App Info &amp; Data popup is
        /// where signing in lives as well as where the tiers are, so NeedsLogin and Spent can share
        /// one destination even though their button labels differ.</summary>
        private void BtnGI_GateUnlock_Click(object? sender, RoutedEventArgs e) { }
    }
}
