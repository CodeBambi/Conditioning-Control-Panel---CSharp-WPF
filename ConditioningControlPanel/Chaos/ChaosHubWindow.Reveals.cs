using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Reveal-framework wiring for the Dollhouse: the UI starts naked and every gateable
/// surface stays HIDDEN until <see cref="RevealService"/> says otherwise. Freshly
/// unlocked surfaces flash once (soft opacity pulse) on the next hub open, then settle.
/// Settings clamp rule: gating here only flips Visibility / visual selection, it NEVER
/// writes a user setting.
/// </summary>
public partial class ChaosHubWindow
{
    /// <summary>[LOCKED] tooltip for adjacent hazy stubs and reserved bench rows.</summary>
    internal const string WALL_TIP = "there's a wall here that isn't quite a wall.";

    /// <summary>Reveal id → the hub element that flashes when it unlocks. Ids missing
    /// here (or mapped to a still-hidden element) are marked seen silently.</summary>
    private readonly Dictionary<string, FrameworkElement> _revealMap = new();

    private bool _dollhouseBeatsFired;   // first-open beat fires once per window

    /// <summary>True while the on-screen difficulty selection is a reveal-gate fallback,
    /// not the player's choice — SaveToSettings must not persist it (clamp, never overwrite).</summary>
    private bool _diffAutoClamped;

    /// <summary>Build the id→element map (static elements; bench rows re-register in BuildBench).</summary>
    private void InitRevealMap()
    {
        _revealMap[RevealIds.TabLookingGlass] = TabImprove;
        _revealMap[RevealIds.SectionToys] = HdrToys;
        _revealMap[RevealIds.SectionAccessories] = HdrAccessories;
        _revealMap[RevealIds.HerCorner] = HerCornerCard;
        _revealMap[RevealIds.PillTeasing] = SegMedium;
        _revealMap[RevealIds.PillRelentless] = SegHard;
        _revealMap[RevealIds.PillInescapable] = SegExtreme;
        _revealMap[RevealIds.StartPicker] = CardMantras;
        _revealMap[RevealIds.Diary] = HdrDiary;
        _revealMap[RevealIds.StatsPanel] = HdrStats;

        ChaosTips.Attach(StubToys, "???", WALL_TIP,
            "opens with your first toy pocket. she sews pockets for gold (her corner, later her bench).");
        ChaosTips.Attach(StubAccessories, "???", WALL_TIP,
            "opens with your first accessory pocket. she sews pockets for gold (her corner, later her bench).");
    }

    /// <summary>Hide/show every gated surface from current reveal state. Called on open
    /// and after any purchase/state change. Never writes user settings.</summary>
    internal void ApplyReveals()
    {
        try
        {
            // ---- the Looking Glass tab (Slipping) ----
            // Locked = GREYED, not hidden: a "??? 🔒" stub tells the player a room exists
            // behind a wall. The name and the contents stay a mystery until the reveal flips.
            bool lookingGlass = RevealService.IsUnlocked(RevealIds.TabLookingGlass);
            TabImprove.Visibility = Visibility.Visible;
            TabImprove.IsEnabled = lookingGlass;
            TabImprove.Opacity = lookingGlass ? 1.0 : 0.40;
            TabImprove.Content = lookingGlass ? "the Looking Glass" : "??? 🔒";
            if (!lookingGlass)
            {
                System.Windows.Controls.ToolTipService.SetShowOnDisabled(TabImprove, true);
                ChaosTips.Attach(TabImprove, "???", WALL_TIP,
                    ChaosRanks.RankSpecifics(ChaosRank.Slipping, ChaosMeta.State.RunsCompleted));
            }
            else TabImprove.ToolTip = null;   // the wall came down — drop the locked tooltip
            if (!lookingGlass && TabImprove.IsChecked == true) ShowTab("loadout");

            // ---- Toybox shelves (pocket-driven) + adjacent hazy stubs ----
            bool toys = RevealService.IsUnlocked(RevealIds.SectionToys);
            HdrToys.Visibility = toys ? Visibility.Visible : Visibility.Collapsed;
            BoonHostSkills.Visibility = toys ? Visibility.Visible : Visibility.Collapsed;
            StubToys.Visibility = toys ? Visibility.Collapsed : Visibility.Visible;

            bool accs = RevealService.IsUnlocked(RevealIds.SectionAccessories);
            HdrAccessories.Visibility = accs ? Visibility.Visible : Visibility.Collapsed;
            BoonHostAccessories.Visibility = accs ? Visibility.Visible : Visibility.Collapsed;
            StubAccessories.Visibility = accs ? Visibility.Collapsed : Visibility.Visible;

            // The BAG collection grids mirror the shelves — a naked start shows neither.
            CardBagToys.Visibility = toys ? Visibility.Visible : Visibility.Collapsed;
            CardBagAccessories.Visibility = accs ? Visibility.Visible : Visibility.Collapsed;

            // ---- her corner (early bench access inside the Toybox; registry turns it off at Slipping) ----
            bool corner = RevealService.IsUnlocked(RevealIds.HerCorner);
            HerCornerCard.Visibility = corner ? Visibility.Visible : Visibility.Collapsed;
            if (corner) BuildHerCorner();

            // ---- difficulty pills (Inescapable keeps its own lock rendering, unchanged) ----
            bool teasing = RevealService.IsUnlocked(RevealIds.PillTeasing);
            bool relentless = RevealService.IsUnlocked(RevealIds.PillRelentless);
            SegMedium.Visibility = teasing ? Visibility.Visible : Visibility.Collapsed;
            SegHard.Visibility = relentless ? Visibility.Visible : Visibility.Collapsed;
            // A hidden pill can't stay the visible selection — fall back to Gentle on
            // screen only. _diffAutoClamped keeps SaveToSettings from persisting the
            // fallback over the player's saved choice (clamp, never overwrite); a real
            // click on a difficulty pill clears it.
            if ((!teasing && SegMedium.IsChecked == true) || (!relentless && SegHard.IsChecked == true))
            {
                SetSegment(GrpDifficulty, "Easy");
                _diffAutoClamped = true;
            }

            // ---- Looking Glass internals (bench purchases) ----
            CardMantras.Visibility = RevealService.IsUnlocked(RevealIds.StartPicker) ? Visibility.Visible : Visibility.Collapsed;
            // The diary now lives in its own tab (so a full stats sheet can't crowd it out);
            // its reveal drives whether that tab is offered. HdrDiary sits inside the panel and
            // is always shown when the tab is open.
            bool diary = RevealService.IsUnlocked(RevealIds.Diary);
            TabDiary.Visibility = diary ? Visibility.Visible : Visibility.Collapsed;
            if (!diary && TabDiary.IsChecked == true) ShowTab("loadout");   // don't strand the player on a vanished tab
            bool stats = RevealService.IsUnlocked(RevealIds.StatsPanel);
            HdrStats.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
            StatsGrid.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;

            // ---- toy keybinds mirror the sewn pockets: none = no hints at all ----
            int toyPockets = ChaosMeta.SlotsFor(ChaosBoonCategory.Skill);
            CardKeybinds.Visibility = toyPockets >= 1 ? Visibility.Visible : Visibility.Collapsed;
            RowToyKey2.Visibility = toyPockets >= 2 ? Visibility.Visible : Visibility.Collapsed;
            TxtKeybindsSub.Text = toyPockets >= 2
                ? "your equipped toys fire on these, mid-descent."
                : "your equipped toy fires on this, mid-descent. the second pocket isn't sewn yet.";

            TxtPocketsSub.Text = ChaosMeta.State.ToyPockets + ChaosMeta.State.AccessoryPockets == 0
                ? "you fall in with empty hands, for now."
                : "what you take down with you. choose like it matters.";
        }
        catch (Exception ex) { App.Logger?.Warning("ApplyReveals failed ({E})", ex.Message); }
    }

    /// <summary>First hub open ever + the reveal-flash pass. Runs once the window loaded
    /// (animations need live elements).</summary>
    private void OnHubOpenedReveals()
    {
        if (_dollhouseBeatsFired) return;
        _dollhouseBeatsFired = true;
        // The invitation: a one-time, spoiler-free intro card BEFORE anything else gets
        // to speak — barks and reveal flashes wait until it's been read and dismissed.
        try
        {
            if (!ChaosMeta.State.SeenIntroGuide)
            {
                var intro = new ChaosIntroWindow { Owner = this };
                intro.ShowDialog();
                ChaosMeta.State.SeenIntroGuide = true;
                ChaosMeta.Save();
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Chaos intro guide failed ({E})", ex.Message); }
        try
        {
            if (!ChaosMeta.State.SeenDollhouse)
            {
                ChaosMeta.State.SeenDollhouse = true;
                ChaosMeta.Save();
                App.Bark?.NotifyChaosDollhouseFirstOpen();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Dollhouse first-open beat: {E}", ex.Message); }
        RunRevealFlashes("hub_open");
    }

    /// <summary>Sync, then flash every pending reveal that maps to a now-visible element
    /// (~3s soft pulse, staggered 0.6s apart; one bark for the whole batch). Ids without
    /// a visible hub element are marked seen silently.</summary>
    private void RunRevealFlashes(string reason)
    {
        try
        {
            RevealService.Sync(reason);
            var pending = RevealService.PendingIds();
            if (pending.Count == 0) return;

            double stagger = 0;
            string? firstFlashed = null;
            foreach (var id in pending)
            {
                if (_revealMap.TryGetValue(id, out var el) && el != null && el.Visibility == Visibility.Visible)
                {
                    string captured = id;
                    firstFlashed ??= id;
                    FlashReveal(el, stagger, () => RevealService.MarkSeen(captured));
                    stagger += 0.6;
                }
                else
                {
                    RevealService.MarkSeen(id);   // no hub surface (variant clamps, draft skip, ...)
                }
            }
            if (firstFlashed != null)
            {
                try { App.Bark?.NotifyChaosRevealFlash(firstFlashed); } catch { }
            }
        }
        catch (Exception ex) { App.Logger?.Warning("RunRevealFlashes failed ({E})", ex.Message); }
    }

    /// <summary>A ~3 second soft opacity pulse on a freshly revealed element; the element
    /// settles back to full opacity and <paramref name="done"/> moves it pending → seen.</summary>
    private static void FlashReveal(FrameworkElement el, double beginSec, Action done)
    {
        try
        {
            // A feather-soft chime as each surface pulses awake, riding the same stagger.
            if (beginSec <= 0) ChaosSfx.Play("reveal_chime", 0.5f);
            else
            {
                var chime = new DispatcherTimer { Interval = TimeSpan.FromSeconds(beginSec) };
                chime.Tick += (_, _) => { chime.Stop(); ChaosSfx.Play("reveal_chime", 0.5f); };
                chime.Start();
            }

            var pulse = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(500))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3),
                BeginTime = TimeSpan.FromSeconds(beginSec),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop,
            };
            pulse.Completed += (_, _) =>
            {
                try { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1.0; } catch { }
                try { done(); } catch { }
            };
            el.BeginAnimation(UIElement.OpacityProperty, pulse);
        }
        catch
        {
            // If the animation can't run (element torn down mid-open), settle the state anyway.
            try { done(); } catch { }
        }
    }
}
