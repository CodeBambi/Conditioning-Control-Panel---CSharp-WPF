using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.JustDrop;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Takeaway strip on the Session door - the Just Drop receipt drawer, on the desktop.
    ///
    /// <para>Every chip is an order the account already owns, newest first, and clicking one
    /// replays it in the Just Drop window. The strip ends in a dashed door to the shop, so it reads
    /// as an invitation rather than a dead end even before the first order.</para>
    ///
    /// <para><b>One line and a tray (2026-08-16).</b> The shelf used to be a horizontal
    /// ScrollViewer of up to twelve 158x74 cards. It is now a single 32px line - the three newest
    /// drops, a "+n more" toggle, the shop door, and hard right the page's Community Catalogue
    /// chip and session Export button - over an overflow tray that lists EVERY order the drawer
    /// returned. The strip never wraps and never scrolls sideways; the tray is what makes the rest
    /// reachable.</para>
    ///
    /// <para><b>Where the data comes from, and why that is not a desktop order book.</b>
    /// <see cref="JustDropOrdersService"/> reads the SERVER's drawer live over the device-token
    /// door and keeps nothing. The desktop still cannot create, price or delete an order - every one
    /// of those lives in the shop, and every action on a card here is "open the web player at this
    /// code". That is the same doctrine <see cref="JustDropService"/> has always stated; the shelf
    /// renders the drawer, it does not own it.</para>
    ///
    /// <para><b>Copying a link is not owning sharing.</b> The one exception the shelf carries is a
    /// per-card copy chip, and all it does is put <see cref="JustDropService.TasteUrl"/> on the
    /// clipboard: no dialog, no expiry, no upload, no order mutated, and nothing rendered on screen.
    /// The page that link points at, and everything that decides what it shows, is the web's.</para>
    ///
    /// <para><b>Replays are free.</b> The bare player mints nothing, and the desktop's XP grant is
    /// deduplicated per order code by <see cref="CreditedOrders"/> - so a shelf that exists to
    /// invite replaying cannot be turned into an XP faucet.</para>
    ///
    /// <para>Local session recaps deliberately do NOT appear here. They are a different kind of
    /// thing - runs of the user's own sessions, not deliveries - and they already have a home behind
    /// the header band's Recent button.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// How many receipts are PINNED to the strip. Not a cap on what is rendered any more: the
        /// tray below the strip lists every order the server returns (it keeps 30), so nothing is
        /// hidden - this is only how many fit on one line before the "+n more" toggle takes over.
        /// Three, because the line also has to carry the shop door and the catalogue chip, and a
        /// strip that pushes those off the edge defeats the point of putting them there.
        /// </summary>
        private const int TakeawayShelfCap = 3;

        /// <summary>Guards against two in-flight loads racing to repaint the same strip - a tab show
        /// and a window close can land within a frame of each other.</summary>
        private bool _takeawayLoading;

        /// <summary>Whether the overflow tray is open. Reset to false by every repaint - an
        /// expander that remembered being open would push the session rack down a hundred pixels
        /// on every tab visit.</summary>
        private bool _takeawayTrayOpen;

        /// <summary>The "+n more" chip's own label, kept so the toggle can flip its glyph without
        /// rebuilding the strip. Null whenever there is no overflow.</summary>
        private TextBlock? _takeawayMoreLabel;

        /// <summary>How many orders the tray holds beyond the pinned ones - the n in "+n more".</summary>
        private int _takeawayMoreCount;

        /// <summary>
        /// Rebuild the strip. Kicks off an async read of the order drawer and repaints when it
        /// lands; the strip keeps showing whatever it last had in the meantime rather than blanking,
        /// because a flash of empty shelf on every tab visit reads as "your orders are gone".
        /// </summary>
        internal void RefreshTakeawayShelf()
        {
            if (_takeawayLoading) return;
            var tab = PresetsTab;
            if (tab?.TakeawayShelf == null) return;

            _takeawayLoading = true;
            _ = LoadTakeawayShelfAsync();
        }

        private async System.Threading.Tasks.Task LoadTakeawayShelfAsync()
        {
            IReadOnlyList<JustDropOrdersService.Order> orders = Array.Empty<JustDropOrdersService.Order>();
            try
            {
                orders = await JustDropOrdersService.FetchAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // FetchAsync already swallows its own failures; this is the belt for a cancellation
                // or a dispatcher teardown mid-await. An empty drawer renders fine.
                App.Logger?.Debug("LoadTakeawayShelfAsync: {E}", ex.Message);
            }

            try { PaintTakeawayShelf(orders); }
            catch (Exception ex)
            {
                // A shelf of receipts may never be why the Session door fails to open.
                App.Logger?.Warning(ex, "PaintTakeawayShelf failed; the shelf stays as it was");
            }
            finally { _takeawayLoading = false; }
        }

        private void PaintTakeawayShelf(IReadOnlyList<JustDropOrdersService.Order> orders)
        {
            var tab = PresetsTab;
            var shelf = tab?.TakeawayShelf;
            if (tab == null || shelf == null) return;

            shelf.Children.Clear();
            tab.TakeawayTray?.Children.Clear();
            _takeawayMoreLabel = null;
            _takeawayMoreCount = 0;

            // 1. The pinned chips: the newest three, in the order FetchAsync returned them
            //    (newest first - see its remarks).
            int pinned = 0;
            foreach (var order in orders)
            {
                if (pinned >= TakeawayShelfCap) break;
                var chip = BuildTakeawayDropChip(order);
                if (chip == null) continue;
                shelf.Children.Add(chip);
                pinned++;
            }

            // 2. The tray: EVERY order, not just the ones past the cap. A tray that started at
            //    order four would make the strip and the tray two different lists to hold in your
            //    head; this way the tray is simply "all of them", and the strip is a shortcut.
            int trayRows = 0;
            if (tab.TakeawayTray != null)
            {
                foreach (var order in orders)
                {
                    var row = BuildTakeawayTrayRow(order);
                    if (row == null) continue;
                    tab.TakeawayTray.Children.Add(row);
                    trayRows++;
                }
            }

            // 3. The toggle, only when the tray holds something the strip does not already show.
            _takeawayMoreCount = Math.Max(0, trayRows - pinned);
            if (_takeawayMoreCount > 0)
            {
                var more = BuildTakeawayMoreChip();
                if (more != null) shelf.Children.Add(more);
            }
            // Collapsed on every repaint, deliberately - see the field remarks.
            SetTakeawayTrayOpen(false);

            // 4. The door comes last, so the strip reads newest-first and ends on the invitation.
            //    Hidden entirely when the feature is withheld or the server has it off - a chip
            //    that opens a door the user cannot see is a tease for something that does not
            //    exist.
            bool doorOpen = JustDropService.DoorAvailable;
            if (doorOpen)
            {
                var door = BuildTakeawayDoorChip();
                if (door != null) shelf.Children.Add(door);
            }

            if (tab.TxtTakeawayCount != null)
                tab.TxtTakeawayCount.Text = orders.Count > 0
                    ? Loc.GetF("sd_takeaway_kept", orders.Count)
                    : "";

            // The dashed door doubles as the empty state, so the sentence only shows when there is
            // neither a receipt nor a door: a fresh install, or someone signed out.
            bool empty = pinned == 0 && !doorOpen;
            if (tab.TxtTakeawayEmpty != null)
                tab.TxtTakeawayEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (tab.TakeawayShelf != null)
                tab.TakeawayShelf.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

            // THE ZONE STAYS. It used to collapse when there was neither an order nor a door, back
            // when it held nothing else. The strip now also carries the Community Catalogue chip
            // and the session Export button - both docked to its right end, both live on a fresh
            // install - so hiding the zone would take two working controls with it. Only the order
            // half hides; the line itself is permanent furniture.
            if (tab.TakeawayZone != null)
                tab.TakeawayZone.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Open or close the overflow tray and re-stamp the toggle's glyph. Safe with no toggle on
        /// screen (no overflow, or the tab was never built) - it simply closes the tray.
        /// </summary>
        private void SetTakeawayTrayOpen(bool open)
        {
            try
            {
                _takeawayTrayOpen = open && _takeawayMoreCount > 0;

                var host = PresetsTab?.TakeawayTrayHost;
                if (host != null)
                    host.Visibility = _takeawayTrayOpen ? Visibility.Visible : Visibility.Collapsed;

                if (_takeawayMoreLabel != null)
                    _takeawayMoreLabel.Text = TakeawayMoreText(_takeawayMoreCount, _takeawayTrayOpen);
            }
            catch (Exception ex) { App.Logger?.Debug("SetTakeawayTrayOpen: {E}", ex.Message); }
        }

        /// <summary>"+7 more ▾" / "+7 more ▴". The caret is appended rather than localized: it is a
        /// state glyph, not a word.</summary>
        private static string TakeawayMoreText(int count, bool open)
            => string.Format(LocOr("sd_takeaway_more", "+{0} more"), count) + (open ? "  ▴" : "  ▾");

        /// <summary>
        /// One pinned order chip: 📦 name, its meta, and the copy element. Geometry and colour come
        /// from SdTakeawayChip in PresetsTabView.xaml through <see cref="TryFindTabStyle"/>, with
        /// the same literal fallback every other code-built element here carries so a missing style
        /// degrades to a plain visible chip, never an invisible one.
        /// </summary>
        private Border? BuildTakeawayDropChip(JustDropOrdersService.Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Code)) return null;

            var chip = new Border { Tag = order };
            var style = TryFindTabStyle("SdTakeawayChip");
            if (style != null) chip.Style = style;
            else
            {
                chip.Height = 32;
                chip.CornerRadius = new CornerRadius(16);
                chip.Padding = new Thickness(11, 0, 11, 0);
                chip.Margin = new Thickness(0, 0, 7, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Background = Application.Current.Resources["SurfaceBgBrush"] as Brush;
                chip.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as Brush;
                chip.BorderThickness = new Thickness(1);
                chip.Cursor = System.Windows.Input.Cursors.Hand;
            }

            chip.MouseLeftButtonUp += TakeawayCard_Click;
            chip.ToolTip = Loc.Get("tooltip_takeaway_replay");
            PreparePresetCardFx(chip);

            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            line.Children.Add(new Helpers.EmojiTextBlock
            {
                Text = "📦",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            // A cap, not a width: a short order name keeps a short chip. Without it one long name
            // would eat the whole strip and push the door off the end.
            var title = new Helpers.EmojiTextBlock { Text = SafeOrderName(order), MaxWidth = 120 };
            var titleStyle = TryFindTabStyle("SdTakeawayChipTitle");
            if (titleStyle != null) title.Style = titleStyle;
            else
            {
                title.Foreground = Application.Current.Resources["TextLightBrush"] as Brush
                                   ?? new SolidColorBrush(Colors.White);
                title.FontWeight = FontWeights.SemiBold;
                title.FontSize = 12;
                title.VerticalAlignment = VerticalAlignment.Center;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            line.Children.Add(title);

            var meta = new TextBlock { Text = FormatTakeawayMeta(order) };
            var metaStyle = TryFindTabStyle("SdTakeawayChipMeta");
            if (metaStyle != null) meta.Style = metaStyle;
            else
            {
                meta.Foreground = Application.Current.Resources["TextDimBrush"] as Brush;
                meta.FontFamily = new FontFamily("Consolas");
                meta.FontSize = 9.5;
                meta.VerticalAlignment = VerticalAlignment.Center;
                meta.Margin = new Thickness(8, 0, 0, 0);
            }
            line.Children.Add(meta);

            var copy = BuildTakeawayCopyChip(order);
            if (copy != null) line.Children.Add(copy);

            chip.Child = line;
            return chip;
        }

        /// <summary>
        /// The "+n more" toggle. The only chip on the strip that acts on the PAGE rather than on an
        /// order, which is why it is accent-tinted and why its click is marked handled - nothing
        /// underneath it should read an expand as a replay.
        /// </summary>
        private Border? BuildTakeawayMoreChip()
        {
            var chip = new Border();
            var style = TryFindTabStyle("SdTakeawayChipAccent");
            if (style != null) chip.Style = style;
            else
            {
                chip.Height = 32;
                chip.CornerRadius = new CornerRadius(16);
                chip.Padding = new Thickness(11, 0, 11, 0);
                chip.Margin = new Thickness(0, 0, 7, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Background = Application.Current.Resources["TransparentPink20Brush"] as Brush;
                chip.BorderBrush = Application.Current.Resources["TransparentPink40Brush"] as Brush;
                chip.BorderThickness = new Thickness(1);
                chip.Cursor = System.Windows.Input.Cursors.Hand;
            }

            chip.MouseLeftButtonUp += TakeawayMore_Click;
            chip.ToolTip = LocOr("tooltip_takeaway_more", "Show every drop you have kept");

            var label = new TextBlock
            {
                Text = TakeawayMoreText(_takeawayMoreCount, false),
                Foreground = Application.Current.Resources["PinkBrush"] as Brush
                             ?? new SolidColorBrush(Color.FromRgb(255, 105, 180)),
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _takeawayMoreLabel = label;

            chip.Child = label;
            return chip;
        }

        /// <summary>
        /// One 36px tray row. Same order, same click, same copy element as a pinned chip - the tray
        /// is the strip with room to breathe, not a second feature. The extra column it can afford
        /// is the age, which is what tells "the one from last week" from "the one from March".
        /// </summary>
        private Border? BuildTakeawayTrayRow(JustDropOrdersService.Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Code)) return null;

            var row = new Border { Tag = order };
            var style = TryFindTabStyle("SdTakeawayRow");
            if (style != null) row.Style = style;
            else
            {
                row.Height = 36;
                row.CornerRadius = new CornerRadius(8);
                row.Padding = new Thickness(9, 0, 9, 0);
                row.Margin = new Thickness(0, 0, 0, 4);
                row.Background = Application.Current.Resources["SurfaceBgBrush"] as Brush;
                row.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as Brush;
                row.BorderThickness = new Thickness(1);
                row.Cursor = System.Windows.Input.Cursors.Hand;
            }

            row.MouseLeftButtonUp += TakeawayCard_Click;
            row.ToolTip = Loc.Get("tooltip_takeaway_replay");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 0 icon
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 1 name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 2 minutes
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 3 date
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 4 age
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // 5 copy

            var icon = new Helpers.EmojiTextBlock
            {
                Text = "🎚",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var title = new Helpers.EmojiTextBlock { Text = SafeOrderName(order) };
            var titleStyle = TryFindTabStyle("SdTakeawayRowTitle");
            if (titleStyle != null) title.Style = titleStyle;
            else
            {
                title.Foreground = Application.Current.Resources["TextLightBrush"] as Brush
                                   ?? new SolidColorBrush(Colors.White);
                title.FontWeight = FontWeights.SemiBold;
                title.FontSize = 12;
                title.VerticalAlignment = VerticalAlignment.Center;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            // MinWidths turn the three mono cells into columns down the tray instead of three
            // strings drifting with each row's name length - the same reason MakeRackMeta has them.
            var minutes = MakeTakeawayRowMeta(FormatTakeawayMinutes(order), 58, "SdTakeawayRowMeta");
            Grid.SetColumn(minutes, 2);
            grid.Children.Add(minutes);

            var date = MakeTakeawayRowMeta(FormatTakeawayDate(order), 62, "SdTakeawayRowMeta");
            Grid.SetColumn(date, 3);
            grid.Children.Add(date);

            var age = MakeTakeawayRowMeta(FormatTakeawayAge(order), 78, "SdTakeawayRowAge");
            Grid.SetColumn(age, 4);
            grid.Children.Add(age);

            var copy = BuildTakeawayCopyChip(order);
            if (copy != null)
            {
                Grid.SetColumn(copy, 5);
                grid.Children.Add(copy);
            }

            row.Child = grid;
            return row;
        }

        /// <summary>One mono cell on a tray row.</summary>
        private TextBlock MakeTakeawayRowMeta(string text, double minWidth, string styleKey)
        {
            var block = new TextBlock { Text = text, MinWidth = minWidth };
            var style = TryFindTabStyle(styleKey);
            if (style != null) block.Style = style;
            else
            {
                block.Foreground = Application.Current.Resources["TextSecondaryBrush"] as Brush;
                block.FontFamily = new FontFamily("Consolas");
                block.FontSize = 10.5;
                block.VerticalAlignment = VerticalAlignment.Center;
                block.TextAlignment = TextAlignment.Right;
                block.Margin = new Thickness(10, 0, 0, 0);
            }
            return block;
        }

        /// <summary>
        /// The copy-link chip on one receipt. Copies the drop's PUBLIC taste link - the one page
        /// that plays an order for someone with no account - and nothing else: no dialog, no
        /// window, no upload. The link is never printed on screen, only put on the clipboard,
        /// because a shelf that renders share URLs is a shelf that leaks them into screenshots.
        /// </summary>
        private Border? BuildTakeawayCopyChip(JustDropOrdersService.Order order)
        {
            var chip = new Border { Tag = order };
            var style = TryFindTabStyle("SdTakeawayCopy");
            if (style != null) chip.Style = style;
            else
            {
                chip.Width = 20; chip.Height = 18;
                chip.CornerRadius = new CornerRadius(6);
                chip.Background = Brushes.Transparent;
                chip.BorderThickness = new Thickness(1);
                chip.BorderBrush = Brushes.Transparent;
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Margin = new Thickness(8, 0, 0, 0);
                chip.Cursor = System.Windows.Input.Cursors.Hand;
                chip.Opacity = 0.55;
            }

            chip.Child = new Helpers.EmojiTextBlock
            {
                Text = "🔗",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            chip.ToolTip = Loc.Get("tooltip_takeaway_copy_link");
            chip.MouseLeftButtonUp += TakeawayCopyLink_Click;
            return chip;
        }

        /// <summary>The trailing dashed chip: a door to the shop, nothing more. The subtitle the
        /// 158x74 card carried ("ON THE WEB →") is gone with the card - a 32px chip has one line,
        /// and the tooltip says the same thing.</summary>
        private Border? BuildTakeawayDoorChip()
        {
            var chip = new Border();
            var style = TryFindTabStyle("SdTakeawayChipDoor");
            if (style != null) chip.Style = style;
            else
            {
                chip.Height = 32;
                chip.CornerRadius = new CornerRadius(16);
                chip.Padding = new Thickness(11, 0, 11, 0);
                chip.Margin = new Thickness(0, 0, 7, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Background = Brushes.Transparent;
                chip.BorderBrush = Application.Current.Resources["TransparentPink40Brush"] as Brush;
                chip.BorderThickness = new Thickness(1);
                chip.Cursor = System.Windows.Input.Cursors.Hand;
            }

            chip.MouseLeftButtonUp += TakeawayDoor_Click;
            chip.ToolTip = Loc.Get("tooltip_takeaway_order_drop");
            PreparePresetCardFx(chip);

            var pink = Application.Current.Resources["PinkBrush"] as Brush
                       ?? new SolidColorBrush(Color.FromRgb(255, 105, 180));

            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            line.Children.Add(new TextBlock
            {
                Text = "+",
                Foreground = pink,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            line.Children.Add(new TextBlock
            {
                Text = Loc.Get("sd_takeaway_order"),
                Foreground = pink,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            });

            chip.Child = line;
            return chip;
        }

        /// <summary>
        /// "18 MIN · AUG 12". Consolas and upper case because the line is structural, not prose - it
        /// is scanned down a strip rather than read. A size the app does not know yet yields 0
        /// minutes, and the date carries the chip on its own rather than printing "0 MIN".
        /// </summary>
        private static string FormatTakeawayMeta(JustDropOrdersService.Order order)
        {
            try
            {
                string date = FormatTakeawayDate(order);
                int minutes = order.Minutes;
                return minutes > 0 ? Loc.GetF("sd_takeaway_meta", minutes, date) : date;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FormatTakeawayMeta failed: {E}", ex.Message);
                return "";
            }
        }

        /// <summary>"AUG 12". The tray splits what the chip joins, so it needs the halves.</summary>
        private static string FormatTakeawayDate(JustDropOrdersService.Order order)
        {
            try { return order.At.ToString("MMM d").ToUpperInvariant(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("FormatTakeawayDate failed: {E}", ex.Message);
                return "";
            }
        }

        /// <summary>"18 MIN", or empty for a size the app does not know - never "0 MIN".</summary>
        private static string FormatTakeawayMinutes(JustDropOrdersService.Order order)
        {
            try
            {
                int minutes = order.Minutes;
                return minutes > 0 ? string.Format(LocOr("takeaway_row_min", "{0} MIN"), minutes) : "";
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FormatTakeawayMinutes failed: {E}", ex.Message);
                return "";
            }
        }

        /// <summary>
        /// "3 days ago" / "1 day ago" / "today". Singular and plural are separate keys because a
        /// language that inflects cannot be served by pasting an "s" onto one string, and a clock
        /// that has run backwards (a skewed machine, a re-synced NTP) reads as today rather than as
        /// a negative age.
        /// </summary>
        private static string FormatTakeawayAge(JustDropOrdersService.Order order)
        {
            try
            {
                int days = (int)Math.Floor((DateTimeOffset.UtcNow - order.At).TotalDays);
                if (days <= 0) return LocOr("takeaway_today", "today");
                return days == 1
                    ? string.Format(LocOr("takeaway_day_ago", "{0} day ago"), days)
                    : string.Format(LocOr("takeaway_days_ago", "{0} days ago"), days);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FormatTakeawayAge failed: {E}", ex.Message);
                return "";
            }
        }

        private static string SafeOrderName(JustDropOrdersService.Order order) =>
            string.IsNullOrWhiteSpace(order.Name) ? Loc.Get("sd_takeaway_order_fallback") : order.Name;

        /// <summary>Replay the order in the Just Drop window. Free - see the class remarks.</summary>
        private void TakeawayCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement fe || fe.Tag is not JustDropOrdersService.Order order) return;
                JustDropHostService.LaunchReplay(order.Code);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to replay a Takeaway order");
            }
        }

        /// <summary>
        /// Put this drop's taste link on the clipboard. Marks the click handled so the card
        /// underneath does not also replay the order - copying a link and starting a session are
        /// not the same intention, and one click may only be one of them.
        /// </summary>
        private void TakeawayCopyLink_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            try
            {
                if (sender is not FrameworkElement fe || fe.Tag is not JustDropOrdersService.Order order) return;
                if (string.IsNullOrWhiteSpace(order.Code)) return;

                System.Windows.Clipboard.SetText(JustDropService.TasteUrl(order.Code));
                App.Notifications?.Show(Loc.Get("toast_taste_link_copied"),
                    Services.NotificationType.Success, TimeSpan.FromSeconds(4));
            }
            catch (Exception ex)
            {
                // The clipboard is a shared OS resource another app can be holding open; a failed
                // copy is a "try again", never a crash.
                App.Logger?.Warning(ex, "Failed to copy a Takeaway taste link");
                App.Notifications?.Show(Loc.Get("toast_taste_link_copy_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(4));
            }
        }

        /// <summary>
        /// Open or close the overflow tray. Marks the click handled for the same reason the copy
        /// element does: expanding a list and starting a session are not the same intention, and
        /// one click may only be one of them.
        /// </summary>
        private void TakeawayMore_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            SetTakeawayTrayOpen(!_takeawayTrayOpen);
        }

        private void TakeawayDoor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Re-checked rather than trusted: the card was built when the shelf last painted,
                // and the server can withdraw the door between then and this click.
                if (!JustDropService.DoorAvailable) return;
                JustDropHostService.LaunchShop();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open the Just Drop shop from the Takeaway shelf");
            }
        }
    }
}
