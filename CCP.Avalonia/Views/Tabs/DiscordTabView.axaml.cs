using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/DiscordTabView.xaml.cs — the Profile tab's
    /// "Trainer Card".
    ///
    /// The tab owns no logic of its own on WPF either — each handler is a one-line forward to
    /// <c>Window.GetWindow(this) is MainWindow mw</c>. The Avalonia twin of that lookup is
    /// <see cref="Host"/>, and the host is <see cref="MainShellWindow"/>, so a handler whose WPF
    /// target has been restored on the shell is now a forward again rather than a no-op: Privacy
    /// and Clear. The card itself (own card, search, lookup) lives on this tab, not the shell, so it
    /// reaches its controls through the generated fields. The vat, faucet, wardrobe and spiral map
    /// handlers are still empty - each says which symbol it is waiting on.
    ///
    /// THE CTOR CALLS <c>InitializeComponent()</c>, NOT <c>AvaloniaXamlLoader.Load(this)</c>, and
    /// that is not cosmetic: the loader never assigns the generated <c>x:Name</c> fields, so under
    /// it all 85 of this view's named controls were permanently null and any <c>SomeControl?.X</c>
    /// on this type was a silent no-op. The generated method assigns them. <see cref="Find{T}"/>
    /// works either way and is kept — MainShellWindow.ProfileCard.cs reaches in with the same
    /// FindControl call and must keep working whatever this ctor does.
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

        /// <summary>The shell that hosts this tab, or null when it is rendered on its own (the
        /// --render-view harness). The Avalonia twin of WPF's <c>Window.GetWindow(this)</c>.</summary>
        private Windows.MainShellWindow? Host => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;

        public DiscordTabView()
        {
            InitializeComponent();
            PrivacyPanel = new ProfilePrivacyPanel();
        }

        // ------------------------------------------------------------------
        // The card (read-only): WPF MainWindow.Browser.cs BtnViewMyProfile_Click, SearchAndDisplayProfile,
        // DisplayOwnProfile, DisplayProfileEntry and RefreshProfileViewerAsync, on the tab itself.
        // The picture: yours from Discord when ShareProfilePicture is on, a looked-up player's from the
        // server's avatar_url (Helpers/AvatarPhotos). ponytail: no Patreon
        // badge/banner art, no edit-name/delete/Discord-DM buttons (writes, unit 7), no cosmetics, no staff
        // flag for your own card (no Discord service): each stays hidden rather than drawn wrong.
        // ------------------------------------------------------------------

        private LeaderboardPage<LeaderboardRow>? _board;
        private Task<LeaderboardPage<LeaderboardRow>?>? _boardFetch;
        private DateTime _boardAt;
        private int _cardRequest;   // newest request wins; an older one finishing late draws nothing
        private bool _meFirstDone;

        /// <summary>WPF MainWindow.ProfileCard.cs:61: the first show opens on your own card, never over one already up.</summary>
        internal void EnsureProfileMeFirst()
        {
            if (_meFirstDone) return;
            _meFirstDone = true;
            if (!ProfileCardWrapper.IsVisible) _ = ViewMyProfileAsync();
        }

        /// <summary>WPF BtnViewMyProfile_Click: your board row when you are on it, else the local card.</summary>
        internal async Task ViewMyProfileAsync()
        {
            var req = ++_cardRequest;
            var name = CoreSettings.Current.UserDisplayName;
            var entry = string.IsNullOrEmpty(name) ? null : await FindOnBoardAsync(name);
            if (req != _cardRequest) return;
            if (entry != null) DisplayProfileEntry(entry); else DisplayOwnProfile();
        }

        /// <summary>WPF MainWindow.Leaderboard.cs:930 (row double-click): the name goes into the search box, then the search.</summary>
        internal Task OpenProfileAsync(string name)
        {
            TxtProfileSearch.Text = name;
            return SearchAsync(name);
        }

        /// <summary>WPF SearchAndDisplayProfile: found shows the card, not found puts the "search for a user" plate up.</summary>
        internal async Task SearchAsync(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var req = ++_cardRequest;
            var entry = await FindOnBoardAsync(name);
            if (req != _cardRequest) return;
            if (entry != null) { DisplayProfileEntry(entry); return; }
            // The board is the month's top 200 (all WPF searches). Anyone else, a friend included, is asked
            // for by name: /user/lookup answers an exact display name and honours their sharing choices.
            if (!CoreSettings.Current.OfflineMode && await LookupAsRowAsync(name.Trim()) is { } found)
            {
                if (req != _cardRequest) return;
                DisplayProfileEntry(found);
                return;
            }
            if (req != _cardRequest) return;
            NoProfileSelected.IsVisible = true;
            ProfileCardWrapper.IsVisible = false;
        }

        /// <summary>Test seam: the by-name lookup behind a search that missed the board.</summary>
        internal static Func<string, Task<UserLookupResult?>> LookupByName { get; set; } =
            name => LeaderboardTabView.NewClient().LookupUserAsync(name);

        private static async Task<LeaderboardRow?> LookupAsRowAsync(string name)
        {
            try
            {
                var u = await LookupByName(name);
                if (u == null || string.IsNullOrWhiteSpace(u.DisplayName)) return null;
                // Only the player asked for: a reply naming someone else is not a match.
                if (!string.Equals(u.DisplayName!.Trim(), name, StringComparison.OrdinalIgnoreCase)) return null;
                return new LeaderboardRow
                {
                    DisplayName = u.DisplayName!, Level = u.Level, Xp = u.Xp, BubblesPopped = u.BubblesPopped, GifsSpawned = u.GifsSpawned,
                    VideoMinutes = u.VideoMinutes, LockCardsCompleted = u.LockCardsCompleted, AchievementsCount = u.AchievementsCount,
                    IsOnline = u.IsOnline, IsPatreon = u.IsPatreon, PatreonTier = u.PatreonTier, DiscordId = u.DiscordId,
                };
            }
            catch (Exception ex) { Log.Debug("profile lookup by name failed: {E}", ex.Message); return null; }
        }

        /// <summary>WPF searches LeaderboardService's cached board. This tab keeps its own monthly board for 60 s (one
        /// GET shared by every search in that window, including an in-flight one); offline fetches nothing and searches the
        /// last board it saw, else the Leaderboard tab's last ranked page, as WPF searches its shared cache.</summary>
        private async Task<LeaderboardRow?> FindOnBoardAsync(string name)
        {
            if (!CoreSettings.Current.OfflineMode)
            {
                if (_boardFetch == null || DateTime.UtcNow - _boardAt > TimeSpan.FromSeconds(60))
                {
                    _boardAt = DateTime.UtcNow;
                    _boardFetch = FetchBoardAsync();
                }
                var fetch = _boardFetch;
                var page = await fetch;
                if (page != null) _board = page;
                else if (_boardFetch == fetch) _boardFetch = null; // a failed fetch is not cached
            }
            return TrainerCardText.Find(Board?.Entries, name);
        }

        private LeaderboardPage<LeaderboardRow>? Board =>
            _board ?? Host?.Named<LeaderboardTabView>("LeaderboardTab")?.RankedPage;

        private static async Task<LeaderboardPage<LeaderboardRow>?> FetchBoardAsync()
        {
            try
            {
                var (page, _) = await LeaderboardTabView.NewClient().FetchAsync<LeaderboardRow>("monthly", CoreAccount.UnifiedUserId, DateTime.UtcNow);
                if (page?.Entries == null) return null;
                page.Entries = LeaderboardClient.Rank(page.Entries, false);
                return page;
            }
            catch (Exception ex) { Log.Warning(ex, "Trainer Card board fetch failed"); return null; }
        }

        /// <summary>WPF DisplayOwnProfile: local settings and achievement progress, rank from the last board.</summary>
        internal void DisplayOwnProfile()
        {
            var s = CoreSettings.Current;
            var progress = App.Achievements?.Progress;
            ShowCard(s.IsSeason0Og);
            ApplyOwnIdentityBadges();
            TxtProfileViewerName.Text = OwnCardName();
            ShowOwnActions(true);
            ShowOwnDiscordDm();
            RefreshProfileChrome();
            SetOnline(true, Loc.Get("label_online"));
            TxtProfileViewerLevel.Text = s.PlayerLevel.ToString();
            // WPF Browser.cs:1963-1981: the server rank, else your row by unified id, else by display name.
            var board = Board;
            var rows = board?.Entries;
            TxtProfileViewerRank.Text = TrainerCardText.Rank(board?.YourRank is > 0 ? board.YourRank
                : (rows?.FirstOrDefault(e => e.IsCurrentUser)
                   ?? (string.IsNullOrEmpty(s.UserDisplayName) ? null
                       : rows?.FirstOrDefault(e => string.Equals(e.DisplayName, s.UserDisplayName, StringComparison.OrdinalIgnoreCase))))?.Rank);
            TxtProfileViewerXp.Text = TrainerCardText.Number(XpCurve.GetTotalXP(s.PlayerLevel, s.PlayerXP, s.DescentEpoch));
            TxtProfileViewerBubbles.Text = TrainerCardText.Number(progress?.TotalBubblesPopped ?? 0);
            TxtProfileViewerVideos.Text = TrainerCardText.Video(progress?.TotalVideoMinutes ?? 0);
            TxtProfileViewerGifs.Text = TrainerCardText.Number(progress?.TotalFlashImages ?? 0);
            TxtProfileViewerLockCards.Text = TrainerCardText.Number(progress?.TotalLockCardsCompleted ?? 0);
            var unlocked = App.Achievements?.GetUnlockedCount(exclusive: false) ?? 0;
            var total = FreeTotal();
            TxtProfileViewerAchievements.Text = $"{unlocked} / {total}";
            Host?.SetProfileViewingSelf(true);
            Host?.ApplyOwnProfileWardrobe();
            ApplyOwnPatreonPlates();
            ShowProfilePhoto(TxtProfileViewerName.Text ?? "", Helpers.AvatarPhotos.OwnUrl(256));   // WPF Browser.cs:1909
            SetXpMeter(s.PlayerLevel, s.PlayerXP);
            Host?.UpdateProfileShowcase(unlocked, total, progress?.UnlockedAchievements);
            ShowAchievements(progress?.UnlockedAchievements, Loc.Get("label_no_achievements_yet"));
        }

        /// <summary>WPF DisplayProfileEntry: the board row now, then the fresh lookup (online, badges, achievements).</summary>
        internal void DisplayProfileEntry(LeaderboardRow entry)
        {
            var s = CoreSettings.Current;
            ShowCard(entry.IsSeason0Og);
            var isOwn = string.Equals(entry.DisplayName, s.UserDisplayName, StringComparison.OrdinalIgnoreCase);
            if (isOwn) ApplyOwnIdentityBadges();
            else ApplyIdentityBadges(false, null, false);
            // WPF Browser.cs:2247: your own row reads the local tier, anyone else's the board row.
            if (isOwn) ApplyOwnPatreonPlates();
            else ApplyPatreonPlates(entry.PatreonTier, entry.IsPatreon && entry.PatreonTier >= 1);
            TxtProfileViewerName.Text = entry.DisplayName;
            ShowOwnActions(isOwn);
            ShowDiscordDm(entry.HasDiscord ? entry.DiscordId : null, entry.DisplayName);
            RefreshProfileChrome();
            SetOnline(entry.IsOnline, entry.IsOnline ? "Online" : "Offline"); // WPF's literals
            TxtProfileViewerLevel.Text = entry.Level.ToString();
            TxtProfileViewerRank.Text = TrainerCardText.Rank(entry.Rank);
            TxtProfileViewerXp.Text = entry.XpDisplay;
            TxtProfileViewerBubbles.Text = entry.BubblesPoppedDisplay;
            TxtProfileViewerVideos.Text = TrainerCardText.Video(entry.VideoMinutes);
            TxtProfileViewerGifs.Text = entry.GifsSpawnedDisplay;
            TxtProfileViewerLockCards.Text = entry.LockCardsCompleted.ToString();
            TxtProfileViewerAchievements.Text = entry.AchievementsDisplay;
            ShowAchievements(null, $"{entry.AchievementsCount} achievements unlocked");
            // entry.Xp is lifetime; the meter wants progress inside the level.
            Host?.SetProfileViewingSelf(isOwn);
            ShowProfilePhoto(entry.DisplayName, isOwn ? Helpers.AvatarPhotos.OwnUrl(256) : null);
            // WPF Browser.cs:2290: the board row carries no cosmetics - yours from settings, theirs stripped until the lookup.
            if (isOwn) Host?.ApplyOwnProfileWardrobe();
            else Host?.ApplyViewedProfileWardrobe(null);
            SetXpMeter(entry.Level, XpCurve.GetCurrentLevelXP(entry.Level, entry.Xp, s.DescentEpoch));
            Host?.UpdateProfileShowcase(entry.AchievementsCount, FreeTotal(),
                isOwn ? App.Achievements?.Progress?.UnlockedAchievements : null);
            if (entry.DisplayName.Length > 0 && !s.OfflineMode) _ = RefreshProfileViewerAsync(entry.DisplayName);
        }

        /// <summary>WPF RefreshProfileViewerAsync: GET /user/lookup, drawn only while the same user is still on screen.</summary>
        private async Task RefreshProfileViewerAsync(string name)
        {
            var lookup = await LeaderboardTabView.NewClient().LookupUserAsync(name);
            if (lookup == null || TxtProfileViewerName.Text != name) return;
            SetOnline(lookup.IsOnline, lookup.IsOnline ? "Online" : "Offline");
            if (string.Equals(name, CoreSettings.Current.UserDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                ApplyOwnIdentityBadges();
                Host?.ApplyOwnProfileWardrobe();
            }
            else
            {
                ApplyIdentityBadges(lookup.IsStaff, lookup.StaffRole, lookup.IsWhitelisted);
                Host?.ApplyViewedProfileWardrobe(lookup.Cosmetics);   // WPF Browser.cs:2437
            }
            // WPF Browser.cs:2437: the server's picture; your own card falls back to your Discord one.
            var photo = lookup.AvatarUrl;
            if (string.IsNullOrEmpty(photo) && string.Equals(name, CoreSettings.Current.UserDisplayName, StringComparison.OrdinalIgnoreCase))
                photo = Helpers.AvatarPhotos.OwnUrl(256);
            ShowProfilePhoto(name, photo);
            if (lookup.Achievements is { Count: > 0 }) ShowAchievements(lookup.Achievements, "");
            else if (lookup.AchievementsCount > 0) ShowAchievements(null, $"{lookup.AchievementsCount} achievements unlocked");
        }

        /// <summary>The hero disc's picture (WPF ProfileViewerAvatar.ImageSource): cleared at once, then
        /// filled when the load lands, if the same player is still on the card.</summary>
        private void ShowProfilePhoto(string name, string? url)
        {
            ProfileHeroAvatar.AvatarImage = null;
            // The preset bust shares this slot: it may take it only once the load has come back empty.
            var none = string.IsNullOrEmpty(url);
            Host?.SetProfilePictureLoad(none ? Views.Windows.ProfilePictureLoad.None : Views.Windows.ProfilePictureLoad.Pending);
            if (none) return;
            _ = PaintProfilePhotoAsync(name, url!);
        }

        private async Task PaintProfilePhotoAsync(string name, string url)
        {
            var bmp = await Helpers.AvatarPhotos.LoadAsync(url, 256);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (TxtProfileViewerName.Text != name) return;
                if (bmp != null) ProfileHeroAvatar.AvatarImage = bmp;
                Host?.SetProfilePictureLoad(bmp != null ? Views.Windows.ProfilePictureLoad.Loaded : Views.Windows.ProfilePictureLoad.None);
            });
        }

        private void ShowCard(bool og)
        {
            RefreshProfileStatBadges();
            ProfileCardWrapper.IsVisible = true;
            NoProfileSelected.IsVisible = false;
            OgBorderContainer.IsVisible = og;
            OgBannerBadge.IsVisible = og;
        }

        private void SetOnline(bool online, string text)
        {
            var brush = SolidColorBrush.Parse(online ? "#43B581" : "#747F8D");
            TxtProfileViewerOnline.Text = text;
            TxtProfileViewerOnline.Foreground = brush;
            ProfileHeroAvatar.PresenceDot.Fill = brush;
        }

        /// <summary>WPF Browser.cs:1951: the unified name, then the Discord custom name, then the Patreon name, then "You".</summary>
        internal static string OwnCardName() => CoreSettings.Current.UserDisplayName
            ?? Platform.AccountSeed.Discord?.CustomDisplayName ?? Platform.AccountSeed.Patreon?.DisplayName ?? "You";

        /// <summary>WPF Browser.cs:1899 / :2254: your own card wears your Discord staff role and the whitelist plate.</summary>
        private void ApplyOwnIdentityBadges()
        {
            var discord = Platform.AccountSeed.Discord;
            ApplyIdentityBadges(discord?.IsStaff == true, discord?.StaffRole, CoreAccount.IsWhitelisted);
        }

        /// <summary>WPF Browser.cs:2060: the settings tier (a Discord sign-in with a linked Patreon has one too);
        /// a whitelisted account with tier 0 still gets the tier plate and the banner.</summary>
        private void ApplyOwnPatreonPlates()
        {
            var tier = CoreSettings.Current.PatreonTier;
            ApplyPatreonPlates(tier, tier >= 1 || CoreAccount.IsWhitelisted);
        }

        /// <summary>WPF Browser.cs:2063-2115 and :2264-2315: the tier badge by the level plates, the tier plate by the
        /// name, and the tier art (Prime subject at tier 3, Pink filter below). A picture that will not load hides its plate.</summary>
        internal void ApplyPatreonPlates(int tier, bool hasPatreon)
        {
            var badge = hasPatreon && tier > 0 ? PatreonBadgeArt(tier) : null;
            ProfilePatreonBadge.Source = badge;
            ProfilePatreonBadge.IsVisible = badge != null;

            var plate = hasPatreon ? PatreonBadgeArt(tier > 0 ? tier : 1) : null;
            ProfilePatreonTierBadge.Source = plate;
            ProfilePatreonTierBadge.IsVisible = plate != null;

            // SEAM(csproj): "Pink filter.webp" and "prime subject.webp" sit in /Assets but only /Assets/*.png is
            // compiled in, so this art is null (plate hidden) until the project links /Assets/*.webp under Resources.
            var art = hasPatreon ? PatreonArt(tier >= 3 ? "prime subject.webp" : "Pink filter.webp") : null;
            ImgPatreonTierBanner.Source = art;
            ProfilePatreonTierBanner.IsVisible = art != null;
        }

        /// <summary>WPF LoadPatreonBadgeImage: tiers 1 to 3, anything else draws tier 1.</summary>
        internal static string PatreonBadgeFile(int tier) => tier is 2 or 3 ? $"Patreon tier{tier}.png" : "Patreon tier1.png";

        private static global::Avalonia.Media.Imaging.Bitmap? PatreonBadgeArt(int tier) => PatreonArt(PatreonBadgeFile(tier));

        /// <summary>Tier livery is commerce chrome a mod must not restyle (as Controls/TierBadge), so it reads the
        /// shipped copy and never a mod override. Decoded once per file.</summary>
        private static global::Avalonia.Media.Imaging.Bitmap? PatreonArt(string file)
        {
            if (PatreonArtCache.TryGetValue(file, out var cached)) return cached;
            global::Avalonia.Media.Imaging.Bitmap? bmp = null;
            try
            {
                var uri = new Uri($"avares://CCP.Avalonia/Resources/{Uri.EscapeDataString(file)}");
                if (global::Avalonia.Platform.AssetLoader.Exists(uri))
                {
                    using var stream = global::Avalonia.Platform.AssetLoader.Open(uri);
                    bmp = new global::Avalonia.Media.Imaging.Bitmap(stream);
                }
            }
            catch (Exception ex) { Log.Warning("Patreon art {File} would not load: {E}", file, ex.Message); }
            return PatreonArtCache[file] = bmp;
        }

        private static readonly Dictionary<string, global::Avalonia.Media.Imaging.Bitmap?> PatreonArtCache = new();

        /// <summary>WPF ApplyProfileIdentityBadges: the staff pill's border encodes the role.</summary>
        private void ApplyIdentityBadges(bool isStaff, string? staffRole, bool isWhitelisted)
        {
            StaffBadge.IsVisible = isStaff || !string.IsNullOrEmpty(staffRole);
            if (StaffBadge.IsVisible)
            {
                var (from, to, text) = (staffRole ?? "admin") switch
                {
                    "owner" => ("#8A2BE2", "#C77DFF", "#D9B8FF"),
                    "support" => ("#2F86FF", "#6FC3FF", "#B8D9FF"),
                    _ => ("#DC143C", "#FF6B85", "#FFB3C2"),
                };
                StaffBadge.BorderBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
                };
                StaffBadgeLabel.Foreground = SolidColorBrush.Parse(text);
            }
            WhitelistBadge.IsVisible = isWhitelisted;
        }

        /// <summary>WPF RefreshProfileStatBadges: The Record's six plates each wear an achievement badge
        /// (mod override first). Painted with every card, so a mod switch is picked up on the next one.</summary>
        internal void RefreshProfileStatBadges()
        {
            foreach (var (file, name) in new[]
                     {
                         ("lv_10.png", "ImgStatXp"), ("pop_the_Thought.png", "ImgStatBubbles"), ("10_hours_pink.png", "ImgStatVideos"),
                         ("retinal_burn.png", "ImgStatGifs"), ("total_lockdown.png", "ImgStatLockCards"), ("spiral_eyes.png", "ImgStatAchievements"),
                     })
                if (this.FindControl<Image>(name) is { } image && Helpers.ModArt.TryLoad($"achievements/{file}", 68) is { } art)
                    image.Source = art;
        }

        /// <summary>WPF LoadProfileAchievementImages: each badge with its art (mod override first); a missing
        /// picture leaves the bare plate.</summary>
        private void ShowAchievements(IEnumerable<string>? ids, string emptyText)
        {
            var tiles = ids?.Select(id => Achievement.All.Values.FirstOrDefault(a => a.Id == id)).OfType<Achievement>()
                .Select(a => new ProfileAchievementTile(a.Id, CoreMods.MakeModAware(a.Name),
                    Helpers.ModArt.TryLoad($"achievements/{a.ImageName}", 116))).ToList();
            var any = tiles is { Count: > 0 };
            ProfileAchievementGrid.ItemsSource = any ? tiles : null;
            TxtNoAchievements.Text = emptyText;
            TxtNoAchievements.IsVisible = !any;
        }

        /// <summary>Free achievements only, so the patron set never folds into the count (WPF DisplayOwnProfile).</summary>
        private static int FreeTotal() => App.Achievements?.GetTotalCount(exclusive: false)
            ?? Achievement.All.Values.Count(a => !a.IsExclusive && !a.IsHidden && !(a.IsPremiumFeature && !CoreEntitlement.HasPremium));

        /// <summary>WPF MainWindow.ProfileCard.cs UpdateProfileXpMeter, minus the descent-bonus suffix (receipt not ported).</summary>
        private void SetXpMeter(int level, double levelXp)
        {
            var needed = XpCurve.GetXPForLevel(Math.Max(1, level), CoreSettings.Current.DescentEpoch);
            var have = Math.Max(0, levelXp);
            if (needed > 0 && have > needed) have = needed;
            var fraction = needed > 0 ? have / needed : 0;
            if (double.IsNaN(fraction) || double.IsInfinity(fraction)) fraction = 0;
            SetMeter("ProfileXpBar", Math.Clamp(fraction, 0, 1));
            // WPF UpdateProfileXpMeter: the migration bonus rides the readout, own card only (same kind as the chip).
            var kind = Host?.OwnDescentReceiptKind() ?? ConditioningControlPanel.Services.Descent.DescentReceiptKind.None;
            var bonus = ConditioningControlPanel.Services.Descent.DescentCycleXp.XpBonusFor(CoreSettings.Current);
            TxtProfileXpProgress.Text = ConditioningControlPanel.Services.Descent.DescentReceipt.ShowsXpMultiplier(kind, bonus)
                ? Loc.GetF("profile_xp_progress_boosted", $"{have:N0}", $"{needed:N0}", ConditioningControlPanel.Services.Descent.DescentReceipt.BonusPercentText(bonus))
                : Loc.GetF("profile_xp_progress", $"{have:N0}", $"{needed:N0}");
        }

        private void SetMeter(string gridName, double fraction)
        {
            var grid = Find<Grid>(gridName);
            grid.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(1 - fraction, GridUnitType.Star);
        }

        private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;

        // ------------------------------------------------------------------
        // Handlers — every one forwards to MainWindow on WPF, and to Host here.
        // ------------------------------------------------------------------

        private void BtnChangeDisplayName_Click(object? sender, RoutedEventArgs e) => _ = Host?.ChangeDisplayNameAsync();

        /// <summary>WPF forwards to MainWindow.Browser.cs:ClearProfileViewer; a search still in flight is dropped.</summary>
        private void BtnClearProfile_Click(object? sender, RoutedEventArgs e)
        {
            _cardRequest++;
            Host?.BtnClearProfile_Click(sender, e);
        }

        private void BtnDeleteProfile_Click(object? sender, RoutedEventArgs e) => _ = Host?.DeleteProfileAsync();

        /// <summary>WPF Browser.cs:1934-1942 / 2215-2223: rename and delete show on your own card only, and only
        /// with a unified id to act on.</summary>
        private void ShowOwnActions(bool own)
        {
            var show = own && !string.IsNullOrEmpty(CoreSettings.Current.UnifiedId);
            BtnChangeDisplayName.IsVisible = show;
            BtnDeleteProfile.IsVisible = show;
        }
        private void BtnProfileDiscord_Click(object? sender, RoutedEventArgs e) => OpenDiscordDm(sender);

        private void BtnProfileSearch_Click(object? sender, RoutedEventArgs e) => _ = SearchAsync(TxtProfileSearch.Text);
        private void BtnViewMyProfile_Click(object? sender, RoutedEventArgs e) => _ = ViewMyProfileAsync();

        /// <summary>The link-notice button reuses the Privacy panel's login/link flow verbatim on
        /// WPF — that handler drives BtnDiscordTabLogin on the long-lived panel instance.</summary>
        private void BtnDiscordTabLogin_Click(object? sender, RoutedEventArgs e) => _ = DiscordTabLoginAsync(sender);

        /// <summary>Opens the relocated sharing controls, exactly as the WPF handler does
        /// (<c>mw.OpenProfilePrivacyDialog()</c>). The dialog borrows <see cref="PrivacyPanel"/>,
        /// so the toggles inside it are the very controls the shell keeps writing to.</summary>
        private void BtnProfilePrivacy_Click(object? sender, RoutedEventArgs e) =>
            Host?.OpenProfilePrivacyDialog();

        /// <summary>WPF: <c>mw.OpenProfileCustomizeDialog()</c> (MainShellWindow.ProfileWardrobe.cs).</summary>
        private void BtnProfileCustomize_Click(object? sender, RoutedEventArgs e) =>
            Host?.OpenProfileCustomizeDialog();

        /// <summary>The hero's Share Profile CTA. Same door as the header account menu's
        /// "Public profile" row — MainWindow owns the URL and the launcher.</summary>
        private void BtnProfileShare_Click(object? sender, RoutedEventArgs e) => OpenPublicProfilePage();

        private void TxtProfileSearch_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = SearchAsync(TxtProfileSearch.Text);
        }

        /// <summary>The Trainer Card's spiral plate. Opens the expanded map window — the same door
        /// the nav rail's miniature uses (MainWindow.ProfileSpiral.cs).</summary>
        private void ProfileSpiralPlate_Click(object? sender, PointerReleasedEventArgs e) => Host?.ShowTab(global::ConditioningControlPanel.Services.Descent.SpiralRoom.TabKey);

        /// <summary>Left-click on a badge pins or unpins it (own card only). The tile is reached
        /// through the sender's DataContext exactly as the WPF handler reads it.</summary>
        private void ProfileAchievementTile_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Control { DataContext: ProfileAchievementTile tile }) Host?.ToggleOwnAchievementPin(tile.Id);
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
