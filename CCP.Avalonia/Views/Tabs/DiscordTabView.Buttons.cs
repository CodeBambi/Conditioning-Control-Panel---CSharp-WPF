using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Profile card's buttons (social#35), ported from WPF MainWindow.Browser.cs (BtnProfileDiscord_Click,
    /// BtnDiscordTabLogin_Click, the DM button paint at 1975 / 2195, the link notice at 1468),
    /// MainWindow.ProfileBubble.cs (OpenPublicProfilePage, RefreshProfileShareButton) and
    /// MainWindow.ProfileSpiral.cs (OpenSpiralMapFromProfile).
    /// </summary>
    public partial class DiscordTabView
    {
        /// <summary>WPF MainWindow.TabNavigation.cs ProfileSharingUrl (WebAppUrl + "/dashboard/profile-sharing").</summary>
        internal const string ProfileSharingUrl = "https://app.cclabs.app/dashboard/profile-sharing";

        internal static string DiscordProfileUrl(string discordId) => $"https://discord.com/users/{discordId}";

        /// <summary>WPF RefreshProfileShareButton + the tab's link notice. Visible-but-disabled share CTA when
        /// signed out; the notice shows until a Discord is linked. Never throws.</summary>
        internal void RefreshProfileChrome()
        {
            try
            {
                if (this.FindControl<Button>("BtnProfileShare") is { } share)
                {
                    bool signedIn = CoreAccount.IsLoggedIn;
                    share.IsEnabled = signedIn;
                    share.Opacity = signedIn ? 1.0 : 0.55;
                    ToolTip.SetTip(share, Loc.Get(signedIn ? "profile_btn_share_tip" : "profile_btn_share_tip_locked"));
                }
                if (this.FindControl<Border>("ProfileLinkNotice") is { } notice)
                    notice.IsVisible = CoreSettings.Current?.HasLinkedDiscord != true;
            }
            catch (Exception ex) { Log.Debug("RefreshProfileChrome: {E}", ex.Message); }
        }

        /// <summary>The DM button: shown with a Discord id, the label is the name, the id rides the Tag.</summary>
        internal void ShowDiscordDm(string? discordId, string? label)
        {
            var btn = this.FindControl<Button>("BtnProfileDiscord");
            if (btn == null) return;
            if (string.IsNullOrEmpty(discordId)) { btn.IsVisible = false; btn.Tag = null; return; }
            btn.IsVisible = true;
            btn.Tag = discordId;
            if (this.FindControl<TextBlock>("TxtProfileDiscordId") is { } txt)
                txt.Text = string.IsNullOrEmpty(label) ? Loc.Get("label_message_on_discord") : label;
        }

        /// <summary>Your own card: the DM button only when you allow DMs and Discord is signed in (WPF 1977).</summary>
        private void ShowOwnDiscordDm()
        {
            var s = CoreSettings.Current;
            var discord = AccountSeed.Discord;
            ShowDiscordDm(s?.AllowDiscordDm == true ? discord?.UserId : null,
                s?.UserDisplayName ?? discord?.CustomDisplayName ?? discord?.UserId);
        }

        private void OpenDiscordDm(object? sender)
        {
            if ((sender as Button)?.Tag is not string id || string.IsNullOrEmpty(id)) return;
            _ = ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), DiscordProfileUrl(id));
            Log.Information("Opened Discord profile for a leaderboard entry");
        }

        private void OpenPublicProfilePage()
        {
            if (!CoreAccount.IsLoggedIn) return;
            _ = ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), ProfileSharingUrl);
        }

        /// <summary>WPF BtnDiscordTabLogin_Click: the Settings &gt; Account Discord flow (sign out, link, or the dialog).</summary>
        private async Task DiscordTabLoginAsync(object? sender)
        {
            await AccountSettingsSection.ProviderButtonAsync("discord", TopLevel.GetTopLevel(this) as Window, sender as Button);
            if (sender is Button b) b.Content = new TextBlock { Text = Loc.Get("btn_link_discord_2"), FontSize = 11, FontWeight = global::Avalonia.Media.FontWeight.Bold };
            RefreshProfileChrome();
        }
    }
}
