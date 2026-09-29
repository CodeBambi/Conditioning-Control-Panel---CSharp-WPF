using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The achievement page, built in code as WPF MainWindow.AchievementsTab.cs builds it: one card per
    /// visible Achievement.All entry, free or patron grid, filter chips, meter, tooltip.
    /// ponytail: no reward band text/art, reward count or Rewards chip (WardrobeCatalog is WPF-only) and no
    /// tile FX (entrance stagger, holo tilt, unlock burst - MainWindow.EventFx.cs); both land with their ports.
    /// </summary>
    public partial class AchievementsTabView : UserControl
    {
        internal const string FilterAll = "all", FilterUnlocked = "unlocked", FilterLocked = "locked";

        private static readonly IBrush Muted = Brush.Parse("#9A93B8"), Dim = Brush.Parse("#8079A3"),
            Tick = Brush.Parse("#5EC8F2"), Rule = Brush.Parse("#33FFFFFF"), MeterFill = Brush.Parse("#FF69B4"),
            MeterTrack = Brush.Parse("#CC1A1A2E"), BandBg = Brush.Parse("#F2252542"),
            PatreonFill = Brush.Parse("#3DFF69B4"), PatreonEdge = Brush.Parse("#8CFF69B4"), PatreonInk = Brush.Parse("#FFC9E3");

        private readonly AchievementEngine _engine;
        private readonly Dictionary<string, Tile> _tiles = new();
        private readonly List<ToggleButton> _chips = new();
        private readonly HashSet<string> _pending = new();
        private bool _flushQueued;
        internal string Filter { get; private set; } = FilterAll;

        /// <summary>Everything a card redraws without walking the visual tree (WPF AchievementCardParts).</summary>
        private sealed class Tile
        {
            public Achievement A = null!;
            public ToggleButton Card = null!;
            public Image Badge = null!;
            public TextBlock Name = null!, Info = null!, State = null!;
            public StackPanel InfoStack = null!;
            public Control? Meter;
        }

        /// <summary>Decoded badges keyed by resolved path, so a mod switch re-resolves and a refresh costs a lookup.</summary>
        private static readonly Dictionary<string, global::Avalonia.Media.Imaging.Bitmap?> ArtCache = new();

        /// <summary>Test hooks: how many cards have been redrawn, and in how many UI passes.</summary>
        internal int TileApplies { get; private set; }
        internal int Passes { get; private set; }

        public AchievementsTabView() : this(Engine) { }

        internal AchievementsTabView(AchievementEngine engine)
        {
            AvaloniaXamlLoader.Load(this);
            _engine = engine;
            Build();
            // WPF MainWindow.xaml.cs:420 refreshes the one tile on unlock (AchievementsTab.cs:886); many unlocks in one
            // burst (N level-ups) coalesce into one UI pass. WPF TabNavigation.cs:374 refreshes every tile on show.
            void OnUnlocked(object? _, Achievement a)
            {
                lock (_pending) { _pending.Add(a.Id); if (_flushQueued) return; _flushQueued = true; }
                global::Avalonia.Threading.Dispatcher.UIThread.Post(Flush);
            }
            AttachedToVisualTree += (_, _) => { engine.Unlocked -= OnUnlocked; engine.Unlocked += OnUnlocked; };
            DetachedFromVisualTree += (_, _) => engine.Unlocked -= OnUnlocked;
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible) RefreshAll(); };
        }

        /// <summary>The live engine; on the headless render path a read-only load of the same file
        /// (nothing on that path ever saves it).</summary>
        private static AchievementEngine Engine =>
            App.Achievements ?? new AchievementEngine(new AchievementStore(AchievementStore.DefaultPath));

        internal IEnumerable<ToggleButton> Cards => _tiles.Values.Select(t => t.Card);

        /// <summary>WPF PopulateAchievementGrid, minus the reward map. Runs once.</summary>
        private void Build()
        {
            var free = this.FindControl<WrapPanel>("AchievementGrid")!;
            var patron = this.FindControl<WrapPanel>("PatronAchievementGrid")!;
            var theme = this.FindResource("AchievementCard") as ControlTheme;
            foreach (var a in Achievement.All.Values)
            {
                if (a.IsHidden) continue; // parked: no reachable unlock path
                var tile = BuildCard(a, theme);
                _tiles[a.Id] = tile;
                (a.IsExclusive ? patron : free).Children.Add(tile.Card);
            }
            BuildFilters();
            RefreshAll();
        }

        private void Flush()
        {
            string[] ids;
            lock (_pending) { ids = _pending.ToArray(); _pending.Clear(); _flushQueued = false; }
            foreach (var id in ids) if (_tiles.TryGetValue(id, out var t)) Apply(t);
            AfterApply();
        }

        /// <summary>WPF RefreshAllAchievementTiles: in place, never a rebuild.</summary>
        internal void RefreshAll()
        {
            foreach (var t in _tiles.Values) Apply(t);
            AfterApply();
        }

        private void AfterApply()
        {
            Passes++;
            DataContext = new AchievementsTabViewModel(_engine);
            ApplyFilter();
        }

        private bool IsUnlocked(string id) => _engine.Progress.IsUnlocked(id);

        private static string ModAware(string key, string fallback)
        {
            var s = Loc.Get(key);
            return CoreMods.MakeModAware(s == key ? fallback : s);
        }

        private static string Name(Achievement a) => ModAware($"achievement_{a.Id}_name", a.Name);
        private static string Req(Achievement a) => ModAware($"achievement_{a.Id}_req", a.Requirement);
        private static string Flavor(Achievement a) => ModAware($"achievement_{a.Id}_flavor", a.FlavorText);

        private static global::Avalonia.Media.Imaging.Bitmap? Art(string imageName)
        {
            var name = $"achievements/{imageName}";
            var key = (CoreModArt.OverridePath(name) ?? "") + "|" + name;
            lock (ArtCache)
            {
                if (!ArtCache.TryGetValue(key, out var art)) ArtCache[key] = art = Helpers.ModArt.TryLoad(name, decodeWidth: 150);
                return art;
            }
        }

        /// <summary>The state-free structure of a card; <see cref="Apply"/> fills in the state.</summary>
        private static Tile BuildCard(Achievement a, ControlTheme? theme)
        {
            var t = new Tile { A = a, Card = new ToggleButton { Theme = theme, Tag = a.Id, IsChecked = false } };
            t.Badge = new Image { Width = 150, Height = 150, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            t.Name = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 17, MaxHeight = 34, Margin = new Thickness(10, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            t.Info = new TextBlock { FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 14, MaxHeight = 42, Margin = new Thickness(12, 4, 12, 4) };
            t.InfoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { t.Info } };

            // Bottom band: the category glyph WPF falls back to without reward art, and the state mark.
            var band = new Grid { Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            band.Children.Add(new TextBlock { Text = Glyph(a.Category), FontSize = 22, Foreground = Brushes.White, Opacity = 0.3, Width = 40,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            t.State = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t.State, 2);
            band.Children.Add(t.State);
            var bandChrome = new Border { Height = 56, Background = BandBg, BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 14, 14), Child = band };

            var content = new Grid { RowDefinitions = new RowDefinitions("164,Auto,*,Auto") };
            Grid.SetRow(t.Name, 1);
            Grid.SetRow(t.InfoStack, 2);
            Grid.SetRow(bandChrome, 3);
            content.Children.Add(t.Badge);
            content.Children.Add(t.Name);
            content.Children.Add(t.InfoStack);
            content.Children.Add(bandChrome);

            if (a.IsPremiumFeature)
            {
                var chip = new Border { Background = PatreonFill, BorderBrush = PatreonEdge, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 8, 8, 0),
                    Child = new TextBlock { Text = Loc.Get("achievement_badge_patreon"), FontSize = 9, FontWeight = FontWeight.SemiBold,
                        Foreground = PatreonInk, Margin = new Thickness(7, 2, 7, 3) } };
                ToolTip.SetTip(chip, Loc.Get("tooltip_achievement_premium_program"));
                content.Children.Add(chip);
            }
            t.Card.Content = content;
            return t;
        }

        /// <summary>WPF RefreshAchievementTile: art, blur, name, info, meter, state mark, tooltip.</summary>
        private void Apply(Tile t)
        {
            TileApplies++;
            var a = t.A;
            var unlocked = IsUnlocked(a.Id);
            var art = Art(a.ImageName);
            if (art != null) t.Badge.Source = art; // missing art leaves the old source (WPF contract)
            t.Badge.Effect = unlocked ? null : new BlurEffect { Radius = 15 };
            t.Name.Text = unlocked ? Name(a) : Loc.Get("achv_card_locked_name");

            // Requirement while locked, flavor once earned (WPF ApplyAchievementInfoText).
            t.Info.Text = unlocked ? Flavor(a) : Req(a);
            t.Info.Foreground = unlocked ? Muted : Dim;
            t.Info.FontStyle = unlocked ? FontStyle.Italic : FontStyle.Normal;

            // WPF ApplyAchievementMeter: locked + countable shows the bar and caps the requirement at two lines.
            if (t.Meter != null) { t.InfoStack.Children.Remove(t.Meter); t.Meter = null; }
            t.Info.MaxHeight = 42;
            var meter = unlocked ? null : AchievementMeters.Compute(a, _engine.Progress, CoreSettings.Current.PlayerLevel);
            if (meter is { } m)
            {
                t.Info.MaxHeight = 28;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var cols = new Grid { ColumnDefinitions = new ColumnDefinitions($"{m.Fraction.ToString(inv)}*,{(1 - m.Fraction).ToString(inv)}*") };
                cols.Children.Add(new Border { Background = MeterFill, CornerRadius = new CornerRadius(2) });
                t.Meter = new StackPanel { Name = "Meter", Margin = new Thickness(0, 0, 0, 2), Children =
                {
                    new Border { Height = 4, Background = MeterTrack, CornerRadius = new CornerRadius(2), Margin = new Thickness(14, 0), Child = cols },
                    new TextBlock { FontSize = 10, Foreground = Muted, TextAlignment = TextAlignment.Center, Margin = new Thickness(12, 3, 12, 0), Text = m.Label },
                } };
                t.InfoStack.Children.Add(t.Meter);
            }

            t.State.Text = unlocked ? "✓" : "🔒";
            t.State.FontSize = unlocked ? 16 : 13;
            t.State.FontWeight = unlocked ? FontWeight.Bold : FontWeight.Normal;
            t.State.Foreground = unlocked ? Tick : Muted;

            // ponytail: WPF's unlocked tooltip names the reward item; without WardrobeCatalog only the locked,
            // no-reward template is honest, so unlocked cards carry no tooltip yet.
            ToolTip.SetTip(t.Card, unlocked ? null : Loc.GetF("achv_tooltip_locked_no_reward", Req(a)));
            // The reward arg is "???": the item is unknown here (WardrobeCatalog), and "No item" would be false for gated ones.
            AutomationProperties.SetName(t.Card, unlocked
                ? Loc.GetF("achv_automation_unlocked", Name(a), Loc.Get("achv_card_locked_name"))
                : Loc.GetF("achv_automation_locked", Req(a)));
        }

        private static string Glyph(AchievementCategory c) => c switch
        {
            AchievementCategory.Progression => "▲",
            AchievementCategory.Minigames => "●",
            AchievementCategory.Hardcore => "■",
            AchievementCategory.Deeper => "▼",
            AchievementCategory.Creator => "★",
            _ => "◆",
        };

        /// <summary>WPF BuildAchievementFilters: radio-style chips, built once so the choice survives a rebuild.</summary>
        private void BuildFilters()
        {
            var host = this.FindControl<WrapPanel>("AchievementFilters")!;
            if (host.Children.Count > 0) return;
            var theme = this.FindResource("AchievementFilterChip") as ControlTheme;
            foreach (var (key, locKey) in new[] { (FilterAll, "achv_filter_all"), (FilterUnlocked, "achv_filter_unlocked"), (FilterLocked, "achv_filter_locked") })
            {
                var chip = new ToggleButton { Theme = theme, Tag = key, IsChecked = key == Filter,
                    Content = new TextBlock { Text = Loc.Get(locKey) } };
                chip.IsCheckedChanged += (_, _) => SetFilter(chip);
                _chips.Add(chip);
                host.Children.Add(chip);
            }
        }

        private void SetFilter(ToggleButton chip)
        {
            var key = (string)chip.Tag!;
            if (chip.IsChecked != true)
            {
                if (key == Filter) chip.IsChecked = true; // the active chip cannot be cleared
                return;
            }
            Filter = key;
            foreach (var other in _chips) if (other != chip) other.IsChecked = false;
            ApplyFilter();
        }

        internal void SelectFilter(string key) { foreach (var c in _chips) if ((string)c.Tag! == key) c.IsChecked = true; }

        /// <summary>Hides cards in both grids - the patron section filters with the free one.</summary>
        private void ApplyFilter()
        {
            foreach (var t in _tiles.Values)
                t.Card.IsVisible = Filter switch
                {
                    FilterUnlocked => IsUnlocked(t.A.Id),
                    FilterLocked => !IsUnlocked(t.A.Id),
                    _ => true,
                };
        }
    }

    /// <summary>
    /// Supplies the FORMATTED strings the view binds to. Static strings now come straight from
    /// {loc:Str key} in the XAML (Localization/StrExtension.cs); only the ones with numbers in
    /// them need code, exactly as in the WPF head where they are set with Loc.GetF.
    /// </summary>
    public sealed class AchievementsTabViewModel
    {
        // Keys and arg order: WPF MainWindow.AchievementsTab.cs:153/160/196.
        public string LocUnlockedCount => Loc.GetF("label_0_1_achievements_unlocked", Unlocked, Total);
        public string LocRewardCount => Loc.GetF("achv_reward_count", RewardsEarned, RewardsTotal);
        public string LocPatronCount => Loc.GetF("label_0_1_achievements_unlocked", PatronUnlocked, PatronTotal);

        private readonly AchievementEngine _engine;
        internal AchievementsTabViewModel(AchievementEngine engine) => _engine = engine;

        // Free and patron are never summed.
        public int Unlocked => _engine.GetUnlockedCount(exclusive: false);
        public int Total => _engine.GetTotalCount(exclusive: false);
        public int PatronUnlocked => _engine.GetUnlockedCount(exclusive: true);
        public int PatronTotal => _engine.GetTotalCount(exclusive: true);

        // ponytail: reward count needs WardrobeCatalog.AchievementGates (WPF head only); WPF collapses
        // the line when it has no gates, so it is hidden here until the catalog reaches Core.
        public bool RewardCountVisible => false;
        public int RewardsEarned => 0;
        public int RewardsTotal => 0;

        /// <summary>Free users see the locked collection behind an overlay; content stays in
        /// the tree, just covered - same contract as the WPF view.</summary>
        public bool PatronLocked => !CoreEntitlement.HasPremium;
    }
}
