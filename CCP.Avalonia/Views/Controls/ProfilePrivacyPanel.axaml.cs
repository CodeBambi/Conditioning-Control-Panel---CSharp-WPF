using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The Profile tab's "Privacy &amp; Sharing" body, ported from the WPF head.
    ///
    /// NOTHING HERE IS WIRED, and the refusal STANDS - but not for the reason first written. The
    /// original note blamed MainWindow being a <c>System.Windows.Window</c>; that is stale. Every
    /// flag these twelve toggles write (DiscordShareAchievements, PublicShareRealAvatar,
    /// GoonShareAvatar, GoonShareDiscordDm, GoonRichPresence, ...) is on <c>CoreSettings.Current</c>
    /// today, so the SETTINGS half is trivially reachable.
    ///
    /// ponytail: what actually blocks it is that the settings half is not the whole handler, and on
    /// a consent surface the missing half is the one that matters. Read
    /// ConditioningControlPanel/MainWindow/MainWindow.Patreon.cs:855-925 and
    /// MainWindow.AccountShell.cs:279-300:
    ///  - <c>ChkPublicShareRealAvatar</c>, <c>ChkGoonShareAvatar</c> and <c>ChkGoonShareDiscordDm</c>
    ///    each PUSH to the server on change (<c>App.ProfileSync.SyncProfileAsync</c>), precisely so a
    ///    REVOKE lands before the next duel instead of waiting for a scheduled sync. Writing the
    ///    local flag alone gives a knob that reads "not shared" over an avatar the server still
    ///    holds - a consent gate degraded into a label.
    ///  - <c>ChkDiscordRichPresence</c> refuses to arm at all unless
    ///    <c>Current.HasLinkedDiscord</c>, and snaps itself back when it does. That gate is a
    ///    settings read and IS portable; it is listed here only so the next pass wires it with the
    ///    push, not without.
    ///  - <c>ChkShowLevelInPresence</c> and <c>ChkAllowDiscordDm</c> also drive
    ///    <c>App.DiscordRpc</c>, which has no seam here.
    /// The unblocking symbol is a profile-sync seam in CCP.Core (an equivalent of
    /// ConditioningControlPanel/Services/Profile/ProfileSyncService.cs), not a host. Until then the
    /// toggles render and animate and persist nothing, which is visibly inert rather than quietly
    /// wrong. The x:Names are preserved, so each handler is a few lines once that seam exists.
    ///
    /// NOTE the x:Name hazard if you wire this: the ctor uses <c>AvaloniaXamlLoader.Load</c>, so the
    /// generated fields are null. Switch to <c>InitializeComponent()</c> or use <c>FindControl</c>.
    /// </summary>
    public partial class ProfilePrivacyPanel : UserControl
    {
        public ProfilePrivacyPanel()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new ProfilePrivacyPanelViewModel();
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
