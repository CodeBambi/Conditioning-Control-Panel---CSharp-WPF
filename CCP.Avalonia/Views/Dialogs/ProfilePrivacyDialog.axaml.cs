using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/ProfilePrivacyDialog.xaml.cs. Holds the
    /// Profile tab's relocated sharing controls. The dialog owns no state of its own: it borrows
    /// the long-lived <see cref="ProfilePrivacyPanel"/> instance for as long as it is open, then
    /// hands it back on close so the host keeps writing into the same controls.
    /// </summary>
    public partial class ProfilePrivacyDialog : Window
    {
        private readonly Border _panelHost;

        /// <summary>Render/design constructor: a fresh panel so --render-view can draw the dialog.</summary>
        public ProfilePrivacyDialog() : this(new ProfilePrivacyPanel()) { }

        public ProfilePrivacyDialog(ProfilePrivacyPanel panel)
        {
            AvaloniaXamlLoader.Load(this);
            _panelHost = this.FindControl<Border>("PanelHost")!;

            // An element has exactly one parent: if a previous dialog was torn down without its
            // Closed handler running, detach before re-parenting rather than throwing.
            if (panel.Parent is Border previous)
                previous.Child = null;
            _panelHost.Child = panel;

            Closed += (_, _) => _panelHost.Child = null;
            this.FindControl<Button>("BtnJoinDiscord")!.Click += (_, _) => BtnJoinDiscord_Click();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
        }

        /// <summary>
        /// WPF hopped through <c>MainWindow.BtnDiscord_Click</c>, whose entire body is
        /// <c>Process.Start(DiscordLinks.Invite)</c> plus a log line — so the hop was never the
        /// blocker and nothing here is waiting on Core. <see cref="DiscordLinks"/> is in Core
        /// already; <c>TopLevel.Launcher</c> is this head's shell-execute.
        /// </summary>
        private async void BtnJoinDiscord_Click()
        {
            try
            {
                if (await Launcher.LaunchUriAsync(new Uri(DiscordLinks.Invite)))
                    Log.Information("Opened Discord invite link");
                else
                    Log.Warning("Nothing on this system handled the Discord invite URI");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open Discord link");
            }
        }
    }
}
