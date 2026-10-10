// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: 5. the menu, BuildMenu + Row
// (:1180-1281), ApplyPriceToggles (:1149), PriceToggle_Changed (:1296), ChkFlashDodge_Changed (:1168),
// RefreshTag (:431), and ChasterTabView.Help.cs (How it works, PaintMenuHelp, Reset) and Fx.cs
// HeroArtPlate_SizeChanged (:383, the art's fade into the card).
// The settings list is the truth and the rows are a view of it; a way-out id (TabPrices.NeverPriced)
// is never on the table, so it never gets a row, a price or a toggle here.
// A row answers its click with FxRow (the pop, and sparks when it goes on: ChasterTabView.Fx.cs).
// ponytail: the where line and the misses tooltip are read once per build, not live-bound to a language switch.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private static readonly FontFamily Mono = new("Consolas, Courier New");
        private static readonly Color PlateColour = Color.FromRgb(0x1A, 0x12, 0x30);

        private bool _menuBuilt;
        /// <summary>False until the constructor is through: a slider coerces its value while the
        /// markup loads, and that must never be read as the player asking for a limit.</summary>
        private bool _menuReady;
        private readonly Dictionary<string, ToggleButton> _priceToggles = new();
        private readonly Dictionary<string, Border> _rowDims = new();

        private void MenuInit()
        {
            HeroArt.Source = Helpers.ModArt.TryLoad("features/Phrase_Lock.png", 760);
            JackpotArt.Source = Helpers.ModArt.TryLoad(TabMenuCopy.ArtFor(TabMenuCopy.JackpotId), 88);
            ToolTip.SetTip(JackpotRow, Loc.Get(TabMenuCopy.WhyKey(TabMenuCopy.JackpotId)));
            PaintHeroArtFade();
            ActualThemeVariantChanged += (_, _) => PaintHeroArtFade();
            PaperTag.PointerReleased += (_, _) => ToggleBill();
            // WPF :102: a long lock name shrinks to the hero's inner width instead of wrapping or clipping.
            HeroCard.SizeChanged += (_, _) => HeroTitle.FitWidth = Math.Max(0, HeroCard.Bounds.Width - 52);
            Calendar.SizeChanged += (_, _) => PlaceCalendarTag();
            TrailerInit();
            _menuReady = true;
        }

        /// <summary>WPF HeroArtPlate_SizeChanged (Fx.cs:383): the card's own surface, thinning out to the right.</summary>
        private void PaintHeroArtFade()
        {
            var surface = Brush("SurfaceBgBrush") is ISolidColorBrush s ? s.Color : PlateColour;
            HeroArtFade.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(surface, 0),
                    new GradientStop(Color.FromArgb(0xD0, surface.R, surface.G, surface.B), 0.28),
                    new GradientStop(Color.FromArgb(0x40, surface.R, surface.G, surface.B), 0.62),
                    new GradientStop(Color.FromArgb(0x00, surface.R, surface.G, surface.B), 1),
                },
            };
        }

        // ---- the paper tag (WPF RefreshTag :431) ----

        internal void RefreshTag(int balance)
        {
            TxtTagAmount.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtTagAmount.Foreground = new SolidColorBrush(balance > 0 ? Color.FromRgb(0xC8, 0x24, 0x4A)
                : balance < 0 ? Color.FromRgb(0x1E, 0x8A, 0x6E) : Color.FromRgb(0x24, 0x1A, 0x2E));
            var chaster = ChasterHead.Service;
            var line = TabPageText.Tag(balance, chaster?.PushableTodaySeconds ?? 0, chaster?.IsPaused == true,
                !string.IsNullOrEmpty(CoreSettings.Current.ChasterLockId) && chaster?.LockLookup != LockLookup.Ambiguous,
                chaster?.AddsBlocked == true);
            TxtTagLands.Text = line.Today == null ? Loc.Get(line.Key) : Loc.GetF(line.Key, line.Today, line.Later!);
            TxtTagStamp.Text = Loc.Get(balance > 0 ? "chaster_tag_unpaid" : balance < 0 ? "chaster_tag_credit" : "chaster_tag_clear");
            var stamp = new SolidColorBrush(balance > 0 ? Color.FromRgb(0xC8, 0x24, 0x4A)
                : balance < 0 ? Color.FromRgb(0x1E, 0x8A, 0x6E) : Color.FromRgb(0x6E, 0x66, 0x86));
            TagStamp.BorderBrush = stamp;
            TxtTagStamp.Foreground = stamp;
            PaintCalendarTag(balance);
        }

        // ---- the rows ----

        internal void BuildMenu()
        {
            if (_menuBuilt) return;
            _menuBuilt = true;
            var (costs, earnBacks) = TabPageText.Split(TabPrices.All);
            foreach (var price in costs) CostRows.Children.Add(Row(price, CostColour));
            foreach (var price in earnBacks) EarnRows.Children.Add(Row(price, EarnColour));
        }

        /// <summary>A row is a picture, a short name, where it happens, a tier sign when the feature
        /// needs one, and the price on a stamp. The one row that charges for staying away says how
        /// in its tooltip, and nowhere on the page.</summary>
        private ToggleButton Row(TabPrice price, Color colour)
        {
            var brush = new SolidColorBrush(colour);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            // the picture, dimmed to a shade while the row is off
            var thumb = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0) };
            var dim = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(PlateColour), Opacity = 0.62, IsHitTestVisible = false };
            var plate = new Panel();
            var art = TabMenuCopy.ArtFor(price.Id) is { } file ? Helpers.ModArt.TryLoad(file, 80) : null;
            if (art != null) plate.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
            else plate.Background = new SolidColorBrush(Color.FromArgb(0x30, colour.R, colour.G, colour.B));
            plate.Children.Add(dim);
            thumb.Child = plate;
            grid.Children.Add(thumb);
            _rowDims[price.Id] = dim;

            // the words: a short name, and where it happens with the tier sign beside it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock
            {
                Text = TabMenuCopy.ShortName(price.Id, Loc.Get), FontFamily = Display, FontSize = 13.5, FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("TextLightBrush"),
            });
            var whereRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
            var whereText = Loc.Get(TabMenuCopy.WhereKey(price.Id));
            var where = new TextBlock
            {
                Text = whereText, FontSize = 10.5, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("TextMutedBrush"),
            };
            whereRow.Children.Add(where);
            var tier = TabMenuCopy.BadgeTier(price.Gate);
            if (tier > 0)
                whereRow.Children.Add(new TierBadge { Tier = tier, MaxWidthOverride = 46, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            words.Children.Add(whereRow);
            // A horizontal row measures its line at unlimited width, so the trimming never engages
            // (WPF TAB-13): capped at the column less the tier sign, the whole line shows on hover.
            var signRoom = tier > 0 ? 52.0 : 0.0;
            words.SizeChanged += (_, e) => where.MaxWidth = Math.Max(0, e.NewSize.Width - signRoom);
            ToolTip.SetTip(where, whereText);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);

            // the stamp
            var stamp = new TextBlock
            {
                Text = TabPageText.Price(price, Loc.Get("chaster_each")),
                FontFamily = Mono, FontSize = 12.5, FontWeight = FontWeight.Bold,
                Foreground = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(stamp, 2);
            grid.Children.Add(stamp);
            WireStamp(price, stamp);

            var row = new ToggleButton
            {
                Theme = this.TryFindResource("CirceRow", out var theme) ? theme as ControlTheme : null,
                Tag = price.Id,
                Background = brush,
                BorderBrush = brush,
                Content = grid,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            };
            if (price.Id == CircesMisses.EventId)
                ToolTip.SetTip(row, new TextBlock { Text = Loc.Get("chaster_misses_hint"), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            row.Click += PriceToggle_Changed;
            row.PointerEntered += Row_PointerEntered;
            row.PointerExited += Row_PointerExited;
            _priceToggles[price.Id] = row;
            return row;
        }

        internal ToggleButton? RowFor(string id) => _priceToggles.TryGetValue(id, out var row) ? row : null;
        internal IReadOnlyCollection<string> RowIds => _priceToggles.Keys;

        /// <summary>The signed figure a row books right now, as the stamp, the words and the trailer all print it.</summary>
        internal static int ShownSeconds(string id) => TabPriceEdit.Effective(id, Overrides);

        private void PaintRowLit(string id, bool on)
        {
            if (_rowDims.TryGetValue(id, out var dim)) dim.Opacity = on ? 0 : 0.62;
        }

        /// <summary>Push the saved set onto the rows. Never the other way round.</summary>
        internal void ApplyPriceToggles()
        {
            BuildMenu();
            var on = new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>(), StringComparer.Ordinal);
            _loading = true;
            try
            {
                foreach (var (id, row) in _priceToggles)
                {
                    row.IsChecked = on.Contains(id);
                    PaintRowLit(id, on.Contains(id));
                }
                ChkFlashDodge.IsChecked = CoreSettings.Current.ChasterFlashDodge;
            }
            finally { _loading = false; }
            PaintStamps();
        }

        private void PriceToggle_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading || sender is not ToggleButton row || row.Tag is not string id) return;
            var settings = CoreSettings.Current;
            var on = row.IsChecked == true;
            // A new list every time: the service reads the setting fresh on each event, maybe from
            // another thread, and must never see a list that is being edited.
            var next = new List<string>(settings.ChasterPrices ?? new List<string>());
            next.Remove(id);
            if (on) next.Add(id);
            settings.ChasterPrices = next;
            CoreSettings.Save();
            PaintRowLit(id, on);
            // One hand-flipped row can land exactly on a preset, or step off one. Say which.
            RefreshPresets();
            RefreshHero();   // the first row on ends the "nothing counts yet" nudge (WPF RefreshSetupHint)
            FxRow(row, on);
        }

        /// <summary>The rows that are on, top to bottom, for the key's ripple (WPF LitRows).</summary>
        private IEnumerable<ToggleButton> LitRows() => _priceToggles.Values.Where(r => r.IsChecked == true);

        /// <summary>Red flashes, with a 4 s ring to dodge them, or no red flashes at all (the default).</summary>
        private void ChkFlashDodge_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading || !_menuReady) return;
            CoreSettings.Current.ChasterFlashDodge = ChkFlashDodge.IsChecked == true;
            CoreSettings.Save();
        }

        // ---- the page says what it does (WPF ChasterTabView.Help.cs) ----

        private void BtnHowItWorks_Click(object? sender, RoutedEventArgs e) => HowItWorksCard.IsVisible = !HowItWorksCard.IsVisible;

        /// <summary>The line over the menu, the reset link and the red flash line.</summary>
        internal void PaintMenuHelp()
        {
            var open = FiguresEditable();
            TxtMenuHint.Text = Loc.Get(open ? "chaster_menu_hint_edit" : "chaster_menu_hint_locked");
            var edited = CoreSettings.Current.ChasterPriceOverrides is { Count: > 0 } o && o.Keys.Any(id => TabPriceEdit.IsEdited(id, o));
            BtnResetPrices.IsVisible = open && edited;

            // A red flash books Natasha's row, so it follows that row's figure and needs it on.
            var natashaOn = CoreSettings.Current.ChasterPrices?.Contains(NatashasFavourite.EventId) == true;
            TxtFlashDodgeSub.Text = natashaOn
                ? Loc.GetF("chaster_flash_dodge_sub", NatashasFavourite.DodgeMs / 1000,
                    CircesTab.Format(ShownSeconds(NatashasFavourite.EventId), signed: false))
                : Loc.Get("chaster_flash_dodge_needs");
            FlashDodgeRow.Opacity = natashaOn ? 1 : 0.7;
        }

        /// <summary>Every edited time back to the table's figure. Only while no lock runs.</summary>
        private void BtnResetPrices_Click(object? sender, RoutedEventArgs e) => ResetPrices();

        internal void ResetPrices()
        {
            if (!FiguresEditable()) return;
            if (_editingId != null) CancelPriceEdit();
            CoreSettings.Current.ChasterPriceOverrides = new();
            CoreSettings.Save();
            Serilog.Log.Information("[Chaster] prices reset to the table by the player");
            PaintStamps();
            if (_trailerShown && _trailerRow != null) OpenTrailer(_trailerRow);
        }
    }
}
