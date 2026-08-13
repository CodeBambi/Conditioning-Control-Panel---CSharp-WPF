using System;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "What's New" dialog shown once after an update. Uses a fixed window size with a
    /// scrollable notes region and a pinned OK button, so long patch notes can never push
    /// the button off-screen (the old MessageBox-based version could — see ccp-bugs #427).
    ///
    /// <para>A release that moves things around can also hand the dialog a secondary CTA
    /// (<paramref name="tourAction"/>) - v6.8's "Show me around (60s)" upgrade tour. The button
    /// is Collapsed whenever no action is passed, so every other caller gets the dialog it
    /// always got. Copy is hardcoded English by repo convention (Windows/FeatureIntroPopup).</para>
    /// </summary>
    public partial class WhatsNewDialog : Window
    {
        private readonly Action? _tourAction;

        public WhatsNewDialog(string title, string notes,
                              Action? tourAction = null, string? tourButtonText = null)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtNotes.Text = notes;

            _tourAction = tourAction;
            if (tourAction != null)
            {
                BtnTour.Content = string.IsNullOrWhiteSpace(tourButtonText)
                    ? "Show me around (60s)"
                    : tourButtonText;
                BtnTour.Visibility = Visibility.Visible;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnTour_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();

            // QUEUED, never called inline: Close() only starts the unwind, so invoking here would
            // run the tour from inside this dialog's modal message loop - before ShowDialog()
            // returns to the caller and before the caller's finally block releases
            // IsStartupDialogShowing. A Normal-priority post runs after the whole call stack that
            // opened us has finished, which is exactly what the tour needs.
            var action = _tourAction;
            if (action == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "What's New: the tour action threw"); }
            }), DispatcherPriority.Normal);
        }
    }
}
