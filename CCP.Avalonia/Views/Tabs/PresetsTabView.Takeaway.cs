using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.JustDrop;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Takeaway strip: WPF MainWindow/MainWindow.Takeaway.cs. A receipt, not a launcher: a chip
    /// replays one delivered Just Drop order and mints nothing. Three chips pinned, the rest behind
    /// "+n more" in the tray, the "order a drop" door last while the server's door is open. Just
    /// Drop is for everyone: no tier check and no badge anywhere on the strip. Signed out, or the
    /// drawer unreachable, paints WPF's own empty state - never sample orders.
    /// </summary>
    public partial class PresetsTabView
    {
        /// <summary>WPF TakeawayShelfCap: pinned before the "+n more" toggle.</summary>
        internal const int TakeawayShelfCap = 3;

        private bool _takeawayLoading;
        private bool _takeawayTrayOpen;
        private bool _takeawayHooked;
        private TextBlock? _takeawayMoreLabel;
        private int _takeawayMoreCount;

        /// <summary>Test seams: the drawer read, the replay, the shop door and the clipboard.</summary>
        internal Func<Task<IReadOnlyList<JustDropOrdersService.Order>>> TakeawayFetch = () => JustDropOrdersService.FetchAsync();
        internal Action<string> TakeawayReplay = code => { Windows.MainShellWindow.SeedWindowDoors(); JustDropHostService.LaunchReplay(code); };
        internal Action TakeawayOpenShop = () => { Windows.MainShellWindow.SeedWindowDoors(); JustDropHostService.LaunchShop(); };
        internal Func<string, Task>? TakeawayCopy;

        private void HookTakeaway()
        {
            if (_takeawayHooked) return;
            _takeawayHooked = true;
            JustDropOrdersService.DrawerChanged += OnTakeawayWorldChanged;
            JustDropService.AvailabilityChanged += OnTakeawayDoorChanged;
        }

        private void UnhookTakeaway()
        {
            if (!_takeawayHooked) return;
            _takeawayHooked = false;
            JustDropOrdersService.DrawerChanged -= OnTakeawayWorldChanged;
            JustDropService.AvailabilityChanged -= OnTakeawayDoorChanged;
        }

        private void OnTakeawayDoorChanged(object? sender, EventArgs e) => OnTakeawayWorldChanged();

        private void OnTakeawayWorldChanged() =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshTakeawayShelf);

        /// <summary>WPF RefreshTakeawayShelf: one read in flight at a time, then a repaint.</summary>
        internal void RefreshTakeawayShelf()
        {
            if (_takeawayLoading) return;
            _takeawayLoading = true;
            _ = LoadTakeawayShelfAsync();
        }

        internal async Task LoadTakeawayShelfAsync()
        {
            IReadOnlyList<JustDropOrdersService.Order> orders = Array.Empty<JustDropOrdersService.Order>();
            try { orders = await TakeawayFetch(); }
            catch (Exception ex) { Log.Debug("LoadTakeawayShelfAsync: {E}", ex.Message); }

            try { PaintTakeawayShelf(orders); }
            catch (Exception ex) { Log.Warning(ex, "PaintTakeawayShelf failed; the shelf stays as it was"); }
            finally { _takeawayLoading = false; }
        }

        internal void PaintTakeawayShelf(IReadOnlyList<JustDropOrdersService.Order> orders)
        {
            TakeawayShelf.Children.Clear();
            TakeawayTray.Children.Clear();
            _takeawayMoreLabel = null;
            _takeawayMoreCount = 0;

            int pinned = 0;
            foreach (var order in orders)
            {
                if (pinned >= TakeawayShelfCap) break;
                if (BuildTakeawayDropChip(order) is not { } chip) continue;
                TakeawayShelf.Children.Add(chip);
                pinned++;
            }

            int trayRows = 0;
            foreach (var order in orders)
            {
                if (BuildTakeawayTrayRow(order) is not { } row) continue;
                TakeawayTray.Children.Add(row);
                trayRows++;
            }

            _takeawayMoreCount = Math.Max(0, trayRows - pinned);
            if (_takeawayMoreCount > 0) TakeawayShelf.Children.Add(BuildTakeawayMoreChip());
            SetTakeawayTrayOpen(false);

            bool doorOpen = JustDropService.DoorAvailable;
            if (doorOpen) TakeawayShelf.Children.Add(BuildTakeawayDoorChip());

            TxtTakeawayCount.Text = orders.Count > 0 ? Loc.GetF("sd_takeaway_kept", orders.Count) : "";
            bool empty = pinned == 0 && !doorOpen;
            TxtTakeawayEmpty.IsVisible = empty;
            TakeawayShelf.IsVisible = !empty;
            TakeawayZone.IsVisible = true;
        }

        private void SetTakeawayTrayOpen(bool open)
        {
            _takeawayTrayOpen = open && _takeawayMoreCount > 0;
            TakeawayTrayHost.IsVisible = _takeawayTrayOpen;
            if (_takeawayMoreLabel != null)
                _takeawayMoreLabel.Text = TakeawayMoreText(_takeawayMoreCount, _takeawayTrayOpen);
        }

        internal static string TakeawayMoreText(int count, bool open)
            => Loc.GetF("sd_takeaway_more", count) + (open ? "  ▴" : "  ▾");

        private static IBrush AppBrush(string key, Color fallback) =>
            Application.Current != null && Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) && v is IBrush b
                ? b : new SolidColorBrush(fallback);

        private Border? BuildTakeawayDropChip(JustDropOrdersService.Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Code)) return null;

            var chip = new Border { Tag = order, Theme = TabTheme("SdTakeawayChip") };
            chip.PointerReleased += TakeawayCard_Click;
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_replay"));

            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new TextBlock
            {
                Text = "\U0001F4E6",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            line.Children.Add(new TextBlock { Text = SafeOrderName(order), MaxWidth = 120, Theme = TabTheme("SdTakeawayChipTitle") });
            line.Children.Add(new TextBlock { Text = FormatTakeawayMeta(order), Theme = TabTheme("SdTakeawayChipMeta") });
            line.Children.Add(BuildTakeawayCopyChip(order));
            chip.Child = line;
            return chip;
        }

        private Border BuildTakeawayMoreChip()
        {
            var chip = new Border { Theme = TabTheme("SdTakeawayChipAccent") };
            chip.PointerReleased += TakeawayMore_Click;
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_more"));
            var label = new TextBlock
            {
                Text = TakeawayMoreText(_takeawayMoreCount, false),
                Foreground = AppBrush("PinkBrush", Color.FromRgb(255, 105, 180)),
                FontSize = 11.5,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _takeawayMoreLabel = label;
            chip.Child = label;
            return chip;
        }

        private Border? BuildTakeawayTrayRow(JustDropOrdersService.Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Code)) return null;

            var row = new Border { Tag = order, Theme = TabTheme("SdTakeawayRow") };
            row.PointerReleased += TakeawayCard_Click;
            ToolTip.SetTip(row, Loc.Get("tooltip_takeaway_replay"));

            // icon | name | minutes | date | age | copy
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto") };
            void Put(Control c, int col) { Grid.SetColumn(c, col); grid.Children.Add(c); }

            Put(new TextBlock
            {
                Text = "\U0001F39A",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            }, 0);
            Put(new TextBlock { Text = SafeOrderName(order), Theme = TabTheme("SdTakeawayRowTitle") }, 1);
            Put(new TextBlock { Text = FormatTakeawayMinutes(order), MinWidth = 58, Theme = TabTheme("SdTakeawayRowMeta") }, 2);
            Put(new TextBlock { Text = FormatTakeawayDate(order), MinWidth = 62, Theme = TabTheme("SdTakeawayRowMeta") }, 3);
            Put(new TextBlock { Text = FormatTakeawayAge(order), MinWidth = 78, Theme = TabTheme("SdTakeawayRowAge") }, 4);
            Put(BuildTakeawayCopyChip(order), 5);
            row.Child = grid;
            return row;
        }

        private Border BuildTakeawayCopyChip(JustDropOrdersService.Order order)
        {
            var chip = new Border
            {
                Tag = order,
                Theme = TabTheme("SdTakeawayCopy"),
                Child = new TextBlock
                {
                    Text = "\U0001F517",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_copy_link"));
            chip.PointerReleased += TakeawayCopyLink_Click;
            return chip;
        }

        /// <summary>The "order a drop" door. For everyone: never a Prime badge, never a tier check.</summary>
        private Border BuildTakeawayDoorChip()
        {
            var chip = new Border { Theme = TabTheme("SdTakeawayChipDoor") };
            chip.PointerReleased += TakeawayDoor_Click;
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_order_drop"));

            var pink = AppBrush("PinkBrush", Color.FromRgb(255, 105, 180));
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new TextBlock
            {
                Text = "+",
                Foreground = pink,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            line.Children.Add(new TextBlock
            {
                Text = Loc.Get("sd_takeaway_order"),
                Foreground = pink,
                FontSize = 11.5,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            chip.Child = line;
            return chip;
        }

        // ---- formatters (WPF FormatTakeaway*) ----

        internal static string FormatTakeawayMeta(JustDropOrdersService.Order order)
        {
            string date = FormatTakeawayDate(order);
            int minutes = order.Minutes;
            return minutes > 0 ? Loc.GetF("sd_takeaway_meta", minutes, date) : date;
        }

        internal static string FormatTakeawayDate(JustDropOrdersService.Order order)
        {
            try { return order.At.ToString("MMM d").ToUpperInvariant(); }
            catch { return ""; }
        }

        internal static string FormatTakeawayMinutes(JustDropOrdersService.Order order)
            => order.Minutes > 0 ? Loc.GetF("takeaway_row_min", order.Minutes) : "";

        internal static string FormatTakeawayAge(JustDropOrdersService.Order order)
        {
            int days = (int)Math.Floor((DateTimeOffset.UtcNow - order.At).TotalDays);
            if (days <= 0) return Loc.Get("takeaway_today");
            return Loc.GetF(days == 1 ? "takeaway_day_ago" : "takeaway_days_ago", days);
        }

        internal static string SafeOrderName(JustDropOrdersService.Order order) =>
            string.IsNullOrWhiteSpace(order.Name) ? Loc.Get("sd_takeaway_order_fallback") : order.Name;

        // ---- actions ----

        private void TakeawayCard_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            try
            {
                if (sender is not Control { Tag: JustDropOrdersService.Order order }) return;
                TakeawayReplay(order.Code);
            }
            catch (Exception ex) { Log.Error(ex, "Failed to replay a Takeaway order"); }
        }

        private async void TakeawayCopyLink_Click(object? sender, PointerReleasedEventArgs e)
        {
            e.Handled = true;   // the chip under it replays; the link only copies
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            try
            {
                if (sender is not Control { Tag: JustDropOrdersService.Order order }) return;
                if (string.IsNullOrWhiteSpace(order.Code)) return;
                var url = JustDropService.TasteUrl(order.Code);
                if (TakeawayCopy != null) await TakeawayCopy(url);
                else if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(url);
                else throw new InvalidOperationException("no clipboard");
                App.Notifications.Show(Loc.Get("toast_taste_link_copied"), Helpers.NotificationType.Success, TimeSpan.FromSeconds(4));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to copy a Takeaway taste link");
                try { App.Notifications.Show(Loc.Get("toast_taste_link_copy_failed"), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(4)); }
                catch { /* the toast is a courtesy */ }
            }
        }

        private void TakeawayMore_Click(object? sender, PointerReleasedEventArgs e)
        {
            e.Handled = true;
            SetTakeawayTrayOpen(!_takeawayTrayOpen);
        }

        private void TakeawayDoor_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            try
            {
                if (!JustDropService.DoorAvailable) return;
                TakeawayOpenShop();
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to open the Just Drop shop from the Takeaway shelf"); }
        }

        /// <summary>Test seams.</summary>
        internal IReadOnlyList<Border> TakeawayChips => TakeawayShelf.Children.OfType<Border>().ToList();
        internal int TakeawayTrayRows => TakeawayTray.Children.Count;
        internal bool TakeawayTrayIsOpen => _takeawayTrayOpen;
        internal void ToggleTakeawayTrayForTest() => SetTakeawayTrayOpen(!_takeawayTrayOpen);
    }
}
