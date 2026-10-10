// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.PriceEdit.cs.
// Click the figure to set it (tester feedback 2026-09-29). A row's stamp turns into a small box
// while no lock runs; Enter or clicking away keeps the new time, Esc drops it, empty text puts the
// default back. The row's sign never moves and the size is clamped by TabPriceEdit, so a cost stays
// a cost and the day and backlog limits still decide what books. Rows without one fixed figure (the
// way out, "misses", the leash, a stake, the day-end verdicts) never show an editable stamp.
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private readonly Dictionary<string, TextBlock> _stamps = new();
        private TextBox? _stampEditor;
        private string? _editingId;

        private static IReadOnlyDictionary<string, int>? Overrides => CoreSettings.Current.ChasterPriceOverrides;

        /// <summary>Figures move only while no lock runs.</summary>
        internal static bool FiguresEditable() =>
            TabPriceEdit.CanEdit(ChasterHead.Service?.IsLinked == true, ChasterHead.Service?.LockLookup ?? LockLookup.Unlinked);

        /// <summary>The id being edited, for the tests. Null when no box is open.</summary>
        internal string? EditingPriceId => _editingId;
        internal TextBox? PriceEditor => _stampEditor;

        internal TextBlock? StampFor(string id) => _stamps.TryGetValue(id, out var s) ? s : null;

        private void WireStamp(TabPrice price, TextBlock stamp)
        {
            _stamps[price.Id] = stamp;
            if (TabPriceEdit.Editable(price.Id))
            {
                // Taken before the row sees it: a click on the figure edits, it never flips the row.
                stamp.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(stamp).Properties.IsLeftButtonPressed && BeginPriceEdit(price.Id)) e.Handled = true;
                };
            }
            PaintStamp(price.Id);
        }

        /// <summary>Stamps after a lock starts or ends, or after an edit.</summary>
        internal void PaintStamps()
        {
            foreach (var id in _stamps.Keys) PaintStamp(id);
            if (_editingId != null && !FiguresEditable()) CancelPriceEdit();
            PaintMenuHelp();
        }

        private void PaintStamp(string id)
        {
            if (!_stamps.TryGetValue(id, out var stamp) || TabPrices.Find(id) is not { } price) return;
            var overrides = Overrides;
            stamp.Text = TabPageText.Price(TabPriceEdit.Shown(price, overrides), Loc.Get("chaster_each"));
            stamp.FontStyle = TabPriceEdit.IsEdited(id, overrides) ? FontStyle.Italic : FontStyle.Normal;
            if (!TabPriceEdit.Editable(id)) return;
            var open = FiguresEditable();
            stamp.Cursor = open ? new Cursor(StandardCursorType.Ibeam) : null;
            stamp.TextDecorations = open ? TextDecorations.Underline : null;
            ToolTip.SetTip(stamp, open
                ? Loc.Get("chaster_price_edit_tip").Replace("{0}", CircesTab.Format(price.Seconds, signed: false))
                : Loc.Get("chaster_price_edit_locked"));
        }

        /// <summary>Open the box on a row's figure. False when this row or this moment does not allow it.</summary>
        internal bool BeginPriceEdit(string id)
        {
            if (!TabPriceEdit.Editable(id) || !FiguresEditable()) return false;
            if (!_stamps.TryGetValue(id, out var stamp) || stamp.Parent is not Grid grid) return false;
            if (_editingId != null) CommitPriceEdit();
            var seconds = TabPriceEdit.Effective(id, Overrides);
            var box = new TextBox
            {
                Text = CircesTab.Format(seconds, signed: false),
                FontFamily = stamp.FontFamily,
                FontSize = stamp.FontSize,
                FontWeight = stamp.FontWeight,
                Foreground = stamp.Foreground,
                CaretBrush = stamp.Foreground,
                Background = Brushes.Transparent,
                BorderBrush = stamp.Foreground,
                BorderThickness = new Thickness(0, 0, 0, 1.5),
                MinWidth = 44,
                MinHeight = 0,
                MaxLength = 8,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Margin = stamp.Margin,
            };
            ToolTip.SetTip(box, ToolTip.GetTip(stamp));
            Grid.SetColumn(box, Grid.GetColumn(stamp));
            // Esc here drops the edit, it is not the panic key's press (WPF bug hunt 2026-09-29, TAB-8).
            EscapeClaim.Mark(box);
            box.AddHandler(KeyDownEvent, PriceBox_PreviewKeyDown, RoutingStrategies.Tunnel);
            // the row under the box would take a space, or the release of one, as a click
            box.KeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) e.Handled = true; };
            box.PointerPressed += (_, e) => e.Handled = true;
            box.PointerReleased += (_, e) => e.Handled = true;
            box.LostFocus += (_, _) => { if (ReferenceEquals(_stampEditor, box)) CommitPriceEdit(); };
            stamp.IsVisible = false;
            grid.Children.Add(box);
            _stampEditor = box;
            _editingId = id;
            box.Focus();
            box.SelectAll();
            return true;
        }

        private void PriceBox_PreviewKeyDown(object? sender, KeyEventArgs e) => PriceBoxKey(e);

        internal void PriceBoxKey(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    CommitPriceEdit();
                    break;
                case Key.Escape:
                    e.Handled = true;
                    EscapeClaim.Taken();
                    CancelPriceEdit();
                    break;
                case Key.Space:
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Keep what the box says. Text that does not read as a time is dropped.</summary>
        internal void CommitPriceEdit() => ClosePriceEdit(keep: true);

        internal void CancelPriceEdit() => ClosePriceEdit(keep: false);

        private void ClosePriceEdit(bool keep)
        {
            if (_stampEditor is not { } box || _editingId is not { } id) return;
            _stampEditor = null;
            _editingId = null;
            var text = box.Text;
            if (box.Parent is Grid grid) grid.Children.Remove(box);
            if (_stamps.TryGetValue(id, out var stamp)) stamp.IsVisible = true;
            if (keep && FiguresEditable() && TabPriceEdit.Parse(text) is { } seconds)
            {
                var settings = CoreSettings.Current;
                var before = TabPriceEdit.Effective(id, settings.ChasterPriceOverrides);
                settings.ChasterPriceOverrides = TabPriceEdit.With(settings.ChasterPriceOverrides, id, seconds);
                CoreSettings.Save();
                if (TabPriceEdit.Effective(id, settings.ChasterPriceOverrides) != before)
                    Serilog.Log.Information("[Chaster] price {Id} set to {Seconds}s by the player", id,
                        TabPriceEdit.Effective(id, settings.ChasterPriceOverrides));
            }
            PaintStamp(id);
            PaintMenuHelp();
            // The words and the trailer read the figure too.
            if (_trailerShown && _trailerRow?.Tag as string == id) OpenTrailer(_trailerRow);
        }
    }
}
