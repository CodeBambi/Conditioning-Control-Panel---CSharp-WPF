using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

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

        /// <summary>
        /// Raises the dialog when it is required and returns whether awareness may now be enabled.
        /// Accepting records the consent flag, seeds the recommended deny groups and performs the
        /// one-time cooldown → intensity migration, so the defaults exist before anything is observed.
        ///
        /// <para>Returns true without showing anything when consent was already given, and false when
        /// the user declines — the caller must then leave awareness off rather than "asking again
        /// later", which is how a declined dialog turns into a nag.</para>
        ///
        /// <para>Async because Avalonia's <c>ShowDialog</c> is; the WPF original was blocking. The
        /// owner is required for the same reason — a parentless modal has nothing to be modal to.</para>
        /// </summary>
        public static async Task<bool> EnsureConsentAsync(Window? owner, AppSettings? settings)
        {
            if (settings == null) return false;
            if (!IsRequired(settings)) return true;

            bool accepted;
            try
            {
                var dialog = new AwarenessConsentDialog();
                // Avalonia's ShowDialog has no ownerless form. No owner means the question cannot
                // be asked, and an unasked question is a decline — never an implicit yes.
                if (owner == null || ReferenceEquals(owner, dialog))
                {
                    Log.Warning("Awareness consent needs an owner window — treating as declined");
                    return false;
                }
                accepted = await dialog.ShowDialog<bool>(owner);
            }
            catch (Exception ex)
            {
                // A dialog that cannot be shown may never become an implicit yes.
                Log.Warning(ex, "Awareness consent dialog failed to open — treating as declined");
                return false;
            }

            if (!accepted)
            {
                Log.Information("Awareness consent declined");
                return false;
            }

            settings.AwarenessConsentShownV2 = true;
            AwarenessPrivacyRules.EnsureSeeded(settings);
            AwarenessIntensityMigration.EnsureMigrated(settings);
            CoreSettings.Save();

            Log.Information("Awareness consent accepted (deny groups seeded, intensity migrated)");
            return true;
        }
    }
}
