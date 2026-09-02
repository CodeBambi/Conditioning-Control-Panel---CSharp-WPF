using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// First-enable consent for Awareness (doc 02 §6.3). Ported from the WPF dialog of the same
    /// name; the copy map lives in that file's doc comment. ShowDialog&lt;bool&gt; yields true on
    /// accept, false on decline or dismiss.
    /// </summary>
    public partial class AwarenessConsentDialog : Window
    {
        /// <summary>Matches <c>AppSettings.AwarenessRetentionDays</c>'s default; used only headlessly.</summary>
        private const int AppSettingsRetentionFallback = 30;

        public AwarenessConsentDialog()
        {
            AvaloniaXamlLoader.Load(this);

            // ponytail: needs AppSettings (UseAwarenessV2, AwarenessRetentionDays), wired when it moves to Core.
            // Until then the WPF null-settings fallbacks apply: v2 on, 30 days.
            const bool v2 = true;
            this.FindControl<TextBlock>("TxtLeavesBody")!.Text = Loc.Get(v2
                ? "awareness_consent_leaves_body"
                : "awareness_consent_leaves_body_legacy");
            this.FindControl<TextBlock>("TxtRetention")!.Text =
                Loc.GetF("awareness_consent_retention_fmt", AppSettingsRetentionFallback);

            this.FindControl<Button>("BtnAccept")!.Click += (_, _) => Close(true);
            this.FindControl<Button>("BtnDecline")!.Click += (_, _) => Close(false);
        }

        // ponytail: IsRequired/EnsureConsent need AppSettings, AwarenessPrivacyRules and
        // AwarenessIntensityMigration (all head-only); ported when they move to Core.
    }
}
