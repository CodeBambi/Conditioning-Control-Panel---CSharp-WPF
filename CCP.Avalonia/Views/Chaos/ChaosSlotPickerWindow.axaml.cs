using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The pre-descent save picker: shown right before the Rabbit Hole opens so the player
    /// chooses which of the three local saves to play. Each card summarises a slot (rank /
    /// descents / sparks / gold / last played) or reads "New Journey" when empty, can be
    /// deleted, and the footer tells the player exactly where the saves live on disk.
    ///
    /// PORTED from ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml.cs. Deviations:
    ///  - <c>ChaosMeta</c> / <c>ChaosMetaStore</c> / <c>ChaosRanks</c> are WPF-head services, so
    ///    the picker draws <see cref="SampleSummaries"/> and the delete action is a stub. The
    ///    three card shapes (a live save, an empty slot, a stitched-shut slot) are all exercised
    ///    by that sample, so the render still proves every builder.
    ///  - <c>DialogResult = x; Close()</c> -> <c>Close(x)</c>; <see cref="Pick"/> is async, because
    ///    Avalonia's <c>ShowDialog</c> is.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>; <c>MouseLeftButtonUp</c> ->
    ///    <c>PointerReleased</c>; <c>Cursors.Hand</c> -> <c>StandardCursorType.Hand</c>;
    ///    <c>ToolTip =</c> -> <c>ToolTip.SetTip</c>; <c>App.Logger</c> -> Serilog's static Log.
    ///  - <c>MessageBox.Show</c> has no Avalonia equivalent and no package may be added, so the
    ///    erase confirmation is not raised at all (see <see cref="DeleteSlot_Click"/>).
    ///  - The constructor is <c>internal</c> and parameterless, as in WPF; that also makes it the
    ///    render constructor <c>--render-all</c> discovers.
    /// </summary>
    public partial class ChaosSlotPickerWindow : Window
    {
        /// <summary>Headline stats for one save slot. Mirrors the head's
        /// <c>ConditioningControlPanel.Services.Chaos.SlotSummary</c> field for field.
        /// ponytail: needs ChaosMetaStore.ReadSummary, wired when it moves to Core.</summary>
        internal sealed class SlotSummary
        {
            public int Slot { get; set; }
            public bool Exists { get; set; }
            public int Sparks { get; set; }
            public int Gold { get; set; }
            public int RunsCompleted { get; set; }
            public long BestScore { get; set; }
            public DateTime? LastPlayedUtc { get; set; }
            public bool HasRagdoll { get; set; }
            public bool HasPorcelain { get; set; }
        }

        private static readonly Color BrandPink = Color.FromRgb(0xE8, 0x43, 0x93);
        private static readonly IBrush CardBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1A, 0x36));
        private static readonly IBrush CardBgSel = new SolidColorBrush(Color.FromRgb(0x2A, 0x20, 0x42));
        private static readonly IBrush CardBorder = new SolidColorBrush(Color.FromArgb(0x44, 0xE8, 0x43, 0x93));
        private static readonly IBrush CardBorderSel = new SolidColorBrush(BrandPink);
        private static readonly IBrush TextDim = new SolidColorBrush(Color.FromArgb(0xAA, 0xB8, 0xB8, 0xD0));
        private static readonly IBrush TextMut = new SolidColorBrush(Color.FromArgb(0x88, 0xA0, 0xA0, 0xC0));
        private static readonly IBrush White = Brushes.White;

        private readonly StackPanel _slotsPanel;
        private int _selected;
        private readonly Dictionary<int, Border> _cards = new();
        private readonly HashSet<int> _locked = new();

        /// <summary>The slot the player committed to (only meaningful when the dialog returned true).</summary>
        public int ChosenSlot { get; private set; }

        internal ChaosSlotPickerWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _slotsPanel = this.FindControl<StackPanel>("SlotsPanel")!;
            // ponytail: needs ChaosMeta.ActiveSlot, wired when it moves to Core.
            _selected = 1;
            // ChaosMetaStore.SaveFolder is exactly this expression in the head.
            this.FindControl<TextBlock>("PathText")!.Text = CorePaths.UserData;

            var header = this.FindControl<Grid>("HeaderBar")!;
            header.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
                try { BeginMoveDrag(e); } catch { /* dragging can throw if not pressed */ }
            };

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnDescend")!.Click += (_, _) => { ChosenSlot = _selected; Close(true); };
            this.FindControl<Button>("BtnOpenFolder")!.Click += BtnOpenFolder_Click;

            RebuildCards();
        }

        /// <summary>Modal entry point. Returns the chosen slot (1-3), or null if the player cancelled.</summary>
        public static async Task<int?> Pick(Window? owner)
        {
            var w = new ChaosSlotPickerWindow();
            if (owner == null || !owner.IsVisible)
            {
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                w.Show();
                return null;   // ponytail: needs an owner to be modal against; callers always have one.
            }
            return await w.ShowDialog<bool?>(owner) == true ? w.ChosenSlot : (int?)null;
        }

        /// <summary>
        /// Stand-in for <c>ChaosMeta.AllSlotSummaries()</c>. Slot 1 is a played save that owns the
        /// Ragdoll, slot 2 is therefore an open but empty seat, slot 3 has no Porcelain anywhere
        /// and is stitched shut - one of each card the picker can draw.
        /// ponytail: needs ChaosMeta, wired when it moves to Core.
        /// </summary>
        private static List<SlotSummary> SampleSummaries() => new()
        {
            new SlotSummary
            {
                Slot = 1, Exists = true, RunsCompleted = 4, Sparks = 1820, Gold = 640,
                BestScore = 12400, LastPlayedUtc = new DateTime(2026, 8, 30, 21, 15, 0, DateTimeKind.Utc),
                HasRagdoll = true,
            },
            new SlotSummary { Slot = 2 },
            new SlotSummary { Slot = 3 },
        };

        /// <summary>Stand-in for <c>ChaosRanks.Name(ChaosRanks.For(runs))</c>; same thresholds and
        /// same words. ponytail: needs ChaosRanks, wired when it moves to Core.</summary>
        private static string RankName(int runsCompleted) => runsCompleted switch
        {
            >= 100 => "Claimed",
            >= 50 => "Devoted",
            >= 25 => "Entranced",
            >= 10 => "Slipping",
            >= 3 => "Tempted",
            _ => "Curious",
        };

        private void RebuildCards()
        {
            _slotsPanel.Children.Clear();
            _cards.Clear();
            _locked.Clear();

            // Crafting Part 2: slots 2/3 are stitched shut until the Ragdoll / Porcelain
            // dolls are crafted in THE BOUDOIR. Any save's craft unlocks globally, and a
            // pre-existing save keeps its slot open (back-compat with pre-craft slots).
            var summaries = SampleSummaries();
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
                _slotsPanel.Children.Add(card);
            }
            if (_locked.Contains(_selected)) _selected = 1;
            UpdateSelectionVisuals();
        }

        /// <summary>A stitched-shut slot: dimmed, no click, no delete — crafting the doll opens it.</summary>
        private static Border BuildLockedCard(SlotSummary s)
        {
            var body = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
            body.Children.Add(new TextBlock
            {
                Text = $"SAVE {s.Slot}",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
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
                FontWeight = FontWeight.SemiBold,
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
                FontWeight = FontWeight.Bold,
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
                    FontWeight = FontWeight.SemiBold,
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
                body.Children.Add(new TextBlock
                {
                    Text = RankName(s.RunsCompleted),
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(BrandPink),
                    Margin = new Thickness(0, 10, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                body.Children.Add(StatRow($"{s.RunsCompleted}", s.RunsCompleted == 1 ? "descent" : "descents"));
                body.Children.Add(StatRow($"✦ {s.Sparks:N0}", "drops"));
                body.Children.Add(StatRow($"🪙 {s.Gold:N0}", "gold"));
                if (s.BestScore > 0) body.Children.Add(StatRow($"{s.BestScore:N0}", "best score"));

                var when = s.LastPlayedUtc.HasValue
                    ? "Last played " + s.LastPlayedUtc.Value.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture)
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
                    Content = new TextBlock { Text = "🗑" },
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    FontSize = 12,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(0),
                    Foreground = TextDim,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 10, 10, 0),
                    Tag = s.Slot,
                };
                ToolTip.SetTip(del, $"Erase Save {s.Slot}");
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
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = content,
                Tag = s.Slot,
            };
            card.PointerReleased += Card_Click;
            return card;
        }

        private static StackPanel StatRow(string value, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = White });
            row.Children.Add(new TextBlock { Text = "  " + label, FontSize = 12, Foreground = TextDim, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private void Card_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if (sender is Border { Tag: int slot })
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
                    ? new DropShadowEffect { Color = BrandPink, BlurRadius = 18, OffsetX = 0, OffsetY = 0, Opacity = 0.55 }
                    : null;
            }
        }

        // ponytail: needs ChaosMeta.DeleteSlot plus a confirmation dialog (WPF used MessageBox,
        // which Avalonia has no equivalent of), wired when they move to Core. Erasing a save
        // unprompted is worse than not erasing it, so this only logs until both exist.
        private void DeleteSlot_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            e.Handled = true;   // don't let the click also select the card
            if (sender is not Button { Tag: int slot }) return;
            Log.Debug("ChaosSlotPicker: erase Save {Slot} requested; no service on this head yet", slot);
        }

        private void BtnOpenFolder_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(CorePaths.UserData);
                Process.Start(new ProcessStartInfo { FileName = CorePaths.UserData, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Debug("ChaosSlotPicker: open folder failed: {E}", ex.Message);
            }
        }
    }
}
