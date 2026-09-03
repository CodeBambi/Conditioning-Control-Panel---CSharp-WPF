using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// First-enable consent for Awareness (doc 02 §6.3). Ported from the WPF dialog of the same
    /// name; the copy map lives in that file's doc comment. ShowDialog&lt;bool&gt; yields true on
    /// accept, false on decline or dismiss.
    /// </summary>
    public partial class AwarenessConsentDialog : Window
    {
        public AwarenessConsentDialog()
        {
            AvaloniaXamlLoader.Load(this);

            var settings = CoreSettings.Current;

            // The "what leaves your PC" sentence is the one claim the v2 kill switch can invalidate:
            // with UseAwarenessV2 off the legacy pipeline runs and it does send the page title.
            bool v2 = settings.UseAwarenessV2;
            this.FindControl<TextBlock>("TxtLeavesBody")!.Text = Loc.Get(v2
                ? "awareness_consent_leaves_body"
                : "awareness_consent_leaves_body_legacy");

            this.FindControl<TextBlock>("TxtRetention")!.Text =
                Loc.GetF("awareness_consent_retention_fmt", settings.AwarenessRetentionDays);

            this.FindControl<Button>("BtnAccept")!.Click += (_, _) => Close(true);
            this.FindControl<Button>("BtnDecline")!.Click += (_, _) => Close(false);
        }

        /// <summary>
        /// True when the dialog must be raised before awareness may be switched on: v2's consent has
        /// never been accepted. Null settings read as "ask", because the only thing worse than an extra
        /// dialog is a silent one.
        /// </summary>
        public static bool IsRequired(AppSettings? settings) => settings?.AwarenessConsentShownV2 != true;

        // ponytail: EnsureConsent needs AwarenessPrivacyRules.EnsureSeeded
        // (ConditioningControlPanel/Services/Awareness/AwarenessPrivacyRules.cs) and
        // AwarenessIntensityMigration.EnsureMigrated
        // (ConditioningControlPanel/Services/Awareness/AwarenessIntensityMigration.cs), both still
        // in the WPF head. Accepting consent without seeding the recommended deny groups would
        // change what awareness observes, so this stays unported rather than half-ported.
    }
}
