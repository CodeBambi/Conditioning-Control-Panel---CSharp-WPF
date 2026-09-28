using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Her bench — the gold shop. Gold never buys power: everything here is convenience or
/// cosmetic surface (pockets, the diary, the stats panel, the starting mantra). Rendered
/// in the Looking Glass; the first two rows also sell early from "her corner" in the
/// Toybox (run 2+, until the Looking Glass opens).
/// </summary>
public partial class ChaosHubWindow
{
    // ---- gold prices (tunable) ----
    private const int GOLD_TOY_POCKET_1 = 50;
    private const int GOLD_ACC_POCKET_1 = 150;
    private const int GOLD_START_MANTRA = 200;
    private const int GOLD_DIARY        = 150;
    private const int GOLD_STATS_PANEL  = 100;
    private const int GOLD_TOY_POCKET_2 = 2000;
    private const int GOLD_ACC_POCKET_2 = 2500;

    /// <summary>[LOCKED] tooltip for a bench row that is visible but rank-short.</summary>
    private const string DEEPER_TIP = "she'll sell this to someone deeper.";
    /// <summary>[LOCKED] tooltip on the Claimed-reserved rows.</summary>
    private const string BOTTOM_TIP = "the bottom is not where you think it is.";

    private sealed class BenchItem
    {
        public string Id = "";
        public string Glyph = "👝";
        public string Label = "";
        public string Line = "";
        public int Cost;
        /// <summary>Rank required to buy (row shows rank-locked below it).</summary>
        public ChaosRank? RankNeed;
        /// <summary>Reveal id that keeps the row hazy (???) until it unlocks.</summary>
        public string? RevealGate;
        public Action? ApplyEffect;
    }

    private List<BenchItem>? _benchItems;

    private List<BenchItem> BenchItems => _benchItems ??= new List<BenchItem>
    {
        new() { Id = BenchIds.ToyPocket1, Glyph = "👝", Label = "first toy pocket",
                Line = "she sews you a pocket.", Cost = GOLD_TOY_POCKET_1,
                ApplyEffect = () => ChaosMeta.State.ToyPockets++ },
        new() { Id = BenchIds.AccPocket1, Glyph = "👝", Label = "first accessory pocket",
                Line = "she only has two hands. she found a third.", Cost = GOLD_ACC_POCKET_1,
                ApplyEffect = () => ChaosMeta.State.AccessoryPockets++ },
        new() { Id = BenchIds.StartMantra, Glyph = "◈", Label = "the starting mantra",
                Line = "fall in holding something.", Cost = GOLD_START_MANTRA },
        new() { Id = BenchIds.Diary, Glyph = "📓", Label = "the diary",
                Line = "she keeps notes on what you meet down there.", Cost = GOLD_DIARY },
        new() { Id = BenchIds.StatsPanel, Glyph = "🕰", Label = "the stats panel",
                Line = "the numbers, if you want them.", Cost = GOLD_STATS_PANEL },
        new() { Id = BenchIds.ToyPocket2, Glyph = "👝", Label = "second toy pocket",
                Line = "she found room for one more.", Cost = GOLD_TOY_POCKET_2,
                RankNeed = ChaosRank.Devoted, RevealGate = RevealIds.BenchToyPocket2,
                ApplyEffect = () => ChaosMeta.State.ToyPockets++ },
        new() { Id = BenchIds.AccPocket2, Glyph = "👝", Label = "second accessory pocket",
                Line = "a fourth hand. don't ask.", Cost = GOLD_ACC_POCKET_2,
                RankNeed = ChaosRank.Devoted, RevealGate = RevealIds.BenchAccPocket2,
                ApplyEffect = () => ChaosMeta.State.AccessoryPockets++ },
    };

    /// <summary>Reserved hazy rows: names on the bench, nothing behind them yet.</summary>
    private static readonly string[] ReservedRows =
    {
        "the clocks", "descent ledger", "payout eyes", "the fine print",
        "fall right in", "held breath", "soft landing", "no countdown",
        "dollhouse wallpapers", "recap frames", "a chattier companion", "the pact",
    };

    /// <summary>Claimed-reserved rows: visible only at Devoted+.</summary>
    private static readonly string[] ClaimedReservedRows = { "daily descent", "leaderboard", "prestige" };

    /// <summary>The full bench into the Looking Glass shelf.</summary>
    private void BuildBench()
    {
        ImprovementsHost.Children.Clear();
        ImprovementsHost.Children.Add(GoldBalanceLine());

        foreach (var item in BenchItems)
        {
            var row = BenchRow(item);
            ImprovementsHost.Children.Add(row);
            // The pocket-2 rows flash when their reveal flips at Devoted.
            if (item.RevealGate == RevealIds.BenchToyPocket2) _revealMap[RevealIds.BenchToyPocket2] = row;
            if (item.RevealGate == RevealIds.BenchAccPocket2) _revealMap[RevealIds.BenchAccPocket2] = row;
        }

        foreach (var name in ReservedRows)
            ImprovementsHost.Children.Add(HazyRow(name, WALL_TIP));

        if (ChaosMeta.AtLeast(ChaosRank.Devoted))
            foreach (var name in ClaimedReservedRows)
                ImprovementsHost.Children.Add(HazyRow(name, BOTTOM_TIP));
    }

    /// <summary>Her corner inside the Toybox: just the two first-pocket rows, sold early.</summary>
    private void BuildHerCorner()
    {
        HerCornerHost.Children.Clear();
        HerCornerHost.Children.Add(GoldBalanceLine());
        foreach (var id in new[] { BenchIds.ToyPocket1, BenchIds.AccPocket1 })
        {
            var item = BenchItems.First(i => i.Id == id);
            HerCornerHost.Children.Add(BenchRow(item));
        }
    }

    private TextBlock GoldBalanceLine() => new()
    {
        Text = $"you're carrying {ChaosGlyphs.Gold} {ChaosMeta.State.Gold:N0}",
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x43)),
        FontSize = 11, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// <summary>One bench row in its current state: hazy (reveal-gated), rank-locked,
    /// owned, or for sale.</summary>
    private Border BenchRow(BenchItem item)
    {
        bool revealed = item.RevealGate == null || RevealService.IsUnlocked(item.RevealGate);
        if (!revealed) return HazyRow("???", WALL_TIP);

        bool owned = ChaosMeta.State.BenchPurchases.Contains(item.Id);
        bool rankShort = item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value);
        var goldColor = Color.FromRgb(0xE8, 0xB4, 0x43);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = item.Glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0), Opacity = owned ? 1.0 : 0.7,
        };
        Grid.SetColumn(glyph, 0);
        grid.Children.Add(glyph);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock
        {
            Text = item.Label,
            Foreground = owned ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xE0)),
            FontSize = 12, FontWeight = FontWeights.SemiBold,
        });
        mid.Children.Add(new TextBlock
        {
            Text = item.Line,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8)),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        FrameworkElement right;
        if (owned)
        {
            right = new TextBlock
            {
                Text = "sewn ✓",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xE0, 0x96)),
                FontSize = 11, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else if (rankShort)
        {
            right = new ConditioningControlPanel.Helpers.EmojiTextBlock
            {
                Text = "🔒",
                FontSize = 13, Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            bool afford = ChaosMeta.State.Gold >= item.Cost;
            // Stays clickable when short — her one gift rides on a short first-pocket buy.
            var buy = new Button
            {
                Content = $"buy  {ChaosGlyphs.Gold} {item.Cost:N0}",
                Tag = item.Id,
                Padding = new Thickness(14, 6, 14, 6),
                Background = afford
                    ? new SolidColorBrush(goldColor)
                    : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = afford ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0x88, 0xA0, 0xA0)),
                BorderThickness = new Thickness(0),
                FontSize = 12, FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Pillify(buy);
            buy.Click += BenchBuy_Click;
            right = buy;
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var card = new Border
        {
            Child = grid,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(owned ? (byte)70 : (byte)45, goldColor.R, goldColor.G, goldColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Opacity = rankShort ? 0.6 : 1.0,
        };
        if (rankShort) ChaosTips.Attach(card, item.Label, DEEPER_TIP,
            item.RankNeed.HasValue ? ChaosRanks.RankSpecifics(item.RankNeed.Value) : null);
        else ChaosTips.Attach(card, item.Label, item.Line, accent: goldColor);
        return card;
    }

    /// <summary>A dim reserved row: a name, no function, a tooltip that gives nothing away.</summary>
    private Border HazyRow(string name, string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "▢", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 11, 0), Opacity = 0.4 });
        row.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x90)),
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        });
        var card = new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x38)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(20, 0xE8, 0x43, 0x93)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Opacity = 0.55,
        };
        ChaosTips.Attach(card, name, tip);
        return card;
    }

    private void BenchBuy_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as Button)?.Tag?.ToString();
        if (string.IsNullOrEmpty(id)) return;
        var item = BenchItems.FirstOrDefault(i => i.Id == id);
        if (item == null || ChaosMeta.State.BenchPurchases.Contains(item.Id)) return;
        if (item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value)) return;
        if (item.RevealGate != null && !RevealService.IsUnlocked(item.RevealGate)) return;

        bool paid = ChaosMeta.TrySpendGold(item.Cost);
        if (!paid)
        {
            // THE GIFT: the very first short buy on the first toy pocket, she covers it. Once.
            if (item.Id == BenchIds.ToyPocket1 && !ChaosMeta.State.GiftGiven)
            {
                ChaosMeta.State.GiftGiven = true;
                ChaosMeta.State.Gold = 0;
                paid = true;
                try { App.Bark?.NotifyChaosGiftGiven(); } catch { }
            }
            else
            {
                ChaosSfx.Play("ui_denied", 0.45f);
                return;
            }
        }

        ChaosMeta.State.BenchPurchases.Add(item.Id);
        try { item.ApplyEffect?.Invoke(); }
        catch (Exception ex) { App.Logger?.Warning("Bench effect {Id} failed ({E})", item.Id, ex.Message); }
        ChaosMeta.Save();
        // Pocket buys get their cue from the unlock card below — no doubled sting.
        bool cardFollows = item.Id is BenchIds.ToyPocket1 or BenchIds.ToyPocket2
                                   or BenchIds.AccPocket1 or BenchIds.AccPocket2;
        if (!cardFollows) ChaosSfx.Play("ui_unlock", 0.55f);

        RevealService.Sync("purchase");
        ApplyReveals();
        BuildBench();
        if (HerCornerCard.Visibility == Visibility.Visible) BuildHerCorner();
        BuildLifetimeBoons();   // pocket-full states on the shelves changed
        BuildLoadoutTiles();    // the BAG pocket slots changed
        RefreshTopBar();
        RefreshStats();
        App.Chaos?.NotifyLoadoutChanged();
        RunRevealFlashes("purchase");   // a freshly revealed surface flashes right away

        // Pockets get an unlock card — a new SLOT is the thing players miss most easily.
        // The other bench rows (diary, stats, mantra) reveal their own surface on purchase.
        if (item.Id is BenchIds.ToyPocket1 or BenchIds.ToyPocket2)
            ShowUnlockCard(ChaosUnlockCards.ForPocket(isToy: true, item.Label, item.Line));
        else if (item.Id is BenchIds.AccPocket1 or BenchIds.AccPocket2)
            ShowUnlockCard(ChaosUnlockCards.ForPocket(isToy: false, item.Label, item.Line));
    }
}
