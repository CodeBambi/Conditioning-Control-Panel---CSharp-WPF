using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The Down the Rabbit Hole hub ("the Dollhouse"): opened from the Lab card. A main menu
    /// lands first; behind it are four tabs — BAG (pocket slots + the whole collection as
    /// clickable tiles), THE TOYBOX (unlock/deepen toys, accessories and habits), SETTINGS
    /// (run setup), and THE LOOKING GLASS (the seamstress's bench, mantras and stats).
    ///
    /// PORTED from ConditioningControlPanel/Chaos/ChaosHubWindow.xaml.cs. What changed and why:
    ///
    ///  - Every shelf, tile, mantra, diary entry and bench row is built from the sample data at
    ///    the bottom of this file instead of <c>ChaosMeta</c> / <c>ChaosUpgrades</c> /
    ///    <c>ChaosLifetimeBoons</c> / <c>ChaosBoonPool</c> / <c>ChaosBubbleVariants</c> /
    ///    <c>ChaosArt</c>, which are WPF-head services. The samples deliberately hit EVERY visual
    ///    state each builder branches on (equipped / owned / locked / rank-locked / empty;
    ///    trained-on / trained-off / untrained; seen / unseen / sin; sewn / for-sale / rank-short
    ///    / hazy), so the render proves the builders rather than one branch of them.
    ///    ponytail: needs the Chaos services, wired when they move to Core.
    ///  - The four partials the WPF class spans (Bench / Reveals / Lessons / Debug) are NOT in
    ///    this layer. <c>BuildBench</c> is inlined here because <c>ImprovementsHost</c> must not
    ///    render empty; the reveal framework, the lesson gates and the CCP_CHAOS_DEBUG strip are
    ///    dropped, so every pill and header renders in its revealed state.
    ///  - The whole Skia menu scene (fog, per-frame glint masks, blooms, the crossfading
    ///    flipbook) and the NAudio menu music are gone: no SkiaSharp and no NAudio on this head,
    ///    and a view layer may not add a package. <c>SetupMenuMotion</c>'s breathing/wobble/glow
    ///    loop has no faithful Avalonia twin without re-authoring it, so it is a stub and the
    ///    static frame renders — see the note on that method.
    ///  - <c>Visibility</c> -> <c>IsVisible</c>; <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>;
    ///    <c>MouseLeftButtonDown</c> -> <c>PointerPressed</c>; <c>StateChanged</c> ->
    ///    <c>GetObservable(WindowStateProperty)</c>; <c>ToolTip =</c> -> <c>ToolTip.SetTip</c>;
    ///    <c>FindResource</c> -> <c>this.FindResource</c>; <c>App.Logger</c> -> Serilog's static
    ///    <c>Log</c>; <c>FontWeights.X</c> -> <c>FontWeight.X</c>; <c>Cursors.Hand</c> ->
    ///    <c>new Cursor(StandardCursorType.Hand)</c>.
    ///  - The constructor is parameterless, as in WPF, which also makes it the constructor
    ///    <c>--render-all</c> discovers. It lands on the main menu, exactly as the WPF one does.
    /// </summary>
    public partial class ChaosHubWindow : Window
    {
        private static readonly Random _rng = new();
        private int _waves = 5;

        // ---- palette (the same literals the WPF code-behind builds brushes from) ----
        private static readonly Color Gold = Color.FromRgb(0xFF, 0xD7, 0x00);
        private static readonly Color BoonAccent = Color.FromRgb(0xE8, 0x43, 0x93);
        private static readonly IBrush White = Brushes.White;
        private static readonly IBrush BodyText = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8));
        private static readonly IBrush FlavorText = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8));
        private static readonly IBrush MutedText = new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xC0));
        private static readonly IBrush DimText = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90));
        private static readonly IBrush RowBg = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40));
        private static readonly IBrush CardBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1A, 0x36));
        private static readonly IBrush GoldBrush = new SolidColorBrush(Gold);

        private const double TILE = 96;

        // ---- named parts (WPF got these as generated fields; Avalonia looks them up) ----
        private readonly Grid _dollhouseView, _menuView, _menuLeftCol, _panelImprove, _titleBar, _menuArtClip;
        private readonly Border _menuArtPanel, _menuOptions, _menuHowTo, _dragBar, _herCornerCard, _howToImageBox;
        private readonly StackPanel _menuTitleBar;
        private readonly ToggleButton _tabLoadout, _tabEnhance, _tabRun, _tabImprove, _tabDiary, _tglTesting;
        private readonly ToggleButton _segMedium, _segHard, _segExtreme;
        private readonly Control _panelLoadout, _panelEnhance, _panelRun, _panelDiary;
        private readonly StackPanel _testingBody, _pocketSlotsHost, _boonHostSkills, _boonHostAccessories;
        private readonly StackPanel _habitsHost, _herCornerHost, _improvementsHost, _mantrasHost, _diaryHost;
        private readonly StackPanel _howToBody, _howToDots;
        private readonly Panel _grpDifficulty, _grpLength, _grpMotion, _grpPool;
        private readonly UniformGrid _tilesAccessories, _tilesSkills, _tilesHabits;
        private readonly TextBlock _txtHint, _txtRank, _txtSparks, _txtGold, _menuRank, _menuSparks, _menuGold;
        private readonly TextBlock _txtAccCount, _txtSkillCount, _txtHabitCount, _txtWaves, _menuMuteIcon;
        private readonly TextBlock _stSparks, _stRuns, _stTimeUnder, _stBestScore, _stBestCombo, _stDefused, _stTimeHeld;
        private readonly TextBlock _howToStep, _howToTitle, _hdrDiary;
        private readonly Button _howToBack, _howToNext, _btnMenuStory;
        private readonly CheckBox _chkShake, _chkFlashes, _chkSkiaFx, _chkPinTop, _chkSharedHost, _chkAnnouncer;
        private readonly CheckBox _chkNarrative, _chkBackdrop, _chkTunnel, _chkBoonDraft, _chkCurses, _chkDarters;
        private readonly CheckBox _optFullscreen;
        private readonly Slider _sldShake, _sldEffect, _sldBackdropOpacity;
        private readonly ComboBox _cmbAccKey1, _cmbAccKey2;

        private T Part<T>(string name) where T : Control => this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"ChaosHubWindow: no '{name}' in the XAML");

        public ChaosHubWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _dollhouseView = Part<Grid>("DollhouseView");
            _menuView = Part<Grid>("MenuView");
            _menuLeftCol = Part<Grid>("MenuLeftCol");
            _panelImprove = Part<Grid>("PanelImprove");
            _titleBar = Part<Grid>("TitleBar");
            _menuArtClip = Part<Grid>("MenuArtClip");
            _menuArtPanel = Part<Border>("MenuArtPanel");
            _menuOptions = Part<Border>("MenuOptions");
            _menuHowTo = Part<Border>("MenuHowTo");
            _dragBar = Part<Border>("DragBar");
            _herCornerCard = Part<Border>("HerCornerCard");
            _howToImageBox = Part<Border>("HowToImageBox");
            _menuTitleBar = Part<StackPanel>("MenuTitleBar");
            _tabLoadout = Part<ToggleButton>("TabLoadout");
            _tabEnhance = Part<ToggleButton>("TabEnhance");
            _tabRun = Part<ToggleButton>("TabRun");
            _tabImprove = Part<ToggleButton>("TabImprove");
            _tabDiary = Part<ToggleButton>("TabDiary");
            _tglTesting = Part<ToggleButton>("TglTesting");
            _segMedium = Part<ToggleButton>("SegMedium");
            _segHard = Part<ToggleButton>("SegHard");
            _segExtreme = Part<ToggleButton>("SegExtreme");
            _panelLoadout = Part<ScrollViewer>("PanelLoadout");
            _panelEnhance = Part<ScrollViewer>("PanelEnhance");
            _panelRun = Part<ScrollViewer>("PanelRun");
            _panelDiary = Part<ScrollViewer>("PanelDiary");
            _testingBody = Part<StackPanel>("TestingBody");
            _pocketSlotsHost = Part<StackPanel>("PocketSlotsHost");
            _boonHostSkills = Part<StackPanel>("BoonHostSkills");
            _boonHostAccessories = Part<StackPanel>("BoonHostAccessories");
            _habitsHost = Part<StackPanel>("HabitsHost");
            _herCornerHost = Part<StackPanel>("HerCornerHost");
            _improvementsHost = Part<StackPanel>("ImprovementsHost");
            _mantrasHost = Part<StackPanel>("MantrasHost");
            _diaryHost = Part<StackPanel>("DiaryHost");
            _howToBody = Part<StackPanel>("HowToBody");
            _howToDots = Part<StackPanel>("HowToDots");
            _grpDifficulty = Part<StackPanel>("GrpDifficulty");
            _grpLength = Part<StackPanel>("GrpLength");
            _grpMotion = Part<StackPanel>("GrpMotion");
            _grpPool = Part<WrapPanel>("GrpPool");
            _tilesAccessories = Part<UniformGrid>("TilesAccessories");
            _tilesSkills = Part<UniformGrid>("TilesSkills");
            _tilesHabits = Part<UniformGrid>("TilesHabits");
            _txtHint = Part<TextBlock>("TxtHint");
            _txtRank = Part<TextBlock>("TxtRank");
            _txtSparks = Part<TextBlock>("TxtSparks");
            _txtGold = Part<TextBlock>("TxtGold");
            _menuRank = Part<TextBlock>("MenuRank");
            _menuSparks = Part<TextBlock>("MenuSparks");
            _menuGold = Part<TextBlock>("MenuGold");
            _txtAccCount = Part<TextBlock>("TxtAccCount");
            _txtSkillCount = Part<TextBlock>("TxtSkillCount");
            _txtHabitCount = Part<TextBlock>("TxtHabitCount");
            _txtWaves = Part<TextBlock>("TxtWaves");
            _menuMuteIcon = Part<TextBlock>("MenuMuteIcon");
            _stSparks = Part<TextBlock>("StSparks");
            _stRuns = Part<TextBlock>("StRuns");
            _stTimeUnder = Part<TextBlock>("StTimeUnder");
            _stBestScore = Part<TextBlock>("StBestScore");
            _stBestCombo = Part<TextBlock>("StBestCombo");
            _stDefused = Part<TextBlock>("StDefused");
            _stTimeHeld = Part<TextBlock>("StTimeHeld");
            _howToStep = Part<TextBlock>("HowToStep");
            _howToTitle = Part<TextBlock>("HowToTitle");
            _hdrDiary = Part<TextBlock>("HdrDiary");
            _howToBack = Part<Button>("HowToBack");
            _howToNext = Part<Button>("HowToNext");
            _btnMenuStory = Part<Button>("BtnMenuStory");
            _chkShake = Part<CheckBox>("ChkShake");
            _chkFlashes = Part<CheckBox>("ChkFlashes");
            _chkSkiaFx = Part<CheckBox>("ChkSkiaFx");
            _chkPinTop = Part<CheckBox>("ChkPinTop");
            _chkSharedHost = Part<CheckBox>("ChkSharedHost");
            _chkAnnouncer = Part<CheckBox>("ChkAnnouncer");
            _chkNarrative = Part<CheckBox>("ChkNarrative");
            _chkBackdrop = Part<CheckBox>("ChkBackdrop");
            _chkTunnel = Part<CheckBox>("ChkTunnel");
            _chkBoonDraft = Part<CheckBox>("ChkBoonDraft");
            _chkCurses = Part<CheckBox>("ChkCurses");
            _chkDarters = Part<CheckBox>("ChkDarters");
            _optFullscreen = Part<CheckBox>("OptFullscreen");
            _sldShake = Part<Slider>("SldShake");
            _sldEffect = Part<Slider>("SldEffect");
            _sldBackdropOpacity = Part<Slider>("SldBackdropOpacity");
            _cmbAccKey1 = Part<ComboBox>("CmbAccKey1");
            _cmbAccKey2 = Part<ComboBox>("CmbAccKey2");

            // The three drag strips. WPF hooked MouseLeftButtonDown -> DragMove().
            foreach (var strip in new Control[] { _titleBar, _menuTitleBar, _dragBar })
                strip.PointerPressed += DragWindow;
            _menuArtClip.PointerPressed += MenuArt_Click;
            _menuHowTo.PointerPressed += HowTo_Backdrop_Click;
            _hdrDiary.PointerPressed += Diary_PopOut;

            // WPF: StateChanged += OnHubStateChanged. Avalonia has no such event.
            this.GetObservable(WindowStateProperty).Subscribe(new AnonymousObserver<WindowState>(OnHubStateChanged));

            LoadFromSettings();
            BuildHabits();
            BuildLifetimeBoons();
            BuildLoadoutTiles();
            BuildBench();
            BuildHerCorner();
            BuildMantras();
            BuildDiary();
            RefreshTopBar();
            RefreshStats();
            ApplyUnlocks();
            ShowTab("loadout");
            _btnMenuStory.IsEnabled = StoryModeEnabled;   // greyed until story ships
            ShowMenuView();      // the main menu is the landing view; the dollhouse waits behind it
            SetupMenuMotion();   // ponytail stub — see the method
        }

        /// <summary>Stand-in for <c>Services.Chaos.ChaosModeService.StoryModeEnabled</c>, which is
        /// hard-false in the head until story content ships.
        /// ponytail: needs ChaosModeService, wired when it moves to Core.</summary>
        private const bool StoryModeEnabled = false;

        /// <summary>Avalonia's <c>IObservable.Subscribe</c> wants an observer; this is the
        /// smallest one that calls back. (Avalonia's own ReactiveUI helpers are not referenced.)</summary>
        private sealed class AnonymousObserver<T> : IObserver<T>
        {
            private readonly Action<T> _onNext;
            public AnonymousObserver(Action<T> onNext) => _onNext = onNext;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _onNext(value);
        }

        // ============================ tabs / gating ============================

        private void ApplyUnlocks()
        {
            // All four tabs render from the first visit (the drops cost is the real gate, and
            // run setup must be reachable before run #1).
            _tabLoadout.IsEnabled = true;
            _tabEnhance.IsEnabled = true;
            _tabRun.IsEnabled = true;
            _tabImprove.IsEnabled = true;
            // The Diary tab only appears once there's something in it (met a bubble down there).
            _tabDiary.IsVisible = DiaryUnlocked;
        }

        private void Tab_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton { IsEnabled: true } tb) ShowTab(tb.Tag?.ToString() ?? "loadout");
            else if (sender is ToggleButton tb2) tb2.IsChecked = false;
        }

        private void ShowTab(string tag)
        {
            // The old Habits tab folded into the Toybox; external callers may still ask for it.
            if (tag == "habits") tag = "enhance";
            if (tag == "improve" && !_tabImprove.IsEnabled) tag = "loadout";
            // The diary lives behind its own reveal — nothing met yet, nothing to read.
            if (tag == "diary" && !DiaryUnlocked) tag = "loadout";

            _panelLoadout.IsVisible = tag == "loadout";
            _panelEnhance.IsVisible = tag == "enhance";
            _panelRun.IsVisible     = tag == "run";
            _panelImprove.IsVisible = tag == "improve";
            _panelDiary.IsVisible   = tag == "diary";

            _tabLoadout.IsChecked = tag == "loadout";
            _tabEnhance.IsChecked = tag == "enhance";
            _tabRun.IsChecked     = tag == "run";
            _tabImprove.IsChecked = tag == "improve";
            _tabDiary.IsChecked   = tag == "diary";

            _txtHint.Text = tag switch
            {
                "loadout" => "click a tile to slip it into a pocket. + takes you where it's sold.",
                "enhance" => "spend your emotes. deepen what you like.",
                "run"     => "dress up the fall, then FALL IN.",
                "improve" => "the bench, the mantras, how far you've fallen.",
                "diary"   => "everything you've met down there. click an entry to pop it out.",
                _ => "",
            };
        }

        /// <summary>WPF: <c>RevealService.IsUnlocked(RevealIds.Diary)</c>. The sample save has met
        /// things down there, so the tab is offered.
        /// ponytail: needs RevealService, wired when it moves to Core.</summary>
        private static bool DiaryUnlocked => true;

        /// <summary>Settings tab: fold the dev/test knobs (bubble pool, mantra toggles, loops)
        /// in and out. Collapsed every open — the casual read stays short.</summary>
        private void TestingToggle_Click(object? sender, RoutedEventArgs e)
        {
            bool open = _tglTesting.IsChecked == true;
            _testingBody.IsVisible = open;
            _tglTesting.Content = open ? "🧪 testing options ▾" : "🧪 testing options ▸";
        }

        /// <summary>A + tile wants to take the player shopping.</summary>
        private void JumpToTab(string tag) => ShowTab(tag);

        /// <summary>External navigation (the loadout sidebar's empty "+" tiles): switch tab and
        /// bring the Dollhouse forward.</summary>
        public void NavigateTo(string tag)
        {
            ShowTab(tag);
            try { Activate(); } catch { /* not shown yet */ }
        }

        // ============================ top bar / stats ============================

        private int _shownSparks = -1, _shownGold = -1;
        private readonly Dictionary<TextBlock, DispatcherTimer> _balanceAnims = new();

        private void RefreshTopBar()
        {
            AnimateBalance(_txtSparks, _shownSparks, Sample.Sparks);
            AnimateBalance(_txtGold, _shownGold, Sample.Gold);
            _shownSparks = Sample.Sparks;
            _shownGold = Sample.Gold;
            _txtRank.Text = Sample.Rank;
            // mirror onto the main-menu chips (plain text; the animated balance lives on the top bar)
            _menuRank.Text = Sample.Rank;
            _menuSparks.Text = Sample.Sparks.ToString("N0");
            _menuGold.Text = Sample.Gold.ToString("N0");
            RefreshTabBadges();   // every balance change re-counts what the shelves can sell
        }

        /// <summary>Signposting on the shop tabs: a small count of what's buyable RIGHT NOW
        /// (drops purchases on the Toybox, gold purchases on the Looking Glass) — the answer
        /// to "is there anything for me in there?" without scanning four shelves.</summary>
        private void RefreshTabBadges()
        {
            SetTabBadge(_tabEnhance, "the Toybox", CountAffordableToybox());
            SetTabBadge(_tabImprove, "the Looking Glass", _tabImprove.IsEnabled ? CountAffordableBench() : 0);
        }

        private static void SetTabBadge(ToggleButton tab, string label, int count)
        {
            if (count <= 0)
            {
                tab.Content = label;
                ToolTip.SetTip(tab, null);
                return;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new Border
            {
                Background = new SolidColorBrush(BoonAccent),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = count.ToString(), Foreground = White, FontSize = 10.5, FontWeight = FontWeight.Bold },
            });
            tab.Content = row;
            ToolTip.SetTip(tab, count == 1 ? "1 thing you can afford right now" : $"{count} things you can afford right now");
        }

        /// <summary>Drops purchases buyable this instant: untrained habits + boon unlocks +
        /// boon level-ups — the same gates the shelf buttons enforce.</summary>
        private static int CountAffordableToybox()
        {
            int n = Sample.Habits.Count(u => !u.Owned && Sample.Sparks >= u.Cost);
            foreach (var b in Sample.Boons)
            {
                if (b.RankLocked) continue;
                if (b.Level <= 0) { if (Sample.Sparks >= b.UnlockCost) n++; }
                else if (b.Level < b.MaxLevel && Sample.Sparks >= b.UpgradeCost) n++;
            }
            return n;
        }

        /// <summary>Gold purchases buyable this instant at her bench (rank + reveal gated).</summary>
        private static int CountAffordableBench() =>
            Sample.Bench.Count(i => !i.Owned && !i.RankShort && !i.Hazy && Sample.Gold >= i.Cost);

        /// <summary>Roll a top-bar balance from its last shown value to the new one (~500ms) so
        /// spending visibly *costs* — first paint just snaps. WPF layered a soft tick cue under
        /// it; ChaosSfx is a head service, so the sound is dropped.</summary>
        private void AnimateBalance(TextBlock tb, int from, int to)
        {
            if (_balanceAnims.TryGetValue(tb, out var old))
            {
                old.Stop();
                _balanceAnims.Remove(tb);
            }
            if (from < 0 || from == to || !IsLoaded)
            {
                tb.Text = to.ToString("N0");
                return;
            }

            const int DURATION_MS = 500, FRAME_MS = 33;
            int elapsed = 0;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
            _balanceAnims[tb] = timer;
            timer.Tick += (_, _) =>
            {
                elapsed += FRAME_MS;
                if (elapsed >= DURATION_MS)
                {
                    timer.Stop();
                    _balanceAnims.Remove(tb);
                    tb.Text = to.ToString("N0");
                    return;
                }
                double eased = 1 - Math.Pow(1 - elapsed / (double)DURATION_MS, 3);
                tb.Text = ((int)Math.Round(from + (to - from) * eased)).ToString("N0");
            };
            timer.Start();
        }

        private void RefreshStats()
        {
            _stSparks.Text = Sample.Sparks.ToString("N0");
            _stRuns.Text = Sample.RunsCompleted.ToString("N0");
            _stTimeUnder.Text = FormatPlaytime(Sample.TotalRunSeconds);
            _stBestScore.Text = Sample.BestScore.ToString("N0");
            _stBestCombo.Text = Sample.BestCombo.ToString("N0");
            _stDefused.Text = Sample.TotalDefused.ToString("N0");
            _stTimeHeld.Text = FormatPlaytime(Sample.TotalChannelSeconds);
        }

        private static string FormatPlaytime(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
        }

        // ============================ habits (the Toybox) ============================

        private static Color BranchColor(string branch) => branch switch
        {
            "Control" => Color.FromRgb(0x49, 0xB6, 0xE8),
            "Greed"   => Color.FromRgb(0xE8, 0xB4, 0x43),
            "Depth"   => Color.FromRgb(0x8B, 0x5C, 0xF6),
            _         => Color.FromRgb(0xE8, 0x43, 0x93)
        };

        private static string BranchLabel(string branch) => branch switch
        {
            "Control" => "RESTRAINT",
            "Greed"   => "CRAVING",
            _         => "DEPTH",
        };

        /// <summary>One grouped list of the trainable passives — untrained rows sell, trained
        /// rows toggle on/off.</summary>
        private void BuildHabits()
        {
            _habitsHost.Children.Clear();
            foreach (var u in Sample.Habits) _habitsHost.Children.Add(BuildUpgradeRow(u));
            foreach (var b in Sample.Charms) _habitsHost.Children.Add(BuildLifetimeBoonRow(b, habitVoice: true));
        }

        /// <summary>One habit card, in the same dress as the boon rows (72px art, big card,
        /// gold edge while switched on) — the Habits list is one cohesive shelf.</summary>
        private Border BuildUpgradeRow(SampleHabit u)
        {
            bool owned = u.Owned;
            bool afford = Sample.Sparks >= u.Cost;
            bool on = owned && u.On;
            var accent = BranchColor(u.Branch);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            // ---- icon: a branch-tinted square with the glyph (ChaosArt sprites are head-side) ----
            var icon = GlyphIcon(u.Glyph, 72, 14, accent, owned ? 1.0 : 0.5);
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            // ---- middle: name + desc + flavor + branch tag ----
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock { Text = u.Name, Foreground = White, FontSize = 14, FontWeight = FontWeight.SemiBold });
            mid.Children.Add(new TextBlock { Text = u.Desc, Foreground = BodyText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (!string.IsNullOrEmpty(u.Flavor))
                mid.Children.Add(new TextBlock
                {
                    Text = u.Flavor, FontStyle = FontStyle.Italic, FontSize = 11,
                    Foreground = FlavorText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                });
            mid.Children.Add(new TextBlock
            {
                Text = BranchLabel(u.Branch).ToLowerInvariant(),
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)),
                FontSize = 10.5, FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 6, 0, 0)
            });
            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            // ---- right: ON badge + on/off toggle, or the Train buy button ----
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Width = 132 };
            if (owned)
            {
                if (on) right.Children.Add(StateBadge("ON ✓"));
                var toggle = StepperButton(on ? "switch off" : "switch on", u.Id);
                ToolTip.SetTip(toggle, on ? "switched on — shapes your next descent." : "switched off — sits out the descent.");
                toggle.Click += HabitToggle_Click;
                right.Children.Add(toggle);
            }
            else
            {
                right.Children.Add(BuyButton($"Train  ✦{u.Cost}", u.Id, afford, Buy_Click));
            }
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            return ShelfCard(grid, on, owned, accent);
        }

        /// <summary>The card every shelf row sits in: gold edge while switched on, pink otherwise,
        /// dimmed while still locked.</summary>
        private static Border ShelfCard(Control body, bool on, bool owned, Color accent) => new()
        {
            Child = body,
            Background = CardBg,
            BorderBrush = on
                ? new SolidColorBrush(Color.FromArgb(180, Gold.R, Gold.G, Gold.B))
                : new SolidColorBrush(Color.FromArgb(owned ? (byte)90 : (byte)40, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(on ? 3 : 2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };

        private static TextBlock StateBadge(string text) => new()
        {
            Text = text,
            Foreground = GoldBrush,
            FontSize = 11, FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 4)
        };

        /// <summary>WPF resolved real art through ChaosArt and fell back to this tinted glyph
        /// square. Only the fallback exists on this head.
        /// ponytail: needs ChaosArt, wired when it moves to Core.</summary>
        private static Border GlyphIcon(string glyph, double size, double radius, Color accent, double opacity) => new()
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 12, 0),
            Opacity = opacity,
            Child = new TextBlock
            {
                Text = glyph, FontSize = size >= 60 ? 34 : 19, Foreground = White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };

        // ponytail: needs ChaosMeta.TryPurchase + the unlock-card queue, wired when they move to
        // Core. Until then a buy is a no-op that says so rather than silently spending nothing.
        private void Buy_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ChaosHub: train {Id} requested; no ChaosMeta on this head yet", (sender as Button)?.Tag);

        private void HabitToggle_Click(object? sender, RoutedEventArgs e)
        {
            var id = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrEmpty(id)) return;
            Sample.ToggleHabit(id!);
            BuildHabits();
            BuildLoadoutTiles();
        }

        // ===================== lifetime boons (toys / accessories / charms) =====================

        private void BuildLifetimeBoons()
        {
            BuildBoonShelf(_boonHostSkills, "Skill");
            BuildBoonShelf(_boonHostAccessories, "Accessory");
            // Utility charms train on the Habits shelf (BuildHabits) — they're habits in spirit.
        }

        private void BuildBoonShelf(Panel host, string category)
        {
            host.Children.Clear();
            var boons = Sample.Boons.Where(b => b.Category == category).ToList();
            if (boons.Count == 0)
            {
                host.Children.Add(new Border
                {
                    Theme = this.FindResource("CardStyle") as ControlTheme,
                    Child = new TextBlock
                    {
                        Text = "something is being prepared for you.",
                        Foreground = MutedText, FontSize = 12, TextWrapping = TextWrapping.Wrap
                    }
                });
                return;
            }
            foreach (var b in boons) host.Children.Add(BuildLifetimeBoonRow(b, habitVoice: false));
        }

        private Border BuildLifetimeBoonRow(SampleBoon b, bool habitVoice)
        {
            int level = b.Level;
            bool unlocked = level >= 1;
            bool active = b.Active;
            bool maxed = unlocked && level >= b.MaxLevel;
            // Rank-locked = below your depth tier: a MYSTERY. Hide the art, name and what it does —
            // only the rank gate shows, so the deeper toys stay a reveal instead of a spoiled preview.
            bool rankLocked = b.RankLocked;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var icon = GlyphIcon(rankLocked ? "?" : b.Glyph, 72, 14, BoonAccent,
                                 unlocked ? 1.0 : (rankLocked ? 0.6 : 0.5));
            ToolTip.SetTip(icon, rankLocked ? RankLockedTip : b.Desc);
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            // ---- middle: name + desc + level pips + value ----
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock { Text = rankLocked ? "? ? ?" : b.Name, Foreground = White, FontSize = 14, FontWeight = FontWeight.SemiBold });
            if (rankLocked)
            {
                // No desc, no flavor — only the gate. The reveal is the reward for sinking deeper.
                mid.Children.Add(new TextBlock
                {
                    Text = RankLockedTip + " " + RankSpecifics(b.RankFloor),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x80, 0xA8)), FontStyle = FontStyle.Italic,
                    FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0)
                });
            }
            else
            {
                mid.Children.Add(new TextBlock { Text = b.Desc, Foreground = BodyText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
                if (!string.IsNullOrEmpty(b.Flavor))
                    mid.Children.Add(new TextBlock
                    {
                        Text = b.Flavor, FontStyle = FontStyle.Italic, FontSize = 11,
                        Foreground = FlavorText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                    });
            }

            // Active-use toys carry their trigger: the keybind (when equipped) or a generic hint.
            if (b.IsActiveUse && !rankLocked)
            {
                string key = _cmbAccKey1.SelectedItem as string ?? "Q";
                string useHint = b.UseCooldownSec > 0 ? $"{b.UseCooldownSec:0}s cooldown" : "limited uses";
                mid.Children.Add(new TextBlock
                {
                    Text = active ? $"ACTIVE · fires on {key} mid-descent · {useHint}"
                                  : $"ACTIVE · fires on your toy key mid-descent · {useHint}",
                    Foreground = GoldBrush, FontSize = 10.5, FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            // Capstone teaser: dim until the final rank is bought, gold once it's live.
            if (!string.IsNullOrEmpty(b.CapstoneDesc) && !rankLocked)
                mid.Children.Add(new TextBlock
                {
                    Text = "max: " + b.CapstoneDesc,
                    Foreground = maxed ? GoldBrush : new SolidColorBrush(Color.FromArgb(0x90, 0x8A, 0x86, 0xB8)),
                    FontSize = 11,
                    FontStyle = maxed ? FontStyle.Normal : FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                });

            var pips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 1; i <= b.MaxLevel; i++)
                pips.Children.Add(new TextBlock
                {
                    Text = i <= level ? "●" : "○",
                    Foreground = new SolidColorBrush(i <= level ? BoonAccent : Color.FromArgb(0x66, 0xB8, 0xB8, 0xD0)),
                    FontSize = 13, Margin = new Thickness(0, 0, 3, 0)
                });
            pips.Children.Add(new TextBlock
            {
                Text = "   " + (unlocked ? b.ValueLabel : "locked"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
                FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center
            });
            mid.Children.Add(pips);
            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            // ---- right: on/off toggle + unlock/upgrade ----
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Width = 132 };

            if (unlocked)
            {
                // Equip semantics instead of an ambiguous ON/OFF: the badge shows the STATE,
                // the button is always the ACTION. Pockets cap Toys/Accessories at 2 each.
                if (active) right.Children.Add(StateBadge(habitVoice ? "ON ✓" : "EQUIPPED ✓"));
                bool pocketFree = active || Sample.HasFreePocket(b.Category);
                var equip = StepperButton(
                    active ? (habitVoice ? "switch off" : "Unequip")
                           : pocketFree ? (habitVoice ? "switch on" : "Equip") : "pockets full",
                    b.Id);
                equip.IsEnabled = pocketFree;
                equip.Margin = new Thickness(0, 0, 0, 8);
                equip.Click += BoonEquip_Click;
                right.Children.Add(equip);
            }

            if (!unlocked)
            {
                if (rankLocked)
                {
                    // Mystery: hide the cost too — show only the depth gate.
                    var held = BuyButton($"🔒 {b.RankFloor}", b.Id, false, BoonUnlock_Click);
                    ToolTip.SetTip(held, RankLockedTip + "\n" + RankSpecifics(b.RankFloor));
                    right.Children.Add(held);
                }
                else
                {
                    right.Children.Add(BuyButton($"Unlock  ✦{b.UnlockCost}", b.Id, Sample.Sparks >= b.UnlockCost, BoonUnlock_Click));
                }
            }
            else if (maxed)
                right.Children.Add(new TextBlock { Text = "MAX  ✓", Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)), FontSize = 13, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Right });
            else
                right.Children.Add(BuyButton($"deepen  ✦{b.UpgradeCost}", b.Id, Sample.Sparks >= b.UpgradeCost, BoonUpgrade_Click));

            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            return ShelfCard(grid, active, unlocked, BoonAccent);
        }

        private Button BuyButton(string text, string id, bool afford, EventHandler<RoutedEventArgs> onClick)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = text },   // TextBlock, not a raw string: Avalonia
                Tag = id,                                  // reads "_" in Content as an access key
                Padding = new Thickness(20, 10, 20, 10),
                HorizontalAlignment = HorizontalAlignment.Right,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = afford ? new SolidColorBrush(BoonAccent) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = afford ? White : new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0)),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Cursor = new Cursor(afford ? StandardCursorType.Hand : StandardCursorType.Arrow),
                IsEnabled = afford,
                CornerRadius = new CornerRadius(11),
            };
            btn.Click += onClick;
            return btn;
        }

        /// <summary>WPF built these from the keyed "Stepper" style and then rounded them with
        /// <c>Pillify</c>. Here the theme is applied and the radius set directly.</summary>
        private Button StepperButton(string text, string id) => new()
        {
            Theme = this.FindResource("Stepper") as ControlTheme,
            Content = new TextBlock { Text = text },
            Tag = id,
            Width = double.NaN, Height = double.NaN, MinWidth = 112,
            FontSize = 12.5,
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(11),
        };

        private void AccKey_Changed(object? sender, SelectionChangedEventArgs e)
        {
            // ponytail: needs App.Settings to persist the bind, wired when it moves to Core.
            // The shelf rows print the chosen key, so they are rebuilt either way.
            if (IsLoaded) BuildLifetimeBoons();
        }

        private void BoonEquip_Click(object? sender, RoutedEventArgs e)
        {
            var id = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrEmpty(id)) return;
            Sample.ToggleBoon(id!);
            AfterBoonChange();
        }

        // ponytail: needs ChaosMeta.TryUnlockBoon / TryUpgradeBoon, wired when they move to Core.
        private void BoonUnlock_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ChaosHub: unlock {Id} requested; no ChaosMeta on this head yet", (sender as Button)?.Tag);

        private void BoonUpgrade_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ChaosHub: deepen {Id} requested; no ChaosMeta on this head yet", (sender as Button)?.Tag);

        private void AfterBoonChange()
        {
            BuildLifetimeBoons();
            BuildHabits();
            BuildLoadoutTiles();
            RefreshTopBar();
        }

        /// <summary>The loadout sidebar pushes unequips back in through here.</summary>
        public void RefreshAfterExternalLoadoutChange() => AfterBoonChange();

        // ============================ the BAG (glance page) ============================

        private enum TileState { Equipped, Owned, Locked, Empty }

        /// <summary>The whole glance page: pocket slots + accessory/toy/habit tile grids.</summary>
        private void BuildLoadoutTiles()
        {
            // ---- pocket slots (big tiles, one group per category; unsewn categories don't render) ----
            _pocketSlotsHost.Children.Clear();
            var toyGroup = PocketGroup("TOY", "Skill");
            if (toyGroup != null) _pocketSlotsHost.Children.Add(toyGroup);
            var accGroup = PocketGroup("ACCESSORY", "Accessory");
            if (accGroup != null) _pocketSlotsHost.Children.Add(accGroup);
            if (_pocketSlotsHost.Children.Count == 0)
                _pocketSlotsHost.Children.Add(new TextBlock
                {
                    Text = "no pockets sewn yet.",
                    Foreground = DimText,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 2),
                });

            // ---- collections ----
            FillCategoryTiles(_tilesAccessories, "Accessory", padTo: 8);
            FillCategoryTiles(_tilesSkills, "Skill", padTo: 8);
            _txtAccCount.Text = $"{Sample.EquippedCountIn("Accessory")}/{Sample.SlotsFor("Accessory")} equipped";
            _txtSkillCount.Text = $"{Sample.EquippedCountIn("Skill")}/{Sample.SlotsFor("Skill")} equipped";

            // ---- habits 4x4 (trained = on/off toggle; click an untrained one to go train it) ----
            _tilesHabits.Children.Clear();
            int trained = 0, switchedOn = 0;
            foreach (var u in Sample.Habits)
            {
                string id = u.Id;
                bool owned = u.Owned, on = owned && u.On;
                if (owned) trained++;
                if (on) switchedOn++;
                Action onClick = owned
                    ? () => { Sample.ToggleHabit(id); BuildHabits(); BuildLoadoutTiles(); }
                    : () => JumpToTab("enhance");
                _tilesHabits.Children.Add(LoadoutTile(u.Glyph, u.Name, u.Desc,
                    on ? "click to switch off" : owned ? "click to switch on" : $"train for ✦{u.Cost} in the Toybox",
                    BranchColor(u.Branch),
                    on ? TileState.Equipped : owned ? TileState.Owned : TileState.Locked,
                    onClick,
                    cornerBadge: on ? "✓" : null,
                    flavor: u.Flavor));
            }
            // Charms live with the habits: leveled, always-on once worn, toggled like a habit.
            foreach (var b in Sample.Charms)
            {
                string bid = b.Id;
                bool unlocked = b.Level >= 1, active = b.Active, charmRankLocked = b.RankLocked;
                if (unlocked) trained++;
                if (active) switchedOn++;
                Action onClick = unlocked
                    ? () => { Sample.ToggleBoon(bid); BuildHabits(); BuildLoadoutTiles(); }
                    : () => JumpToTab("enhance");
                _tilesHabits.Children.Add(LoadoutTile(charmRankLocked ? "?" : b.Glyph,
                    charmRankLocked ? "? ? ?" : unlocked ? $"{b.Name} · L{b.Level}" : b.Name,
                    charmRankLocked ? RankLockedTip : b.Desc,
                    active ? "click to switch off" : unlocked ? "click to switch on"
                        : charmRankLocked ? RankSpecifics(b.RankFloor) : $"unlock for ✦{b.UnlockCost} in the Toybox",
                    BoonAccent,
                    active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked,
                    onClick,
                    cornerBadge: active ? "✓" : null,
                    flavor: charmRankLocked ? null : b.Flavor));
            }
            int shown = Sample.Habits.Count + Sample.Charms.Count;
            int target = Math.Max(16, ((shown + 3) / 4) * 4);
            for (int i = shown; i < target; i++)
                _tilesHabits.Children.Add(LoadoutTile("+", "a habit not yet formed",
                    "more training arrives in a later fitting.", null,
                    Color.FromRgb(0xB8, 0xB8, 0xD0), TileState.Empty, null));
            _txtHabitCount.Text = $"{switchedOn} on · {trained}/{shown} trained";
        }

        /// <summary>One labelled pocket column: equipped boon as a big gold tile, plus + tiles for
        /// free slots. Null when the category has no pockets sewn (and nothing stale equipped).</summary>
        private Control? PocketGroup(string label, string category)
        {
            if (Sample.SlotsFor(category) <= 0 && Sample.EquippedCountIn(category) == 0) return null;
            var col = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
            col.Children.Add(new TextBlock
            {
                Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0xB8)),
                FontFamily = new FontFamily("Consolas, Courier New"), FontWeight = FontWeight.Bold, FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var equipped = Sample.Boons.Where(b => b.Category == category && b.Active).ToList();
            foreach (var b in equipped)
            {
                string id = b.Id;
                var cell = LoadoutTile(b.Glyph, $"{b.Name} · L{b.Level}", b.Desc,
                    "click to unequip", BoonAccent, TileState.Equipped,
                    () => { Sample.ToggleBoon(id); AfterBoonChange(); },
                    size: 114, flavor: b.Flavor);
                cell.Margin = new Thickness(0, 0, 24, 0);
                row.Children.Add(cell);
            }
            for (int i = equipped.Count; i < Sample.SlotsFor(category); i++)
            {
                var cell = LoadoutTile("+", $"empty {label.ToLowerInvariant()} pocket",
                    "pick one from the shelf below, or go shopping in the Toybox.", null,
                    BoonAccent, TileState.Empty, () => JumpToTab("enhance"), size: 114, caption: "empty");
                cell.Margin = new Thickness(0, 0, 24, 0);
                row.Children.Add(cell);
            }
            col.Children.Add(row);
            return col;
        }

        /// <summary>A category's collection as tiles: equipped gold, owned pink (click = equip,
        /// swapping out the current occupant), locked dim (click = go shopping), padded with
        /// placeholder + tiles to full rows.</summary>
        private void FillCategoryTiles(Panel host, string category, int padTo)
        {
            host.Children.Clear();
            var boons = Sample.Boons.Where(b => b.Category == category).ToList();
            foreach (var b in boons)
            {
                string id = b.Id;
                bool unlocked = b.Level >= 1, active = b.Active, rankLocked = b.RankLocked;
                var state = active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked;
                Action onClick = active ? () => { Sample.ToggleBoon(id); AfterBoonChange(); }
                    : unlocked ? () => { Sample.EquipSwapping(id, category); AfterBoonChange(); }
                    : () => JumpToTab("enhance");
                // Rank-locked → mystery: "???" everywhere, the depth gate instead of a price.
                string extra = active ? "click to unequip"
                    : unlocked ? "click to equip"
                    : rankLocked ? RankSpecifics(b.RankFloor)
                    : $"unlock for ✦{b.UnlockCost} in the Toybox";
                host.Children.Add(LoadoutTile(rankLocked ? "?" : b.Glyph,
                    rankLocked ? "? ? ?" : unlocked ? $"{b.Name} · L{b.Level}" : b.Name,
                    rankLocked ? RankLockedTip : b.Desc, extra, BoonAccent, state, onClick,
                    cornerBadge: active ? "★" : null,
                    flavor: rankLocked ? null : b.Flavor));
            }
            int target = Math.Max(padTo, ((boons.Count + 3) / 4) * 4);
            for (int i = boons.Count; i < target; i++)
                host.Children.Add(LoadoutTile("+",
                    category == "Skill" ? "another toy is being stitched" : "another accessory is being stitched",
                    "it'll hang here when it's ready.", null,
                    Color.FromRgb(0xB8, 0xB8, 0xD0), TileState.Empty, null));
        }

        private Control LoadoutTile(string glyph, string title, string? desc, string? extra, Color accent,
                                    TileState state, Action? onClick, string? cornerBadge = null,
                                    double size = TILE, string? caption = null, string? flavor = null)
        {
            // Rounded clip so the square art can't poke past the ring corners (Border doesn't
            // clip children to its CornerRadius; the tile is fixed-size so a geometry works).
            var content = new Grid
            {
                Clip = new RectangleGeometry(new Rect(0, 0, size, size)) { RadiusX = 12, RadiusY = 12 }
            };
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontSize = size >= 114 ? 46 : 36,
                Foreground = White,
                Opacity = state switch { TileState.Locked => 0.35, TileState.Empty => 0.4, _ => 1.0 },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            var ringBrush = state switch
            {
                TileState.Equipped => new SolidColorBrush(Color.FromArgb(200, Gold.R, Gold.G, Gold.B)),
                TileState.Owned    => new SolidColorBrush(accent),
                TileState.Locked   => new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)),
                _                  => new SolidColorBrush(Color.FromArgb(0x50, 0xB8, 0xB8, 0xD0)),
            };
            // The ring rides ABOVE the art and ABOVE the badge — always the tile's topmost layer.
            content.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = ringBrush,
                BorderThickness = new Thickness(state == TileState.Equipped ? 4 : 3.5),
                IsHitTestVisible = false,
            });
            if (cornerBadge != null)
                content.Children.Add(new TextBlock
                {
                    Text = cornerBadge, FontSize = 15, FontWeight = FontWeight.Bold,
                    Foreground = GoldBrush,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 6, 3)
                });

            var tile = new Border
            {
                Width = size, Height = size,
                CornerRadius = new CornerRadius(12),
                Child = content,
                ClipToBounds = true,
                Background = state switch
                {
                    TileState.Equipped => new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)),
                    TileState.Owned    => new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B)),
                    TileState.Locked   => new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
                    _                  => Brushes.Transparent,
                },
                BorderBrush = ringBrush,
                BorderThickness = new Thickness(0),
            };

            // Name under the tile — locked/placeholder tiles keep their mystery.
            caption ??= state is TileState.Locked or TileState.Empty ? "???" : title.Split(" · ")[0];
            var label = new TextBlock
            {
                Text = caption,
                FontSize = 12,
                FontWeight = state == TileState.Equipped ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = state switch
                {
                    TileState.Equipped => GoldBrush,
                    TileState.Owned    => White,
                    _                  => new SolidColorBrush(Color.FromArgb(0x80, 0xB8, 0xB8, 0xD0)),
                },
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = size + 36,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var cell = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 22),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = new Cursor(onClick != null ? StandardCursorType.Hand : StandardCursorType.Arrow),
                Background = Brushes.Transparent,
            };
            cell.Children.Add(tile);
            cell.Children.Add(label);
            if (onClick != null)
                cell.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) onClick();
                };
            // WPF attached a rich ChaosTips card here; Avalonia gets the same words as a tooltip.
            ToolTip.SetTip(cell, string.Join("\n", new[] { title, desc, flavor, extra }.Where(s => !string.IsNullOrEmpty(s))));
            return cell;
        }

        // ============================ the seamstress's bench ============================
        // WPF keeps this in ChaosHubWindow.Bench.cs. That partial is not part of this layer, so
        // the row builder is inlined here rather than splitting the port across two files.

        private void BuildBench()
        {
            _improvementsHost.Children.Clear();
            _improvementsHost.Children.Add(GoldBalanceLine());
            foreach (var item in Sample.Bench) _improvementsHost.Children.Add(BenchRow(item));
            foreach (var name in Sample.ReservedRows) _improvementsHost.Children.Add(HazyRow(name, WallTip));
        }

        /// <summary>Her corner inside the Toybox: just the two first-pocket rows, sold early.</summary>
        private void BuildHerCorner()
        {
            _herCornerHost.Children.Clear();
            _herCornerHost.Children.Add(GoldBalanceLine());
            foreach (var item in Sample.Bench.Take(2)) _herCornerHost.Children.Add(BenchRow(item));
            _herCornerCard.IsVisible = true;   // WPF gates this on RevealIds.HerCorner
        }

        private const string WallTip = "not yet. she hasn't decided what it costs.";
        private const string DeeperTip = "she'll sell this to someone deeper.";

        private static TextBlock GoldBalanceLine() => new()
        {
            Text = $"you're carrying 🪙 {Sample.Gold:N0}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x43)),
            FontSize = 11, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        /// <summary>One bench row in its current state: hazy (reveal-gated), rank-locked,
        /// owned, or for sale.</summary>
        private Border BenchRow(SampleBench item)
        {
            if (item.Hazy) return HazyRow("???", WallTip);

            var goldColor = Color.FromRgb(0xE8, 0xB4, 0x43);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var glyph = new TextBlock
            {
                Text = item.Glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0), Opacity = item.Owned ? 1.0 : 0.7,
            };
            Grid.SetColumn(glyph, 0);
            grid.Children.Add(glyph);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock
            {
                Text = item.Label,
                Foreground = item.Owned ? White : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xE0)),
                FontSize = 12, FontWeight = FontWeight.SemiBold,
            });
            mid.Children.Add(new TextBlock
            {
                Text = item.Line,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8)),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            Control right;
            if (item.Owned)
                right = new TextBlock
                {
                    Text = "sewn ✓",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)),
                    FontSize = 11, FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            else if (item.RankShort)
            {
                right = new TextBlock { Text = "🔒", FontSize = 13, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(right, DeeperTip);
            }
            else
            {
                bool afford = Sample.Gold >= item.Cost;
                // Stays clickable when short — her one gift rides on a short first-pocket buy.
                var buy = new Button
                {
                    Content = new TextBlock { Text = $"buy  🪙 {item.Cost:N0}" },
                    Tag = item.Id,
                    Padding = new Thickness(14, 6, 14, 6),
                    Background = afford ? new SolidColorBrush(goldColor) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    Foreground = afford ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0)),
                    BorderThickness = new Thickness(0),
                    FontSize = 11.5,
                    FontWeight = FontWeight.Bold,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    CornerRadius = new CornerRadius(9),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                // ponytail: needs ChaosMeta bench purchases, wired when they move to Core.
                buy.Click += (_, _) => Log.Debug("ChaosHub: bench buy {Id} requested; no ChaosMeta on this head yet", item.Id);
                right = buy;
            }
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            return new Border
            {
                Child = grid,
                Background = RowBg,
                BorderBrush = new SolidColorBrush(Color.FromArgb(item.Owned ? (byte)70 : (byte)30, 0xE8, 0xB4, 0x43)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6),
            };
        }

        /// <summary>A name on the bench with nothing behind it yet.</summary>
        private static Border HazyRow(string name, string tip)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "◌", FontSize = 14, Opacity = 0.5, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = name, Foreground = DimText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var border = new Border
            {
                Child = row,
                Background = RowBg,
                BorderBrush = new SolidColorBrush(Color.FromArgb(20, 0xE8, 0x43, 0x93)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Opacity = 0.7,
            };
            ToolTip.SetTip(border, tip);
            return border;
        }

        // ============================ mantras box ============================

        private void BuildMantras()
        {
            _mantrasHost.Children.Clear();
            foreach (var b in Sample.Mantras) _mantrasHost.Children.Add(MantraRow(b));
        }

        /// <summary>One mantra/sin row: ??? until met in a draft; discovered mantras are clickable
        /// to set/clear the start mantra (gold ring + ★ on the whispered one). Sins are listed but
        /// can only be taken mid-fall, never chosen.</summary>
        private Border MantraRow(SampleMantra b)
        {
            bool seen = b.Seen;
            bool isStart = Sample.StartMantra == b.Id;
            bool pickable = seen && !b.IsCurse;
            var accent = b.IsCurse ? Color.FromRgb(0xFF, 0x8A, 0x8A) : Color.FromRgb(0x9C, 0xE8, 0xA0);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var icon = new Border
            {
                Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(seen ? Color.FromArgb(60, accent.R, accent.G, accent.B) : Color.FromArgb(40, 120, 120, 140)),
                BorderBrush = new SolidColorBrush(seen ? accent : Color.FromRgb(90, 90, 110)), BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = seen ? (b.IsCurse ? "☠" : "◈") : "?",
                    Foreground = new SolidColorBrush(seen ? accent : Color.FromRgb(0x88, 0x88, 0xA0)),
                    FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock { Text = seen ? b.Name : "???", Foreground = seen ? White : DimText, FontSize = 12, FontWeight = FontWeight.SemiBold });
            mid.Children.Add(new TextBlock { Text = seen ? b.Desc : "hazy. go back down and look closer.", Foreground = BodyText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (seen && !string.IsNullOrEmpty(b.Flavor))
                mid.Children.Add(new TextBlock
                {
                    Text = b.Flavor, FontStyle = FontStyle.Italic, FontSize = 11,
                    Foreground = FlavorText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                });
            Grid.SetColumn(mid, 1);
            row.Children.Add(mid);

            if (seen)
            {
                var badge = new TextBlock
                {
                    Text = isStart ? "start ★" : b.IsCurse ? "taken, never chosen" : "set start",
                    Foreground = isStart ? GoldBrush : new SolidColorBrush(Color.FromArgb(0x90, 0xB8, 0xB8, 0xD0)),
                    FontSize = 10.5, FontWeight = isStart ? FontWeight.Bold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(badge, 2);
                row.Children.Add(badge);
            }

            var card = new Border
            {
                Child = row,
                Background = RowBg,
                BorderBrush = isStart
                    ? new SolidColorBrush(Color.FromArgb(190, Gold.R, Gold.G, Gold.B))
                    : new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(isStart ? 3 : 2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = new Cursor(pickable ? StandardCursorType.Hand : StandardCursorType.Arrow)
            };
            if (seen)
                ToolTip.SetTip(card, b.Name + "\n" + b.Desc + "\n" +
                    (b.IsCurse ? "a sin. it can only be taken mid-fall."
                               : isStart ? "whispered on the way down. click to fall in bare."
                                         : "click to whisper it on the way down."));
            if (pickable)
                card.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                    Sample.StartMantra = isStart ? null : b.Id;
                    BuildMantras();
                };
            return card;
        }

        // ============================ diary (bubbles met) ============================

        private void BuildDiary()
        {
            _diaryHost.Children.Clear();
            FillDiaryRows(_diaryHost, 270);
        }

        /// <summary>The interaction sheet at the top of the diary: every core verb, always
        /// readable — the recall surface for all the one-time in-run teaches.</summary>
        private static readonly (string Glyph, string Name, string Desc)[] DiaryVerbs =
        {
            ("✋", "hold to snap", "press and HOLD a live (ringed) bubble about a second to defuse it — costs 30 focus. a quick click, or letting go early, TRIGGERS it instead."),
            ("○", "click the treats", "a tap pops a treat: its payload plays, the streak climbs, and +10 focus flows back (+15 from heavies and rabbits)."),
            ("🌊", "right-click · the ripple", "casts a wave from your cursor (near the bubbles): treats pop fully paid, trances snap clean, rabbits get flung. one charge, gathered back over time — READY on the sidebar means it's in your hand."),
            ("◌", "focus", "the defuse fuel: max 100, you fall in with 50, no regen on its own. when the bar runs red you can't afford a hold — farm treats before touching a live one (pressing one anyway triggers it in your grip)."),
            ("🔥", "lust", "the orange bar. climbs while you perform and pays up to x2 at full burn; an unblocked trigger cools it to zero."),
            ("💨", "never let treats rot", "a treat that fades unpopped HALVES your streak. chase the rewards too, not just the threats."),
            ("🐇", "catch the white rabbit", "everything slows to a crawl for six seconds. with the Spanker worn, you smack it into the field instead."),
            ("❄", "the pickups", "freeze ❄ holds the whole field 3.5 seconds (still poppable, and snaps cost no focus) · the lucky bubble 🍀 pays gold on the spot."),
            ("⏸", "your panic key", "one press holds the field mid-fall; pressing it again wakes you up to the recap."),
        };

        /// <summary>Every diary entry into <paramref name="host"/> — shared by the in-tab box
        /// (narrow wrap) and the pop-out reader (roomier).</summary>
        private void FillDiaryRows(Panel host, double maxWidth)
        {
            // How to play before what you've met — never discovery-gated.
            host.Children.Add(SubHeader("VERBS · how to play down there"));
            foreach (var v in DiaryVerbs) host.Children.Add(VerbRow(v.Glyph, v.Name, v.Desc, maxWidth));
            host.Children.Add(SubHeader("WHAT YOU'VE MET"));
            foreach (var c in Sample.Codex)
                host.Children.Add(CodexRow(c.Name, c.Desc, c.Glyph, c.Accent, c.Seen, maxWidth));
        }

        private Window? _diaryPopout;

        /// <summary>The diary box is a teaser — clicking it opens the full, scrollable reader.</summary>
        private void Diary_PopOut(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_hdrDiary).Properties.IsLeftButtonPressed) return;
            if (_diaryPopout != null)
            {
                try { _diaryPopout.Activate(); } catch { /* already closing */ }
                return;
            }
            var host = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
            host.Children.Add(new TextBlock
            {
                Text = "DIARY · what you've met down there",
                Foreground = new SolidColorBrush(BoonAccent),
                FontFamily = new FontFamily("Consolas, Courier New"), FontWeight = FontWeight.Bold, FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            });
            FillDiaryRows(host, 440);
            _diaryPopout = new Window
            {
                Title = "Diary",
                Width = 580, Height = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x26)),
                Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = host },
            };
            _diaryPopout.Closed += (_, _) => _diaryPopout = null;
            _diaryPopout.Show(this);
        }

        private static TextBlock SubHeader(string text) => new()
        {
            Text = text, Foreground = new SolidColorBrush(BoonAccent),
            FontFamily = new FontFamily("Consolas, Courier New"), FontWeight = FontWeight.Bold, FontSize = 11,
            Margin = new Thickness(0, 12, 0, 6)
        };

        /// <summary>A diary verb row: the CodexRow look without the discovery gate — the
        /// how-to-play sheet is always legible.</summary>
        private static Border VerbRow(string glyph, string name, string desc, double maxWidth)
        {
            var accent = Color.FromRgb(0x7A, 0xE0, 0xFF);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = glyph, Foreground = new SolidColorBrush(accent), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            // MaxWidth keeps descs wrapping inside their box (a StackPanel row otherwise
            // measures at infinite width and clips); the pop-out reader passes a roomier one.
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = maxWidth };
            mid.Children.Add(new TextBlock { Text = name, Foreground = White, FontSize = 12, FontWeight = FontWeight.SemiBold });
            mid.Children.Add(new TextBlock { Text = desc, Foreground = BodyText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            row.Children.Add(mid);
            return new Border
            {
                Child = row,
                Background = RowBg,
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6),
            };
        }

        private static Border CodexRow(string name, string desc, string glyph, Color accent, bool seen, double maxWidth)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new Border
            {
                Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(seen ? Color.FromArgb(60, accent.R, accent.G, accent.B) : Color.FromArgb(40, 120, 120, 140)),
                BorderBrush = new SolidColorBrush(seen ? accent : Color.FromRgb(90, 90, 110)), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock
                {
                    Text = seen ? glyph : "?",
                    Foreground = new SolidColorBrush(seen ? accent : Color.FromRgb(0x88, 0x88, 0xA0)),
                    FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            if (seen) ToolTip.SetTip(icon, name + "\n" + desc);
            row.Children.Add(icon);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = maxWidth };
            mid.Children.Add(new TextBlock { Text = seen ? name : "???", Foreground = seen ? White : DimText, FontSize = 12, FontWeight = FontWeight.SemiBold });
            mid.Children.Add(new TextBlock { Text = seen ? desc : "hazy. go back down and look closer.", Foreground = BodyText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            row.Children.Add(mid);

            return new Border
            {
                Child = row,
                Background = RowBg,
                BorderBrush = new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        // ============================ run-setup load / save ============================

        private static readonly string[] KeyOptions = { "Q", "E", "R", "F", "Z", "X", "C", "V", "1", "2", "3", "4" };

        /// <summary>WPF read every knob off <c>App.Settings.Current</c> and fell back to
        /// <see cref="LoadDefaults"/> when settings were missing. There is no AppSettings on this
        /// head yet, so this IS the fallback path — plus the two accessory-key combos, which WPF
        /// filled only on the settings path and which would otherwise render empty.
        /// ponytail: needs App.Settings, wired when it moves to Core.</summary>
        private void LoadFromSettings()
        {
            _cmbAccKey1.ItemsSource = KeyOptions;
            _cmbAccKey2.ItemsSource = KeyOptions;
            _cmbAccKey1.SelectedItem = "Q";
            _cmbAccKey2.SelectedItem = "E";
            LoadDefaults();
            ApplyExtremeGate();
        }

        /// <summary>Lock/unlock the Extreme difficulty pill from meta state; fall back off it when locked.</summary>
        private void ApplyExtremeGate()
        {
            bool unlocked = Sample.ExtremeUnlocked;
            _segExtreme.IsEnabled = unlocked;
            _segExtreme.Content = unlocked ? "Inescapable" : "Inescapable 🔒";
            if (!unlocked)
                // The lock keeps its mystery on the face; the hover gives the exact path.
                // ponytail: needs ChaosLessons / ChaosRanks for the real thresholds and price.
                ToolTip.SetTip(_segExtreme,
                    "a deeper door. she sells the key in the Toybox: finish enough relentless "
                    + "descents, reach Devoted, then train it.");
            ApplyDifficultyPillTips(extremeUnlocked: unlocked);
            if (!unlocked && _segExtreme.IsChecked == true) SetSegment(_grpDifficulty, "Hard");
        }

        /// <summary>What each pill actually changes, on hover — pay multiplier, spawn pace, field
        /// size — so picking a difficulty is a choice instead of a mystery. The locked Inescapable
        /// pill keeps its unlock-path tooltip (set in <see cref="ApplyExtremeGate"/>).</summary>
        private void ApplyDifficultyPillTips(bool extremeUnlocked)
        {
            foreach (var pill in _grpDifficulty.Children.OfType<ToggleButton>())
            {
                string? tip = pill.Tag?.ToString() switch
                {
                    "Easy" => "x1.0 pay. the calmest fall: baseline spawn pace, the longest trances, and the strange bubbles roll half as often.",
                    "Medium" => "x1.3 on every payout. bubbles surface ~30% faster and the field holds ~14% more of them at once.",
                    "Hard" => "x1.7 on every payout. ~70% faster spawns, ~30% more on screen, shorter trances — and the Bound hunts here on any rank.",
                    "Extreme" => extremeUnlocked
                        ? "x2.2 on every payout. spawns at more than double pace, ~48% more on screen. the deepest the hole goes."
                        : null,   // the unlock-path tooltip owns the locked pill
                    _ => null,
                };
                if (tip != null) ToolTip.SetTip(pill, tip);
            }
        }

        private void LoadDefaults()
        {
            SetSegment(_grpDifficulty, "Easy");
            SetSegment(_grpLength, "180");
            SetSegment(_grpMotion, "Mixed");
            _waves = 5; _txtWaves.Text = "5";
            foreach (var t in _grpPool.Children.OfType<ToggleButton>()) t.IsChecked = true;
            _chkShake.IsChecked = true; _sldShake.Value = 0.8;
            _chkFlashes.IsChecked = true; _sldEffect.Value = 0.85;
            _chkSkiaFx.IsChecked = true;
            _chkPinTop.IsChecked = true;
            _chkSharedHost.IsChecked = false;
            _chkBoonDraft.IsChecked = true; _chkCurses.IsChecked = true;
            _chkDarters.IsChecked = true;
            _chkAnnouncer.IsChecked = true;
            _chkNarrative.IsChecked = true;
            _chkBackdrop.IsChecked = true;
            _sldBackdropOpacity.Value = 0.55;
            _chkTunnel.IsChecked = false;
        }

        /// <summary>ponytail: needs App.Settings, wired when it moves to Core. WPF wrote every
        /// knob back on FALL IN and on leaving the dollhouse; nothing persists on this head.</summary>
        private void SaveToSettings() =>
            Log.Debug("ChaosHub: run setup not persisted; no App.Settings on this head yet");

        // ============================ run-setup controls ============================

        private void Segment_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton btn) return;
            if (!btn.IsEnabled) { btn.IsChecked = false; return; }   // locked (Extreme)
            if (btn.Parent is not Panel grp) return;
            foreach (var t in grp.Children.OfType<ToggleButton>()) t.IsChecked = ReferenceEquals(t, btn);
        }

        private void Stepper_Click(object? sender, RoutedEventArgs e)
        {
            switch ((sender as Button)?.Tag?.ToString())
            {
                case "waves-": _waves = Math.Max(1, _waves - 1); _txtWaves.Text = _waves.ToString(); break;
                case "waves+": _waves = Math.Min(12, _waves + 1); _txtWaves.Text = _waves.ToString(); break;
            }
        }

        /// <summary>The three bubble-pool presets. WPF read them from
        /// <c>ChaosBubbleVariants.Presets</c>; the ids and groupings are copied verbatim.
        /// ponytail: needs ChaosBubbleVariants, wired when it moves to Core.</summary>
        private static readonly Dictionary<string, string[]> PoolPresets = new()
        {
            ["Balanced"] = new[] { "flash", "subliminal", "pink", "spiral", "braindrain", "bambifreeze", "video", "htlink" },
            ["Tease"] = new[] { "flash", "subliminal", "pink", "spiral" },
            ["Flash-only"] = new[] { "flash" },
        };

        private void Preset_Click(object? sender, RoutedEventArgs e)
        {
            var name = (sender as Button)?.Tag?.ToString();
            if (name == null || !PoolPresets.TryGetValue(name, out var ids)) return;
            foreach (var t in _grpPool.Children.OfType<ToggleButton>())
                t.IsChecked = ids.Contains(t.Tag?.ToString() ?? "");
        }

        private void BtnRandomize_Click(object? sender, RoutedEventArgs e)
        {
            // Only difficulties whose pills are revealed can roll (the saved setting is untouched).
            var diffs = new List<string> { "Easy" };
            if (_segMedium.IsVisible) diffs.Add("Medium");
            if (_segHard.IsVisible) diffs.Add("Hard");
            if (Sample.ExtremeUnlocked) diffs.Add("Extreme");
            SetSegment(_grpDifficulty, diffs[_rng.Next(diffs.Count)]);
            SetSegment(_grpLength, new[] { "120", "180", "300" }[_rng.Next(3)]);
            SetSegment(_grpMotion, new[] { "Mixed", "FloatUp", "RainDown", "RoamBounce" }[_rng.Next(4)]);
            var pool = _grpPool.Children.OfType<ToggleButton>().ToList();
            foreach (var t in pool) t.IsChecked = _rng.NextDouble() < 0.6;
            if (!pool.Any(t => t.IsChecked == true)) pool[0].IsChecked = true;
        }

        private void BtnDefaults_Click(object? sender, RoutedEventArgs e) { LoadDefaults(); ApplyExtremeGate(); }

        /// <summary>WPF saved the setup, closed the hub and handed the config to
        /// <c>App.Chaos.StartRun</c>. There is no ChaosService on this head, so this closes and
        /// says so. ponytail: needs ChaosService + ChaosRunConfig, wired when they move to Core.</summary>
        private void BtnBegin_Click(object? sender, RoutedEventArgs e)
        {
            var mode = (sender as Control)?.Tag?.ToString() == "FreeDesktop" ? "FreeDesktop" : "Story";
            SaveToSettings();
            Log.Debug("ChaosHub: FALL IN ({Mode}) requested; no ChaosService on this head yet", mode);
            Close();
        }

        /// <summary>The loadout sidebar's FALL IN hero button lands here — same path as the
        /// footer button (no Tag ⇒ Story mode).</summary>
        public void FallIn() => BtnBegin_Click(this, new RoutedEventArgs());

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();

        // ============================ main menu / view swap ============================

        private void ShowMenuView()
        {
            _menuView.IsVisible = true;
            _dollhouseView.IsVisible = false;
            _menuLeftCol.IsVisible = true;
            _menuArtPanel.IsVisible = true;
            _menuOptions.IsVisible = false;
            RefreshTopBar();   // keep the menu chips current
        }

        private void ShowDollhouseView()
        {
            _menuView.IsVisible = false;
            _dollhouseView.IsVisible = true;
        }

        /// <summary>Straight into a descent. WPF also detached the companion for the handoff.
        /// ponytail: needs App.AvatarWindow, wired when it moves to Core.</summary>
        private void Menu_FallIn_Click(object? sender, RoutedEventArgs e) => FallIn();

        private void Menu_Dollhouse_Click(object? sender, RoutedEventArgs e)
        {
            // WPF also spawned the loadout sidebar here (App.Chaos.ShowLoadoutSidebar).
            // ponytail: needs ChaosService, wired when it moves to Core.
            ShowDollhouseView();
            ShowTab("loadout");
        }

        /// <summary>Story is greyed (BtnMenuStory.IsEnabled = StoryModeEnabled); this only ever
        /// fires if the flag flips true, at which point it would route into the story descent.</summary>
        private void Menu_Story_Click(object? sender, RoutedEventArgs e) { /* coming soon — disabled */ }

        private void Menu_Options_Click(object? sender, RoutedEventArgs e)
        {
            _optFullscreen.IsChecked = WindowState == WindowState.Maximized;
            _menuLeftCol.IsVisible = false;
            _menuArtPanel.IsVisible = false;
            _menuOptions.IsVisible = true;
        }

        private void Options_Back_Click(object? sender, RoutedEventArgs e)
        {
            _menuOptions.IsVisible = false;
            _menuLeftCol.IsVisible = true;
            _menuArtPanel.IsVisible = true;
        }

        private void Menu_Exit_Click(object? sender, RoutedEventArgs e) => Close();

        // ======================= HOW TO PLAY (card tutorial overlay) =======================

        private sealed record HowToLine(string Emoji, string EmojiColor, string Lead, string LeadColor, string Body);
        private sealed record HowToCard(string Title, string Image, HowToLine[] Lines);

        private static readonly HowToCard[] _howToCards =
        {
            new("What the Rabbit Hole is", "howto_1", new[]
            {
                new HowToLine("", "", "", "",
                    "Bubbles drift up the screen carrying flashes, videos and overlays. Pop the good ones, snap the dangerous ones before they go off, and ride it deeper. One descent is about **five minutes** — survive the waves, take what she offers, climb out a little more hers."),
            }),
            new("What you do", "howto_2", new[]
            {
                new HowToLine("🫧", "#FFFF9FD0", "Left-click", "#FFFF9FD0", "pop the treats — the soft pink bubbles. One click builds your streak and refills your focus."),
                new HowToLine("◉", "#FFFFD228", "Press & hold", "#FFFFD228", "the glowing bubbles are live. Keep pressing until they snap — let one finish and it goes off (a flash or video fires)."),
                new HowToLine("🌊", "#FF7AE0FF", "Right-click", "#FF7AE0FF", "the ripple. A wave near the bubbles pops treats, snaps live ones and scatters rabbits. Strong, but slow to gather again."),
                new HowToLine("🐇", "#FFFF69B4", "The rabbits", "#FFFF69B4", "chase them for little bonuses. Everything else down there is yours to find out."),
            }),
            new("The two bars", "howto_3", new[]
            {
                new HowToLine("", "", "FOCUS", "#FFFFFFFF", "your nerve. Snapping live bubbles spends it; popping treats refills it. Run dry and you can't snap — so keep feeding."),
                new HowToLine("", "", "HEAT", "#FFFFFFFF", "the burn. It climbs every time something triggers. Let it run high and the descent gets harder to resist."),
            }),
            new("A descent", "howto_4", new[]
            {
                new HowToLine("", "", "", "",
                    "Four waves, then it ends. Between waves she offers you a **mantra** — pick one and it bends the rules for that run only. Finish the whole descent for the full reward; slip out early and you forfeit it."),
            }),
            new("What you keep", "howto_5", new[]
            {
                new HowToLine("", "", "", "",
                    "Every descent earns **XP** toward your normal level, plus **Sparks** (gold) you carry back out."),
                new HowToLine("", "", "", "",
                    "Spend Sparks in **the dollhouse** — accessories at the table by the door, charms, active toys you trigger mid-descent, and the seamstress's bench for permanent upgrades."),
                new HowToLine("", "", "", "",
                    "The more descents you finish, the higher your **RANK** — curious, tempted, slipping, entranced, devoted… — and the more of the Rabbit Hole opens up to you."),
            }),
        };

        private int _howToIdx;

        private void Menu_HowTo_Click(object? sender, RoutedEventArgs e)
        {
            _howToIdx = 0;
            HowToShow();
            _menuHowTo.IsVisible = true;
        }

        private void HowTo_Close_Click(object? sender, RoutedEventArgs e) => _menuHowTo.IsVisible = false;

        // backdrop dismiss: only when the click lands on the dim backdrop itself, not the card
        private void HowTo_Backdrop_Click(object? sender, PointerPressedEventArgs e)
        {
            if (ReferenceEquals(e.Source, _menuHowTo)) _menuHowTo.IsVisible = false;
        }

        private void HowTo_Back_Click(object? sender, RoutedEventArgs e)
        {
            if (_howToIdx > 0) { _howToIdx--; HowToShow(); }
        }

        private void HowTo_Next_Click(object? sender, RoutedEventArgs e)
        {
            if (_howToIdx < _howToCards.Length - 1) { _howToIdx++; HowToShow(); }
            else _menuHowTo.IsVisible = false;   // last card: "DONE" closes
        }

        private void HowToShow()
        {
            var card = _howToCards[_howToIdx];

            _howToStep.Text = $"STEP {_howToIdx + 1} / {_howToCards.Length}";
            _howToTitle.Text = card.Title;

            // Card art comes from ChaosArt.Resolve("howto", …) in the head; with no art the box
            // collapses, exactly as it does in WPF when no screenshot has been dropped in.
            // ponytail: needs ChaosArt, wired when it moves to Core.
            _howToImageBox.IsVisible = false;

            // body lines
            _howToBody.Children.Clear();
            foreach (var line in card.Lines) _howToBody.Children.Add(BuildHowToLine(line));

            // dots
            _howToDots.Children.Clear();
            for (int i = 0; i < _howToCards.Length; i++)
                _howToDots.Children.Add(new Ellipse
                {
                    Width = 8, Height = 8, Margin = new Thickness(4, 0, 4, 0),
                    Fill = i == _howToIdx
                        ? this.FindResource("Pink") as IBrush
                        : new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                });

            // nav state
            _howToBack.IsVisible = _howToIdx > 0;
            _howToNext.Content = _howToIdx < _howToCards.Length - 1 ? "NEXT  ›" : "DONE";
        }

        private static Control BuildHowToLine(HowToLine line)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 21, Margin = new Thickness(0, 0, 0, 9) };
            var inlines = new InlineCollection();

            if (!string.IsNullOrEmpty(line.Lead))
                inlines.Add(new Run(line.Lead + "  ")
                {
                    FontWeight = FontWeight.Bold,
                    Foreground = BrushFromHex(line.LeadColor),
                });
            // body supports inline **bold** spans
            bool bold = false;
            foreach (var part in line.Body.Split("**"))
            {
                if (part.Length > 0)
                    inlines.Add(new Run(part)
                    {
                        Foreground = bold ? White : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xDE)),
                        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                    });
                bold = !bold;
            }
            tb.Inlines = inlines;

            if (string.IsNullOrEmpty(line.Emoji)) return tb;

            // emoji-led row: glyph in a fixed gutter, text beside it
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(34)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var glyph = new TextBlock { Text = line.Emoji, FontSize = 17, VerticalAlignment = VerticalAlignment.Top, Foreground = BrushFromHex(line.EmojiColor) };
            Grid.SetColumn(glyph, 0);
            Grid.SetColumn(tb, 1);
            grid.Children.Add(glyph);
            grid.Children.Add(tb);
            return grid;
        }

        private static IBrush BrushFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return White;
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch (FormatException) { return White; }
        }

        private void Back_To_Menu_Click(object? sender, RoutedEventArgs e)
        {
            SaveToSettings();    // keep any loadout/setup tweaks made in the dollhouse
            ShowMenuView();
        }

        // ============================ menu art motion ============================

        /// <summary>
        /// WPF put almost-imperceptible life on the menu art: a slow breathing zoom of the
        /// ImageBrush's RelativeTransform, a tiny rotate, and a pulsing pink glow on the border,
        /// all as looping WPF <c>DoubleAnimation</c>s started with <c>BeginAnimation</c>.
        ///
        /// ponytail: no faithful twin here. Avalonia has no <c>BeginAnimation</c>, animating a
        /// brush's RelativeTransform needs the whole thing re-authored as Avalonia Animations,
        /// and the art the motion exists to move (ChaosArt's menu frames) is not on this head
        /// anyway. The static frame renders instead — 1.02 baseline zoom, no drift, steady glow.
        /// Restore it together with the Skia menu scene.
        /// </summary>
        private void SetupMenuMotion() { }

        /// <summary>WPF advanced the crossfading flipbook on a click and restarted the dwell
        /// timer. ponytail: needs the ChaosArt frames + the Skia scene, wired with them.</summary>
        private void MenuArt_Click(object? sender, PointerPressedEventArgs e) { }

        // ============================ menu music ============================

        /// <summary>WPF drove a NAudio loop with fades and a master-volume hook. There is no
        /// audio stack on this head, so the button only swaps its icon.
        /// ponytail: needs the audio service, wired when it moves to Core.</summary>
        private bool _menuMuted;

        private void BtnMenuMute_Click(object? sender, RoutedEventArgs e)
        {
            _menuMuted = !_menuMuted;
            _menuMuteIcon.Text = _menuMuted ? "🔇" : "🔊";
        }

        // ============================ window chrome (move / fullscreen) ============================

        private void DragWindow(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Visual v || !e.GetCurrentPoint(v).Properties.IsLeftButtonPressed) return;
            try { BeginMoveDrag(e); } catch { /* dragging can throw if not pressed */ }
        }

        private void BtnMin_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnFull_Click(object? sender, RoutedEventArgs e) => SetFullscreen(WindowState != WindowState.Maximized);

        private void OptFullscreen_Click(object? sender, RoutedEventArgs e) => SetFullscreen(_optFullscreen.IsChecked == true);

        private void SetFullscreen(bool on) => WindowState = on ? WindowState.Maximized : WindowState.Normal;

        /// <summary>WPF also detached the companion tube while maximized (it is anchored to the
        /// main window). ponytail: needs App.AvatarWindow, wired when it moves to Core.</summary>
        private void OnHubStateChanged(WindowState state) => _optFullscreen.IsChecked = state == WindowState.Maximized;

        /// <summary>Re-open the spoiler-free rules card on demand. WPF showed ChaosIntroWindow
        /// modally OVER the dollhouse; that view is not in this layer, and routing to the menu's
        /// HOW TO PLAY card instead would kick the player out of the tab they are on.
        /// ponytail: needs ChaosIntroWindow, wired when that view is ported.</summary>
        private void BtnGuide_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ChaosHub: guide requested; ChaosIntroWindow is not on this head yet");

        // ============================ helpers ============================

        private static void SetSegment(Panel grp, string? tag)
        {
            foreach (var t in grp.Children.OfType<ToggleButton>()) t.IsChecked = t.Tag?.ToString() == tag;
        }

        /// <summary>WPF: <c>ChaosRanks.RankLockedTip</c> / <c>RankSpecifics(rank)</c>.
        /// ponytail: needs ChaosRanks, wired when it moves to Core.</summary>
        private const string RankLockedTip = "she'll show you this one when you've fallen further.";

        private static string RankSpecifics(string rank) => $"reach {rank} to be shown this.";

        // ============================ sample data ============================
        // Everything below stands in for the Chaos services. It is deliberately shaped to hit
        // every branch each builder above can take, so the render proves all of them at once.

        private sealed record SampleHabit(string Id, string Glyph, string Name, string Desc, string Flavor,
                                          string Branch, int Cost, bool Owned, bool On);

        private sealed record SampleBoon(string Id, string Glyph, string Name, string Desc, string Flavor,
                                         string Category, string ValueLabel, int Level, int MaxLevel,
                                         bool Active, int UnlockCost, int UpgradeCost, bool RankLocked,
                                         string RankFloor, string CapstoneDesc, bool IsActiveUse,
                                         double UseCooldownSec);

        private sealed record SampleMantra(string Id, string Name, string Desc, string Flavor, bool Seen, bool IsCurse);

        private sealed record SampleCodexEntry(string Name, string Desc, string Glyph, Color Accent, bool Seen);

        private sealed record SampleBench(string Id, string Glyph, string Label, string Line, int Cost,
                                          bool Owned, bool RankShort, bool Hazy);

        /// <summary>A played save partway down: two pockets sewn, one toy worn, one accessory
        /// still on the shelf, one habit trained and switched on, one trained and off, one
        /// untrained, and one charm behind the rank wall.</summary>
        private static class Sample
        {
            public const string Rank = "Slipping";
            public const int Sparks = 1820;
            public const int Gold = 640;
            public const int RunsCompleted = 14;
            public const double TotalRunSeconds = 4_930;
            public const long BestScore = 12_400;
            public const int BestCombo = 37;
            public const int TotalDefused = 268;
            public const double TotalChannelSeconds = 1_190;
            public static readonly bool ExtremeUnlocked = false;

            public static string? StartMantra = "soft_focus";

            public static readonly List<SampleHabit> Habits = new()
            {
                new("start_resistance", "🛡", "It would never work on me...", "fall in with 20 resistance already spent.", "you said that last time, too.", "Control", 120, true, true),
                new("blank_eyes", "👁", "Blank Eyes", "trances hold ~15% longer before they let go.", "nobody's home. that's the point.", "Depth", 180, true, false),
                new("slow_fuses", "🕯", "Slow Fuses", "live bubbles take an extra half second to go off.", "she likes to watch you decide.", "Control", 260, false, false),
            };

            public static readonly List<SampleBoon> Boons = new()
            {
                new("the_spanker", "🪄", "The Spanker", "swat the white rabbit into the field instead of chasing it.",
                    "she calls it a training aid.", "Skill", "3 uses per descent", 2, 3, true, 400, 750, false, "", "the field flinches when you raise it.", true, 12),
                new("the_ripple", "🌊", "The Ripple", "your wave reaches a little further from the cursor.",
                    "one good push and the whole room moves.", "Skill", "+18% radius", 1, 3, false, 500, 900, false, "", "", false, 0),
                new("deep_pockets", "👛", "Deep Pockets", "treats pay 10% more gold.",
                    "", "Accessory", "+10% gold", 1, 3, true, 300, 600, false, "", "", false, 0),
                new("the_collar", "⛓", "The Collar", "focus refunds on a clean snap.",
                    "it isn't locked. it doesn't need to be.", "Accessory", "+8 focus", 0, 3, false, 850, 0, false, "", "", false, 0),
                new("porcelain_mask", "🎭", "???", "", "", "Accessory", "", 0, 3, false, 0, 0, true, "Devoted", "", false, 0),
            };

            /// <summary>Utility charms — they train on the Habits shelf, not the toy shelves.</summary>
            public static readonly List<SampleBoon> Charms = new()
            {
                new("rabbits_foot", "🍀", "Rabbit's Foot", "lucky bubbles surface a little more often.",
                    "worn smooth already.", "Utility", "+6% lucky rate", 2, 3, true, 250, 500, false, "", "", false, 0),
                new("the_pact", "🖋", "???", "", "", "Utility", "", 0, 3, false, 0, 0, true, "Entranced", "", false, 0),
            };

            public static readonly List<SampleMantra> Mantras = new()
            {
                new("soft_focus", "Soft Focus", "trances start 20% deeper, and hold.", "you stopped blinking a while ago.", true, false),
                new("open_hands", "Open Hands", "treats pay double for the first wave.", "", true, false),
                new("the_slip", "The Slip", "one free snap, no focus spent.", "", false, false),
                new("greedy_little_thing", "Greedy Little Thing", "gold doubles, focus never refills.", "she wrote this one down.", true, true),
            };

            public static readonly List<SampleCodexEntry> Codex = new()
            {
                new("Flash", "a treat carrying an image. pops clean, pays focus back.", "●", Color.FromRgb(0xFF, 0x9F, 0xD0), true),
                new("Subliminal", "a treat that whispers on the way past.", "●", Color.FromRgb(0xC9, 0xC4, 0xE8), true),
                new("Spiral", "live. snap it or it takes the screen for a while.", "●", Color.FromRgb(0x8B, 0x5C, 0xF6), true),
                new("White Rabbit", "fast, small, and worth chasing — everything slows when you catch one.", "✧", Color.FromRgb(0xFF, 0x4D, 0xC4), true),
                new("Lucky Bubble", "pays gold on the spot. rare.", "🍀", Color.FromRgb(0xFF, 0xD7, 0x00), true),
                new("The Echo", "it repeats the last thing that got through.", "◌", Color.FromRgb(0xC9, 0xC4, 0xE8), false),
                new("The Chaperone", "it wants to help. it does not help.", "💞", Color.FromRgb(0x9C, 0xE8, 0xFF), false),
                new("The Tease", "it never goes off. that is the whole trick.", "✖", Color.FromRgb(0xB3, 0x0E, 0x2E), false),
                new("The Bound", "it hunts on relentless.", "⛓", Color.FromRgb(0xFF, 0x69, 0xB4), false),
                new("The Brittle", "one touch and it shatters into three.", "◇", Color.FromRgb(0xD9, 0xEF, 0xFF), false),
            };

            public static readonly List<SampleBench> Bench = new()
            {
                new("toy_pocket_1", "👝", "first toy pocket", "she sews you a pocket.", 50, true, false, false),
                new("acc_pocket_1", "👝", "first accessory pocket", "she only has two hands. she found a third.", 150, true, false, false),
                new("start_mantra", "◈", "the starting mantra", "fall in holding something.", 200, false, false, false),
                new("diary", "📓", "the diary", "she keeps notes on what you meet down there.", 150, false, false, false),
                new("stats_panel", "🕰", "the stats panel", "the numbers, if you want them.", 100, false, false, false),
                new("toy_pocket_2", "👝", "second toy pocket", "she found room for one more.", 2000, false, true, false),
                new("acc_pocket_2", "👝", "second accessory pocket", "a fourth hand. don't ask.", 2500, false, false, true),
            };

            /// <summary>Reserved hazy rows: names on the bench, nothing behind them yet.</summary>
            public static readonly string[] ReservedRows =
            {
                "the clocks", "descent ledger", "payout eyes", "the fine print",
                "fall right in", "held breath", "soft landing", "no countdown",
            };

            public static int SlotsFor(string category) => category == "Skill" ? 1 : 1;

            public static int EquippedCountIn(string category) => Boons.Count(b => b.Category == category && b.Active);

            public static bool HasFreePocket(string category) => EquippedCountIn(category) < SlotsFor(category);

            public static void ToggleHabit(string id)
            {
                for (int i = 0; i < Habits.Count; i++)
                    if (Habits[i].Id == id) Habits[i] = Habits[i] with { On = !Habits[i].On };
            }

            public static void ToggleBoon(string id)
            {
                for (int i = 0; i < Boons.Count; i++)
                    if (Boons[i].Id == id) Boons[i] = Boons[i] with { Active = !Boons[i].Active };
                for (int i = 0; i < Charms.Count; i++)
                    if (Charms[i].Id == id) Charms[i] = Charms[i] with { Active = !Charms[i].Active };
            }

            /// <summary>Equip into a full 1-slot pocket by quietly swapping the occupant out.</summary>
            public static void EquipSwapping(string id, string category)
            {
                if (!HasFreePocket(category))
                    for (int i = 0; i < Boons.Count; i++)
                        if (Boons[i].Category == category && Boons[i].Active) Boons[i] = Boons[i] with { Active = false };
                for (int i = 0; i < Boons.Count; i++)
                    if (Boons[i].Id == id) Boons[i] = Boons[i] with { Active = true };
            }
        }
    }
}
