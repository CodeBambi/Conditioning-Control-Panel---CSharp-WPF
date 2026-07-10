using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// The Down the Rabbit Hole hub ("the Dollhouse"): opened from the Lab card. Four tabs:
/// BAG (pocket slots + the whole collection as clickable tiles), THE TOYBOX
/// (unlock/deepen toys, accessories and habits), SETTINGS (run setup), and
/// THE LOOKING GLASS (stats + diary + start-mantra picker + the seamstress's bench).
/// </summary>
public partial class ChaosHubWindow : Window
{
    private static readonly Random _rng = new();
    private int _waves = 5;

    /// <summary>The open Dollhouse, if any. Lets the loadout sidebar push unequips back into it.</summary>
    public static ChaosHubWindow? Current { get; private set; }

    private bool _uiSoundsReady;   // suppress cues while the ctor builds the initial view

    public ChaosHubWindow()
    {
        InitializeComponent();
        Current = this;
        // A quiet wet click under every button press; specific cues (equip/unlock/…) layer on top.
        AddHandler(ButtonBase.ClickEvent,
            new RoutedEventHandler((_, _) => { if (_uiSoundsReady) ChaosSfx.Play("ui_click", 0.3f); }), true);
        // The loadout sidebar opens beside the Warren so the pockets read at a glance while
        // equipping; it follows this window's lifetime (a started run swaps in the real HUD).
        Closed += (_, _) =>
        {
            Current = null;
            StopMenuFog();
            StopFlipbook();
            DisposeMenuSkia();
            DisposeMenuMusic();
            App.Chaos?.CloseLoadoutSidebar();
            // Entering the Dollhouse detached the avatar; if we're leaving WITHOUT a descent
            // starting (FALL IN sets _fallingIn), put it back where it was.
            if (!_fallingIn) App.AvatarWindow?.SetChaosRunActive(false);
        };
        // The loadout sidebar + avatar detach are deferred: they no longer fire on open (that
        // would pop the sidebar over a bare menu). EnterRunContext() spawns them the moment the
        // player commits — picking THE DOLL HOUSE or FALL IN. See ShowMenuView/Menu_* handlers.
        TitleBar.MouseLeftButtonDown += DragWindow;
        MenuTitleBar.MouseLeftButtonDown += DragWindow;
        DragBar.MouseLeftButtonDown += DragWindow;
        StateChanged += OnHubStateChanged;

        LoadFromSettings();
        InitRevealMap();
        BuildHabits();
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        BuildBench();
        BuildMantras();
        BuildDiary();
        RefreshTopBar();
        RefreshStats();
        ApplyUnlocks();
        ApplyReveals();
        LoadBanner();
        BuildDebugStrip();   // CCP_CHAOS_DEBUG=1 only — a normal launch builds nothing
        ShowTab("loadout");
        BtnMenuStory.IsEnabled = Services.Chaos.ChaosModeService.StoryModeEnabled;   // greyed until story ships
        ShowMenuView();      // the main menu is the landing view; the dollhouse waits behind it
        SetupMenuMotion();   // breathing + wobble + pulsing neon border
        Loaded += (_, _) => { OnHubOpenedReveals(); FireHubGreeting(); };
        _uiSoundsReady = true;
    }

    /// <summary>The Madam's hub-return greeting (gated on NarrativeModeEnabled inside the director).
    /// A short beat lets the hub paint before the card slides in over it.</summary>
    private void FireHubGreeting()
    {
        try
        {
            // Story is disabled until there's story content, so the Madam stays silent in the hub
            // too (this greeting isn't tied to a run mode otherwise). See ChaosModeService.StoryModeEnabled.
            if (!Services.Chaos.ChaosModeService.StoryModeEnabled) return;
            if (App.Settings?.Current?.NarrativeModeEnabled != true) return;
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (Current != this) return;   // hub was closed in the meantime
                var ctx = new Services.Chaos.ChaosNarrativeContext { RankIndex = (int)Services.Chaos.ChaosMeta.RankIndex };
                Services.Chaos.ChaosNarrativeHooks.OnHubMoment("hub_return", ctx);
            };
            t.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHub.FireHubGreeting: {E}", ex.Message); }
    }

    private void LoadBanner()
    {
        var src = ChaosArt.ResolveBanner();
        if (src != null) { BannerImage.Source = src; BannerImage.Visibility = Visibility.Visible; }

        // Menu's right panel: the crossfading flipbook (menu_1/2/3.png) or a still fallback.
        LoadMenuFrames();
        var bd = ChaosArt.Resolve("hub", "backdrop");
        if (bd != null) { HubBackdrop.Source = bd; HubBackdrop.Visibility = Visibility.Visible; }
    }

    // ============================ tabs / gating ============================

    private void ApplyUnlocks()
    {
        // All four tabs render from the first visit (the drops cost is the real gate, and
        // run setup must be reachable before run #1). Gating returns with the reveal framework.
        TabLoadout.IsEnabled = true;
        TabEnhance.IsEnabled = true;
        TabRun.IsEnabled = true;
        TabImprove.IsEnabled = true;
        // The Diary tab only appears once there's something in it (met a bubble down there).
        TabDiary.Visibility = DiaryUnlocked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsEnabled) ShowTab(tb.Tag?.ToString() ?? "loadout");
        else if (sender is ToggleButton tb2) tb2.IsChecked = false;
    }

    private void ShowTab(string tag)
    {
        // The old Habits tab folded into the Toybox; external callers may still ask for it.
        if (tag == "habits") tag = "enhance";
        // The Looking Glass stays out of reach until its reveal flips (the tab renders
        // greyed + locked, so gate on IsEnabled — it's always Visible now).
        if (tag == "improve" && !TabImprove.IsEnabled) tag = "loadout";
        // The diary lives behind its own reveal — nothing met yet, nothing to read.
        if (tag == "diary" && !DiaryUnlocked) tag = "loadout";

        PanelLoadout.Visibility = tag == "loadout" ? Visibility.Visible : Visibility.Collapsed;
        PanelEnhance.Visibility = tag == "enhance" ? Visibility.Visible : Visibility.Collapsed;
        PanelRun.Visibility     = tag == "run"     ? Visibility.Visible : Visibility.Collapsed;
        PanelImprove.Visibility = tag == "improve" ? Visibility.Visible : Visibility.Collapsed;
        PanelDiary.Visibility   = tag == "diary"   ? Visibility.Visible : Visibility.Collapsed;

        TabLoadout.IsChecked = tag == "loadout";
        TabEnhance.IsChecked = tag == "enhance";
        TabRun.IsChecked     = tag == "run";
        TabImprove.IsChecked = tag == "improve";
        TabDiary.IsChecked   = tag == "diary";

        TxtHint.Text = tag switch
        {
            "loadout" => "click a tile to slip it into a pocket. + takes you where it's sold.",
            "enhance" => "spend your emotes. deepen what you like.",
            "run"     => "dress up the fall, then FALL IN.",
            "improve" => "the bench, the mantras, how far you've fallen.",
            "diary"   => "everything you've met down there. click an entry to pop it out.",
            _ => "",
        };
    }

    /// <summary>The Diary tab is offered once its reveal flips (the same "you've met something
    /// down there" unlock the old in-card diary used) — before that it's an empty page.</summary>
    private static bool DiaryUnlocked => RevealService.IsUnlocked(RevealIds.Diary);

    /// <summary>Settings tab: fold the dev/test knobs (bubble pool, mantra toggles, loops)
    /// in and out. Collapsed every open — the casual read stays short.</summary>
    private void TestingToggle_Click(object sender, RoutedEventArgs e)
    {
        bool open = TglTesting.IsChecked == true;
        TestingBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TglTesting.Content = open ? "🧪 testing options ▾" : "🧪 testing options ▸";
    }

    /// <summary>A + tile wants to take the player shopping.</summary>
    private void JumpToTab(string tag) => ShowTab(tag);

    /// <summary>External navigation (the loadout sidebar's empty "+" tiles): switch tab and
    /// bring the Dollhouse forward.</summary>
    public void NavigateTo(string tag)
    {
        ShowTab(tag);
        try { Activate(); } catch { }
    }

    // ============================ top bar / stats ============================

    private int _shownSparks = -1, _shownGold = -1;
    private readonly Dictionary<TextBlock, DispatcherTimer> _balanceAnims = new();
    private DispatcherTimer? _balanceTickOwner;   // one tick stream even when both balances move

    private void RefreshTopBar()
    {
        AnimateBalance(TxtSparks, _shownSparks, ChaosMeta.State.Sparks);
        AnimateBalance(TxtGold, _shownGold, ChaosMeta.State.Gold);
        _shownSparks = ChaosMeta.State.Sparks;
        _shownGold = ChaosMeta.State.Gold;
        TxtRank.Text = ChaosMeta.Rank;
        // mirror onto the main-menu chips (plain text; the animated balance lives on the dollhouse top bar)
        try { MenuRank.Text = ChaosMeta.Rank; MenuSparks.Text = ChaosMeta.State.Sparks.ToString(); MenuGold.Text = ChaosMeta.State.Gold.ToString(); }
        catch { /* menu chips not built yet during very early ctor calls */ }
        RefreshTabBadges();   // every balance change re-counts what the shelves can sell
    }

    /// <summary>Signposting on the shop tabs: a small count of what's buyable RIGHT NOW
    /// (drops purchases on the Toybox, gold purchases on the Looking Glass) — the answer
    /// to "is there anything for me in there?" without scanning four shelves.</summary>
    private void RefreshTabBadges()
    {
        SetTabBadge(TabEnhance, "the Toybox", CountAffordableToybox());
        SetTabBadge(TabImprove, "the Looking Glass", TabImprove.IsEnabled ? CountAffordableBench() : 0);
    }

    private static void SetTabBadge(ToggleButton tab, string label, int count)
    {
        if (count <= 0)
        {
            tab.Content = label;
            tab.ClearValue(ToolTipProperty);
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold },
        });
        tab.Content = row;
        tab.ToolTip = count == 1 ? "1 thing you can afford right now" : $"{count} things you can afford right now";
    }

    /// <summary>Drops purchases buyable this instant: untrained habits + boon unlocks +
    /// boon level-ups — same gates the shelf buttons enforce (lesson, rank, her script).</summary>
    private static int CountAffordableToybox()
    {
        int n = 0;
        foreach (var u in ChaosUpgrades.All)
            if (!ChaosMeta.IsOwned(u.Id) && !ChaosMeta.IsPurchaseRankLocked(u.Id)
                && !ChaosLessons.IsLessonBlocked(u.Id) && ChaosMeta.CanAfford(u.Id)) n++;
        foreach (var b in ChaosLifetimeBoons.LegacyAll)
        {
            int level = ChaosMeta.BoonLevel(b.Id);
            if (level <= 0)
            {
                if (!ChaosMeta.IsBoonRankLocked(b.Id) && !ChaosMeta.IsAccessoryScriptLocked(b.Id)
                    && !ChaosLessons.IsLessonBlocked(b.Id) && ChaosMeta.CanAffordUnlock(b.Id)) n++;
            }
            else if (level < b.MaxLevel && !ChaosMeta.IsCapstonePurchaseRankLocked(b.Id)
                     && ChaosMeta.CanAffordUpgrade(b.Id)) n++;
        }
        return n;
    }

    /// <summary>Gold purchases buyable this instant at her bench (rank + reveal gated).</summary>
    private int CountAffordableBench()
    {
        int n = 0;
        foreach (var item in BenchItems)
        {
            if (ChaosMeta.State.BenchPurchases.Contains(item.Id)) continue;
            if (item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value)) continue;
            if (item.RevealGate != null && !RevealService.IsUnlocked(item.RevealGate)) continue;
            if (ChaosMeta.State.Gold >= item.Cost) n++;
        }
        return n;
    }

    /// <summary>Roll a top-bar balance from its last shown value to the new one (~500ms,
    /// soft tick underneath) so spending visibly *costs* — first paint just snaps.</summary>
    private void AnimateBalance(TextBlock tb, int from, int to)
    {
        if (_balanceAnims.TryGetValue(tb, out var old))
        {
            old.Stop();
            _balanceAnims.Remove(tb);
            if (ReferenceEquals(_balanceTickOwner, old)) _balanceTickOwner = null;
        }
        if (from < 0 || from == to || !IsLoaded)
        {
            tb.Text = to.ToString("N0");
            return;
        }

        const int DURATION_MS = 500, FRAME_MS = 33, TICK_EVERY_MS = 90;
        int elapsed = 0, lastTick = -TICK_EVERY_MS;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
        _balanceAnims[tb] = timer;
        _balanceTickOwner ??= timer;             // only one stream of ticks at a time
        timer.Tick += (_, _) =>
        {
            elapsed += FRAME_MS;
            if (elapsed >= DURATION_MS)
            {
                timer.Stop();
                _balanceAnims.Remove(tb);
                if (ReferenceEquals(_balanceTickOwner, timer)) _balanceTickOwner = null;
                tb.Text = to.ToString("N0");
                return;
            }
            double eased = 1 - Math.Pow(1 - elapsed / (double)DURATION_MS, 3);
            tb.Text = ((int)Math.Round(from + (to - from) * eased)).ToString("N0");
            if (ReferenceEquals(_balanceTickOwner, timer) && elapsed - lastTick >= TICK_EVERY_MS)
            {
                lastTick = elapsed;
                ChaosSfx.Play("count_tick", 0.4f);
            }
        };
        timer.Start();
    }

    private void RefreshStats()
    {
        var s = ChaosMeta.State;
        StSparks.Text = s.Sparks.ToString("N0");
        StRuns.Text = s.RunsCompleted.ToString("N0");
        StTimeUnder.Text = FormatPlaytime(s.TotalRunSeconds);
        StBestScore.Text = s.BestScore.ToString("N0");
        StBestCombo.Text = s.BestCombo.ToString("N0");
        StDefused.Text = s.TotalDefused.ToString("N0");
        StTimeHeld.Text = FormatPlaytime(s.TotalChannelSeconds);
    }

    private static string FormatPlaytime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }

    // ============================ habits (The Descent tab) ============================

    private static Color BranchColor(ChaosBranch b) => b switch
    {
        ChaosBranch.Control => Color.FromRgb(0x49, 0xB6, 0xE8),
        ChaosBranch.Greed   => Color.FromRgb(0xE8, 0xB4, 0x43),
        ChaosBranch.Depth   => Color.FromRgb(0x8B, 0x5C, 0xF6),
        _                   => Color.FromRgb(0xE8, 0x43, 0x93)
    };

    private static string BranchLabel(ChaosBranch b) => b switch
    {
        ChaosBranch.Control => "RESTRAINT",
        ChaosBranch.Greed   => "CRAVING",
        _                   => "DEPTH",
    };

    // ---- happy path: the Toybox starter view (after run 1, before run 2) ----
    // Exactly three rows: two cheap lessonless charms + the one habit whose lesson
    // already shows what run 1 banked. Full shelves render from RunsCompleted >= 2.
    private static readonly string[] StarterShelfIds = { "start_resistance", "blank_eyes", "slow_fuses" };

    private static bool OnShelfNow(string id) =>
        ChaosMeta.State.RunsCompleted >= 2 || Array.IndexOf(StarterShelfIds, id) >= 0;

    /// <summary>One grouped list (RESTRAINT / CRAVING / DEPTH) of the trainable passives —
    /// untrained rows sell, trained rows toggle on/off.</summary>
    private void BuildHabits()
    {
        // One flat list, one card style: the single-rank habits first (branch order),
        // then the leveled ones (Utility lifetime boons — Rabbit's Foot etc.), unlabeled.
        HabitsHost.Children.Clear();
        foreach (var u in ChaosUpgrades.All)
            if (OnShelfNow(u.Id)) HabitsHost.Children.Add(BuildUpgradeRow(u));
        foreach (var b in ChaosLifetimeBoons.LegacyInCategory(ChaosBoonCategory.Utility))
            if (OnShelfNow(b.Id)) HabitsHost.Children.Add(BuildLifetimeBoonRow(b));
    }

    /// <summary>One habit card, in the same dress as the boon rows (72px art, big card,
    /// gold edge while switched on) — the Habits tab is one cohesive list.</summary>
    private Border BuildUpgradeRow(ChaosUpgrade u)
    {
        bool owned = ChaosMeta.IsOwned(u.Id);
        bool afford = ChaosMeta.CanAfford(u.Id);
        bool on = owned && ChaosMeta.IsUpgradeActive(u.Id);
        var accent = BranchColor(u.Branch);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---- icon (real art if present, else a branch-tinted square with the glyph) ----
        var iconSrc = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", u.Id);
        FrameworkElement icon;
        if (iconSrc != null)
            icon = ArtIcon(iconSrc, 72, 14, accent, ring: 3);
        else
            icon = new Border
            {
                Width = 72, Height = 72, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = u.Glyph, FontSize = 34, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.Opacity = owned ? 1.0 : 0.5;
        ChaosTips.Attach(icon, u.Name, u.Desc, accent: accent, flavor: u.Flavor);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // ---- middle: name + desc + flavor + branch tag ----
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = u.Name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = u.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        if (!string.IsNullOrEmpty(u.Flavor))
            mid.Children.Add(new TextBlock
            {
                Text = u.Flavor, FontStyle = FontStyles.Italic, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
        mid.Children.Add(new TextBlock
        {
            Text = BranchLabel(u.Branch).ToLowerInvariant(),
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)),
            FontSize = 10.5, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 6, 0, 0)
        });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        // ---- right: ON badge + on/off toggle, or the Train buy button ----
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Width = 132 };
        if (owned)
        {
            if (on)
                right.Children.Add(new TextBlock
                {
                    Text = "ON ✓",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            var toggle = new Button
            {
                Style = (Style)FindResource("Stepper"),
                Content = on ? "switch off" : "switch on",
                Tag = u.Id,
                Width = double.NaN, Height = double.NaN, MinWidth = 112,
                FontSize = 12.5,
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Pillify(toggle);
            ChaosTips.Attach(toggle, u.Name, on ? "switched on — shapes your next descent." : "switched off — sits out the descent.", accent: accent);
            toggle.Click += HabitToggle_Click;
            right.Children.Add(toggle);
        }
        else
        {
            if (ChaosLessons.IsLessonBlocked(u.Id))
            {
                // the lesson gates the training — no buy button
                right.Children.Add(BuildLessonLockPanel(u.Id, accent));
            }
            else
            {
                // Rank-floored purchases (extreme_tier needs Devoted) stay visible but locked.
                bool rankLocked = ChaosMeta.IsPurchaseRankLocked(u.Id);
                var train = BuyButton($"Train  ✦{u.Cost}", u.Id, afford && !rankLocked, Buy_Click);
                if (rankLocked)
                {
                    train.ToolTip = ChaosRanks.RankLockedTip + "\n" + ChaosRanks.RankSpecifics(ChaosRank.Devoted);
                    ToolTipService.SetShowOnDisabled(train, true);
                }
                right.Children.Add(train);
            }
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Child = grid,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1A, 0x36)),
            BorderBrush = on
                ? new SolidColorBrush(Color.FromArgb(180, 0xFF, 0xD7, 0x00))
                : new SolidColorBrush(Color.FromArgb(owned ? (byte)90 : (byte)40, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(on ? 3 : 2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    private void Buy_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        if (ChaosMeta.TryPurchase(id!))
        {
            // No cue here — the unlock card below carries the sting.
            if (id == "extreme_tier") ApplyExtremeGate();
            BuildHabits();
            BuildLoadoutTiles();   // the habit grid lights up
            RefreshTopBar();
            RefreshStats();
            RevealService.Sync("purchase");   // extreme_tier flips the Inescapable pill reveal
            ApplyReveals();
            RunRevealFlashes("purchase");
            ShowUnlockCard(ChaosUnlockCards.ForHabit(id!));
        }
        else ChaosSfx.Play("ui_denied", 0.45f);
    }

    private void HabitToggle_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        ChaosMeta.SetUpgradeActive(id!, !ChaosMeta.IsUpgradeActive(id!));
        ChaosSfx.Play(ChaosMeta.IsUpgradeActive(id!) ? "ui_equip" : "ui_unequip", 0.45f);
        BuildHabits();
        BuildLoadoutTiles();
        App.Chaos?.NotifyLoadoutChanged();   // sidebar CONDITIONING list mirrors the switch
    }

    // ===================== lifetime boons (skills/accessories/utility) =====================

    private static readonly Color BoonAccent = Color.FromRgb(0xE8, 0x43, 0x93);

    private void BuildLifetimeBoons()
    {
        BuildBoonShelf(BoonHostSkills, ChaosBoonCategory.Skill);
        BuildBoonShelf(BoonHostAccessories, ChaosBoonCategory.Accessory);
        // Utility charms train on the HABITS tab (BuildHabits) — they're habits in spirit.
    }

    private void BuildBoonShelf(Panel host, ChaosBoonCategory cat)
    {
        host.Children.Clear();
        var boons = ChaosLifetimeBoons.LegacyInCategory(cat).ToList();
        if (boons.Count == 0)
        {
            host.Children.Add(new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Child = new TextBlock
                {
                    Text = "something is being prepared for you.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xC0)),
                    FontSize = 12, TextWrapping = TextWrapping.Wrap
                }
            });
            return;
        }
        foreach (var b in boons) host.Children.Add(BuildLifetimeBoonRow(b));
    }

    private Border BuildLifetimeBoonRow(ChaosLifetimeBoon b)
    {
        int level = ChaosMeta.BoonLevel(b.Id);
        bool unlocked = level >= 1;
        bool active = ChaosMeta.IsBoonActive(b.Id);
        bool maxed = unlocked && level >= b.MaxLevel;
        // Rank-locked = below your depth tier: a MYSTERY. Hide the art, name and what it does —
        // only the rank gate shows, so the deeper toys stay a reveal instead of a spoiled preview.
        bool rankLocked = ChaosMeta.IsBoonRankLocked(b.Id);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---- icon (real art if present, else a tinted square with the glyph) ----
        // Rank-locked → the keyhole mystery art (or a "?" box), never the boon's own sprite.
        var iconSrc = rankLocked ? null : ChaosArt.Resolve("boons", b.Id);
        FrameworkElement icon;
        if (rankLocked && TileUnknownArt != null)
            icon = ArtIcon(TileUnknownArt, 72, 14, BoonAccent, ring: 3);
        else if (iconSrc != null)
            icon = ArtIcon(iconSrc, 72, 14, BoonAccent, ring: 3);
        else
            icon = new Border
            {
                Width = 72, Height = 72, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(70, BoonAccent.R, BoonAccent.G, BoonAccent.B)),
                BorderBrush = new SolidColorBrush(BoonAccent), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = rankLocked ? "?" : b.Glyph, FontSize = 34, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.Opacity = unlocked ? 1.0 : (rankLocked ? 0.6 : 0.5);
        if (rankLocked)
            ChaosTips.Attach(icon, "? ? ?", ChaosRanks.RankLockedTip, ChaosRanks.RankSpecifics(b.RankFloor));
        else
            ChaosTips.Attach(icon, unlocked ? $"{b.Name} · L{level}" : b.Name, b.Desc,
                string.IsNullOrEmpty(b.CapstoneDesc) ? null : "max: " + b.CapstoneDesc,
                flavor: b.Flavor);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // ---- middle: name + desc + level pips + value ----
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = rankLocked ? "? ? ?" : b.Name, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (rankLocked)
        {
            // No desc, no flavor — only the gate. The reveal is the reward for sinking deeper.
            mid.Children.Add(new TextBlock
            {
                Text = ChaosRanks.RankLockedTip + " " + ChaosRanks.RankSpecifics(b.RankFloor),
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x80, 0xA8)), FontStyle = FontStyles.Italic,
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0)
            });
        }
        else
        {
            mid.Children.Add(new TextBlock { Text = b.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (!string.IsNullOrEmpty(b.Flavor))
                mid.Children.Add(new TextBlock
                {
                    Text = b.Flavor, FontStyle = FontStyles.Italic, FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                });
        }

        // Active-use toys carry their trigger: the keybind (when equipped) or a generic hint.
        if (b.IsActiveUse && !rankLocked)
        {
            string key = App.Settings?.Current?.ChaosAccessoryKey1 ?? "Q";
            string useHint = b.UseCooldownSec > 0 ? $"{b.UseCooldownSec:0}s cooldown" : "limited uses";
            mid.Children.Add(new TextBlock
            {
                Text = active ? $"ACTIVE · fires on {key} mid-descent · {useHint}"
                              : $"ACTIVE · fires on your toy key mid-descent · {useHint}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                FontSize = 10.5, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        // Capstone teaser: dim until the final rank is bought, gold once it's live.
        if (!string.IsNullOrEmpty(b.CapstoneDesc) && !rankLocked)
            mid.Children.Add(new TextBlock
            {
                Text = "max: " + b.CapstoneDesc,
                Foreground = new SolidColorBrush(maxed ? Color.FromRgb(0xFF, 0xD7, 0x00) : Color.FromArgb(0x90, 0x8A, 0x86, 0xB8)),
                FontSize = 11,
                FontStyle = maxed ? FontStyles.Normal : FontStyles.Italic,
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
        string valueText = unlocked ? string.Format(b.ValueLabel, b.ValueAt(level)) : "locked";
        pips.Children.Add(new TextBlock
        {
            Text = $"   {valueText}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
            FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        mid.Children.Add(pips);
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        // ---- right: on/off toggle + unlock/upgrade ----
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Width = 132 };

        // Utility rows live on the Habits tab and speak habit language (switch on/off);
        // Skills/Accessories keep the pocketed Equip semantics.
        bool habitVoice = b.Category == ChaosBoonCategory.Utility;
        if (unlocked)
        {
            // Equip semantics instead of an ambiguous ON/OFF: the badge shows the STATE,
            // the button is always the ACTION. Pockets cap Skills/Accessories at 2 each.
            if (active)
                right.Children.Add(new TextBlock
                {
                    Text = habitVoice ? "ON ✓" : "EQUIPPED ✓",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            bool pocketFree = active || ChaosMeta.HasFreePocket(b.Category);
            var equip = new Button
            {
                Style = (Style)FindResource("Stepper"),
                Content = active ? (habitVoice ? "switch off" : "Unequip")
                                 : pocketFree ? (habitVoice ? "switch on" : "Equip") : "pockets full",
                Tag = b.Id,
                IsEnabled = pocketFree,
                Width = double.NaN,
                Height = double.NaN,
                MinWidth = 112,
                FontSize = 12.5,
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Pillify(equip);
            equip.Click += BoonEquip_Click;
            right.Children.Add(equip);
        }

        if (!unlocked)
        {
            // Rank floor is the outermost gate: too shallow and the row stays priced but locked,
            // with the exact descent count on hover. Lessons + her script still stack beneath it.
            if (ChaosMeta.IsBoonRankLocked(b.Id))
            {
                // Mystery: hide the cost too — show only the depth gate. The button names the
                // rank you must reach, not the price of a toy you're not meant to know yet.
                var held = BuyButton($"🔒 {ChaosRanks.Name(b.RankFloor)}", b.Id, false, BoonUnlock_Click);
                held.ToolTip = ChaosRanks.RankLockedTip + "\n" + ChaosRanks.RankSpecifics(b.RankFloor);
                ToolTipService.SetShowOnDisabled(held, true);
                right.Children.Add(held);
            }
            // Happy path: until the_spanker is owned, every OTHER accessory hangs locked —
            // even with its lesson complete. ChaosMeta.TryUnlockBoon enforces the same gate.
            else if (ChaosMeta.IsAccessoryScriptLocked(b.Id))
            {
                var held = BuyButton($"Unlock  ✦{b.UnlockCost}", b.Id, false, BoonUnlock_Click);
                held.ToolTip = "she sells these in an order of her own.";
                ToolTipService.SetShowOnDisabled(held, true);
                right.Children.Add(held);
            }
            else
            {
                right.Children.Add(ChaosLessons.IsLessonBlocked(b.Id)
                    ? BuildLessonLockPanel(b.Id, BoonAccent)   // the lesson gates the unlock — no buy button
                    : BuyButton($"Unlock  ✦{b.UnlockCost}", b.Id, ChaosMeta.CanAffordUnlock(b.Id), BoonUnlock_Click));
            }
        }
        else if (maxed)
            right.Children.Add(new TextBlock { Text = "MAX  ✓", Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)), FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
        else
        {
            int cost = ChaosMeta.NextUpgradeCostOf(b.Id) ?? 0;
            // The capstone (final) level waits for the Devoted rank: the row stays visible,
            // priced and locked, with her line on hover.
            bool capLocked = ChaosMeta.IsCapstonePurchaseRankLocked(b.Id);
            var deepen = BuyButton($"deepen  ✦{cost}", b.Id,
                !capLocked && ChaosMeta.CanAffordUpgrade(b.Id), BoonUpgrade_Click);
            if (capLocked)
            {
                deepen.ToolTip = ChaosRanks.CapstoneLockedTip + "\n" + ChaosRanks.RankSpecifics(ChaosRank.Devoted);
                ToolTipService.SetShowOnDisabled(deepen, true);
            }
            right.Children.Add(deepen);
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Child = grid,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1A, 0x36)),
            // Equipped cards read at a glance: gold edge; otherwise the usual pink, dimmed when locked.
            BorderBrush = active
                ? new SolidColorBrush(Color.FromArgb(180, 0xFF, 0xD7, 0x00))
                : new SolidColorBrush(Color.FromArgb(unlocked ? (byte)90 : (byte)40, BoonAccent.R, BoonAccent.G, BoonAccent.B)),
            BorderThickness = new Thickness(active ? 3 : 2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    private Button BuyButton(string text, string id, bool afford, RoutedEventHandler onClick)
    {
        var btn = new Button
        {
            Content = text,
            Tag = id,
            Padding = new Thickness(20, 10, 20, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = afford ? new SolidColorBrush(BoonAccent) : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Foreground = afford ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0)),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Cursor = afford ? Cursors.Hand : Cursors.Arrow,
            IsEnabled = afford
        };
        Pillify(btn);
        btn.Click += onClick;
        return btn;
    }

    /// <summary>Round a code-built button's corners (same implicit-Border-style trick the
    /// Lab's FALL IN button uses — the default button chrome is a Border).</summary>
    private static void Pillify(Button b, double radius = 11)
    {
        var s = new Style(typeof(Border));
        s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(radius)));
        b.Resources.Add(typeof(Border), s);
    }

    private void AccKey_Changed(object sender, SelectionChangedEventArgs e)
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        if (CmbAccKey1?.SelectedItem is string k1) s.ChaosAccessoryKey1 = k1;
        if (CmbAccKey2?.SelectedItem is string k2) s.ChaosAccessoryKey2 = k2;
    }

    private void BoonEquip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            ChaosMeta.SetBoonActive(id, !ChaosMeta.IsBoonActive(id));
            ChaosSfx.Play(ChaosMeta.IsBoonActive(id) ? "ui_equip" : "ui_unequip", 0.45f);
            AfterBoonChange();   // rebuild shelves (badges + full-pocket states) and the Pockets tab
        }
    }

    private void BoonUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryUnlockBoon(id))
            {
                // No cue here — the unlock card below carries the sting.
                AfterBoonChange();
                ShowUnlockCard(ChaosUnlockCards.ForBoonUnlock(id));   // after the unlock: reads auto-equip state
            }
            else ChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    private void BoonUpgrade_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (ChaosMeta.TryUpgradeBoon(id))
            {
                AfterBoonChange();
                // Only the FINAL level gets a card — the capstone changes behavior; mid levels
                // just grow a number (and keep the quiet deepen cue; the card stings itself).
                var b = ChaosLifetimeBoons.ById(id);
                var capstoneCard = b != null && ChaosMeta.BoonLevel(id) >= b.MaxLevel
                    ? ChaosUnlockCards.ForCapstone(id) : null;
                if (capstoneCard != null) ShowUnlockCard(capstoneCard);
                else ChaosSfx.Play("ui_deepen", 0.5f);
            }
            else ChaosSfx.Play("ui_denied", 0.45f);
        }
    }

    // ===================== unlock announcement cards =====================

    private readonly Queue<ChaosUnlockCardData> _unlockCards = new();
    private bool _unlockCardShowing;
    private const int UNLOCK_CARD_HOLD_MS = 4500;

    /// <summary>Float an unlock card top-center over the hub: slide-in, dwell, fade. Cards
    /// queue so back-to-back purchases play in sequence; the layer never takes clicks, so
    /// shopping continues underneath.</summary>
    private void ShowUnlockCard(ChaosUnlockCardData? data)
    {
        if (data == null) return;
        _unlockCards.Enqueue(data);
        if (!_unlockCardShowing) ShowNextUnlockCard();
    }

    private void ShowNextUnlockCard()
    {
        if (_unlockCards.Count == 0) { _unlockCardShowing = false; return; }
        _unlockCardShowing = true;

        var card = ChaosUnlockCards.BuildCardVisual(_unlockCards.Dequeue());
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(0, 96, 0, 0);   // below the title bar + tab strip
        var slide = new TranslateTransform(0, -14);
        card.RenderTransform = slide;
        card.Opacity = 0;
        UnlockCardLayer.Children.Add(card);

        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });

        var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UNLOCK_CARD_HOLD_MS) };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (_, _)
                => { try { UnlockCardLayer.Children.Remove(card); } catch { } ShowNextUnlockCard(); };
            card.BeginAnimation(OpacityProperty, fade);
        };
        hold.Start();
    }

    private void AfterBoonChange()
    {
        BuildLifetimeBoons();
        BuildHabits();          // charms render on the Habits tab
        BuildLoadoutTiles();
        RefreshTopBar();
        RefreshStats();
        App.Chaos?.NotifyLoadoutChanged();   // mirror into the loadout sidebar, if up
    }

    /// <summary>The loadout sidebar unequipped something — re-render shelves/tiles here
    /// WITHOUT notifying the sidebar back (it already refreshed itself).</summary>
    public void RefreshAfterExternalLoadoutChange()
    {
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        RefreshTopBar();
    }

    // ============================ loadout tab (tile grids) ============================

    private const double TILE = 96;
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xD7, 0x00);

    // Stitched-keyhole sprite for the not-yet-formed ??? pads. Resolved once per window —
    // tile rebuilds run on every loadout change and must not re-hit the disk.
    private bool _tileUnknownTried;
    private ImageSource? _tileUnknownArt;
    private ImageSource? TileUnknownArt
    {
        get
        {
            if (!_tileUnknownTried) { _tileUnknownTried = true; _tileUnknownArt = ChaosArt.Resolve("hub", "tile_unknown"); }
            return _tileUnknownArt;
        }
    }

    private enum TileState { Equipped, Owned, Locked, Empty }

    /// <summary>The whole glance page: pocket slots + accessory/skill/habit tile grids.</summary>
    private void BuildLoadoutTiles()
    {
        // ---- pocket slots (big tiles, one group per category; unsewn categories don't render) ----
        PocketSlotsHost.Children.Clear();
        var toyGroup = PocketGroup("TOY", ChaosBoonCategory.Skill);
        if (toyGroup != null) PocketSlotsHost.Children.Add(toyGroup);
        var accGroup = PocketGroup("ACCESSORY", ChaosBoonCategory.Accessory);
        if (accGroup != null) PocketSlotsHost.Children.Add(accGroup);
        if (PocketSlotsHost.Children.Count == 0)
        {
            // Empty-state vignette (turned-out pocket) above the line, art-optional as ever.
            var vignette = ChaosArt.Resolve("hub", "pockets_empty");
            var emptyCol = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            if (vignette != null)
                emptyCol.Children.Add(new Image
                {
                    Source = vignette,
                    Height = 96,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Opacity = 0.8,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            emptyCol.Children.Add(new TextBlock
            {
                Text = "no pockets sewn yet.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)),
                FontSize = 12,
            });
            PocketSlotsHost.Children.Add(emptyCol);
        }

        // ---- collections ----
        FillCategoryTiles(TilesAccessories, ChaosBoonCategory.Accessory, padTo: 8);
        FillCategoryTiles(TilesSkills, ChaosBoonCategory.Skill, padTo: 8);
        TxtAccCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Accessory)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Accessory)} equipped";
        TxtSkillCount.Text = $"{ChaosMeta.EquippedCountIn(ChaosBoonCategory.Skill)}/{ChaosMeta.SlotsFor(ChaosBoonCategory.Skill)} equipped";

        // ---- habits 4x4 (trained = on/off toggle; click an untrained one to go train it) ----
        TilesHabits.Children.Clear();
        var habits = ChaosUpgrades.All.Where(u => OnShelfNow(u.Id)).ToList();
        int trained = 0, switchedOn = 0;
        foreach (var u in habits)
        {
            string id = u.Id;
            bool owned = ChaosMeta.IsOwned(id);
            bool on = owned && ChaosMeta.IsUpgradeActive(id);
            if (owned) trained++;
            if (on) switchedOn++;
            Action onClick = owned
                ? () => { ChaosMeta.SetUpgradeActive(id, !ChaosMeta.IsUpgradeActive(id)); BuildHabits(); BuildLoadoutTiles(); App.Chaos?.NotifyLoadoutChanged(); }
                : () => JumpToTab("enhance");
            TilesHabits.Children.Add(LoadoutTile(u.Glyph, u.Name, u.Desc,
                on ? "click to switch off" : owned ? "click to switch on" : $"train for ✦{u.Cost} in the Toybox",
                BranchColor(u.Branch),
                on ? TileState.Equipped : owned ? TileState.Owned : TileState.Locked,
                onClick,
                cornerBadge: on ? "✓" : null,
                art: u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", id),
                flavor: u.Flavor));
        }
        // Charms (Utility lifetime boons — Rabbit's Foot etc.) live with the habits:
        // leveled, always-on once worn, toggled exactly like a trained habit.
        var charms = ChaosLifetimeBoons.LegacyInCategory(ChaosBoonCategory.Utility).Where(b => OnShelfNow(b.Id)).ToList();
        foreach (var b in charms)
        {
            string bid = b.Id;
            bool unlocked = ChaosMeta.IsBoonUnlocked(bid);
            bool active = ChaosMeta.IsBoonActive(bid);
            if (unlocked) trained++;
            if (active) switchedOn++;
            Action onClick = unlocked
                ? () =>
                  {
                      ChaosMeta.SetBoonActive(bid, !ChaosMeta.IsBoonActive(bid));
                      ChaosSfx.Play(ChaosMeta.IsBoonActive(bid) ? "ui_equip" : "ui_unequip", 0.45f);
                      BuildHabits(); BuildLoadoutTiles(); App.Chaos?.NotifyLoadoutChanged();
                  }
                : () => JumpToTab("enhance");
            bool charmRankLocked = ChaosMeta.IsBoonRankLocked(bid);
            TilesHabits.Children.Add(LoadoutTile(charmRankLocked ? "?" : b.Glyph,
                charmRankLocked ? "? ? ?" : unlocked ? $"{b.Name} · L{ChaosMeta.BoonLevel(bid)}" : b.Name,
                charmRankLocked ? ChaosRanks.RankLockedTip : b.Desc,
                active ? "click to switch off" : unlocked ? "click to switch on"
                    : charmRankLocked ? ChaosRanks.RankSpecifics(b.RankFloor) : $"unlock for ✦{b.UnlockCost} in the Toybox",
                BoonAccent,
                active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked,
                onClick,
                cornerBadge: active ? "✓" : null,
                art: charmRankLocked ? TileUnknownArt : ChaosArt.Resolve("boons", bid),
                flavor: charmRankLocked ? null : b.Flavor));
        }
        int shown = habits.Count + charms.Count;
        int target = Math.Max(16, ((shown + 3) / 4) * 4);
        for (int i = shown; i < target; i++)
            TilesHabits.Children.Add(LoadoutTile("+", "a habit not yet formed",
                "more training arrives in a later fitting.", null,
                Color.FromRgb(0xB8, 0xB8, 0xD0), TileState.Empty, null));
        TxtHabitCount.Text = $"{switchedOn} on · {trained}/{shown} trained";
    }

    /// <summary>One labelled pocket column: equipped boon as a big gold tile, plus + tiles for
    /// free slots. Null when the category has no pockets sewn (and nothing stale equipped).</summary>
    private FrameworkElement? PocketGroup(string label, ChaosBoonCategory cat)
    {
        if (ChaosMeta.SlotsFor(cat) <= 0 && ChaosMeta.EquippedCountIn(cat) == 0) return null;
        var col = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };
        col.Children.Add(new TextBlock
        {
            Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0xB8)),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var equipped = ChaosLifetimeBoons.LegacyInCategory(cat).Where(b => ChaosMeta.IsBoonActive(b.Id)).ToList();
        foreach (var b in equipped)
        {
            string id = b.Id;
            var cell = LoadoutTile(b.Glyph, $"{b.Name} · L{ChaosMeta.BoonLevel(b.Id)}", b.Desc,
                "click to unequip", BoonAccent, TileState.Equipped,
                () => { ChaosMeta.SetBoonActive(id, false); ChaosSfx.Play("ui_unequip", 0.45f); AfterBoonChange(); },
                art: ChaosArt.Resolve("boons", b.Id), size: 114, flavor: b.Flavor);
            cell.Margin = new Thickness(0, 0, 24, 0);
            row.Children.Add(cell);
        }
        for (int i = equipped.Count; i < ChaosMeta.SlotsFor(cat); i++)
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
    private void FillCategoryTiles(Panel host, ChaosBoonCategory cat, int padTo)
    {
        host.Children.Clear();
        var boons = ChaosLifetimeBoons.LegacyInCategory(cat).ToList();
        foreach (var b in boons)
        {
            string id = b.Id;
            int level = ChaosMeta.BoonLevel(id);
            bool unlocked = level >= 1;
            bool active = ChaosMeta.IsBoonActive(id);
            bool rankLocked = ChaosMeta.IsBoonRankLocked(id);
            var state = active ? TileState.Equipped : unlocked ? TileState.Owned : TileState.Locked;
            Action onClick = active ? () => { ChaosMeta.SetBoonActive(id, false); ChaosSfx.Play("ui_unequip", 0.45f); AfterBoonChange(); }
                : unlocked ? () => EquipSwapping(id, cat)
                : () => JumpToTab("enhance");
            // Rank-locked → mystery: keyhole art, "???" everywhere, the depth gate instead of a price.
            string extra = active ? "click to unequip"
                : unlocked ? "click to equip"
                : rankLocked ? ChaosRanks.RankSpecifics(b.RankFloor)
                : $"unlock for ✦{b.UnlockCost} in the Toybox";
            host.Children.Add(LoadoutTile(rankLocked ? "?" : b.Glyph,
                rankLocked ? "? ? ?" : unlocked ? $"{b.Name} · L{level}" : b.Name,
                rankLocked ? ChaosRanks.RankLockedTip : b.Desc, extra, BoonAccent, state, onClick,
                cornerBadge: active ? "★" : null,
                art: rankLocked ? TileUnknownArt : ChaosArt.Resolve("boons", id),
                flavor: rankLocked ? null : b.Flavor));
        }
        int target = Math.Max(padTo, ((boons.Count + 3) / 4) * 4);
        for (int i = boons.Count; i < target; i++)
            host.Children.Add(LoadoutTile("+",
                cat == ChaosBoonCategory.Skill ? "another toy is being stitched" : "another accessory is being stitched",
                "it'll hang here when it's ready.", null,
                Color.FromRgb(0xB8, 0xB8, 0xD0), TileState.Empty, null));
    }

    /// <summary>Equip into a full 1-slot pocket by quietly swapping the current occupant out.</summary>
    private void EquipSwapping(string id, ChaosBoonCategory cat)
    {
        if (!ChaosMeta.HasFreePocket(cat))
        {
            var current = ChaosLifetimeBoons.LegacyInCategory(cat).FirstOrDefault(b => ChaosMeta.IsBoonActive(b.Id));
            if (current != null) ChaosMeta.SetBoonActive(current.Id, false);
        }
        ChaosMeta.SetBoonActive(id, true);
        ChaosSfx.Play("ui_equip", 0.45f);
        AfterBoonChange();
    }

    private FrameworkElement LoadoutTile(string glyph, string title, string? desc, string? extra, Color accent,
                               TileState state, Action? onClick, string? cornerBadge = null,
                               ImageSource? art = null, double size = TILE, string? caption = null,
                               string? flavor = null)
    {
        // Rounded clip so the square art can't poke past the ring corners (Border doesn't
        // clip children to its CornerRadius; the tile is fixed-size so a geometry works).
        var content = new Grid
        {
            Clip = new RectangleGeometry(new Rect(0, 0, size, size), 12, 12)
        };
        // Mystery pads (Empty + default ??? caption) wear the stitched keyhole instead of a
        // bare "+"; clickable "empty pocket" tiles keep the + — there it means "add one".
        var keyhole = state == TileState.Empty && caption == null ? TileUnknownArt : null;
        if (art != null && state != TileState.Empty)
            content.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill, Opacity = state == TileState.Locked ? 0.35 : 1.0 });
        else if (keyhole != null)
            content.Children.Add(new Image
            {
                Source = keyhole,
                Width = size * 0.5,
                Height = size * 0.5,
                Stretch = Stretch.Uniform,
                Opacity = 0.45,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        else
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontSize = size >= 114 ? 46 : 36,
                Foreground = Brushes.White,
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
        // The ring rides ABOVE the art (full-bleed sprites were burying a thin behind-border)
        // and ABOVE the badge — always the topmost layer of the tile.
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
                Text = cornerBadge, FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Gold),
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
            FontWeight = state == TileState.Equipped ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = state switch
            {
                TileState.Equipped => new SolidColorBrush(Gold),
                TileState.Owned    => Brushes.White,
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
            Cursor = onClick != null ? Cursors.Hand : Cursors.Arrow,
            Background = Brushes.Transparent,
        };
        cell.Children.Add(tile);
        cell.Children.Add(label);
        if (onClick != null) cell.MouseLeftButtonDown += (_, _) => onClick();
        ChaosTips.Attach(cell, title, desc, extra, accent, flavor: flavor);
        return cell;
    }

    // ============================ improvements tab ============================
    // Her bench (the gold shop) lives in ChaosHubWindow.Bench.cs — see BuildBench().

    // ============================ mantras box ============================

    private void BuildMantras()
    {
        MantrasHost.Children.Clear();
        foreach (var b in ChaosBoonPool.All)
            MantrasHost.Children.Add(MantraRow(b));
    }

    /// <summary>One mantra/sin row: ??? until met in a draft; discovered mantras are clickable
    /// to set/clear the start mantra (gold ring + ★ on the whispered one). Sins are listed but
    /// can only be taken mid-fall, never chosen.</summary>
    private Border MantraRow(ChaosBoon b)
    {
        bool seen = ChaosMeta.IsDiscovered("boon:" + b.Id);
        bool isStart = ChaosMeta.State.EquippedStartBoon == b.Id;
        bool pickable = seen && !b.IsCurse;
        var accent = b.IsCurse ? Color.FromRgb(0xFF, 0x8A, 0x8A) : Color.FromRgb(0x9C, 0xE8, 0xA0);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(seen ? Color.FromArgb(60, accent.R, accent.G, accent.B) : Color.FromArgb(40, 120, 120, 140)),
            BorderBrush = new SolidColorBrush(seen ? accent : Color.FromRgb(90, 90, 110)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = seen ? (b.IsCurse ? "☠" : "◈") : "?", Foreground = new SolidColorBrush(seen ? accent : Color.FromRgb(0x88, 0x88, 0xA0)), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = seen ? b.Name : "???", Foreground = seen ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)), FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = seen ? b.Desc : "hazy. go back down and look closer.", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (seen && !string.IsNullOrEmpty(b.Flavor))
            mid.Children.Add(new TextBlock
            {
                Text = b.Flavor, FontStyle = FontStyles.Italic, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(mid, 1);
        row.Children.Add(mid);

        if (seen)
        {
            var badge = new TextBlock
            {
                Text = isStart ? "start ★" : b.IsCurse ? "taken, never chosen" : "set start",
                Foreground = isStart ? new SolidColorBrush(Gold) : new SolidColorBrush(Color.FromArgb(0x90, 0xB8, 0xB8, 0xD0)),
                FontSize = 10.5, FontWeight = isStart ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
        }

        var card = new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = isStart
                ? new SolidColorBrush(Color.FromArgb(190, Gold.R, Gold.G, Gold.B))
                : new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(isStart ? 3 : 2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = pickable ? Cursors.Hand : Cursors.Arrow
        };
        if (seen)
            ChaosTips.Attach(card, b.Name, b.Desc,
                b.IsCurse ? "a sin. it can only be taken mid-fall." : isStart ? "whispered on the way down. click to fall in bare." : "click to whisper it on the way down.",
                accent, flavor: b.Flavor);
        if (pickable)
            card.MouseLeftButtonDown += (_, _) =>
            {
                ChaosMeta.EquipStartBoon(isStart ? null : b.Id);
                BuildMantras();
            };
        return card;
    }

    // ============================ diary (bubbles met) ============================

    private void BuildDiary()
    {
        DiaryHost.Children.Clear();
        FillDiaryRows(DiaryHost, 270);
    }

    /// <summary>The interaction sheet at the top of the diary: every core verb, always
    /// readable — the recall surface for all the one-time in-run teaches (miss a beat
    /// mid-chaos and this is where it can be re-read).</summary>
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

    /// <summary>Every diary entry into <paramref name="host"/> — shared by the half-width
    /// Improvements box (narrow wrap) and the pop-out reader (roomier).</summary>
    private void FillDiaryRows(Panel host, double maxWidth)
    {
        // How to play before what you've met — never discovery-gated.
        host.Children.Add(SubHeader("VERBS · how to play down there"));
        foreach (var v in DiaryVerbs)
            host.Children.Add(VerbRow(v.Glyph, v.Name, v.Desc, maxWidth));
        host.Children.Add(SubHeader("WHAT YOU'VE MET"));
        foreach (var v in ChaosBubbleVariants.All)
            host.Children.Add(CodexRow("bubble:" + v.Id, v.Name, ChaosBubbleVariants.DescriptionFor(v.Id),
                ChaosArt.Resolve("bubbles", v.Id), "●", Color.FromRgb(v.Tint.R, v.Tint.G, v.Tint.B), maxWidth));
        // Not part of the weighted pool table; listed explicitly.
        host.Children.Add(CodexRow("bubble:darter", "White Rabbit", ChaosBubbleVariants.DescriptionFor("darter"),
            ChaosArt.Resolve("bubbles", "darter"), "✧", Color.FromRgb(0xFF, 0x4D, 0xC4), maxWidth));
        host.Children.Add(CodexRow("bubble:golden", "Lucky Bubble", ChaosBubbleVariants.DescriptionFor("golden"),
            ChaosArt.Resolve("bubbles", "golden"), "🍀", Color.FromRgb(0xFF, 0xD7, 0x00), maxWidth));
        host.Children.Add(CodexRow("bubble:echo", "The Echo", ChaosBubbleVariants.DescriptionFor("echo"),
            ChaosArt.Resolve("bubbles", "echo"), "◌", Color.FromRgb(0xC9, 0xC4, 0xE8), maxWidth));
        host.Children.Add(CodexRow("bubble:chaperone", "The Chaperone", ChaosBubbleVariants.DescriptionFor("chaperone"),
            ChaosArt.Resolve("bubbles", "chaperone"), "💞", Color.FromRgb(0x9C, 0xE8, 0xFF), maxWidth));
        host.Children.Add(CodexRow("bubble:tease", "The Tease", ChaosBubbleVariants.DescriptionFor("tease"),
            ChaosArt.Resolve("bubbles", "tease"), "✖", Color.FromRgb(0xB3, 0x0E, 0x2E), maxWidth));
        host.Children.Add(CodexRow("bubble:bound", "The Bound", ChaosBubbleVariants.DescriptionFor("bound"),
            ChaosArt.Resolve("bubbles", "bound"), "⛓", Color.FromRgb(0xFF, 0x69, 0xB4), maxWidth));
        host.Children.Add(CodexRow("bubble:brittle", "The Brittle", ChaosBubbleVariants.DescriptionFor("brittle"),
            ChaosArt.Resolve("bubbles", "brittle"), "◇", Color.FromRgb(0xD9, 0xEF, 0xFF), maxWidth));
    }

    private Window? _diaryPopout;

    /// <summary>The diary box is a teaser — clicking it opens the full, scrollable reader.</summary>
    private void Diary_PopOut(object sender, MouseButtonEventArgs e)
    {
        if (_diaryPopout != null)
        {
            try { _diaryPopout.Activate(); } catch { }
            return;
        }
        var host = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
        host.Children.Add(new TextBlock
        {
            Text = "DIARY · what you've met down there",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        });
        FillDiaryRows(host, 440);
        _diaryPopout = new Window
        {
            Title = "Diary",
            Owner = this,
            Width = 580, Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x26)),
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = host },
        };
        _diaryPopout.Closed += (_, _) => _diaryPopout = null;
        _diaryPopout.Show();
    }

    /// <summary>A square art icon cut to rounded corners with its accent ring ON TOP —
    /// raw full-bleed sprites otherwise show sharp corners and bury thin borders.</summary>
    private static FrameworkElement ArtIcon(ImageSource src, double size, double radius, Color accent, double ring = 2.5)
    {
        return new Border
        {
            Width = size, Height = size,
            Child = new Grid
            {
                Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius),
                Children =
                {
                    new Image { Source = src, Stretch = Stretch.UniformToFill },
                    new Border
                    {
                        CornerRadius = new CornerRadius(radius),
                        BorderBrush = new SolidColorBrush(accent),
                        BorderThickness = new Thickness(ring),
                        IsHitTestVisible = false,
                    },
                }
            },
        };
    }

    private TextBlock SubHeader(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93)),
        FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 11,
        Margin = new Thickness(0, 12, 0, 6)
    };

    /// <summary>A diary verb row: the CodexRow look without the discovery gate — the
    /// how-to-play sheet is always legible.</summary>
    private Border VerbRow(string glyph, string name, string desc, double maxWidth)
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
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = maxWidth };
        mid.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = desc, Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);
        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

    private Border CodexRow(string codexId, string name, string desc, ImageSource? iconSrc, string glyph, Color accent, double maxWidth = 270)
    {
        bool seen = ChaosMeta.IsDiscovered(codexId);

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        // icon / silhouette
        FrameworkElement icon;
        if (seen && iconSrc != null)
            icon = ArtIcon(iconSrc, 39, 9, accent);
        else
            icon = new Border
            {
                Width = 39, Height = 39, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(seen ? Color.FromArgb(60, accent.R, accent.G, accent.B) : Color.FromArgb(40, 120, 120, 140)),
                BorderBrush = new SolidColorBrush(seen ? accent : Color.FromRgb(90, 90, 110)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = seen ? glyph : "?", Foreground = new SolidColorBrush(seen ? accent : Color.FromRgb(0x88, 0x88, 0xA0)), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 12, 0);
        if (seen) ChaosTips.Attach(icon, name, desc, accent: accent);
        row.Children.Add(icon);

        // MaxWidth keeps descs wrapping inside their box (StackPanel rows otherwise
        // measure at infinite width and clip); the pop-out reader passes a roomier one.
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = maxWidth };
        mid.Children.Add(new TextBlock { Text = seen ? name : "???", Foreground = seen ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)), FontSize = 12, FontWeight = FontWeights.SemiBold });
        mid.Children.Add(new TextBlock { Text = seen ? desc : "hazy. go back down and look closer.", Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB8, 0xB8)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(mid);

        return new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(seen ? (byte)70 : (byte)25, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    // ============================ run-setup load / save ============================

    private void LoadFromSettings()
    {
        var s = App.Settings?.Current;
        if (s == null) { LoadDefaults(); ApplyExtremeGate(); return; }

        SetSegment(GrpDifficulty, s.ChaosDifficulty);
        SetSegment(GrpLength, s.ChaosRunDurationSec.ToString());
        SetSegment(GrpMotion, s.ChaosMotionMode);
        _waves = s.ChaosWaveCount; TxtWaves.Text = _waves.ToString();

        var enabled = s.ChaosEnabledVariants; // null = all
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = enabled == null || enabled.Contains(t.Tag?.ToString() ?? "");

        ChkShake.IsChecked = s.ChaosScreenShakeEnabled;
        SldShake.Value = s.ChaosShakeIntensity;
        ChkFlashes.IsChecked = s.ChaosColorFlashesEnabled;
        ChkSkiaFx.IsChecked = s.ChaosSkiaFxEnabled;
        ChkPinTop.IsChecked = s.ChaosPinOnTop;
        ChkSharedHost.IsChecked = s.ChaosBubbleSharedHost;
        SldEffect.Value = s.ChaosEffectIntensity;
        ChkBoonDraft.IsChecked = s.ChaosBoonDraftEnabled;
        ChkCurses.IsChecked = s.ChaosAllowCurses;
        ChkDarters.IsChecked = s.ChaosDartersEnabled;
        ChkAnnouncer.IsChecked = s.ChaosAnnouncerEnabled;
        ChkNarrative.IsChecked = s.NarrativeModeEnabled;
        ChkBackdrop.IsChecked = s.BackdropEnabled;
        SldBackdropOpacity.Value = s.BackdropOpacity;
        ChkTunnel.IsChecked = s.ChaosTunnelEnabled;

        // Accessory keybinds (future active-use; the binds persist now so loadouts feel real).
        var keyOpts = new[] { "Q", "E", "R", "F", "Z", "X", "C", "V", "1", "2", "3", "4" };
        CmbAccKey1.ItemsSource = keyOpts;
        CmbAccKey2.ItemsSource = keyOpts;
        CmbAccKey1.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey1) ? s.ChaosAccessoryKey1 : "Q";
        CmbAccKey2.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey2) ? s.ChaosAccessoryKey2 : "E";

        ApplyExtremeGate();
    }

    /// <summary>Lock/unlock the Extreme difficulty pill from meta state; fall back off it when locked.</summary>
    private void ApplyExtremeGate()
    {
        bool unlocked = ChaosMeta.State.ExtremeUnlocked;
        SegExtreme.IsEnabled = unlocked;
        SegExtreme.Content = unlocked ? "Inescapable" : "Inescapable 🔒";
        if (!unlocked)
        {
            // The lock keeps its mystery on the face; the hover gives the exact path.
            int cost = ChaosUpgrades.ById("extreme_tier")?.Cost ?? 0;
            SegExtreme.ToolTip = "a deeper door. she sells the key in the Toybox: "
                + $"finish {ChaosLessons.T_EXTREME_TIER} relentless descents, reach Devoted "
                + $"({ChaosRanks.Thresholds[(int)ChaosRank.Devoted]} descents), then train it for ✦{cost}.";
            ToolTipService.SetShowOnDisabled(SegExtreme, true);
        }
        ApplyDifficultyPillTips(extremeUnlocked: unlocked);
        if (!unlocked && SegExtreme.IsChecked == true)
            SetSegment(GrpDifficulty, "Hard");
    }

    /// <summary>What each pill actually changes, on hover — pay multiplier, spawn pace, field
    /// size — so picking a difficulty is a choice instead of a mystery. The locked Inescapable
    /// pill keeps its unlock-path tooltip (set in <see cref="ApplyExtremeGate"/>).</summary>
    private void ApplyDifficultyPillTips(bool extremeUnlocked)
    {
        foreach (var pill in GrpDifficulty.Children.OfType<ToggleButton>())
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
            if (tip != null) pill.ToolTip = tip;
        }
    }

    private void LoadDefaults()
    {
        SetSegment(GrpDifficulty, "Easy");
        SetSegment(GrpLength, "180");
        SetSegment(GrpMotion, "Mixed");
        _waves = 5; TxtWaves.Text = "5";
        foreach (var t in GrpPool.Children.OfType<ToggleButton>()) t.IsChecked = true;
        ChkShake.IsChecked = true; SldShake.Value = 0.8;
        ChkFlashes.IsChecked = true; SldEffect.Value = 0.85;
        ChkBoonDraft.IsChecked = true; ChkCurses.IsChecked = true;
        ChkDarters.IsChecked = true;
        ChkAnnouncer.IsChecked = true;
        ChkNarrative.IsChecked = true;
        ChkBackdrop.IsChecked = true;
        SldBackdropOpacity.Value = 0.55;
        ChkTunnel.IsChecked = false;
    }

    private void SaveToSettings()
    {
        var s = App.Settings?.Current;
        if (s == null) return;

        // Reveal-gate fallbacks are visual only — never persist them over the saved choice.
        if (!_diffAutoClamped) s.ChaosDifficulty = GetSegment(GrpDifficulty) ?? "Easy";
        if (int.TryParse(GetSegment(GrpLength), out var len)) s.ChaosRunDurationSec = len;
        s.ChaosMotionMode = GetSegment(GrpMotion) ?? "Mixed";
        s.ChaosWaveCount = _waves;

        var checkd = GrpPool.Children.OfType<ToggleButton>()
            .Where(t => t.IsChecked == true).Select(t => t.Tag?.ToString() ?? "").Where(x => x.Length > 0).ToList();
        s.ChaosEnabledVariants = (checkd.Count == 0 || checkd.Count == GrpPool.Children.OfType<ToggleButton>().Count())
            ? null : checkd;

        s.ChaosScreenShakeEnabled = ChkShake.IsChecked == true;
        s.ChaosShakeIntensity = SldShake.Value;
        s.ChaosColorFlashesEnabled = ChkFlashes.IsChecked == true;
        s.ChaosSkiaFxEnabled = ChkSkiaFx.IsChecked == true;
        s.ChaosPinOnTop = ChkPinTop.IsChecked == true;
        s.ChaosBubbleSharedHost = ChkSharedHost.IsChecked == true;
        s.ChaosEffectIntensity = SldEffect.Value;
        s.ChaosBoonDraftEnabled = ChkBoonDraft.IsChecked == true;
        s.ChaosAllowCurses = ChkCurses.IsChecked == true;
        s.ChaosDartersEnabled = ChkDarters.IsChecked == true;
        s.ChaosAnnouncerEnabled = ChkAnnouncer.IsChecked == true;
        s.NarrativeModeEnabled = ChkNarrative.IsChecked == true;
        s.BackdropEnabled = ChkBackdrop.IsChecked == true;
        s.BackdropOpacity = SldBackdropOpacity.Value;
        s.ChaosTunnelEnabled = ChkTunnel.IsChecked == true;
    }

    // ============================ run-setup controls ============================

    private void Segment_Click(object sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender;
        if (!btn.IsEnabled) { btn.IsChecked = false; return; }   // locked (Extreme)
        var grp = (Panel)btn.Parent;
        foreach (var t in grp.Children.OfType<ToggleButton>()) t.IsChecked = ReferenceEquals(t, btn);
        if (ReferenceEquals(grp, GrpDifficulty)) _diffAutoClamped = false;   // a real choice again
    }

    private void Stepper_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as Button)?.Tag?.ToString())
        {
            case "waves-": _waves = Math.Max(1, _waves - 1); TxtWaves.Text = _waves.ToString(); break;
            case "waves+": _waves = Math.Min(12, _waves + 1); TxtWaves.Text = _waves.ToString(); break;
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag?.ToString();
        var preset = ChaosBubbleVariants.Presets.FirstOrDefault(p => p.Name == name);
        if (preset == null) return;
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = preset.VariantIds.Contains(t.Tag?.ToString() ?? "");
    }

    private void BtnRandomize_Click(object sender, RoutedEventArgs e)
    {
        // Only difficulties whose pills are revealed can roll (the saved setting is untouched).
        var diffs = new List<string> { "Easy" };
        if (SegMedium.Visibility == Visibility.Visible) diffs.Add("Medium");
        if (SegHard.Visibility == Visibility.Visible) diffs.Add("Hard");
        if (ChaosMeta.State.ExtremeUnlocked) diffs.Add("Extreme");
        SetSegment(GrpDifficulty, diffs[_rng.Next(diffs.Count)]);
        SetSegment(GrpLength, new[] { "120", "180", "300" }[_rng.Next(3)]);
        SetSegment(GrpMotion, new[] { "Mixed", "FloatUp", "RainDown", "RoamBounce" }[_rng.Next(4)]);
        var pool = GrpPool.Children.OfType<ToggleButton>().ToList();
        foreach (var t in pool) t.IsChecked = _rng.NextDouble() < 0.6;
        if (!pool.Any(t => t.IsChecked == true)) pool[0].IsChecked = true;
    }

    private void BtnDefaults_Click(object sender, RoutedEventArgs e) { LoadDefaults(); ApplyExtremeGate(); }

    private bool _fallingIn;   // FALL IN clicked: the Closed handler must NOT re-attach the avatar

    private void BtnBegin_Click(object sender, RoutedEventArgs e)
    {
        // Tag picks the play mode; default Story (the sidebar hero / programmatic FallIn pass no Tag).
        var mode = (sender as FrameworkElement)?.Tag?.ToString() == "FreeDesktop"
            ? Services.Chaos.ChaosPlayMode.FreeDesktop
            : Services.Chaos.ChaosPlayMode.Story;
        _fallingIn = true;   // keep the avatar detached through the hub→run handoff (no flicker)
        SaveToSettings();
        Close();
        // Build the run from the just-saved settings (same as StartRun's null path), then stamp the mode.
        var cfg = Services.Chaos.ChaosRunConfig.FromSettings();
        cfg.PlayMode = mode;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => App.Chaos?.StartRun(cfg)));
    }

    /// <summary>The loadout sidebar's FALL IN hero button lands here — same path as the footer Story
    /// button (no Tag ⇒ Story mode).</summary>
    public void FallIn() => BtnBegin_Click(this, new RoutedEventArgs());

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ============================ main menu / view swap ============================

    /// <summary>True once the player has committed (entered the dollhouse or fallen in): the
    /// loadout sidebar + detached avatar belong to that context, not the bare menu.</summary>
    private bool _runContext;

    /// <summary>Spawn the loadout sidebar and detach the companion — deferred from the ctor so the
    /// main menu stays clean. Idempotent; safe to call from both DOLL HOUSE and (not used by) FALL IN.</summary>
    private void EnterRunContext()
    {
        if (_runContext) return;
        _runContext = true;
        App.Chaos?.ShowLoadoutSidebar();
        App.AvatarWindow?.SetChaosRunActive(true);
    }

    /// <summary>Tear the sidebar back down and re-attach the companion (unless we're falling in) —
    /// used when backing out of the dollhouse to the menu.</summary>
    private void LeaveRunContext()
    {
        if (!_runContext) return;
        _runContext = false;
        App.Chaos?.CloseLoadoutSidebar();
        if (!_fallingIn) App.AvatarWindow?.SetChaosRunActive(false);
    }

    private void ShowMenuView()
    {
        MenuView.Visibility = Visibility.Visible;
        DollhouseView.Visibility = Visibility.Collapsed;
        MenuLeftCol.Visibility = Visibility.Visible;
        MenuArtPanel.Visibility = Visibility.Visible;
        MenuOptions.Visibility = Visibility.Collapsed;
        RefreshTopBar();   // keep the menu chips current (logo wordmark renders in MenuLogoFx, Skia)
        StartMenuIntro();
        StartMenuFog();
        StartFlipbook();
        StartMenuMusic();
    }

    private void ShowDollhouseView()
    {
        MenuView.Visibility = Visibility.Collapsed;
        DollhouseView.Visibility = Visibility.Visible;
        StopMenuMusic();
    }

    private void Menu_FallIn_Click(object sender, RoutedEventArgs e)
    {
        // Straight into a descent. No sidebar (it would only flash before the run swaps in the
        // real HUD) — just detach the companion for the handoff, the way FALL IN always has.
        StopMenuMusic();
        App.AvatarWindow?.SetChaosRunActive(true);
        FallIn();
    }

    private void Menu_Dollhouse_Click(object sender, RoutedEventArgs e)
    {
        EnterRunContext();   // the loadout sidebar belongs beside the dollhouse
        StopMenuFog();
        StopFlipbook();
        ShowDollhouseView();
        ShowTab("loadout");
    }

    /// <summary>Story is greyed (BtnMenuStory.IsEnabled = StoryModeEnabled); this only ever fires
    /// if the flag flips true, at which point it would route into the story descent.</summary>
    private void Menu_Story_Click(object sender, RoutedEventArgs e) { /* coming soon — disabled */ }

    private void Menu_Options_Click(object sender, RoutedEventArgs e)
    {
        if (OptFullscreen != null) OptFullscreen.IsChecked = WindowState == WindowState.Maximized;
        MenuLeftCol.Visibility = Visibility.Collapsed;
        MenuArtPanel.Visibility = Visibility.Collapsed;
        MenuOptions.Visibility = Visibility.Visible;
        StopMenuFog();   // art hidden — no need to render
        StopFlipbook();
    }

    private void Options_Back_Click(object sender, RoutedEventArgs e)
    {
        MenuOptions.Visibility = Visibility.Collapsed;
        MenuLeftCol.Visibility = Visibility.Visible;
        MenuArtPanel.Visibility = Visibility.Visible;
        StartMenuFog();
        StartFlipbook();
    }

    private void Menu_Exit_Click(object sender, RoutedEventArgs e) => Close();

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

    private void Menu_HowTo_Click(object sender, RoutedEventArgs e)
    {
        _howToIdx = 0;
        HowToShow();
        MenuHowTo.Visibility = Visibility.Visible;
    }

    private void HowTo_Close_Click(object sender, RoutedEventArgs e) => MenuHowTo.Visibility = Visibility.Collapsed;

    // backdrop dismiss: only when the click lands on the dim backdrop itself, not the card
    private void HowTo_Backdrop_Click(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, MenuHowTo)) MenuHowTo.Visibility = Visibility.Collapsed;
    }

    private void HowTo_Back_Click(object sender, RoutedEventArgs e)
    {
        if (_howToIdx > 0) { _howToIdx--; HowToShow(); }
    }

    private void HowTo_Next_Click(object sender, RoutedEventArgs e)
    {
        if (_howToIdx < _howToCards.Length - 1) { _howToIdx++; HowToShow(); }
        else MenuHowTo.Visibility = Visibility.Collapsed;   // last card: "DONE" closes
    }

    private void HowToShow()
    {
        var card = _howToCards[_howToIdx];

        HowToStep.Text = $"STEP {_howToIdx + 1} / {_howToCards.Length}";
        HowToTitle.Text = card.Title;

        // image (graceful hide when no screenshot dropped in yet)
        var img = ChaosArt.Resolve("howto", card.Image);
        HowToImageBrush.ImageSource = img;
        HowToImageBox.Visibility = img != null ? Visibility.Visible : Visibility.Collapsed;

        // body lines
        HowToBody.Children.Clear();
        foreach (var line in card.Lines)
            HowToBody.Children.Add(BuildHowToLine(line));

        // dots
        HowToDots.Children.Clear();
        for (int i = 0; i < _howToCards.Length; i++)
        {
            HowToDots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8, Margin = new Thickness(4, 0, 4, 0),
                Fill = i == _howToIdx
                    ? (Brush)FindResource("Pink")
                    : new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            });
        }

        // nav state
        HowToBack.Visibility = _howToIdx > 0 ? Visibility.Visible : Visibility.Hidden;
        HowToNext.Content = _howToIdx < _howToCards.Length - 1 ? "NEXT  ›" : "DONE";
    }

    private FrameworkElement BuildHowToLine(HowToLine line)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 21, Margin = new Thickness(0, 0, 0, 9) };

        if (!string.IsNullOrEmpty(line.Lead))
        {
            tb.Inlines.Add(new System.Windows.Documents.Run(line.Lead + "  ")
            {
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex(line.LeadColor),
            });
        }
        // body supports inline **bold** spans
        bool bold = false;
        foreach (var part in line.Body.Split("**"))
        {
            if (part.Length > 0)
                tb.Inlines.Add(new System.Windows.Documents.Run(part)
                {
                    Foreground = bold ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xDE)),
                    FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                });
            bold = !bold;
        }

        if (string.IsNullOrEmpty(line.Emoji)) return tb;

        // emoji-led row: glyph in a fixed gutter, text beside it
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = new TextBlock { Text = line.Emoji, FontSize = 17, VerticalAlignment = VerticalAlignment.Top, Foreground = BrushFromHex(line.EmojiColor) };
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(tb);
        return grid;
    }

    private static Brush BrushFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Brushes.White;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.White; }
    }

    private void Back_To_Menu_Click(object sender, RoutedEventArgs e)
    {
        SaveToSettings();    // keep any loadout/setup tweaks made in the dollhouse
        LeaveRunContext();
        ShowMenuView();
    }

    // ============================ menu art motion (breathing, wobble, neon glow) ============================

    /// <summary>Almost-imperceptible life on the menu art: a slow breathing scale, a gentle
    /// up/down drift and a tiny wobble — plus a pulsing pink neon glow on the border.</summary>
    private void SetupMenuMotion()
    {
        var ease = new System.Windows.Media.Animation.SineEase
        { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
        System.Windows.Media.Animation.DoubleAnimation Loop(double from, double to, double secs) =>
            new(from, to, new Duration(TimeSpan.FromSeconds(secs)))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = ease,
            };

        // Breathing happens INSIDE the fixed rounded image Borders, by zooming the ImageBrush
        // (RelativeTransform, 0..1 space) — NOT by scaling the Border. Scaling the Border made it
        // overflow the panel and its square-ish corner poked past the rounded neon edge (the "box").
        // Baseline 1.02 zoom gives overscan so the tiny rotate never bares an edge.
        var grp = new TransformGroup();
        var sx = new ScaleTransform(1.02, 1.02, 0.5, 0.5);
        var rot = new RotateTransform(0, 0.5, 0.5);
        grp.Children.Add(sx); grp.Children.Add(rot);
        MenuArtBrush.RelativeTransform = grp;
        MenuArtTopBrush.RelativeTransform = grp;   // shared so both crossfade layers breathe together
        sx.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(1.02, 1.035, 6.5));
        sx.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(1.02, 1.035, 6.5));
        rot.BeginAnimation(RotateTransform.AngleProperty, Loop(-0.2, 0.2, 7.5));

        // pulsing neon border glow
        if (MenuArtPanel.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
        {
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, Loop(16, 34, 2.4));
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, Loop(0.55, 0.95, 2.4));
        }
    }

    // ============================ menu flipbook (crossfading frames) ============================

    private ImageSource?[]? _frames;
    private (int f, int holdMs)[] _flipSeq = System.Array.Empty<(int, int)>();
    private int _seqPos;
    private int _shownFrame = -1;
    private DispatcherTimer? _flipTimer;

    /// <summary>Load the menu art: the 3-frame flipbook if all of menu_1/2/3.png are present,
    /// else a single still (menu.png, then the banner). Shows the first frame.</summary>
    private void LoadMenuFrames()
    {
        // frame indices: 0 idle · 1 blink · 2 invite · 3 kiss · 4 wink · 5 hair-tuck
        var all = new ImageSource?[6];
        for (int i = 0; i < 6; i++) all[i] = ChaosArt.ResolveMenuFrame(i + 1);
        LoadMenuFx();   // SK frames + per-frame fx masks + tuning (Skia renders the scene)

        if (all[0] != null && all[1] != null && all[2] != null)   // core 3 must exist
        {
            _frames = all;
            _flipSeq = BuildFlipSeq();
            _baseIdx = 0; _topIdx = 0; _fadeT = 1f; _fading = false;
            _shownFrame = 0; _seqPos = 0;
            if (_skFrames[0] != null) MenuArtBrush.ImageSource = null; else MenuArtBrush.ImageSource = all[0];
            return;
        }
        // fallback: a single still
        var still = ChaosArt.ResolveMenu();
        if (still != null)
        {
            _baseIdx = -1;
            if (_skStill != null) MenuArtBrush.ImageSource = null; else MenuArtBrush.ImageSource = still;
            return;
        }
        var banner = ChaosArt.ResolveBanner();
        if (banner != null)
        {
            _baseIdx = -1;
            if (_skStill != null) MenuArtBrush.ImageSource = null;
            else { MenuArtBrush.ImageSource = banner; MenuArtBrush.Stretch = System.Windows.Media.Stretch.Uniform; }
        }
    }

    /// <summary>Build the loop: settle on idle (0) between each expression so they read as momentary.
    /// Any frame that's missing from disk is skipped. Holds in ms; the crossfade rides on top.</summary>
    private (int f, int holdMs)[] BuildFlipSeq()
    {
        const int IDLE = 6000;   // rest on idle ~6s, then one expression, then back to idle
        var seq = new List<(int, int)>();
        void Add(int idx, int hold) { if (_frames != null && idx < _frames.Length && _frames[idx] != null) seq.Add((idx, hold)); }
        Add(0, IDLE);  Add(1, 1900);  // idle, blink     (+1s linger)
        Add(0, IDLE);  Add(4, 2300);  // idle, wink      (+1s linger)
        Add(0, IDLE);  Add(2, 3000);  // idle, invite    (+1s linger)
        Add(0, IDLE);  Add(3, 2700);  // idle, kiss      (+1s linger)
        Add(0, IDLE);  Add(5, 2500);  // idle, hair-tuck (+1s linger)
        if (seq.Count == 0) seq.Add((0, IDLE));
        return seq.ToArray();
    }

    private void StartFlipbook()
    {
        if (_frames == null || _frames.Length < 2 || _flipSeq.Length == 0) return;
        if (_flipTimer == null)
        {
            _flipTimer = new DispatcherTimer();
            _flipTimer.Tick += (_, _) => AdvanceFlip();
        }
        _flipTimer.Interval = TimeSpan.FromMilliseconds(_flipSeq[_seqPos].holdMs);
        _flipTimer.Start();
    }

    private void StopFlipbook() => _flipTimer?.Stop();

    private void AdvanceFlip()
    {
        if (_frames == null || _flipSeq.Length == 0) return;
        _seqPos = (_seqPos + 1) % _flipSeq.Length;
        var step = _flipSeq[_seqPos];
        CrossfadeTo(step.f, 550);   // gentler crossfade
        if (_flipTimer != null) _flipTimer.Interval = TimeSpan.FromMilliseconds(step.holdMs);
    }

    /// <summary>Crossfade the top layer in, then settle it onto the base layer.</summary>
    private void CrossfadeTo(int idx, double fadeMs)
    {
        if (_frames == null || _frames.Length == 0) return;
        idx = ((idx % _frames.Length) + _frames.Length) % _frames.Length;
        var src = _frames[idx];
        if (src == null || idx == _shownFrame) return;
        _shownFrame = idx;

        if (_skFrames[idx] != null)   // Skia crossfade (the render timer drives _fadeT)
        {
            _topIdx = idx; _fadeT = 0f; _fading = true; _fadeDurSec = (float)(fadeMs / 1000.0);
            return;
        }

        // WPF fallback when this frame has no SK image
        MenuArtTopBrush.ImageSource = src;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(fadeMs)))
        {
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        };
        fade.Completed += (_, _) =>
        {
            MenuArtBrush.ImageSource = src;
            MenuArtTopBox.BeginAnimation(UIElement.OpacityProperty, null);
            MenuArtTopBox.Opacity = 0;
        };
        MenuArtTopBox.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private long _clickReadyAtMs;   // spam-click guard: no new click until the current pose plays out

    /// <summary>Clicking the art advances the flipbook now and restarts the dwell timer — but only
    /// once the previous click's pose has fully played (crossfade in + linger + fade back), so
    /// spam-clicking can't race through every animation.</summary>
    private void MenuArt_Click(object sender, MouseButtonEventArgs e)
    {
        if (_frames == null || _frames.Length < 2) return;
        long now = Environment.TickCount64;
        if (now < _clickReadyAtMs) return;   // still cooling down
        AdvanceFlip();
        int hold = _flipSeq.Length > 0 ? _flipSeq[_seqPos].holdMs : 1000;
        _clickReadyAtMs = now + 550 + hold + 550;   // fade-in + linger + fade-back
        if (_flipTimer != null) { _flipTimer.Stop(); _flipTimer.Start(); }
    }

    // ============================ menu scene (Skia: art + authored glint FX) ============================
    //
    // The SKElement (MenuFog) renders the whole menu scene so the glint can be true additive (Plus)
    // light on the real pixels. Placement is authored per frame in assets/Chaos/menu_{n}_fx.png
    // (R=Glow, G=Twinkle, B=Sheen — painted with tools/menu_glint_painter.py), and the per-effect
    // intensity/frequency come from assets/Chaos/menu_fx.json. Gated on Enhanced FX (ChaosSkiaFxEnabled);
    // with it off, or with no fx masks present, the art just shows plainly.

    private const float MenuDt = 0.033f;          // ~30fps render tick
    private const float SweepDur = 1.4f;          // seconds the sheen band takes to cross
    private const float SweepPeriodBase = 7.5f;   // base seconds between sweeps (÷ frequency)

    private DispatcherTimer? _fogTimer;
    private readonly SKImage?[] _skFrames = new SKImage?[6];
    private readonly SKImage?[] _fxMasks = new SKImage?[6];   // R=glow G=twinkle B=sheen
    private SKImage? _skStill, _fxStill, _bloomStill;
    private readonly SKImage?[] _blooms = new SKImage?[6];    // per-frame glow bloom (cached so FX can crossfade)
    private SKColorFilter? _rToA, _gToA, _bToA;               // channel -> alpha (white) for DstIn
    private int _baseIdx, _topIdx = -1;                       // -1 = use _skStill
    private float _fadeT = 1f, _fadeDurSec = 0.55f;
    private bool _fading;
    private float _breathClock;
    // tuning from menu_fx.json (intensity, frequency) per effect
    private float _glowI = 1f, _glowF = 1f, _twkI = 1f, _twkF = 1f, _shI = 1f, _shF = 1f;
    // twinkle particles + per-frame candidate spots (from the green channel)
    private struct Tw { public float Nx, Ny, Age, Life, Size; public SKColor Col; }
    private readonly List<Tw> _tw = new();
    private readonly (float nx, float ny, float w)[][] _twSpots = new (float, float, float)[6][];
    private (float nx, float ny, float w)[] _twStill = System.Array.Empty<(float, float, float)>();
    private float _twAccum, _sweepClock;
    // pink fog drifting in front of the art
    private struct FogPuff { public float X, Y, R, VX, VY, Phase, PhaseSpd, BaseA; }
    private readonly List<FogPuff> _fog = new();
    private int _fogW, _fogH;
    // one-shot intro reveal (fade + settle) when the menu appears
    private const float IntroDur = 1.1f;
    private float _introClock;
    private bool _introActive;

    // ---- logo wordmark glint (separate scene: menu_logo.png + menu_logo_fx.png, tuned by
    // menu_logo_fx.json; rendered in MenuLogoFx as additive light on the logo's real pixels) ----
    private SKImage? _logoImg, _logoFx, _logoBloom;
    private (float nx, float ny, float w)[] _logoTwSpots = System.Array.Empty<(float, float, float)>();
    private float _lGlowI = 1f, _lGlowF = 1f, _lTwkI = 1f, _lTwkF = 1f, _lShI = 1f, _lShF = 1f;
    private readonly List<Tw> _logoTw = new();
    private float _logoTwAccum, _logoSweepClock;

    private static SKImage? LoadSk(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var s = File.OpenRead(path);
            return SKImage.FromEncodedData(s);
        }
        catch { return null; }
    }

    private SKImage? FrameImage(int idx) =>
        idx >= 0 && idx < _skFrames.Length && _skFrames[idx] != null ? _skFrames[idx] : _skStill;

    private SKImage? FxMask(int idx) =>
        idx >= 0 && idx < _fxMasks.Length && _fxMasks[idx] != null ? _fxMasks[idx] : _fxStill;

    private (float nx, float ny, float w)[] TwSpots(int idx) =>
        (idx >= 0 && idx < _twSpots.Length && _twSpots[idx] != null) ? _twSpots[idx] : _twStill;

    /// <summary>Load SK frames + per-frame fx masks + tuning + channel filters. Called from LoadMenuFrames.</summary>
    private void LoadMenuFx()
    {
        for (int i = 0; i < 6; i++)
        {
            _skFrames[i] = LoadSk(ChaosArt.MenuFramePath(i + 1));
            var fxp = ChaosArt.FilePath($"menu_{i + 1}_fx.png");
            _fxMasks[i] = LoadSk(fxp);
            _twSpots[i] = ExtractSpots(fxp, 1);   // green channel
        }
        _skStill = LoadSk(ChaosArt.FilePath("menu.png")) ?? LoadSk(ChaosArt.FilePath("banner.png"));
        var stillFx = ChaosArt.FilePath("menu_fx.png");
        _fxStill = LoadSk(stillFx);
        _twStill = ExtractSpots(stillFx, 1);

        _rToA ??= ChanToAlpha(0); _gToA ??= ChanToAlpha(1); _bToA ??= ChanToAlpha(2);
        LoadFxTuning();
        BuildAllBlooms();
        LoadMenuLogoFx();
    }

    /// <summary>Load the logo wordmark + its authored glint mask + tuning (its own files so it never
    /// clobbers the character-art FX). Drawn in MenuLogoFx, transparent — no box around the logo.</summary>
    private void LoadMenuLogoFx()
    {
        _rToA ??= ChanToAlpha(0); _gToA ??= ChanToAlpha(1); _bToA ??= ChanToAlpha(2);
        _logoImg = LoadSk(ChaosArt.FilePath("menu_logo.png"));
        var fxp = ChaosArt.FilePath("menu_logo_fx.png");
        _logoFx = LoadSk(fxp);
        _logoTwSpots = ExtractSpots(fxp, 1);   // green channel = twinkle anchors
        try
        {
            var p = ChaosArt.FilePath("menu_logo_fx.json");
            if (p != null && File.Exists(p))
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(p));
                float Get(string fx, string k, float def) => (float?)o[fx]?[k] ?? def;
                _lGlowI = Get("glow", "intensity", 1f); _lGlowF = Get("glow", "frequency", 1f);
                _lTwkI = Get("twinkle", "intensity", 1f); _lTwkF = Get("twinkle", "frequency", 1f);
                _lShI = Get("sheen", "intensity", 1f); _lShF = Get("sheen", "frequency", 1f);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHub.LoadMenuLogoFx tuning: {E}", ex.Message); }
        _logoBloom?.Dispose();
        _logoBloom = BuildBloom(_logoImg, _logoFx);
    }

    /// <summary>Colour filter: output white with alpha = the given source channel (0=R,1=G,2=B).
    /// Lets a painted RGB mask act as a per-effect alpha matte for DstIn compositing.</summary>
    private static SKColorFilter ChanToAlpha(int ch)
    {
        var m = new float[20];
        m[4] = 1; m[9] = 1; m[14] = 1;          // RGB -> white
        m[15 + ch] = 1;                          // A = source channel
        return SKColorFilter.CreateColorMatrix(m);
    }

    private void LoadFxTuning()
    {
        try
        {
            var p = ChaosArt.FilePath("menu_fx.json");
            if (p == null) return;
            var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(p));
            float Get(string fx, string k, float def) => (float?)o[fx]?[k] ?? def;
            _glowI = Get("glow", "intensity", 1f); _glowF = Get("glow", "frequency", 1f);
            _twkI = Get("twinkle", "intensity", 1f); _twkF = Get("twinkle", "frequency", 1f);
            _shI = Get("sheen", "intensity", 1f); _shF = Get("sheen", "frequency", 1f);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHub.LoadFxTuning: {E}", ex.Message); }
    }

    /// <summary>Scan a downsized copy of an fx mask for bright spots in one channel — the twinkle
    /// spawn anchors. Returns normalized (x,y,weight), the brightest ~10 cells of a coarse grid.</summary>
    private static (float nx, float ny, float w)[] ExtractSpots(string? path, int channel)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return System.Array.Empty<(float, float, float)>();
            using var raw = SKBitmap.Decode(path);
            if (raw == null) return System.Array.Empty<(float, float, float)>();
            int tw = 96, th = Math.Max(1, raw.Height * 96 / Math.Max(1, raw.Width));
            using var small = raw.Resize(new SKImageInfo(tw, th), SKFilterQuality.Medium) ?? raw;
            int w = small.Width, h = small.Height;
            const int cell = 8;
            int cols = Math.Max(1, w / cell), rows = Math.Max(1, h / cell);
            var best = new (float v, int x, int y)[cols * rows];
            float gmax = 0.001f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = small.GetPixel(x, y);
                    float v = (channel == 0 ? c.Red : channel == 1 ? c.Green : c.Blue) / 255f;
                    int ci = Math.Min(rows - 1, y * rows / h) * cols + Math.Min(cols - 1, x * cols / w);
                    if (v > best[ci].v) best[ci] = (v, x, y);
                    if (v > gmax) gmax = v;
                }
            var list = new List<(float, float, float)>();
            foreach (var b in best)
                if (b.v > gmax * 0.5f)
                    list.Add(((b.x + 0.5f) / w, (b.y + 0.5f) / h, b.v / gmax));
            return list.OrderByDescending(z => z.Item3).Take(10).ToArray();
        }
        catch { return System.Array.Empty<(float, float, float)>(); }
    }

    /// <summary>Start the menu render loop — ALWAYS runs while the menu art shows (Skia draws the art);
    /// the authored FX inside gate on Enhanced FX.</summary>
    private void StartMenuFog()
    {
        MenuFog.Visibility = Visibility.Visible;
        if (_fogTimer == null)
        {
            _fogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _fogTimer.Tick += (_, _) => StepMenu();
        }
        _fogTimer.Start();
    }

    private void StopMenuFog() => _fogTimer?.Stop();

    private void StepMenu()
    {
        bool fx = App.Settings?.Current?.ChaosSkiaFxEnabled == true;
        if (_fading)
        {
            _fadeT += MenuDt / Math.Max(0.05f, _fadeDurSec);
            if (_fadeT >= 1f) { _fadeT = 1f; _fading = false; _baseIdx = _topIdx; }
        }
        _breathClock += MenuDt;
        if (_introActive) { _introClock += MenuDt; if (_introClock >= IntroDur) _introActive = false; }
        if (fx) { StepFogPuffs(); StepTwinkles(); StepLogoTwinkles(); }
        MenuFog.InvalidateVisual();
        MenuLogoFx.InvalidateVisual();
    }

    private void StepTwinkles()
    {
        _sweepClock += MenuDt;
        var spots = TwSpots(_baseIdx);
        if (spots.Length > 0)
        {
            _twAccum -= MenuDt;
            int maxn = Math.Max(1, (int)Math.Round(3 * _twkI));
            if (_twAccum <= 0f)
            {
                _twAccum = (0.35f + (float)_rng.NextDouble() * 0.45f) / Math.Max(0.05f, _twkF);
                if (_tw.Count < maxn)
                {
                    float total = 0; foreach (var s in spots) total += s.w + 0.05f;
                    float pick = (float)_rng.NextDouble() * total; var hs = spots[0];
                    foreach (var s in spots) { pick -= s.w + 0.05f; if (pick <= 0) { hs = s; break; } }
                    double r = _rng.NextDouble();
                    var col = r < 0.55 ? new SKColor(255, 255, 255) : r < 0.8 ? new SKColor(255, 230, 176) : new SKColor(255, 199, 230);
                    _tw.Add(new Tw
                    {
                        Nx = hs.nx, Ny = hs.ny, Age = 0,
                        Life = 0.7f + (float)_rng.NextDouble() * 0.45f,
                        Size = 6f + (float)_rng.NextDouble() * 8f,
                        Col = col,
                    });
                }
            }
        }
        for (int i = _tw.Count - 1; i >= 0; i--)
        {
            var t = _tw[i]; t.Age += MenuDt;
            if (t.Age >= t.Life) _tw.RemoveAt(i); else _tw[i] = t;
        }
    }

    private static SKRect CoverRect(SKImage img, SKImageInfo info)
    {
        float ew = info.Width, eh = info.Height, iw = img.Width, ih = img.Height;
        float s = Math.Max(ew / iw, eh / ih);
        float dw = iw * s, dh = ih * s;
        return new SKRect((ew - dw) / 2f, (eh - dh) / 2f, (ew + dw) / 2f, (eh + dh) / 2f);
    }

    /// <summary>Uniform "contain" fit (whole image visible, centered, never cropped) — used for the
    /// logo so the full wordmark shows on a transparent surface.</summary>
    private static SKRect ContainRect(SKImage img, SKImageInfo info)
    {
        float ew = info.Width, eh = info.Height, iw = img.Width, ih = img.Height;
        float s = Math.Min(ew / iw, eh / ih);
        float dw = iw * s, dh = ih * s;
        return new SKRect((ew - dw) / 2f, (eh - dh) / 2f, (ew + dw) / 2f, (eh + dh) / 2f);
    }

    /// <summary>Paint the logo wordmark + authored glint FX on a fully transparent surface (no box).
    /// FX gate on Enhanced FX; with it off (or no mask) the logo just shows plainly.</summary>
    private void MenuLogoFx_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var info = e.Info;
        if (_logoImg == null || info.Width <= 0 || info.Height <= 0) return;

        var rect = ContainRect(_logoImg, info);
        using (var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            canvas.DrawImage(_logoImg, rect, p);

        if (App.Settings?.Current?.ChaosSkiaFxEnabled == true)
        {
            try { DrawLogoFx(canvas, rect); }
            catch (Exception ex) { App.Logger?.Debug("ChaosHub.DrawLogoFx: {E}", ex.Message); }
        }
    }

    /// <summary>Step the logo's twinkle particles (same model as the character art, separate state).</summary>
    private void StepLogoTwinkles()
    {
        _logoSweepClock += MenuDt;
        if (_logoTwSpots.Length > 0)
        {
            _logoTwAccum -= MenuDt;
            int maxn = Math.Max(1, (int)Math.Round(3 * _lTwkI));
            if (_logoTwAccum <= 0f)
            {
                _logoTwAccum = (0.35f + (float)_rng.NextDouble() * 0.45f) / Math.Max(0.05f, _lTwkF);
                if (_logoTw.Count < maxn)
                {
                    float total = 0; foreach (var s in _logoTwSpots) total += s.w + 0.05f;
                    float pick = (float)_rng.NextDouble() * total; var hs = _logoTwSpots[0];
                    foreach (var s in _logoTwSpots) { pick -= s.w + 0.05f; if (pick <= 0) { hs = s; break; } }
                    double r = _rng.NextDouble();
                    var col = r < 0.55 ? new SKColor(255, 255, 255) : r < 0.8 ? new SKColor(255, 230, 176) : new SKColor(255, 199, 230);
                    _logoTw.Add(new Tw
                    {
                        Nx = hs.nx, Ny = hs.ny, Age = 0,
                        Life = 0.7f + (float)_rng.NextDouble() * 0.45f,
                        Size = 6f + (float)_rng.NextDouble() * 8f,
                        Col = col,
                    });
                }
            }
        }
        for (int i = _logoTw.Count - 1; i >= 0; i--)
        {
            var t = _logoTw[i]; t.Age += MenuDt;
            if (t.Age >= t.Life) _logoTw.RemoveAt(i); else _logoTw[i] = t;
        }
    }

    /// <summary>Authored glint over the logo: breathing glow bloom, a masked sheen sweep, and twinkle
    /// pops — single static frame (no crossfade). Mirrors the painter preview, tuned by menu_logo_fx.json.</summary>
    private void DrawLogoFx(SKCanvas canvas, SKRect rect)
    {
        float t = _breathClock;

        if (_lGlowI > 0.001f && _logoBloom != null)
        {
            float pulse = (0.42f + 0.22f * (float)Math.Sin(t * 1.6 * _lGlowF)) * _lGlowI;
            DrawBloom(canvas, rect, _logoBloom, pulse);
        }

        if (_bToA != null && _lShI > 0.001f && _logoFx != null)
        {
            float period = SweepPeriodBase / Math.Max(0.05f, _lShF);
            float ph = _logoSweepClock % period;
            if (ph < SweepDur)
            {
                float pp = ph / SweepDur;
                float env = (float)Math.Sin(Math.PI * pp);
                float center = -0.15f + 1.3f * pp;
                DrawSheen(canvas, rect, _logoFx, center, 0.5f * env * _lShI);
            }
        }

        if (_logoTw.Count > 0 && _lTwkI > 0.001f)
        {
            float surf = rect.Height / 240f;   // twinkle size scaled to the logo
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            foreach (var tw in _logoTw)
            {
                float env = (float)Math.Sin(Math.PI * (tw.Age / tw.Life)) * _lTwkI;
                if (env <= 0.01f) continue;
                float cx = rect.Left + tw.Nx * rect.Width;
                float cy = rect.Top + tw.Ny * rect.Height;
                float r = tw.Size * surf * (0.7f + 0.3f * env);
                using (var glow = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 2.4f,
                    new[] { tw.Col.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 150)), tw.Col.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
                {
                    paint.Shader = glow; canvas.DrawCircle(cx, cy, r * 2.4f, paint);
                }
                paint.Shader = null;
                paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 220));
                canvas.DrawCircle(cx, cy, r * 0.45f, paint);
            }
        }
    }

    /// <summary>Bake a frame's glow pixels (art × glow-channel), blurred, into an SKImage. Cached per
    /// frame so the glow can crossfade between poses instead of hard-swapping at the transition.</summary>
    private SKImage? MakeBloom(int idx) => BuildBloom(FrameImage(idx), FxMask(idx));

    private SKImage? BuildBloom(SKImage? src, SKImage? mask)
    {
        if (src == null || mask == null || _rToA == null) return null;
        try
        {
            int bw = Math.Min(src.Width, 540);
            int bh = Math.Max(1, src.Height * bw / src.Width);
            var bi = new SKImageInfo(bw, bh, SKColorType.Rgba8888, SKAlphaType.Premul);
            var rect = new SKRect(0, 0, bw, bh);
            using var s1 = SKSurface.Create(bi);
            s1.Canvas.Clear(SKColors.Transparent);
            using (var ap = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium })
                s1.Canvas.DrawImage(src, rect, ap);
            using (var mp = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = _rToA, FilterQuality = SKFilterQuality.Medium })
                s1.Canvas.DrawImage(mask, rect, mp);
            using var masked = s1.Snapshot();
            using var s2 = SKSurface.Create(bi);
            s2.Canvas.Clear(SKColors.Transparent);
            using (var bp = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(bw * 0.013f, bw * 0.013f) })
                s2.Canvas.DrawImage(masked, rect, bp);
            return s2.Snapshot();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHub.MakeBloom: {E}", ex.Message); return null; }
    }

    private void BuildAllBlooms()
    {
        for (int i = 0; i < 6; i++) { _blooms[i]?.Dispose(); _blooms[i] = MakeBloom(i); }
        _bloomStill?.Dispose(); _bloomStill = MakeBloom(-1);
    }

    private SKImage? BloomFor(int idx) =>
        (idx >= 0 && idx < _blooms.Length && _blooms[idx] != null) ? _blooms[idx] : _bloomStill;

    // ---- pink fog ----
    private static float Frac(float v) { v -= (float)Math.Floor(v); return v; }

    private void InitFog(int w, int h)
    {
        _fog.Clear();
        _fogW = w; _fogH = h;
        int n = 14;                                     // denser cloud
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            _fog.Add(new FogPuff
            {
                X = w * (0.10f + 0.85f * Frac(t * 1.7f)),
                Y = h * (0.40f + 0.65f * Frac(t * 2.3f)),
                R = w * (0.34f + 0.26f * Frac(t * 3.1f)),   // bigger puffs
                VX = w * (0.004f + 0.006f * Frac(t * 5f)) * (i % 2 == 0 ? 1 : -1),
                VY = -h * (0.003f + 0.004f * Frac(t * 4f)),
                Phase = t * 6.283f,
                PhaseSpd = 0.012f + 0.01f * Frac(t * 6f),
                BaseA = 0.34f + 0.22f * Frac(t * 7f),   // more prominent pink
            });
        }
    }

    private void StepFogPuffs()
    {
        for (int i = 0; i < _fog.Count; i++)
        {
            var p = _fog[i];
            p.X += p.VX; p.Y += p.VY; p.Phase += p.PhaseSpd;
            if (p.Y + p.R < 0) { p.Y = _fogH + p.R; p.X = _fogW * (0.15f + 0.7f * Frac(p.Phase)); }
            if (p.X - p.R > _fogW) p.X = -p.R;
            if (p.X + p.R < 0) p.X = _fogW + p.R;
            _fog[i] = p;
        }
    }

    private void DrawFog(SKCanvas canvas, SKImageInfo info)
    {
        if (_fog.Count == 0 || _fogW != info.Width || _fogH != info.Height) InitFog(info.Width, info.Height);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var p in _fog)
        {
            float a = p.BaseA * (0.7f + 0.3f * (float)Math.Sin(p.Phase));
            if (a <= 0.01f) continue;
            var c = new SKPoint(p.X, p.Y);
            using var shader = SKShader.CreateRadialGradient(
                c, p.R,
                new[] { new SKColor(0xE8, 0x43, 0x93, (byte)(a * 255)), new SKColor(0xE8, 0x43, 0x93, 0) },
                null, SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawCircle(c, p.R, paint);
        }
    }

    /// <summary>Kick the one-shot intro reveal (fade + settle) the next time the scene paints.</summary>
    private void StartMenuIntro() { _introClock = 0f; _introActive = true; }

    private static float EaseOutCubic(float p) { p = Math.Clamp(p, 0f, 1f); float u = 1f - p; return 1f - u * u * u; }

    private void DisposeMenuSkia()
    {
        for (int i = 0; i < _skFrames.Length; i++) { _skFrames[i]?.Dispose(); _skFrames[i] = null; }
        for (int i = 0; i < _fxMasks.Length; i++) { _fxMasks[i]?.Dispose(); _fxMasks[i] = null; }
        for (int i = 0; i < _blooms.Length; i++) { _blooms[i]?.Dispose(); _blooms[i] = null; }
        _skStill?.Dispose(); _skStill = null;
        _fxStill?.Dispose(); _fxStill = null;
        _bloomStill?.Dispose(); _bloomStill = null;
        _logoImg?.Dispose(); _logoImg = null;
        _logoFx?.Dispose(); _logoFx = null;
        _logoBloom?.Dispose(); _logoBloom = null;
    }

    private void MenuFog_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var info = e.Info;
        if (info.Width <= 0 || info.Height <= 0) return;

        float rad = 22f * (MenuFog.ActualWidth > 0 ? (float)(info.Width / MenuFog.ActualWidth) : 1f);
        canvas.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, info.Width, info.Height), rad, rad), antialias: true);

        bool fx = App.Settings?.Current?.ChaosSkiaFxEnabled == true;

        // intro reveal: fade the whole scene in (one shot) the first time the menu paints
        float ia = _introActive ? EaseOutCubic(_introClock / IntroDur) : 1f;
        bool introLayer = ia < 0.999f;
        if (introLayer) canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)(ia * 255)) });

        canvas.Save();
        ApplyBreath(canvas, info);
        DrawMenuArt(canvas, info);
        if (fx) { try { DrawAuthoredFx(canvas, info); } catch (Exception ex) { App.Logger?.Debug("ChaosHub.DrawAuthoredFx: {E}", ex.Message); } }
        canvas.Restore();

        // pink fog drifts in front of the character (fades in with the intro layer)
        if (fx) { try { DrawFog(canvas, info); } catch (Exception ex) { App.Logger?.Debug("ChaosHub.DrawFog: {E}", ex.Message); } }

        if (introLayer) canvas.Restore();
    }

    /// <summary>Slow breathing scale + drift (the SKElement is outside the WPF MenuArtMotion transform,
    /// so the art animates here). Baseline scale &gt;1 gives overscan so the drift never bares an edge.</summary>
    private void ApplyBreath(SKCanvas canvas, SKImageInfo info)
    {
        float t = _breathClock;
        float s = 1.035f + 0.012f * (float)Math.Sin(t * 0.9);
        float dx = 0.0025f * info.Width * (float)Math.Sin(t * 0.50);
        float dy = 0.0040f * info.Height * (float)Math.Sin(t * 0.78);
        float ang = 0.18f * (float)Math.Sin(t * 0.62);
        // intro: start a touch zoomed-in + lifted, settle into place (ease-out)
        if (_introActive)
        {
            float e = EaseOutCubic(_introClock / IntroDur);
            s *= 1.10f - 0.10f * e;
            dy += (1f - e) * 0.03f * info.Height;
        }
        canvas.Translate(info.Width / 2f, info.Height / 2f);
        canvas.Scale(s, s);
        canvas.RotateDegrees(ang);
        canvas.Translate(-info.Width / 2f + dx, -info.Height / 2f + dy);
    }

    private void DrawMenuArt(SKCanvas canvas, SKImageInfo info)
    {
        var baseImg = FrameImage(_baseIdx);
        if (baseImg == null) return;
        using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        canvas.DrawImage(baseImg, CoverRect(baseImg, info), p);
        if (_fading)
        {
            var topImg = FrameImage(_topIdx);
            if (topImg != null)
            {
                p.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(_fadeT, 0f, 1f) * 255));
                canvas.DrawImage(topImg, CoverRect(topImg, info), p);
            }
        }
    }

    /// <summary>The authored, masked, additive glint pass — matches the painter's preview:
    /// (Glow) breathing bloom of the painted glow pixels, (Sheen) a band masked to the painted gloss
    /// that sweeps across, (Twinkle) sparkle pops on the painted twinkle spots. Tuned by menu_fx.json.</summary>
    private void DrawAuthoredFx(SKCanvas canvas, SKImageInfo info)
    {
        var baseImg = FrameImage(_baseIdx);
        if (baseImg == null) return;
        var rect = CoverRect(baseImg, info);
        float t = _breathClock;
        bool fading = _fading;
        float tt = Math.Clamp(_fadeT, 0f, 1f);

        // Glow — breathing additive bloom, crossfaded between the outgoing and incoming frame so it
        // never hard-pops at the transition (was the "blur vanishes the instant we swap" bug).
        if (_glowI > 0.001f)
        {
            float pulse = (0.42f + 0.22f * (float)Math.Sin(t * 1.6 * _glowF)) * _glowI;
            DrawBloom(canvas, rect, BloomFor(_baseIdx), pulse * (fading ? 1f - tt : 1f));
            if (fading) DrawBloom(canvas, rect, BloomFor(_topIdx), pulse * tt);
        }

        // Sheen — diagonal band masked to the painted gloss (B channel), crossfaded between frames.
        if (_bToA != null && _shI > 0.001f)
        {
            float period = SweepPeriodBase / Math.Max(0.05f, _shF);
            float ph = _sweepClock % period;
            if (ph < SweepDur)
            {
                float pp = ph / SweepDur;
                float env = (float)Math.Sin(Math.PI * pp);
                float center = -0.15f + 1.3f * pp;
                float baseA = 0.5f * env * _shI;
                DrawSheen(canvas, rect, FxMask(_baseIdx), center, baseA * (fading ? 1f - tt : 1f));
                if (fading) DrawSheen(canvas, rect, FxMask(_topIdx), center, baseA * tt);
            }
        }

        // Twinkle — soft glint pops on the painted twinkle spots
        if (_tw.Count > 0 && _twkI > 0.001f)
        {
            float surf = Math.Min(rect.Width, rect.Height) / 760f;
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            foreach (var tw in _tw)
            {
                float env = (float)Math.Sin(Math.PI * (tw.Age / tw.Life)) * _twkI;
                if (env <= 0.01f) continue;
                float cx = rect.Left + tw.Nx * rect.Width;
                float cy = rect.Top + tw.Ny * rect.Height;
                float r = tw.Size * surf * (0.7f + 0.3f * env);
                using (var glow = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 2.4f,
                    new[] { tw.Col.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 150)), tw.Col.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
                {
                    paint.Shader = glow; canvas.DrawCircle(cx, cy, r * 2.4f, paint);
                }
                paint.Shader = null;
                paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 220));
                canvas.DrawCircle(cx, cy, r * 0.45f, paint);
            }
        }
    }

    /// <summary>Additive glow bloom at the given alpha (0..1). Used twice during a crossfade.</summary>
    private static void DrawBloom(SKCanvas canvas, SKRect rect, SKImage? bloom, float alpha)
    {
        if (bloom == null || alpha <= 0.002f) return;
        byte a = (byte)Math.Clamp(alpha * 255f, 0, 255);
        using var p = new SKPaint { BlendMode = SKBlendMode.Plus, IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha(a) };
        canvas.DrawImage(bloom, rect, p);
    }

    /// <summary>One sheen-band pass masked to a frame's gloss (B channel) at the given alpha.</summary>
    private void DrawSheen(SKCanvas canvas, SKRect rect, SKImage? mask, float center, float alpha)
    {
        if (mask == null || _bToA == null || alpha <= 0.002f) return;
        byte a = (byte)Math.Clamp(alpha * 255f, 0, 255);
        const float hw = 0.16f;
        float c0 = Math.Max(0f, center - hw), c2 = Math.Min(1f, center + hw);
        if (a <= 1 || c2 <= c0) return;
        float c1 = Math.Min(Math.Max(center, c0), c2);
        using var layer = new SKPaint { BlendMode = SKBlendMode.Plus };
        canvas.SaveLayer(layer);
        using (var band = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            new[] { SKColors.Transparent, new SKColor(0xFF, 0xFF, 0xFF, a), SKColors.Transparent },
            new[] { c0, c1, c2 }, SKShaderTileMode.Clamp))
        using (var bp = new SKPaint { Shader = band })
            canvas.DrawRect(rect, bp);
        using (var mp = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = _bToA, FilterQuality = SKFilterQuality.Medium })
            canvas.DrawImage(mask, rect, mp);
        canvas.Restore();
    }

    // ============================ menu music (looping soundtrack + fade + mute) ============================
    //
    // Resources/sounds/chaos/menu_theme.mp3 loops under the main menu with a 2s fade in/out (fades
    // out when leaving the menu for the dollhouse / a descent). The 🔊/🔇 chip toggles + persists mute.

    // Ceiling for the menu theme BEFORE the app's master volume is applied. The actual MediaPlayer
    // volume is always this (or the fade target) scaled by MasterVolume, so the soundtrack tracks the
    // master slider like every other audio source instead of blasting at a fixed level.
    private const double MenuMusicVol = 0.5;
    private MediaPlayer? _music;
    private DispatcherTimer? _musicFade;
    private double _fadeFrom, _fadeTo;
    private int _fadeStep, _fadeSteps;
    private Action? _fadeDone;
    private double _musicLevel;   // logical target (0..MenuMusicVol), pre master-volume scaling
    private System.ComponentModel.PropertyChangedEventHandler? _masterVolHandler;

    /// <summary>The app's master volume as a 0..1 multiplier (defaults to full if settings are missing).</summary>
    private static double MasterScale()
        => System.Math.Clamp((App.Settings?.Current?.MasterVolume ?? 100) / 100.0, 0.0, 1.0);

    private void StartMenuMusic()
    {
        try
        {
            if (_music == null)
            {
                var path = ConditioningControlPanel.Services.ModResourceResolver.ResolveAudioPath("chaos/menu_theme.mp3");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                _music = new MediaPlayer();
                _music.MediaEnded += (_, _) => { try { if (_music != null) { _music.Position = TimeSpan.Zero; _music.Play(); } } catch { } };
                _music.Open(new Uri(path, UriKind.Absolute));
                _music.Volume = 0;
                HookMasterVolume();
            }
            bool muted = App.Settings?.Current?.ChaosMenuMusicMuted == true;
            UpdateMuteIcon(muted);
            _music.Play();                                   // resumes where it left off (or from 0 on first open)
            FadeMusicTo(muted ? 0.0 : MenuMusicVol, 2.0);    // 2s fade in
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHub.StartMenuMusic: {E}", ex.Message); }
    }

    private void StopMenuMusic()
    {
        if (_music == null) return;
        FadeMusicTo(0.0, 2.0, () => { try { _music?.Pause(); } catch { } });   // 2s fade out, then pause
    }

    private void DisposeMenuMusic()
    {
        _musicFade?.Stop();
        UnhookMasterVolume();
        try { _music?.Stop(); _music?.Close(); } catch { }
        _music = null;
    }

    /// <summary>
    /// Ramp the music volume toward <paramref name="level"/> (a logical 0..MenuMusicVol target, before
    /// master-volume scaling) over <paramref name="secs"/> (50ms steps). The fade target is baked with
    /// the current <see cref="MasterScale"/>; <see cref="ApplyMasterVolumeToMusic"/> keeps it honest if
    /// the master slider moves mid-fade.
    /// </summary>
    private void FadeMusicTo(double level, double secs, Action? onDone = null)
    {
        if (_music == null) return;
        _musicLevel = level;
        _fadeFrom = _music.Volume; _fadeTo = Math.Clamp(level * MasterScale(), 0, 1);
        _fadeSteps = Math.Max(1, (int)(secs / 0.05)); _fadeStep = 0; _fadeDone = onDone;
        if (_musicFade == null)
        {
            _musicFade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _musicFade.Tick += MusicFadeTick;
        }
        _musicFade.Start();
    }

    private void MusicFadeTick(object? sender, EventArgs e)
    {
        if (_music == null) { _musicFade?.Stop(); return; }
        _fadeStep++;
        double t = Math.Min(1.0, (double)_fadeStep / _fadeSteps);
        _music.Volume = Math.Clamp(_fadeFrom + (_fadeTo - _fadeFrom) * t, 0, 1);
        if (t >= 1.0)
        {
            _musicFade?.Stop();
            var d = _fadeDone; _fadeDone = null; d?.Invoke();
        }
    }

    /// <summary>Listen for master-volume changes so the soundtrack re-scales live while it's open.</summary>
    private void HookMasterVolume()
    {
        if (_masterVolHandler != null || App.Settings?.Current == null) return;
        _masterVolHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(ConditioningControlPanel.Models.AppSettings.MasterVolume))
                ApplyMasterVolumeToMusic();
        };
        App.Settings.Current.PropertyChanged += _masterVolHandler;
    }

    private void UnhookMasterVolume()
    {
        if (_masterVolHandler == null) return;
        try { if (App.Settings?.Current != null) App.Settings.Current.PropertyChanged -= _masterVolHandler; } catch { }
        _masterVolHandler = null;
    }

    /// <summary>Re-apply the current master volume to the music: re-target an in-flight fade, else set directly.</summary>
    private void ApplyMasterVolumeToMusic()
    {
        if (_music == null) return;
        double target = Math.Clamp(_musicLevel * MasterScale(), 0, 1);
        if (_musicFade?.IsEnabled == true) _fadeTo = target;
        else { try { _music.Volume = target; } catch { } }
    }

    private void UpdateMuteIcon(bool muted)
    {
        if (MenuMuteIcon != null) MenuMuteIcon.Text = muted ? "🔇" : "🔊";
    }

    private void BtnMenuMute_Click(object sender, RoutedEventArgs e)
    {
        bool muted = !(App.Settings?.Current?.ChaosMenuMusicMuted == true);
        if (App.Settings?.Current != null) App.Settings.Current.ChaosMenuMusicMuted = muted;
        UpdateMuteIcon(muted);
        if (!muted) { try { _music?.Play(); } catch { } }
        FadeMusicTo(muted ? 0.0 : MenuMusicVol, 0.6);   // quick fade on toggle
    }

    // ============================ window chrome (move / resize / fullscreen) ============================

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnFull_Click(object sender, RoutedEventArgs e) => SetFullscreen(WindowState != WindowState.Maximized);

    private void OptFullscreen_Click(object sender, RoutedEventArgs e) => SetFullscreen(OptFullscreen.IsChecked == true);

    /// <summary>Maximize covers the work area (WindowChrome handles the transparent-window sizing).
    /// The actual checkbox/avatar sync happens in OnHubStateChanged so it also catches OS-driven
    /// maximize (snap, Win+Up, double-click).</summary>
    private void SetFullscreen(bool on) => WindowState = on ? WindowState.Maximized : WindowState.Normal;

    /// <summary>Maximizing overlaps the attached companion tube (it's anchored to the main window).
    /// Detach it to float out of the way while maximized; re-attach on restore unless we're already
    /// in a dollhouse/run context (which keeps it detached on purpose).</summary>
    private void OnHubStateChanged(object? sender, EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;
        if (max) App.AvatarWindow?.SetChaosRunActive(true);
        else if (!_runContext && !_fallingIn) App.AvatarWindow?.SetChaosRunActive(false);
        if (OptFullscreen != null) OptFullscreen.IsChecked = max;
    }

    /// <summary>Re-open the spoiler-free rules card on demand (the same card shown the first
    /// time the Dollhouse opened) — a "how do I play this" refresher anytime.</summary>
    private void BtnGuide_Click(object sender, RoutedEventArgs e)
    {
        try { new ChaosIntroWindow { Owner = this }.ShowDialog(); }
        catch (Exception ex) { App.Logger?.Debug("Chaos guide reshow: {E}", ex.Message); }
    }

    // ============================ helpers ============================

    private static string? GetSegment(Panel grp) =>
        grp.Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true)?.Tag?.ToString();

    private static void SetSegment(Panel grp, string? tag)
    {
        foreach (var t in grp.Children.OfType<ToggleButton>())
            t.IsChecked = (t.Tag?.ToString() == tag);
    }
}
