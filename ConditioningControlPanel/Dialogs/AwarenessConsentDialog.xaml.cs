using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel
{
    /// <summary>
    /// First-enable consent for Awareness (doc 02 §6.3): what she watches, what leaves your PC, what
    /// never does, and what you can undo. Shown once — <see cref="AppSettings.AwarenessConsentShownV2"/>
    /// records that it was accepted — after which the dial is a one-click toggle.
    ///
    /// <para><b>The copy map.</b> Every claim on this dialog was checked against the merged code path;
    /// this list is the receipt, and it is expected to be updated in the same commit as any behaviour
    /// change that touches it:</para>
    /// <list type="bullet">
    ///   <item><i>"the app or site in front of you… counted on this PC"</i> —
    ///   <c>ActivityLedger.NoteFocus(appId, cluster, category, at)</c> has no parameter for a title,
    ///   which is what makes "titles are never written to disk" a structural fact rather than a promise;
    ///   the file it writes is <c>%LOCALAPPDATA%\ConditioningControlPanel\awareness_ledger.json</c>.</item>
    ///   <item><i>"a short summary… a category, the app's name and rounded numbers"</i> —
    ///   <c>AwarenessProjection.BuildCloudProjection</c>, which is the only cloud shape and rounds
    ///   minutes to 5.</item>
    ///   <item><i>"for adult sites she is told the category and nothing else"</i> — same method: an
    ///   adult-cluster frame drops the app id, the display name, the title and the day arc.</item>
    ///   <item><i>"page titles stay here unless you name an app yourself"</i> —
    ///   <c>AwarenessPrivacyRules.IsTitleAllowed</c> returns false while
    ///   <c>AwarenessTitleAllowList</c> is empty, and it ships empty.</item>
    ///   <item><i>"a private or incognito window is dropped entirely"</i> —
    ///   <c>AwarenessPrivacyRules.LooksIncognito</c> is checked before anything else and has no
    ///   setting behind it.</item>
    ///   <item><i>"she never reads what you type or what is on the screen"</i> — true of awareness;
    ///   the footnote names the Triggers tab's keyword/OCR engine, which is the separate feature that
    ///   does, so the sentence cannot be read as a claim about the whole app.</item>
    ///   <item><i>"kept for N days"</i> — <c>AwarenessRetentionDays</c>, pruned from
    ///   <c>ActivityLedger.Start</c> and on every day rollover, never from a UI surface. The number is
    ///   substituted live rather than written into the sentence, so changing the setting cannot leave
    ///   the copy stale.</item>
    /// </list>
    /// </summary>
    public partial class AwarenessConsentDialog : Window
    {
        public AwarenessConsentDialog()
        {
            InitializeComponent();

            var settings = App.Settings?.Current;

            // The "what leaves your PC" sentence is the one claim the v2 kill switch can invalidate:
            // with UseAwarenessV2 off the legacy pipeline runs and it does send the page title.
            bool v2 = settings?.UseAwarenessV2 ?? true;
            TxtLeavesBody.Text = Loc.Get(v2
                ? "awareness_consent_leaves_body"
                : "awareness_consent_leaves_body_legacy");

            int days = settings?.AwarenessRetentionDays ?? AppSettingsRetentionFallback;
            TxtRetention.Text = Loc.GetF("awareness_consent_retention_fmt", days);
        }

        /// <summary>Matches <c>AppSettings.AwarenessRetentionDays</c>'s default; used only headlessly.</summary>
        private const int AppSettingsRetentionFallback = 30;

        /// <summary>
        /// True when the dialog must be raised before awareness may be switched on: v2's consent has
        /// never been accepted. Null settings read as "ask", because the only thing worse than an extra
        /// dialog is a silent one.
        /// </summary>
        public static bool IsRequired(AppSettings? settings) => settings?.AwarenessConsentShownV2 != true;

        /// <summary>
        /// Raises the dialog when it is required and returns whether awareness may now be enabled.
        /// Accepting records the consent flag, seeds the recommended deny groups and performs the
        /// one-time cooldown → intensity migration, so the defaults exist before anything is observed.
        ///
        /// <para>Returns true without showing anything when consent was already given, and false when
        /// the user declines — the caller must then leave awareness off rather than "asking again
        /// later", which is how a declined dialog turns into a nag.</para>
        /// </summary>
        public static bool EnsureConsent(Window? owner, AppSettings? settings)
        {
            if (settings == null) return false;
            if (!IsRequired(settings)) return true;

            bool accepted;
            try
            {
                var dialog = new AwarenessConsentDialog();
                if (owner != null && !ReferenceEquals(owner, dialog)) dialog.Owner = owner;
                accepted = dialog.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                // A dialog that cannot be shown may never become an implicit yes.
                App.Logger?.Warning(ex, "Awareness consent dialog failed to open — treating as declined");
                return false;
            }

            if (!accepted)
            {
                App.Logger?.Information("Awareness consent declined");
                return false;
            }

            settings.AwarenessConsentShownV2 = true;
            AwarenessPrivacyRules.EnsureSeeded(settings);
            AwarenessIntensityMigration.EnsureMigrated(settings);
            App.Settings?.Save();

            App.Logger?.Information("Awareness consent accepted (deny groups seeded, intensity migrated)");
            return true;
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnDecline_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
