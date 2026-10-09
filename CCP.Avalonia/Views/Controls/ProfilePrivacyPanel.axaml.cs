using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The Profile tab's "Privacy &amp; Sharing" body, ported from the WPF head.
    ///
    /// <para><b>What is wired (fix wave f4-you, 2026-10-09).</b> Every switch is PAINTED from
    /// <c>CoreSettings.Current</c> each time the panel joins a window (WPF UpdateDiscordTabUI,
    /// MainWindow.Browser.cs:1495), so it shows the stored value instead of a markup default, and the
    /// status line, its caption and the button's word follow the Discord account. Three switches
    /// WRITE, because their whole WPF handler is a settings write: Share achievements, Share level
    /// milestones (MainWindow.Patreon.cs ChkShareAchievements_Changed / ChkShareLevelUps_Changed) and
    /// Goon Game rich presence (ChkGoonRichPresence_Changed, local only by contract).</para>
    ///
    /// <para><b>What is not, and why.</b> On a consent surface the push is the half that matters:
    ///  - SEAM(core sync): Allow DMs, Share profile picture, Show online status, the public real
    ///    avatar and the two Goon share flags each push to the server on change in WPF
    ///    (<c>App.ProfileSync.SyncProfileAsync</c>) so a REVOKE lands at once. Core
    ///    <c>SyncPush.Sent</c> does not carry those fields (SyncBody.Field has them: AllowDiscordDm,
    ///    ShowOnlineStatus, ShareProfilePicture, PublicShareAvatar, GoonShareAvatar, GoonShareDm). A
    ///    local write alone would read "not shared" over data the server still holds, so these six
    ///    stay unwired until SyncPush sends them.
    ///  - SEAM(discord rpc): Rich Presence and Show level drive <c>App.DiscordRpc</c>, which this head
    ///    does not have. Rich Presence must also refuse to arm without
    ///    <c>Current.HasLinkedDiscord</c> (WPF MainWindow.AccountShell.cs:279-300).
    ///  - SEAM(account): the Login / Link Discord / Logout button needs the sign-in, link and logout
    ///    flows that live in Views/Controls/AppSettings/AccountSettingsSection (WPF
    ///    BtnDiscordTabLogin_Click, MainWindow.Browser.cs:1366).</para>
    ///
    /// The ctor uses <c>AvaloniaXamlLoader.Load</c>, so controls are reached with FindControl.
    /// </summary>
    public partial class ProfilePrivacyPanel : UserControl
    {
        private bool _painting;

        public ProfilePrivacyPanel()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new ProfilePrivacyPanelViewModel();

            // The three switches whose WPF handler is a settings write and nothing else.
            Wire("ChkDiscordTabShareAchievements", s => s.DiscordShareAchievements, (s, v) => s.DiscordShareAchievements = v);
            Wire("ChkDiscordTabShareLevelUps", s => s.DiscordShareLevelUps, (s, v) => s.DiscordShareLevelUps = v);
            Wire("ChkGoonRichPresence", s => s.GoonRichPresence, (s, v) => s.GoonRichPresence = v);   // never synced
        }

        private void Wire(string name, System.Func<global::ConditioningControlPanel.Models.AppSettings, bool> read,
            System.Action<global::ConditioningControlPanel.Models.AppSettings, bool> write)
        {
            if (this.FindControl<CheckBox>(name) is not { } box) return;
            box.IsCheckedChanged += (_, _) =>
            {
                if (_painting) return;                       // a repaint of the stored value is not a click
                var s = CoreSettings.Current;
                var on = box.IsChecked == true;
                if (read(s) == on) return;
                write(s, on);
                CoreSettings.Save();
                // The rail's "N on, M off" line counts these.
                if (TopLevel.GetTopLevel(this) is Window { Owner: Views.Windows.MainShellWindow shell })
                    shell.UpdateProfileSharingSummary();
            };
        }

        /// <summary>The panel joins a window only when its dialog opens, after the shell's offline
        /// pass; grey its login button here too (WPF UpdateOfflineModeUI), and repaint from settings.</summary>
        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Refresh();
            Views.Windows.MainShellWindow.SetOfflineDisabled(
                this.FindControl<Button>("BtnDiscordTabLogin"), CoreSettings.Current.OfflineMode);
        }

        /// <summary>WPF UpdateDiscordTabUI: the account line and every switch, from the stored values.</summary>
        internal void Refresh()
        {
            var s = CoreSettings.Current;
            var discord = Platform.AccountSeed.Discord;
            var linked = discord?.IsAuthenticated == true;
            if (this.FindControl<TextBlock>("TxtDiscordTabStatus") is { } status)
                status.Text = linked ? Loc.GetF("label_connected_as_0", discord!.Username) : Loc.Get("label_not_connected");
            if (this.FindControl<TextBlock>("TxtDiscordTabInfo") is { } info)
                info.Text = Loc.Get(linked ? "label_discord_account_linked" : "label_link_discord_for_community_features");
            if (this.FindControl<Button>("BtnDiscordTabLogin")?.Content is TextBlock word)
                word.Text = Loc.Get(linked ? "btn_logout" : string.IsNullOrEmpty(s.UnifiedId) ? "btn_login" : "btn_link_discord_2");

            _painting = true;
            try
            {
                Paint("ChkDiscordTabRichPresence", s.DiscordRichPresenceEnabled);
                Paint("ChkDiscordTabShowLevel", s.DiscordShowLevelInPresence);
                Paint("ChkDiscordTabShowOnline", s.ShowOnlineStatus);
                Paint("ChkDiscordTabShareAchievements", s.DiscordShareAchievements);
                Paint("ChkDiscordTabShareLevelUps", s.DiscordShareLevelUps);
                Paint("ChkDiscordTabAllowDm", s.AllowDiscordDm);
                Paint("ChkDiscordTabSharePfp", s.ShareProfilePicture);
                Paint("ChkPublicShareRealAvatar", s.PublicShareRealAvatar);
                Paint("ChkGoonShareAvatar", s.GoonShareAvatar);
                Paint("ChkGoonShareDiscordDm", s.GoonShareDiscordDm);
                Paint("ChkGoonRichPresence", s.GoonRichPresence);
            }
            finally { _painting = false; }
        }

        private void Paint(string name, bool on)
        {
            if (this.FindControl<CheckBox>(name) is { } box) box.IsChecked = on;
        }
    }

    /// <summary>
    /// Supplies the strings the view binds to, all from CCP.Core's <see cref="Loc"/> - the same
    /// runtime and the same JSON the WPF head reads. This exists because WPF's {loc:Str key}
    /// markup extension derives from System.Windows.Markup.MarkupExtension and stays in the head.
    ///
    /// Every key below is static in the original markup; none of them is formatted. The three
    /// controls MainWindow.Browser.cs overwrites at runtime (TxtDiscordTabStatus,
    /// TxtDiscordTabInfo, BtnDiscordTabLogin) take their markup default here, exactly as the WPF
    /// panel does before a Discord connection exists - the "connected" strings
    /// (label_connected_as_0, label_discord_account_linked, btn_logout, btn_link_discord_2) are
    /// set by that host, which is not ported, so they are not modelled here.
    /// </summary>
    public sealed class ProfilePrivacyPanelViewModel
    {
        public string LocNotConnected => Loc.Get("label_not_connected");
        public string LocLinkDiscordForCommunityFeatures => Loc.Get("label_link_discord_for_community_features");
        public string LocLogin => Loc.Get("btn_login");

        public string LocGroupPresence => Loc.Get("profile_privacy_group_presence");
        public string LocDiscordRichPresence => Loc.Get("label_discord_rich_presence");
        public string LocShowYourActivityStatus => Loc.Get("label_show_your_activity_status");
        public string LocShowLevelInStatus => Loc.Get("label_show_level_in_status");
        public string LocDisplayYourLevel => Loc.Get("label_display_your_level");
        public string LocShowOnlineStatus => Loc.Get("label_show_online_status");
        public string LocAppearOfflineWhenDisabled => Loc.Get("label_appear_offline_when_disabled");

        public string LocCommunitySharing => Loc.Get("label_community_sharing");
        public string LocShareAchievements => Loc.Get("label_share_achievements");
        public string LocPostAchievementsToDiscord => Loc.Get("label_post_achievements_to_discord");
        public string LocShareLevelMilestones => Loc.Get("label_share_level_milestones");
        public string LocPostLevelUpsToDiscord => Loc.Get("label_post_level_ups_to_discord");
        public string LocAllowDmsFromLeaderboard => Loc.Get("label_allow_dms_from_leaderboard");
        public string LocLetOthersMessageYou => Loc.Get("label_let_others_message_you");
        public string LocShareProfilePicture => Loc.Get("label_share_profile_picture");
        public string LocShowYourAvatarToOthers => Loc.Get("label_show_your_avatar_to_others");
        public string LocTooltipPublicShareRealAvatar => Loc.Get("tooltip_public_share_real_avatar");
        public string LocPublicShareRealAvatar => Loc.Get("label_public_share_real_avatar");
        public string LocPublicShareRealAvatarDesc => Loc.Get("label_public_share_real_avatar_desc");

        public string LocGoonGameSharing => Loc.Get("label_goon_game_sharing");
        public string LocTooltipGoonShareAvatar => Loc.Get("tooltip_goon_share_avatar");
        public string LocGoonShareAvatar => Loc.Get("label_goon_share_avatar");
        public string LocTooltipGoonShareDiscordDm => Loc.Get("tooltip_goon_share_discord_dm");
        public string LocGoonShareDiscordDm => Loc.Get("label_goon_share_discord_dm");
        public string LocTooltipGoonRichPresence => Loc.Get("tooltip_goon_rich_presence");
        public string LocGoonRichPresence => Loc.Get("label_goon_rich_presence");
    }
}
