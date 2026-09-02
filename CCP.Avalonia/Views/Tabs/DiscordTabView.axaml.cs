using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/DiscordTabView.xaml.cs — the Profile tab's
    /// "Trainer Card".
    ///
    /// What is stubbed: every one of the WPF view's fourteen members that reached out of the view.
    /// The tab owns no logic of its own on WPF either — each handler is a one-line forward to
    /// <c>Window.GetWindow(this) is MainWindow mw</c>, and that MainWindow is a
    /// <c>System.Windows.Window</c> in the WPF head. Search, the profile ledger, the vat, the
    /// faucet, the wardrobe, the spiral map and the two dialogs all live in MainWindow partials
    /// that have not moved to Core, so every handler here is a no-op with a ponytail marker.
    ///
    /// Dropped: the thirteen <c>ChkDiscordTab*</c> passthrough properties and the two
    /// <c>ProfileViewerAvatar</c>/<c>ProfileOnlineIndicator</c> ones. They exist solely so
    /// MainWindow's partials can write <c>DiscordTab.X</c> without knowing the control moved into
    /// <see cref="ProfilePrivacyPanel"/>/<see cref="AdornedAvatar"/>; nothing on this head calls
    /// them, and both hosts already expose the same members under the same names, so the
    /// passthroughs are a one-line re-add each once a host exists.
    /// ponytail: needs MainWindow, wired when the profile partials move to Core.
    ///
    /// KEPT: the PrivacyPanel instance. It is created here and lives for the life of the tab
    /// exactly as on WPF — the Privacy dialog borrows the panel while it is on screen and hands it
    /// back, so the checkbox state survives the dialog being closed.
    ///
    /// The XP and unlock meters are two star-width columns, as on WPF; a ColumnDefinition cannot
    /// carry an x:Name in Avalonia (AVLN2000: not a StyledElement), so the grid is named instead
    /// and <see cref="SetMeter"/> writes <c>ColumnDefinitions[0]/[1]</c> — the same two GridLengths
    /// MainWindow.ProfileCard.cs assigns today.
    /// </summary>
    public partial class DiscordTabView : UserControl
    {
        /// <summary>
        /// The sharing controls that used to occupy this tab's right-hand column. They now live in
        /// the Privacy &amp; Sharing dialog, but the instance is created here and kept for the life
        /// of the tab: MainWindow writes into these checkboxes from ~25 places (login state changes,
        /// settings loads, cross-tab toggle mirroring) and must never depend on a dialog being open.
        /// </summary>
        internal ProfilePrivacyPanel PrivacyPanel { get; }

        public DiscordTabView()
        {
            AvaloniaXamlLoader.Load(this);
            PrivacyPanel = new ProfilePrivacyPanel();
            LoadPlaceholderProfile();
        }

        // ------------------------------------------------------------------
        // Placeholder card
        // ------------------------------------------------------------------

        /// <summary>
        /// Puts the hero card, the Record and the Showcase on screen with sample numbers.
        ///
        /// The WPF tab boots with ProfileCardWrapper collapsed and only the "search for a user"
        /// plate up; MainWindow.ProfileCard.cs fills and reveals it once the account service
        /// answers. There is no account service on this head, so the whole Trainer Card — the
        /// thing this view IS — would never draw and the render proof would cover a heading and an
        /// empty plate. The state below is the one the real app shows a signed-in user with four
        /// pins, so every template on the card is exercised.
        ///
        /// The vat bay, the faucet and the descent receipt stay dark, deliberately: they are
        /// server-gated on WPF too (the `descent` block is withheld outside the rollout dial), and
        /// their whole safety property is that a dark vat measures to exactly the 104px avatar.
        /// ponytail: replace with the real ledger when MainWindow.ProfileCard moves to Core.
        /// </summary>
        private void LoadPlaceholderProfile()
        {
            Find<Grid>("ProfileCardWrapper").IsVisible = true;
            Find<Border>("NoProfileSelected").IsVisible = false;

            // Identity. TxtProfileViewerName is left on its {loc:Str login_display_name} default:
            // assigning .Text over a loc binding is undone by the next language change
            // (CLAUDE.md, "setting text from code").
            Find<Button>("BtnChangeDisplayName").IsVisible = true;
            Find<Button>("BtnDeleteProfile").IsVisible = true;
            Find<Button>("BtnProfileDiscord").IsVisible = true;
            Find<Border>("OgBannerBadge").IsVisible = true;
            Find<Border>("StaffBadge").IsVisible = true;
            Find<Border>("WhitelistBadge").IsVisible = true;
            Find<Border>("ProfileSpiralPlate").IsVisible = true;

            // Plates + XP bar.
            Find<TextBlock>("TxtProfileViewerLevel").Text = "27";
            Find<TextBlock>("TxtProfileViewerRank").Text = "#12";
            Find<TextBlock>("TxtProfileXpProgress").Text = "3,420 / 5,000 XP";
            SetMeter("ProfileXpBar", 0.684);

            // The Record.
            Find<TextBlock>("TxtProfileViewerXp").Text = "48,310";
            Find<TextBlock>("TxtProfileViewerBubbles").Text = "1,208";
            Find<TextBlock>("TxtProfileViewerGifs").Text = "96";
            Find<TextBlock>("TxtProfileViewerLockCards").Text = "31";
            Find<TextBlock>("TxtProfileViewerAchievements").Text = "18";
            // TxtProfileViewerVideos keeps its {loc:Str label_0h} default, same reason as the name.

            // The Showcase. Four pins, so the placeholder plates step aside exactly as
            // MainWindow.ProfileCard.cs makes them.
            Find<ItemsControl>("ProfilePinnedShowcase").ItemsSource = SampleTiles(4);
            Find<StackPanel>("ProfilePinnedPlaceholders").IsVisible = false;
            Find<ItemsControl>("ProfileAchievementGrid").ItemsSource = SampleTiles(18);
            Find<Expander>("ProfileAllAchievementsExpander").IsExpanded = true;

            // Keys and argument order copied from MainWindow.ProfileCard.RefreshProfileAchievements.
            Find<TextBlock>("TxtProfileAllAchievementsHeader").Text = Loc.GetF("profile_showcase_all_count", 18);
            Find<TextBlock>("TxtProfileUnlockSummary").Text = Loc.GetF("profile_showcase_progress", 18, 40, 45);
            Find<TextBlock>("TxtProfileNextUp").Text = Loc.GetF("profile_showcase_next_up", "Spiral Eyes");
            SetMeter("ProfileUnlockBar", 0.45);
        }

        /// <summary>Sample tiles. The art is pack:// in the WPF head, so Image is null and each
        /// tile draws as its plate — the frame, the star and the tooltip are what these prove.</summary>
        private static List<ProfileAchievementTile> SampleTiles(int count)
        {
            var list = new List<ProfileAchievementTile>(count);
            for (var i = 0; i < count; i++)
                list.Add(new ProfileAchievementTile($"sample_{i}", $"Sample achievement {i + 1}"));
            return list;
        }

        /// <summary>Writes a two-column meter's fill as WPF's ProfileXpFillCol/RestCol pair did.</summary>
        private void SetMeter(string gridName, double fraction)
        {
            var grid = Find<Grid>(gridName);
            grid.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(1 - fraction, GridUnitType.Star);
        }

        private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;

        // ------------------------------------------------------------------
        // Handlers — every one forwards to MainWindow on WPF and is inert here.
        // ponytail: needs MainWindow (profile partials), wired when they move to Core.
        // ------------------------------------------------------------------

        private void BtnChangeDisplayName_Click(object? sender, RoutedEventArgs e) { }
        private void BtnClearProfile_Click(object? sender, RoutedEventArgs e) { }
        private void BtnDeleteProfile_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProfileDiscord_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProfileSearch_Click(object? sender, RoutedEventArgs e) { }
        private void BtnViewMyProfile_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>The link-notice button reuses the Privacy panel's login/link flow verbatim on
        /// WPF — that handler drives BtnDiscordTabLogin on the long-lived panel instance.</summary>
        private void BtnDiscordTabLogin_Click(object? sender, RoutedEventArgs e) { }

        private void BtnProfilePrivacy_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProfileCustomize_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>The hero's Share Profile CTA. Same door as the header account menu's
        /// "Public profile" row — MainWindow owns the URL and the launcher.</summary>
        private void BtnProfileShare_Click(object? sender, RoutedEventArgs e) { }

        private void TxtProfileSearch_KeyDown(object? sender, KeyEventArgs e) { }

        /// <summary>The Trainer Card's spiral plate. Opens the expanded map window — the same door
        /// the nav rail's miniature uses (MainWindow.ProfileSpiral.cs).</summary>
        private void ProfileSpiralPlate_Click(object? sender, PointerReleasedEventArgs e) { }

        /// <summary>Left-click on a badge pins or unpins it (own card only). The tile is reached
        /// through the sender's DataContext exactly as the WPF handler reads it.</summary>
        private void ProfileAchievementTile_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Control { DataContext: ProfileAchievementTile }) { /* mw.ToggleOwnAchievementPin(tile.Id) */ }
        }
    }

    /// <summary>
    /// The Showcase's item model, ported from <c>MainWindow.ProfileCard.cs:ProfileAchievementTile</c>
    /// (internal to the WPF head, so it cannot be referenced from here). Same three members, so the
    /// two DataTemplates bind by the same names.
    /// </summary>
    public sealed class ProfileAchievementTile
    {
        public ProfileAchievementTile(string id, string name, IImage? image = null)
        {
            Id = id;
            Name = name;
            Image = image;
        }

        public string Id { get; }
        public string Name { get; }
        public IImage? Image { get; }
    }
}
