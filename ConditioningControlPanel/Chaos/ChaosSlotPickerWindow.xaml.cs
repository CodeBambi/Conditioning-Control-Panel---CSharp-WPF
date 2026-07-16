using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// The pre-descent save picker: shown right before the Rabbit Hole opens so the player
/// chooses which of the three local saves to play. Each card summarises a slot (rank /
/// descents / sparks / gold / last played) or reads "New Journey" when empty, can be
/// deleted, and the footer tells the player exactly where the saves live on disk.
/// Returns the chosen slot via <see cref="Pick"/>; the caller then calls
/// <see cref="ChaosMeta.SwitchSlot"/> and launches.
/// </summary>
public partial class ChaosSlotPickerWindow : Window
{
    private static readonly Color BrandPink = Color.FromRgb(0xE8, 0x43, 0x93);
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1A, 0x36));
    private static readonly Brush CardBgSel = new SolidColorBrush(Color.FromRgb(0x2A, 0x20, 0x42));
    private static readonly Brush CardBorder = new SolidColorBrush(Color.FromArgb(0x44, 0xE8, 0x43, 0x93));
    private static readonly Brush CardBorderSel = new SolidColorBrush(BrandPink);
    private static readonly Brush TextDim = new SolidColorBrush(Color.FromArgb(0xAA, 0xB8, 0xB8, 0xD0));
    private static readonly Brush TextMut = new SolidColorBrush(Color.FromArgb(0x88, 0xA0, 0xA0, 0xC0));
    private static readonly Brush White = Brushes.White;

    private int _selected;
    private readonly Dictionary<int, Border> _cards = new();
    private readonly HashSet<int> _locked = new();

    /// <summary>The slot the player committed to (only meaningful when the dialog returned true).</summary>
    public int ChosenSlot { get; private set; }

    private ChaosSlotPickerWindow()
    {
        InitializeComponent();
        _selected = ChaosMeta.ActiveSlot;
        PathText.Text = ChaosMetaStore.SaveFolder;
        HeaderBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
        RebuildCards();
    }

    /// <summary>Modal entry point. Returns the chosen slot (1-3), or null if the player cancelled.</summary>
    public static int? Pick(Window? owner)
    {
        var w = new ChaosSlotPickerWindow();
        if (owner != null && owner.IsVisible) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.ChosenSlot : (int?)null;
    }

    private void RebuildCards()
    {
        SlotsPanel.Children.Clear();
        _cards.Clear();
        _locked.Clear();

        // Crafting Part 2: slots 2/3 are stitched shut until the Ragdoll / Porcelain
        // dolls are crafted in THE BOUDOIR. Any save's craft unlocks globally, and a
        // pre-existing save keeps its slot open (back-compat with pre-craft slots).
        var summaries = ChaosMeta.AllSlotSummaries();
        bool anyRagdoll = false, anyPorcelain = false;
        foreach (var s in summaries)
        {
            anyRagdoll |= s.HasRagdoll;
            anyPorcelain |= s.HasPorcelain;
        }
        foreach (var s in summaries)
        {
            bool locked = (s.Slot == 2 && !anyRagdoll && !s.Exists)
                       || (s.Slot == 3 && !anyPorcelain && !s.Exists);
            if (locked) _locked.Add(s.Slot);
            var card = locked ? BuildLockedCard(s) : BuildCard(s);
            _cards[s.Slot] = card;
            SlotsPanel.Children.Add(card);
        }
        if (_locked.Contains(_selected)) _selected = 1;
        UpdateSelectionVisuals();
    }

    /// <summary>A stitched-shut slot: dimmed, no click, no delete — crafting the doll opens it.</summary>
    private Border BuildLockedCard(SlotSummary s)
    {
        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        body.Children.Add(new TextBlock
        {
            Text = $"SAVE {s.Slot}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextMut,
        });
        body.Children.Add(new TextBlock
        {
            Text = "🔒",
            FontSize = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 34, 0, 8),
        });
        body.Children.Add(new TextBlock
        {
            Text = "Stitched Shut",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        body.Children.Add(new TextBlock
        {
            Text = s.Slot == 2
                ? "craft the Ragdoll in THE BOUDOIR to open a second life"
                : "craft the Porcelain doll in THE BOUDOIR to open a third life",
            FontSize = 11.5,
            Foreground = TextMut,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });

        return new Border
        {
            Width = 196,
            Height = 268,
            CornerRadius = new CornerRadius(13),
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(9, 0, 9, 0),
            Opacity = 0.45,
            Child = body,
            Tag = s.Slot,
        };
    }

    private Border BuildCard(SlotSummary s)
    {
        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

        // slot label
        body.Children.Add(new TextBlock
        {
            Text = $"SAVE {s.Slot}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextMut,
        });

        if (!s.Exists)
        {
            body.Children.Add(new TextBlock
            {
                Text = "✨",
                FontSize = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 34, 0, 8),
            });
            body.Children.Add(new TextBlock
            {
                Text = "New Journey",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = White,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            body.Children.Add(new TextBlock
            {
                Text = "Empty slot — start fresh",
                FontSize = 11.5,
                Foreground = TextMut,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }
        else
        {
            var rank = ChaosRanks.Name(ChaosRanks.For(s.RunsCompleted));
            body.Children.Add(new TextBlock
            {
                Text = rank,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(BrandPink),
                Margin = new Thickness(0, 10, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            body.Children.Add(StatRow($"{s.RunsCompleted}", s.RunsCompleted == 1 ? "descent" : "descents"));
            body.Children.Add(StatRow($"✦ {s.Sparks:N0}", "drops"));
            body.Children.Add(StatRow($"🪙 {s.Gold:N0}", "gold"));
            if (s.BestScore > 0) body.Children.Add(StatRow($"{s.BestScore:N0}", "best score"));

            var when = s.LastPlayedUtc.HasValue
                ? "Last played " + s.LastPlayedUtc.Value.ToLocalTime().ToString("MMM d, h:mm tt")
                : "";
            if (when.Length > 0)
                body.Children.Add(new TextBlock
                {
                    Text = when,
                    FontSize = 10.5,
                    Foreground = TextMut,
                    Margin = new Thickness(0, 12, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
        }

        var content = new Grid();
        content.Children.Add(body);

        // delete (only when there's something to erase)
        if (s.Exists)
        {
            var del = new Button
            {
                Content = "🗑",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
                Foreground = TextDim,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                ToolTip = $"Erase Save {s.Slot}",
                Tag = s.Slot,
            };
            del.Click += DeleteSlot_Click;
            content.Children.Add(del);
        }

        var card = new Border
        {
            Width = 196,
            Height = 268,
            CornerRadius = new CornerRadius(13),
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(9, 0, 9, 0),
            Cursor = Cursors.Hand,
            Child = content,
            Tag = s.Slot,
        };
        card.MouseLeftButtonUp += Card_Click;
        return card;
    }

    private static StackPanel StatRow(string value, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = White });
        row.Children.Add(new TextBlock { Text = "  " + label, FontSize = 12, Foreground = TextDim, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is int slot)
        {
            _selected = slot;
            UpdateSelectionVisuals();
        }
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var (slot, card) in _cards)
        {
            bool sel = slot == _selected;
            card.BorderBrush = sel ? CardBorderSel : CardBorder;
            card.BorderThickness = new Thickness(sel ? 2.5 : 1.5);
            card.Background = sel ? CardBgSel : CardBg;
            card.Effect = sel
                ? new System.Windows.Media.Effects.DropShadowEffect { Color = BrandPink, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 }
                : null;
        }
    }

    private void DeleteSlot_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;   // don't let the click also select the card
        if (sender is not Button btn || btn.Tag is not int slot) return;

        var r = MessageBox.Show(
            $"Erase Save {slot}?\n\nThis permanently deletes this local save. It can't be undone.",
            "Erase save", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (r != MessageBoxResult.OK) return;

        ChaosMeta.DeleteSlot(slot);
        RebuildCards();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(ChaosMetaStore.SaveFolder);
            Process.Start(new ProcessStartInfo { FileName = ChaosMetaStore.SaveFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ChaosSlotPicker: open folder failed: {E}", ex.Message);
        }
    }

    private void BtnDescend_Click(object sender, RoutedEventArgs e)
    {
        ChosenSlot = _selected;
        DialogResult = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
