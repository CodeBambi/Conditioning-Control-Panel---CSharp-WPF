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
    /// (<paramref name="tourAction"/>) - v6.8's upgrade tour. The button is Collapsed whenever no
    /// action is passed, so every other caller gets the dialog it always got. Its default label is
    /// the welcome-back sheet's <c>wb_tour</c>, so the same offer reads the same on both surfaces
    /// in every language.</para>
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
                    ? TourLabel()
                    : tourButtonText;
                BtnTour.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// The tour button's default label, shared with the welcome-back sheet so the two never
        /// word the same offer differently. Falls back to the English draft rather than to a raw
        /// key if a language file is missing it.
        /// </summary>
        private static string TourLabel()
        {
            try
            {
                var value = Localization.Loc.Get("wb_tour");
                return string.IsNullOrEmpty(value) || value == "wb_tour"
                    ? "Show me around (60 seconds)"
                    : value;
            }
            catch { return "Show me around (60 seconds)"; }
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
