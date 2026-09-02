using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/LeaderboardTabView.xaml.cs.
    ///
    /// What survived unchanged: the season countdown (it is pure wall-clock arithmetic and touches
    /// no service), the Level-column relabel for the All-Time board, and the timer's start/stop
    /// discipline so a hidden or unloaded tab cannot keep a dead visual tree alive.
    ///
    /// What is stubbed: every handler that forwarded to MainWindow. The tab owns no logic of its
    /// own in the WPF head either - mode switch, refresh, jump-to-me, sorting, search, filtering,
    /// the Discord DM and the season recap all live in MainWindow.Leaderboard.cs, which is still
    /// in the WPF head. Each one is a no-op here with a ponytail marker.
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
        /// <summary>
        /// Ticks the season countdown in the header. One minute is plenty for a
        /// "2d 14h" readout, and the timer is stopped whenever the tab is hidden or
        /// unloaded so it can't keep a dead visual tree alive.
        /// </summary>
        private DispatcherTimer? _seasonTimer;

        private readonly TextBlock _txtSeason;
        private readonly TextBlock _txtSubtitle;
        private readonly TextBlock _hdrLevelSeasonal;
        private readonly TextBlock _hdrLevelPeak;

        public LeaderboardTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _txtSeason = this.FindControl<TextBlock>("TxtLeaderboardSeason")!;
            _txtSubtitle = this.FindControl<TextBlock>("TxtLeaderboardSubtitle")!;
            _hdrLevelSeasonal = this.FindControl<TextBlock>("HdrLevelSeasonal")!;
            _hdrLevelPeak = this.FindControl<TextBlock>("HdrLevelPeak")!;

            // The WPF tab is gated by MainWindow.UpdateTrophyCaseColumns(); there is no skill
            // service on this head yet, so the streak column is switched on so it is actually
            // covered by the render proof rather than sitting invisible.
            // ponytail: needs SkillService, wired when it moves to Core.
            ShowTrophyStats = true;

            Loaded += OnLeaderboardTabLoaded;
            Unloaded += OnLeaderboardTabUnloaded;
            PropertyChanged += OnLeaderboardTabPropertyChanged;

            LoadPlaceholderBoard();
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

        /// <summary>True while the All-Time board is showing (no season countdown).</summary>
        internal bool IsAllTimeMode { get; private set; }

        internal void SetLeaderboardMode(bool isAllTime)
        {
            IsAllTimeMode = isAllTime;
            RefreshSeasonHeader();
            ApplyModeLabels();
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
        // Season countdown
        // ------------------------------------------------------------------

        private void OnLeaderboardTabLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            RefreshSeasonHeader();
            ApplyModeLabels();
            if (IsVisible) StartSeasonTimer();
        }

        private void OnLeaderboardTabUnloaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => StopSeasonTimer();

        /// <summary>WPF's IsVisibleChanged; Avalonia reports it through the property-changed feed.</summary>
        private void OnLeaderboardTabPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != IsVisibleProperty) return;

            if (IsVisible)
            {
                RefreshSeasonHeader();
                StartSeasonTimer();
            }
            else
            {
                StopSeasonTimer();
            }
        }

        private void StartSeasonTimer()
        {
            if (_seasonTimer != null) { _seasonTimer.Start(); return; }

            _seasonTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
                                               (_, _) => RefreshSeasonHeader());
            _seasonTimer.Start();
        }

        private void StopSeasonTimer()
        {
            if (_seasonTimer == null) return;
            _seasonTimer.Stop();
            _seasonTimer = null;
        }

        /// <summary>
        /// THE DAY MONTHLY SEASONS STOPPED EXISTING: 2026-09-01 UTC. Copied from
        /// Services/Descent/DescentMigration.cs (DescentEpochs.SeasonsEndUtc), which is still in
        /// the WPF head. It is a date literal, not a service, and dropping the guard instead would
        /// re-introduce exactly the bug it exists to stop - a countdown promising a season end that
        /// can never arrive.
        /// ponytail: local copy of DescentEpochs.SeasonsEndUtc, delete when Descent moves to Core.
        /// </summary>
        private static readonly DateTime SeasonsEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        private static bool SeasonsHaveEnded => DateTime.UtcNow >= SeasonsEndUtc;

        /// <summary>
        /// Repaint the season name + countdown. Derived entirely locally: the board's season key
        /// is DateTime.UtcNow.ToString("yyyy-MM"), so the season ends at the first instant of the
        /// next UTC month. No server call.
        /// </summary>
        internal void RefreshSeasonHeader()
        {
            // The Descent branch below collapses the subtitle outright; restore it up front so a
            // mode switch can never leave it collapsed against a line that does have text.
            _txtSubtitle.IsVisible = true;

            if (IsAllTimeMode)
            {
                _txtSeason.Text = Loc.Get("lb_all_time_title");
                _txtSubtitle.Text = Loc.Get("lb_all_time_sub");
                return;
            }

            // ponytail: needs App.QuestDefinitions.SeasonTitle, wired when it moves to Core. The
            // WPF fallback for a missing title is section_seasons, which is what shows here.
            _txtSeason.Text = Loc.Get("section_seasons");

            if (SeasonsHaveEnded)
            {
                _txtSubtitle.Text = string.Empty;
                _txtSubtitle.IsVisible = false;
                return;
            }

            var now = DateTime.UtcNow;
            var seasonEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            var left = seasonEnd - now;

            if (left <= TimeSpan.Zero)
            {
                _txtSubtitle.Text = Loc.Get("lb_season_ended");
                return;
            }

            string span;
            if (left.TotalDays >= 1)
                span = Loc.GetF("lb_time_dh", (int)left.TotalDays, left.Hours);
            else if (left.TotalHours >= 1)
                span = Loc.GetF("lb_time_hm", (int)left.TotalHours, left.Minutes);
            else
                span = Loc.GetF("lb_time_m", Math.Max(1, (int)left.TotalMinutes));

            _txtSubtitle.Text = Loc.GetF("lb_season_ends_in", span);
        }

        // ------------------------------------------------------------------
        // Placeholder board
        // ------------------------------------------------------------------

        /// <summary>
        /// Fill the roster, the podium and the sticky you-bar with sample rows.
        ///
        /// ponytail: needs LeaderboardService + LeaderboardEntry, both pinned to the WPF head
        /// (LeaderboardEntry reads App.UnifiedUserId and builds a System.Windows.Media.Brush).
        /// Wired when they move to Core. The sample deliberately covers every branch the templates
        /// carry - podium ranks 1/2/3, a tier band, an OG row, patron tiers I/II/III, a Discord
        /// chip, a no-badges row, an offline row and the current-user row - so the render proof
        /// exercises the markup rather than one happy path.
        /// </summary>
        private void LoadPlaceholderBoard()
        {
            var rows = new List<object>
            {
                Row(1, "Bambi Prime", 312, "1.4M", 41, true, og: true, tier: 3, discord: "1", streak: 96, best: 214.5),
                Row(2, "velvet_hush", 298, "1.2M", 39, true, tier: 2, discord: "2", streak: 71, best: 180.0),
                Row(3, "Nyx", 271, "988.4k", 37, false, og: true, streak: 55, best: 143.5),
                Band(1),
                Row(4, "spiral.doll", 244, "812.0k", 34, true, tier: 1, streak: 48, best: 121.0),
                Row(5, "Quiet Signal", 231, "770.6k", 33, false, discord: "5", streak: 40, best: 98.5),
                Row(6, "hollow-eyed", 219, "702.1k", 30, true, streak: 33, best: 87.0),
                Row(7, "you", 204, "664.3k", 28, true, me: true, tier: 1, streak: 27, best: 76.5),
                Row(8, "MK_Ultraviolet", 190, "610.9k", 26, false, og: true, streak: 21, best: 64.0),
                Band(2),
                Row(11, "pliant_kitten", 155, "480.2k", 22, true, discord: "11", streak: 14, best: 51.5),
                Row(12, "drifting", 141, "421.8k", 19, false, streak: 9, best: 40.0),
                Row(13, "blink", 128, "377.4k", 16, true, tier: 2, streak: 4, best: 28.5),
            };

            var lst = this.FindControl<ListBox>("LstLeaderboard")!;
            lst.ItemsSource = rows;

            var podium = this.FindControl<ItemsControl>("PodiumHost")!;
            podium.ItemsSource = new List<object> { rows[0], rows[1], rows[2] };
            podium.IsVisible = true;

            // Header status + your-rank badge. Keys and argument order from
            // MainWindow.Leaderboard.cs (UpdateYourRankDisplay / the refresh path).
            this.FindControl<TextBlock>("TxtLeaderboardStatus")!.Text = Loc.GetF("lb_online_and_total", 148, 2317);
            var yourRank = this.FindControl<TextBlock>("TxtYourRank")!;
            yourRank.Text = Loc.GetF("label_your_rank_0_of_1", 7, 2317);
            yourRank.IsVisible = true;

            // Sticky you-bar.
            var me = (LeaderboardRow)rows[7];
            this.FindControl<TextBlock>("TxtYouDelta")!.Text = "▲2";
            this.FindControl<TextBlock>("TxtYouRankNumber")!.Text = me.Rank.ToString(CultureInfo.InvariantCulture);
            this.FindControl<Ellipse>("EllYouAvatar")!.Fill = me.AvatarBrush;
            this.FindControl<TextBlock>("TxtYouInitials")!.Text = me.Initials;
            this.FindControl<TextBlock>("TxtYouName")!.Text = me.DisplayName;
            this.FindControl<TextBlock>("TxtYouLevel")!.Text = me.LevelColumnValue.ToString(CultureInfo.InvariantCulture);
            this.FindControl<TextBlock>("TxtYouXp")!.Text = me.XpColumnDisplay;
            this.FindControl<TextBlock>("TxtYouAchievements")!.Text = me.AchievementsDisplay;

            var bar = this.FindControl<ProgressBar>("BarYouAchievements")!;
            bar.Maximum = me.AchievementsTotal;
            bar.Value = me.AchievementsCount;

            var above = (LeaderboardRow)rows[6];
            this.FindControl<TextBlock>("TxtYouGap")!.Text =
                Loc.GetF("lb_gap_to_next", (37800L).ToString("N0", CultureInfo.CurrentCulture), above.DisplayName, above.Rank);
            this.FindControl<TextBlock>("TxtYouPercent")!.Text = Loc.GetF("lb_top_percent", 1);
        }

        private static LeaderboardRow Row(int rank, string name, int level, string xp, int achievements,
                                          bool online, bool og = false, int tier = 0, string? discord = null,
                                          bool me = false, int streak = 0, double best = 0)
            => new()
            {
                Rank = rank,
                DisplayName = name,
                LevelColumnValue = level,
                XpColumnDisplay = xp,
                AchievementsCount = achievements,
                IsOnline = online,
                IsSeason0Og = og,
                EffectivePatreonTier = tier,
                DiscordId = discord,
                IsCurrentUser = me,
                HasTrophyCase = streak > 0,
                HighestStreak = streak,
                LongestSessionMinutes = best,
                // Rank movement, baked exactly as LeaderboardEntry.ApplyRankDelta does it.
                DeltaState = rank switch { 1 => "up", 3 => "down", 5 => "new", _ => "same" },
            };

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
        // Stubs for what MainWindow owned (the tab owns no logic of its own)
        // ------------------------------------------------------------------
        // ponytail: BtnLeaderboardDiscord / Mode / Refresh / JumpToMe / ViewSeasonRecap /
        // SortHeader / Search / Filter / row double-click all forwarded to MainWindow.Leaderboard.cs,
        // which needs LeaderboardService, App.Settings and the Discord DM path. Wired when those
        // move to Core; nothing is hooked up here, so the controls are inert rather than wrong.
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
    /// One roster row. The Avalonia-side stand-in for Services.LeaderboardEntry, which cannot be
    /// referenced from here: it reads App.UnifiedUserId and hands back a System.Windows.Media
    /// brush. Property names, formatting and the derived flags are copied from it so the port is
    /// a rename away from the real model when it reaches Core.
    /// </summary>
    public sealed class LeaderboardRow
    {
        public int Rank { get; set; }
        public string DisplayName { get; set; } = "";
        public int LevelColumnValue { get; set; }
        public string XpColumnDisplay { get; set; } = "";
        public int AchievementsCount { get; set; }
        public bool IsOnline { get; set; }
        public bool IsSeason0Og { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool HasTrophyCase { get; set; }
        public int HighestStreak { get; set; }
        public double LongestSessionMinutes { get; set; }
        public int SeasonsCompleted { get; set; }
        public string? DiscordId { get; set; }
        public bool IsAllTimeView { get; set; }

        /// <summary>Denominator for the achievements column and its bar; they can't disagree.</summary>
        public int AchievementsTotal => 60;

        public string AchievementsDisplay => $"{AchievementsCount} / {AchievementsTotal}";

        /// <summary>
        /// Caption for <see cref="LevelColumnValue"/>. On the All-Time board the number is
        /// HighestLevelEver, not a current standing, so calling it "Level" next to an XP-ordered
        /// rank makes the board look mis-sorted.
        /// </summary>
        public string LevelLabel => Loc.Get(IsAllTimeView ? "lb_col_peak" : "label_level");

        /// <summary>The server ships tier 0 for some legacy patrons; the badge treats that as I.</summary>
        public int EffectivePatreonTier { get; set; }
        public bool ShowPatreonChip => EffectivePatreonTier > 0;
        public bool IsPatreonTier2 => EffectivePatreonTier == 2;
        public bool IsPatreonTier3 => EffectivePatreonTier == 3;

        /// <summary>Roman numeral suffix on the patron chip, so the chip text stays localizable.</summary>
        public string PatreonTierRoman => EffectivePatreonTier switch { 3 => "III", 2 => "II", 1 => "I", _ => "" };

        public bool HasDiscord => !string.IsNullOrEmpty(DiscordId);

        /// <summary>Seasons-completed chip - All-Time board only.</summary>
        public bool ShowSeasonsChip => IsAllTimeView && SeasonsCompleted > 0;
        public string SeasonsChipText => SeasonsCompleted.ToString(CultureInfo.InvariantCulture);

        public bool HasNoBadges => !IsSeason0Og && !ShowPatreonChip && !HasDiscord && !ShowSeasonsChip;

        public string HighestStreakDisplay => HasTrophyCase ? HighestStreak.ToString(CultureInfo.InvariantCulture) : "";
        public string LongestSessionDisplay => HasTrophyCase ? LongestSessionMinutes.ToString("F1", CultureInfo.CurrentCulture) : "";

        public bool IsRank1 => Rank == 1;
        public bool IsRank2 => Rank == 2;
        public bool IsRank3 => Rank == 3;

        /// <summary>"up" | "down" | "same" | "new" | "none". Drives the arrow's colour.</summary>
        public string DeltaState { get; set; } = "none";
        public bool IsDeltaUp => DeltaState == "up";
        public bool IsDeltaDown => DeltaState == "down";
        public bool IsDeltaNew => DeltaState == "new";

        /// <summary>Pre-rendered arrow text ("▲2", "▼1", "–", or the NEW chip label).</summary>
        public string DeltaText => DeltaState switch
        {
            "up" => "▲2",
            "down" => "▼1",
            "new" => Loc.Get("lb_delta_new"),
            _ => "–",
        };

        /// <summary>1-2 uppercase initials for the generated avatar.</summary>
        public string Initials => BuildInitials(DisplayName);

        private IBrush? _avatarBrush;

        /// <summary>
        /// Deterministic two-stop gradient for the initials avatar. The leaderboard payload carries
        /// no avatar URL, so the circle is generated from a stable hash of the display name: the
        /// same subject always gets the same colours. Copied from LeaderboardEntry.BuildAvatarBrush.
        /// </summary>
        public IBrush AvatarBrush => _avatarBrush ??= BuildAvatarBrush(DisplayName);

        /// <summary>1-2 uppercase initials from a display name. "?" when there's nothing usable.</summary>
        public static string BuildInitials(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";

            var parts = name.Split(new[] { ' ', '_', '-', '.', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var a = FirstLetterOrDigit(parts[0]);
                var b = FirstLetterOrDigit(parts[1]);
                if (a != '\0' && b != '\0') return string.Concat(char.ToUpperInvariant(a), char.ToUpperInvariant(b));
            }

            var chars = new List<char>(2);
            foreach (var c in name.Trim())
            {
                if (!char.IsLetterOrDigit(c)) continue;
                chars.Add(char.ToUpperInvariant(c));
                if (chars.Count == 2) break;
            }
            return chars.Count > 0 ? new string(chars.ToArray()) : "?";
        }

        private static char FirstLetterOrDigit(string s)
        {
            foreach (var c in s) if (char.IsLetterOrDigit(c)) return c;
            return '\0';
        }

        /// <summary>
        /// Frozen two-stop gradient derived from a stable hash of the name. Hues are clamped to
        /// 200-345 deg (blue - indigo - violet - magenta - pink) so the generated avatars stay
        /// inside the app's palette instead of turning the roster into a rainbow.
        /// </summary>
        public static IBrush BuildAvatarBrush(string? name)
        {
            var hash = StableHash(name ?? "");
            var hue = 200.0 + (hash % 146);              // 200 .. 345
            var hue2 = hue - 14.0; if (hue2 < 195.0) hue2 += 150.0;

            var brush = new LinearGradientBrush
            {
                // Relative, not absolute: an Avalonia gradient point is device pixels by default,
                // so the WPF "0.15,0" form would collapse the gradient into the top-left pixel.
                StartPoint = new RelativePoint(0.15, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.85, 1, RelativeUnit.Relative),
            };
            brush.GradientStops.Add(new GradientStop(FromHsl(hue, 0.70, 0.70), 0));
            brush.GradientStops.Add(new GradientStop(FromHsl(hue2, 0.52, 0.40), 1));
            return brush;
        }

        /// <summary>FNV-1a over the lower-cased name - stable across runs and machines.</summary>
        private static uint StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in s)
                {
                    h ^= char.ToLowerInvariant(c);
                    h *= 16777619;
                }
                return h;
            }
        }

        private static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            var m = l - c / 2;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb(
                (byte)Math.Round(Math.Clamp((r + m) * 255, 0, 255)),
                (byte)Math.Round(Math.Clamp((g + m) * 255, 0, 255)),
                (byte)Math.Round(Math.Clamp((b + m) * 255, 0, 255)));
        }
    }
}
