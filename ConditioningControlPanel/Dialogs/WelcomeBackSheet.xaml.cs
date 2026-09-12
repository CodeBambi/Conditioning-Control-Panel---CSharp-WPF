using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Everything the welcome-back sheet needs to paint itself, gathered by the caller before the
    /// window exists. A plain value object on purpose: the sheet reads no service and performs no
    /// round trip of its own, which is what lets the render suite build a realistic one with no
    /// <c>App</c>, no settings file and no network.
    /// </summary>
    public sealed class WelcomeBackSheetContent
    {
        /// <summary>What to call them. Empty falls back to a name-less greeting.</summary>
        public string DisplayName { get; init; } = "";

        /// <summary>Level for the subline. Zero or less leaves the level off.</summary>
        public int Level { get; init; }

        /// <summary>Display name of the mod the backup was running, for the subline.</summary>
        public string? BackupModName { get; init; }

        /// <summary>When the backup was taken. Null leaves the date off the subline.</summary>
        public DateTime? BackupTakenAt { get; init; }

        /// <summary>Which rows to show, from <see cref="WelcomeBackDecision.Decide"/>.</summary>
        public WelcomeBackPlan Plan { get; init; } = WelcomeBackPlan.Nothing;

        /// <summary>Mod name for the flavour row's copy. Falls back to the backup's mod name.</summary>
        public string? FlavourModName { get; init; }

        /// <summary>"329 MB" for the flavour row's hint, or empty when the size is unknown.</summary>
        public string FlavourSizeText { get; init; } = "";

        /// <summary>Version the patch notes belong to, e.g. "6.8.0".</summary>
        public string VersionLabel { get; init; } = "";

        /// <summary>Patch notes body. Empty hides the whole "what changed" row.</summary>
        public string PatchNotes { get; init; } = "";

        /// <summary>
        /// The one muted season sentence, or null for the usual case of nothing to say. Gated by
        /// <see cref="WelcomeBackDecision.ShouldShowSeasonLine"/> at the call site - the sheet
        /// prints what it is handed and decides nothing about seasons itself.
        /// </summary>
        public string? SeasonLine { get; init; }

        /// <summary>
        /// Runs the upgrade tour. Posted after this window unwinds, never called inline. Null hides
        /// the offer.
        /// </summary>
        public Action? TourAction { get; init; }
    }

    /// <summary>
    /// The single sheet a returning user meets on a new PC: restore the cloud backup, bring the
    /// flavour, read what changed, and one line about the season if the server rotated the board.
    ///
    /// <para><b>What it replaced.</b> Three unowned MessageBoxes from the old cloud-restore check,
    /// the What's New dialog and the season box - up to five modal stops, in dispatcher order, on
    /// a machine the user had not reached the app on yet.</para>
    ///
    /// <para><b>It decides nothing.</b> Which rows exist comes in as a
    /// <see cref="WelcomeBackPlan"/>; the restore itself, the download and the tour all run in the
    /// caller after <c>ShowDialog</c> returns. The sheet's entire output is two booleans
    /// (<see cref="RestoreChosen"/>, <see cref="BringFlavourChosen"/>) and, for the tour, the same
    /// deferred post <c>WhatsNewDialog</c> uses - a tour started from inside this window's modal
    /// message loop would run before the presenter's finally released
    /// <c>IsStartupDialogShowing</c>, and the spotlight would open underneath a flag every poller
    /// in the app is watching.</para>
    /// </summary>
    public partial class WelcomeBackSheet : Window
    {
        private readonly WelcomeBackSheetContent _content;

        /// <summary>The restore toggle as the user left it. False whenever the row was not shown.</summary>
        public bool RestoreChosen { get; private set; }

        /// <summary>The flavour toggle as the user left it. False whenever the row was not shown.</summary>
        public bool BringFlavourChosen { get; private set; }

        public WelcomeBackSheet(WelcomeBackSheetContent content)
        {
            _content = content ?? new WelcomeBackSheetContent();
            InitializeComponent();
            Render();
        }

        // ------------------------------------------------------------------ copy

        private void Render()
        {
            var plan = _content.Plan ?? WelcomeBackPlan.Nothing;

            TxtHeading.Text = string.IsNullOrWhiteSpace(_content.DisplayName)
                ? Str("wb_heading_anon", "Welcome back.")
                : StrF("wb_heading", "Welcome back, {0}.", _content.DisplayName.Trim());

            TxtSubline.Text = BuildSubline();
            TxtSubline.Visibility = string.IsNullOrWhiteSpace(TxtSubline.Text)
                ? Visibility.Collapsed : Visibility.Visible;

            // ---- restore ----
            RowRestore.Visibility = plan.ShowRestoreRow ? Visibility.Visible : Visibility.Collapsed;
            ChkRestore.IsChecked = plan.ShowRestoreRow;
            TxtRestoreLabel.Text = Str("wb_restore_label", "Restore my settings from the cloud backup");
            TxtRestoreHint.Text = Str("wb_restore_hint",
                "Your sliders, doors and preferences replace this PC's defaults. Your level, your XP and your unlocks are untouched.");

            // ---- flavour ----
            RowFlavour.Visibility = plan.ShowFlavourRow ? Visibility.Visible : Visibility.Collapsed;
            ChkFlavour.IsChecked = plan.ShowFlavourRow;
            var flavourName = FirstNonEmpty(_content.FlavourModName, _content.BackupModName)
                              ?? Str("wb_flavour_generic", "your flavour");
            TxtFlavourLabel.Text = StrF("wb_flavour_label", "Bring {0} with me", flavourName);
            TxtFlavourHint.Text = string.IsNullOrWhiteSpace(_content.FlavourSizeText)
                ? Str("wb_flavour_hint", "It downloads in the background. You can use the app while it lands.")
                : StrF("wb_flavour_hint_size",
                       "{0}, downloading in the background. You can use the app while it lands.",
                       _content.FlavourSizeText);

            // ---- what changed ----
            var hasNotes = !string.IsNullOrWhiteSpace(_content.PatchNotes);
            RowWhatsNew.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
            TxtWhatsNewToggle.Text = string.IsNullOrWhiteSpace(_content.VersionLabel)
                ? Str("wb_whats_new_toggle", "What changed since you were last here")
                : StrF("wb_whats_new_toggle_version", "What changed since you were last here (v{0})",
                       _content.VersionLabel);
            TxtNotes.Text = _content.PatchNotes;

            TxtTour.Text = Str("wb_tour", "Show me around (60 seconds)");
            TxtTour.Visibility = _content.TourAction != null ? Visibility.Visible : Visibility.Collapsed;

            // ---- season ----
            if (!string.IsNullOrWhiteSpace(_content.SeasonLine))
            {
                TxtSeasonLine.Text = _content.SeasonLine;
                TxtSeasonLine.Visibility = Visibility.Visible;
            }

            BtnGo.Content = Str("wb_go", "Let's go");
        }

        /// <summary>
        /// "Level 24 · Circe · backup from Sep 3, 2026". Every part is optional and a missing one
        /// takes its separator with it, so an account with no backup and no mod still reads as a
        /// sentence rather than as punctuation.
        /// </summary>
        private string BuildSubline()
        {
            var parts = new List<string>(3);

            if (_content.Level > 0)
                parts.Add(StrF("wb_sub_level", "Level {0}", _content.Level));

            if (!string.IsNullOrWhiteSpace(_content.BackupModName))
                parts.Add(_content.BackupModName!.Trim());

            if (_content.BackupTakenAt is DateTime taken)
            {
                try
                {
                    parts.Add(StrF("wb_sub_backup", "backup from {0}",
                        taken.ToLocalTime().ToString("MMM d, yyyy")));
                }
                catch { }
            }

            return string.Join("  ·  ", parts);
        }

        private static string? FirstNonEmpty(params string?[] candidates)
        {
            foreach (var c in candidates)
                if (!string.IsNullOrWhiteSpace(c)) return c!.Trim();
            return null;
        }

        // ------------------------------------------------------------------ interaction

        private void RestoreLabel_Click(object sender, MouseButtonEventArgs e)
            => ChkRestore.IsChecked = ChkRestore.IsChecked != true;

        private void FlavourLabel_Click(object sender, MouseButtonEventArgs e)
            => ChkFlavour.IsChecked = ChkFlavour.IsChecked != true;

        private void WhatsNewToggle_Click(object sender, MouseButtonEventArgs e)
        {
            NotesPanel.Visibility = NotesPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// The tour is the ONE thing on this sheet that is also a way out: taking it means the
        /// user has finished reading, so it commits the toggles exactly as the primary button
        /// does and then closes.
        /// </summary>
        private void Tour_Click(object sender, MouseButtonEventArgs e)
        {
            var tour = _content.TourAction;
            Commit();

            if (tour == null) return;

            // QUEUED, never called inline. Close() only starts the unwind: invoking here would run
            // the tour from inside this window's modal message loop, before ShowDialog returns to
            // the presenter and before the presenter's finally has put IsStartupDialogShowing back
            // down. A Normal-priority post runs after the whole stack that opened us has finished.
            // (Normal, never Loaded - this app starves the low priorities and a Loaded post here
            // would silently never run, which is how the original first-launch tour was lost.)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { tour(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[WelcomeBack] the tour action threw"); }
            }), DispatcherPriority.Normal);
        }

        private void BtnGo_Click(object sender, RoutedEventArgs e) => Commit();

        private void Commit()
        {
            var plan = _content.Plan ?? WelcomeBackPlan.Nothing;
            RestoreChosen = plan.ShowRestoreRow && ChkRestore.IsChecked == true;
            BringFlavourChosen = plan.ShowFlavourRow && ChkFlavour.IsChecked == true;

            try { DialogResult = true; } catch { /* not shown modally (render suite) */ }
            Close();
        }

        /// <summary>
        /// Esc is "Let's go with nothing ticked", not "come back next launch". The sheet is the
        /// only offer this backup ever gets, so a dismissal has to be a decision - and both
        /// toggles are recorded as declined rather than left ambiguous.
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            ChkRestore.IsChecked = false;
            ChkFlavour.IsChecked = false;
            Commit();
        }

        /// <summary>WindowStyle="None" leaves no chrome to drag by.</summary>
        private void Sheet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
            catch { /* DragMove throws when the button is already up */ }
        }

        // ------------------------------------------------------------------ strings

        /// <summary>Localized string with an English fallback - a missing wb_ key renders the
        /// English draft, never the raw key.</summary>
        private static string Str(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal)
                    ? english
                    : value;
            }
            catch { return english; }
        }

        private static string StrF(string key, string english, params object[] args)
        {
            var template = Str(key, english);
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }
    }
}
