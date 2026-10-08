using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/LeaderboardTabView.xaml.cs.
    ///
    /// What survived unchanged: the Level-column relabel for the All-Time board. 7.1.5 retired the
    /// season chrome (no season name, no countdown, no recap button) and opens on All-Time; see
    /// <see cref="SettleDefaultMode"/> and <see cref="HeaderText"/>.
    ///
    /// What is restored: everything the toolbar does to rows already in hand.
    /// <see cref="RebuildLeaderboardView"/> is MainWindow.Leaderboard.cs's method of the same
    /// name - filter, search, sort, podium, tier bands, empty state - and the search box, the four
    /// filter chips, the six legend headers, the Monthly/All-Time pill and "jump to me" are wired
    /// to it. None of that needed a service on WPF either: it is view logic over
    /// <c>_leaderboardRanked</c>, which here is the fetched board (<see cref="RefreshLeaderboardAsync"/>, read-only).
    ///
    /// What is still stubbed: the Discord DM, the season recap and the row double-click. Each is named at its call site.
    ///
    /// Dropped: LstLeaderboard_PreviewMouseWheel and its ScrollViewer/row-pitch measuring. Its
    /// whole reason was that WPF's VirtualizingPanel.ScrollUnit=Pixel - forced on so "Jump to me"
    /// could centre a row - dropped the wheel step to 48dip. Avalonia's ListBox virtualizes in
    /// pixels natively and has no such regression, so there is nothing to undo.
    ///
    /// LeaderboardItemTemplateSelector has no port: Avalonia picks a DataTemplate by DataType, so
    /// the two typed templates in the .axaml do the selector's whole job.
    /// </summary>
    public partial class LeaderboardTabView : UserControl
    {
        private readonly TextBlock _txtSeason;
        private readonly TextBlock _txtSubtitle;
        private readonly TextBlock _hdrLevelSeasonal;
        private readonly TextBlock _hdrLevelPeak;
        private readonly ListBox _roster;
        private readonly ItemsControl _podium;
        private readonly TextBlock _empty;

        /// <summary>The board in canonical rank order. Rank is never re-assigned by an alternate
        /// sort - a row's Rank has to keep meaning "standing" or the bands and arrows start lying.</summary>
        private readonly List<LeaderboardRow> _ranked = new();

        /// <summary>Display sort: rank | name | level | xp | achievements | streak.</summary>
        private string _sortKey = "rank";

        /// <summary>Client-side roster filter: all | online | patrons | og.</summary>
        private string _filter = "all";

        /// <summary>Client-side roster search over display names.</summary>
        private string _searchText = "";

        /// <summary>The Tags the legend headers carry; the same six MainWindow sorts on.</summary>
        private static readonly HashSet<string> SortKeys =
            new() { "rank", "name", "level", "xp", "achievements", "streak" };

        public LeaderboardTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _txtSeason = this.FindControl<TextBlock>("TxtLeaderboardSeason")!;
            _txtSubtitle = this.FindControl<TextBlock>("TxtLeaderboardSubtitle")!;
            _hdrLevelSeasonal = this.FindControl<TextBlock>("HdrLevelSeasonal")!;
            _hdrLevelPeak = this.FindControl<TextBlock>("HdrLevelPeak")!;
            _roster = this.FindControl<ListBox>("LstLeaderboard")!;
            _podium = this.FindControl<ItemsControl>("PodiumHost")!;
            _empty = this.FindControl<TextBlock>("TxtLeaderboardEmpty")!;

            // Toolbar. On WPF these hang off MainWindow only because the markup did; every one of
            // them ends in RebuildLeaderboardView, which is right here.
            var search = this.FindControl<TextBox>("TxtLeaderboardSearch")!;
            // PropertyChanged, not TextChanged: TextChanged does not fire for a Text set from code
            // (proved by instrumenting this ctor), which would have made every future programmatic
            // "clear the search" a silent no-op. The property feed catches both.
            search.PropertyChanged += (_, ev) =>
            {
                if (ev.Property != TextBox.TextProperty) return;
                _searchText = search.Text ?? "";
                RebuildLeaderboardView();
            };

            foreach (var name in new[] { "ChipFilterAll", "ChipFilterOnline", "ChipFilterPatrons", "ChipFilterOg" })
                this.FindControl<RadioButton>(name)!.IsCheckedChanged += (s, _) =>
                {
                    if (s is RadioButton { IsChecked: true, Tag: string tag }) { _filter = tag; RebuildLeaderboardView(); }
                };

            // The legend headers carry a Tag and no x:Name, exactly as WPF's do, so they are
            // matched on the Tag rather than named one by one.
            foreach (var header in this.GetLogicalDescendants().OfType<Button>())
                if (header.Tag is string key && SortKeys.Contains(key))
                    header.Click += (_, _) => { _sortKey = key; RebuildLeaderboardView(); };

            this.FindControl<Button>("BtnLeaderboardMonthly")!.Click += (_, _) => SetLeaderboardMode(false);
            this.FindControl<Button>("BtnLeaderboardAllTime")!.Click += (_, _) => SetLeaderboardMode(true);
            this.FindControl<Button>("BtnJumpToMe")!.Click += BtnJumpToMe_Click;
            // WPF LeaderboardTabView.xaml.cs:235 forwards to the window, which owns the re-view.
            this.FindControl<Button>("BtnViewSeasonRecap")!.Click += (s, e) =>
                (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnViewSeasonRecap_Click(s, e);

            this.FindControl<Button>("BtnRefreshLeaderboard")!.Click += (_, _) => _ = RefreshLeaderboardAsync();
            // ponytail: the row double-click (profile lookup on the Discord tab) and the per-row Discord chip (a
            // browser hop) are still unhooked.

            // WPF derives this from the viewer's own skills: UpdateTrophyCaseColumns
            // (MainWindow.Leaderboard.cs:1432) sets it from App.SkillTree.HasSkill("trophy_case")
            // and re-runs the moment the skill is bought. Core has no skill seam to ask - it
            // carries AddXP and TrackBubbleCountResult and nothing else - so this head cannot
            // derive it. Forced ON rather than off on purpose: the Streak column and the
            // tooltip's Best Session line are then covered by the render proof instead of
            // sitting invisible, and neither is a gate on anything.
            // ponytail: needs ConditioningControlPanel/Services/Progression/SkillTreeService.cs
            // behind a Core seam.
            ShowTrophyStats = true;

            Loaded += OnLeaderboardTabLoaded;
            PropertyChanged += OnLeaderboardTabPropertyChanged;

            RebuildLeaderboardView();
        }

        /// <summary>
        /// Whether the viewer has the trophy_case skill. Gates the Streak column and the Best
        /// Session line in the row tooltip - the same gate the old GridView columns used, just
        /// expressed as a bindable property so the DataTemplates can read it through
        /// $parent[LeaderboardTabView]. Set by MainWindow.UpdateTrophyCaseColumns() on WPF.
        /// </summary>
        public static readonly StyledProperty<bool> ShowTrophyStatsProperty =
            AvaloniaProperty.Register<LeaderboardTabView, bool>(nameof(ShowTrophyStats));

        public bool ShowTrophyStats
        {
            get => GetValue(ShowTrophyStatsProperty);
            set => SetValue(ShowTrophyStatsProperty, value);
        }

        /// <summary>True while the All-Time board is showing.</summary>
        internal bool IsAllTimeMode { get; private set; }

        /// <summary>
        /// Switch boards and re-fetch (the two boards are different server slices), resetting the sort to rank so the
        /// podium and the bands line up with the ranks (MainWindow.Leaderboard.cs:778 BtnLeaderboardMode_Click).
        /// </summary>
        internal void SetLeaderboardMode(bool isAllTime)
        {
            _modeSettled = true;
            if (IsAllTimeMode == isAllTime) return;
            IsAllTimeMode = isAllTime;
            foreach (var row in _ranked) row.IsAllTimeView = isAllTime;
            _sortKey = "rank";
            UpdateModeButtons();
            RefreshSeasonHeader();
            ApplyModeLabels();
            RebuildLeaderboardView();
            _ = RefreshLeaderboardAsync();
        }

        /// <summary>
        /// Repaints the segmented Monthly/All-Time pill, as UpdateLeaderboardModeButtons does.
        /// Gold for the active All-Time half is a literal there too.
        ///
        /// ponytail: the mod accent IS on the seam - <c>CoreMods.AccentColorHex</c> plus
        /// <c>CoreMods.TryParseHexColor</c> is what WPF's <c>App.Mods.GetAccentColorHex</c>
        /// reduces to, and this method just has not been switched over. The theme resource used
        /// here is the same colour until a mod repaints it, so nothing reads wrong meanwhile; the
        /// change is two lines plus a <c>CoreMods.ModChanged</c> repaint.
        /// </summary>
        private void UpdateModeButtons()
        {
            var monthly = this.FindControl<Button>("BtnLeaderboardMonthly");
            var allTime = this.FindControl<Button>("BtnLeaderboardAllTime");
            if (monthly == null || allTime == null) return;

            var pink = Res("PinkBrush");
            var inactive = Res("AccentTintedBgBrush");
            var gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));

            Set(monthly, IsAllTimeMode ? inactive : pink, IsAllTimeMode ? pink : Brushes.White);
            Set(allTime, IsAllTimeMode ? gold : inactive, IsAllTimeMode ? Res("DarkerBgBrush") : pink);

            // A missing resource leaves the markup's own brush alone; blanking a button's
            // foreground would hide its label, which is worse than the wrong half looking active.
            static void Set(Button b, IBrush? bg, IBrush? fg)
            {
                if (bg != null) b.Background = bg;
                if (fg != null) b.Foreground = fg;
            }

            IBrush? Res(string key) => this.TryFindResource(key, out var v) ? v as IBrush : null;
        }

        /// <summary>
        /// Retitle the Level column for the active board.
        ///
        /// The All-Time board ranks by cumulative XP while the Level column shows
        /// HighestLevelEver, so a lower-ranked player can legitimately show a HIGHER level
        /// (rank 4 at 300, rank 7 at 309) and under a "Level" header that reads as a sorting
        /// bug. The number is genuinely interesting, so it is relabelled "Peak" instead of
        /// thrown away. Row tooltips and the podium pill follow through LeaderboardRow.LevelLabel.
        ///
        /// Two pre-localized TextBlocks rather than the WPF assignment to Content: Avalonia keeps
        /// a {loc:Str} binding alive under a local value, so setting the text from code here would
        /// be undone by the next language change (CLAUDE.md, "setting text from code").
        /// </summary>
        private void ApplyModeLabels()
        {
            _hdrLevelSeasonal.IsVisible = !IsAllTimeMode;
            _hdrLevelPeak.IsVisible = IsAllTimeMode;
        }

        // ------------------------------------------------------------------
        // Header title. WPF 7.1.5: seasons are retired (owner 2026-09-24): no season name, no
        // countdown, no recap button; the board just says which board it is. The All-Time board
        // is the default (LeaderboardDefaultMode), settled once, the first time the tab shows.
        // ------------------------------------------------------------------

        /// <summary>WPF MainWindow.Leaderboard.cs LeaderboardDefaultMode.</summary>
        internal const string LeaderboardDefaultMode = "all-time";

        private bool _modeSettled;

        private void OnLeaderboardTabLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            RefreshSeasonHeader();
            ApplyModeLabels();
        }

        /// <summary>WPF's IsVisibleChanged; Avalonia reports it through the property-changed feed.
        /// Raised by ShowTab before the shell asks for the first fetch, so the first board fetched
        /// is the default one (WPF settles it at the top of RefreshLeaderboardAsync).</summary>
        private void OnLeaderboardTabPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != IsVisibleProperty || !IsVisible) return;
            SettleDefaultMode();
            RefreshSeasonHeader();
        }

        /// <summary>Once: the board opens on <see cref="LeaderboardDefaultMode"/>, without a fetch of
        /// its own (the shell's show fetches). A player's own switch settles it too.</summary>
        internal void SettleDefaultMode()
        {
            if (_modeSettled) return;
            _modeSettled = true;
            var allTime = LeaderboardDefaultMode == "all-time";
            if (IsAllTimeMode == allTime) return;
            IsAllTimeMode = allTime;
            foreach (var row in _ranked) row.IsAllTimeView = allTime;
            _sortKey = "rank";
            UpdateModeButtons();
            ApplyModeLabels();
            RebuildLeaderboardView();
        }

        /// <summary>The two header lines for the board that is showing (WPF HeaderText).</summary>
        internal static (string Title, string Sub) HeaderText(bool isAllTime) => isAllTime
            ? (Loc.Get("lb_all_time_title"), Loc.Get("lb_all_time_sub"))
            : (Loc.Get("social_lb_month_title"), Loc.Get("social_lb_month_sub"));

        /// <summary>Repaints the header title for the active board. The name is kept for its
        /// callers; there is no season left in it.</summary>
        internal void RefreshSeasonHeader()
        {
            var (title, sub) = HeaderText(IsAllTimeMode);
            _txtSeason.Text = title;
            _txtSubtitle.Text = sub;
            _txtSubtitle.IsVisible = true;
        }

        // ------------------------------------------------------------------
        // The board (read-only)
        // ------------------------------------------------------------------

        /// <summary>Tests only: the client every refresh uses.</summary>
        internal static Func<LeaderboardClient> NewClient = () => new LeaderboardClient();

        private LeaderboardPage<LeaderboardRow>? _page;

        /// <summary>The last fetched board in rank order (the Trainer Card's offline search, as WPF's shared cache).</summary>
        internal LeaderboardPage<LeaderboardRow>? RankedPage => _page == null ? null
            : new() { Entries = _ranked.ToList(), YourRank = _page.YourRank, YourTotal = _page.YourTotal };

        /// <summary>
        /// MainWindow.Leaderboard.cs:953 RefreshLeaderboardAsync + RankLeaderboardEntries. Offline or a failed fetch leaves
        /// the board empty and says so in the status line, as WPF does. Never throws.
        /// ponytail: no profile push before the fetch (write, unit 7), no rank snapshot (LeaderboardRankSnapshotService
        /// is WPF-only), so every arrow is the muted dash; no Bark hook.
        /// </summary>
        internal async Task RefreshLeaderboardAsync()
        {
            if (_fetching) return; // WPF LeaderboardService.IsRefreshing
            _fetching = true;
            var allTime = IsAllTimeMode;
            var stale = false;
            var status = this.FindControl<TextBlock>("TxtLeaderboardStatus")!;
            var button = this.FindControl<Button>("BtnRefreshLeaderboard")!;
            status.Text = Loc.Get("label_loading_2");
            button.IsEnabled = false;
            try
            {
                // WPF LeaderboardService.RefreshAsync: offline returns false with no error text.
                var (page, error) = CoreSettings.Current.OfflineMode
                    ? (null, null)
                    : await NewClient().FetchAsync<LeaderboardRow>(allTime ? "all-time" : "monthly",
                        CoreAccount.UnifiedUserId, DateTime.UtcNow);
                // The board was switched mid-fetch: this slice belongs to the other mode, fetch again below.
                if (IsAllTimeMode != allTime) { stale = true; return; }
                if (page == null)
                {
                    status.Text = error ?? Loc.Get("label_failed_to_load");
                    return;
                }
                _page = page;
                _ranked.Clear();
                _ranked.AddRange(LeaderboardClient.Rank(page.Entries!, allTime));
                _sortKey = "rank";
                RebuildLeaderboardView();
                status.Text = Loc.GetF("lb_online_and_total", page.OnlineUsers, page.TotalUsers);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error refreshing leaderboard");
                status.Text = Loc.Get("label_error_loading_leaderboard");
            }
            finally
            {
                button.IsEnabled = true;
                _fetching = false;
                if (stale) await RefreshLeaderboardAsync();
            }
        }

        private bool _fetching;

        /// <summary>MainWindow.Leaderboard.cs:930: double-clicking a row opens that trainer's card on the Profile tab.</summary>
        private void LstLeaderboard_DoubleTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
        {
            // The tapped row's own item, not SelectedItem (WPF's read): a band header or empty space opens nothing.
            if ((e.Source as Control)?.DataContext is not LeaderboardRow { DisplayName.Length: > 0 } row) return;
            if (TopLevel.GetTopLevel(this) is not Windows.MainShellWindow shell) return;
            Log.Information("Leaderboard double-click: opening profile for rank {Rank}", row.Rank);
            shell.ShowTab("discord");
            _ = shell.ProfilePage?.OpenProfileAsync(row.DisplayName);
        }

        /// <summary>MainWindow.Leaderboard.cs:749 UpdateYourRankDisplay: the server rank only, never a row's Rank (#693).</summary>
        private void UpdateYourRankDisplay()
        {
            var txt = this.FindControl<TextBlock>("TxtYourRank")!;
            var rank = _page?.YourRank is > 0 ? _page.YourRank.Value : 0;
            txt.IsVisible = rank > 0;
            if (rank > 0)
                txt.Text = _page!.YourTotal is > 0
                    ? Loc.GetF("label_your_rank_0_of_1", rank, _page.YourTotal.Value)
                    : Loc.GetF("label_your_rank_0", rank);
        }

        /// <summary>
        /// MainWindow.Leaderboard.cs:1263 UpdateYouBar. Reads the ranked board, not the filtered view, so a filter that
        /// hides you does not blank it.
        /// ponytail: delta is always the dash (no snapshot); off the board, achievements show 0 and the percentile only
        /// the server-rank branch of GetPlayerPercentile.
        /// </summary>
        private void UpdateYouBar()
        {
            T F<T>(string n) where T : Control => this.FindControl<T>(n)!;
            var rank = _page?.YourRank is > 0 ? _page.YourRank.Value : 0;
            var name = CoreAccount.DisplayName;
            F<Border>("YouBar").IsVisible = rank > 0 || !string.IsNullOrWhiteSpace(name);

            var myId = CoreAccount.UnifiedUserId;
            var myIndex = string.IsNullOrEmpty(myId) ? -1 : _ranked.FindIndex(r => r.UnifiedId == myId);
            var me = myIndex >= 0 ? _ranked[myIndex] : null;
            var s = CoreSettings.Current;

            F<TextBlock>("TxtYouRankNumber").Text = rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) : "\u2013";
            F<TextBlock>("TxtYouDelta").Text = "\u2013";
            F<TextBlock>("TxtYouName").Text = name ?? "";
            F<TextBlock>("TxtYouInitials").Text = LeaderboardEntryData.BuildInitials(name);
            F<Ellipse>("EllYouAvatar").Fill = LeaderboardRow.BuildAvatarBrush(name);

            var level = me?.LevelColumnValue ?? (IsAllTimeMode ? s.HighestLevelEver : s.PlayerLevel);
            F<TextBlock>("TxtYouLevel").Text = level > 0 ? level.ToString(CultureInfo.InvariantCulture) : "\u2013";
            F<TextBlock>("TxtYouXp").Text = me?.XpColumnDisplay
                ?? (IsAllTimeMode ? "\u2013" : FormatCompact(XpCurve.GetTotalXP(s.PlayerLevel, s.PlayerXP, s.DescentEpoch)));

            var earned = me?.AchievementsCount ?? 0;
            var earnable = Math.Max(1, ConditioningControlPanel.Models.Achievement.All.Values.Count(a => !a.IsHidden));
            F<TextBlock>("TxtYouAchievements").Text = $"{earned} / {earnable}";
            var bar = F<ProgressBar>("BarYouAchievements");
            bar.Maximum = earnable;
            bar.Value = Math.Min(earned, earnable);

            string? gap = null;
            if (me != null)
            {
                if (me.Rank <= 1 || myIndex <= 0) gap = Loc.Get("lb_gap_leader");
                else
                {
                    var above = _ranked[myIndex - 1];
                    var diff = above.XpColumnValue - me.XpColumnValue;
                    gap = diff > 0 ? Loc.GetF("lb_gap_to_next", diff.ToString("N0"), above.DisplayName, above.Rank) : Loc.Get("lb_gap_unknown");
                }
            }
            F<TextBlock>("TxtYouGap").Text = gap ?? "";
            F<TextBlock>("TxtYouGap").IsVisible = !string.IsNullOrEmpty(gap);

            var pct = _page?.YourRank is > 0 && _page.YourTotal is > 0
                ? Math.Clamp((int)Math.Ceiling((double)_page.YourRank.Value / _page.YourTotal.Value * 100), 1, 99) : 0;
            F<TextBlock>("TxtYouPercent").Text = pct > 0 ? Loc.GetF("lb_top_percent", pct) : "";
            F<TextBlock>("TxtYouPercent").IsVisible = pct > 0;
        }

        /// <summary>
        /// Apply filter + search + sort, build the podium, inject tier bands and push the
        /// heterogeneous ItemsSource at the roster. Ported whole from
        /// MainWindow.Leaderboard.cs:RebuildLeaderboardView.
        ///
        /// <para>Left out: the FX pass and the rank-flash bookkeeping around it
        /// (MainWindow.LeaderboardFx.cs), and UpdateYourRankDisplay/UpdateYouBar, which read the
        /// service's own YourRank rather than the view.</para>
        /// </summary>
        private void RebuildLeaderboardView()
        {
            try
            {
                IEnumerable<LeaderboardRow> query = _ranked;
                switch (_filter)
                {
                    case "online": query = query.Where(x => x.IsOnline); break;
                    case "patrons": query = query.Where(x => x.EffectivePatreonTier > 0); break;
                    case "og": query = query.Where(x => x.IsSeason0Og); break;
                }

                var search = _searchText.Trim();
                if (search.Length > 0)
                    query = query.Where(x => x.DisplayName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);

                var view = _sortKey switch
                {
                    "name" => query.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    "level" => query.OrderByDescending(x => x.LevelColumnValue).ThenBy(x => x.Rank).ToList(),
                    "achievements" => query.OrderByDescending(x => x.AchievementsCount).ThenBy(x => x.Rank).ToList(),
                    "streak" => query.OrderByDescending(x => x.HighestStreak).ThenBy(x => x.Rank).ToList(),
                    _ => query.OrderBy(x => x.Rank).ToList(),
                };

                // Podium + tier bands only make sense on the untouched, rank-ordered board.
                var isCanonical = _filter == "all" && search.Length == 0 && (_sortKey is "rank" or "xp");
                var showPodium = isCanonical && view.Count >= 3;

                if (showPodium)
                {
                    // Silver, gold, bronze - #1 sits in the middle.
                    _podium.ItemsSource = new List<LeaderboardRow> { view[1], view[0], view[2] };
                    _podium.IsVisible = true;
                }
                else
                {
                    _podium.ItemsSource = null;
                    _podium.IsVisible = false;
                }

                var display = new List<object>(view.Count + 8);
                var lastBand = -1;

                for (int i = showPodium ? 3 : 0; i < view.Count; i++)
                {
                    if (isCanonical)
                    {
                        var band = TierIndexForRank(view[i].Rank);
                        // Band 0 (ranks 1-3) never gets a divider - the podium is the divider.
                        if (band != lastBand)
                        {
                            if (band > 0) display.Add(Band(band));
                            lastBand = band;
                        }
                    }

                    display.Add(view[i]);
                }

                _roster.ItemsSource = display;
                _empty.IsVisible = view.Count == 0 && _ranked.Count > 0;
                UpdateYourRankDisplay();
                UpdateYouBar();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to rebuild leaderboard view");
            }
        }

        /// <summary>
        /// "Jump to me". WPF animate-scrolls until the row sits CENTRED and then flares it, and
        /// bounces off the end of travel when you are off the board
        /// (MainWindow.Leaderboard.cs:108 + MainWindow.LeaderboardFx.cs). That FX partial is not on
        /// this head, so this is the scroll without the flare - the row lands on screen, which is
        /// the whole promise of the button.
        ///
        /// <para>ponytail: the off-the-board branch needs LeaderboardService.YourRank for its
        /// message; with no service, and with your row merely filtered out rather than absent, the
        /// honest thing is to do nothing rather than bounce the list at you.</para>
        /// </summary>
        private void BtnJumpToMe_Click(object? sender, RoutedEventArgs e)
        {
            if (_roster.ItemsSource is not IEnumerable<object> items) return;
            var me = items.OfType<LeaderboardRow>().FirstOrDefault(r => r.IsCurrentUser);
            if (me != null) _roster.ScrollIntoView(me);
        }

        /// <summary>MainWindow.Leaderboard.cs:1412.</summary>
        private static string FormatCompact(double value) =>
            value >= 1_000_000 ? FormattableString.Invariant($"{value / 1_000_000.0:F1}M")
            : value >= 1_000 ? FormattableString.Invariant($"{value / 1_000.0:F1}k") : ((int)value).ToString();

        /// <summary>0 = 1-3, 1 = 4-10, 2 = 11-25, 3 = 26-50, 4 = 51-100, 5 = 101-200, 6 = 201+.</summary>
        private static int TierIndexForRank(int rank)
        {
            if (rank <= 3) return 0;
            if (rank <= 10) return 1;
            if (rank <= 25) return 2;
            if (rank <= 50) return 3;
            if (rank <= 100) return 4;
            if (rank <= 200) return 5;
            return 6;
        }

        /// <summary>
        /// A tier band, built the way MainWindow.Leaderboard.cs BuildTierBand does: the tier name
        /// and its rank range, joined by the same separator, with the flavour line underneath.
        /// </summary>
        private static LeaderboardTierBand Band(int index)
        {
            string[] nameKeys = { "lb_tier_dissolved", "lb_tier_hollowed", "lb_tier_spiralbound",
                                  "lb_tier_sunken", "lb_tier_pliant", "lb_tier_drifting", "lb_tier_blinking" };
            string[] subKeys = { "lb_tier_dissolved_sub", "lb_tier_hollowed_sub", "lb_tier_spiralbound_sub",
                                 "lb_tier_sunken_sub", "lb_tier_pliant_sub", "lb_tier_drifting_sub", "lb_tier_blinking_sub" };
            int[] lower = { 1, 4, 11, 26, 51, 101, 201 };
            int[] upper = { 3, 10, 25, 50, 100, 200, 0 };

            index = Math.Clamp(index, 0, nameKeys.Length - 1);
            var range = upper[index] > 0
                ? Loc.GetF("lb_tier_range", lower[index], upper[index])
                : Loc.GetF("lb_tier_range_open", lower[index]);

            return new LeaderboardTierBand
            {
                HeaderText = $"{Loc.Get(nameKeys[index])}   ·   {range}",
                Subtitle = Loc.Get(subKeys[index])
            };
        }

        // ------------------------------------------------------------------
        // What is still MainWindow's
        // ------------------------------------------------------------------
        // ponytail: the row double-click needs DiscordTabView's profile search, which is itself a stub here; the
        // per-row Discord chip opens a browser from inside a DataTemplate, so it needs a Click in
        // LeaderboardTabView.axaml as well as a launcher. BtnViewSeasonRecap is revealed by the shell's
        // BtnLeaderboard_Click (MainShellWindow.AchievementsTab.cs) when a snapshot exists.
    }

    /// <summary>
    /// Non-selectable separator injected between rank groups in the roster. It is a distinct type
    /// so Avalonia's DataTemplate matching picks the band template for it; on WPF the same job
    /// needed a DataTemplateSelector.
    /// </summary>
    public sealed class LeaderboardTierBand
    {
        /// <summary>e.g. "HOLLOWED  ·  ranks 4-10".</summary>
        public string HeaderText { get; set; } = "";

        public string Subtitle { get; set; } = "";
    }

    /// <summary>
    /// One roster row: Core's <see cref="LeaderboardEntryData"/> (the wire model WPF's LeaderboardEntry also derives
    /// from) plus this head's avatar brush and the flags Avalonia's templates bind instead of WPF DataTriggers.
    /// </summary>
    public sealed class LeaderboardRow : LeaderboardEntryData
    {
        public bool IsPatreonTier2 => EffectivePatreonTier == 2;
        public bool IsPatreonTier3 => EffectivePatreonTier == 3;
        public bool IsRank1 => Rank == 1;
        public bool IsRank2 => Rank == 2;
        public bool IsRank3 => Rank == 3;
        public bool IsDeltaUp => DeltaState == "up";
        public bool IsDeltaDown => DeltaState == "down";
        public bool IsDeltaNew => DeltaState == "new";

        private IBrush? _avatarBrush;

        /// <summary>
        /// Deterministic two-stop gradient for the initials avatar. The leaderboard payload carries
        /// no avatar URL, so the circle is generated from a stable hash of the display name: the
        /// same subject always gets the same colours. The stops come from Core AvatarGradient, as WPF's.
        /// </summary>
        public IBrush AvatarBrush => _avatarBrush ??= BuildAvatarBrush(DisplayName);

        /// <summary>
        /// Frozen two-stop gradient derived from a stable hash of the name. Hues are clamped to
        /// 200-345 deg (blue - indigo - violet - magenta - pink) so the generated avatars stay
        /// inside the app's palette instead of turning the roster into a rainbow.
        /// </summary>
        public static IBrush BuildAvatarBrush(string? name)
        {
            var ((r1, g1, b1), (r2, g2, b2)) = AvatarGradient(name);

            var brush = new LinearGradientBrush
            {
                // Relative, not absolute: an Avalonia gradient point is device pixels by default,
                // so the WPF "0.15,0" form would collapse the gradient into the top-left pixel.
                StartPoint = new RelativePoint(0.15, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.85, 1, RelativeUnit.Relative),
            };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(r1, g1, b1), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(r2, g2, b2), 1));
            return brush;
        }
    }
}
