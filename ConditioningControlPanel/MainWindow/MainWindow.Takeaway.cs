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
    /// The Takeaway shelf on the Session door - the Just Drop receipt drawer, on the desktop.
    ///
    /// <para>Every card is an order the account already owns, newest first, and clicking one
    /// replays it in the Just Drop window. The shelf ends in a dashed door to the shop, so it reads
    /// as an invitation rather than a dead end even before the first order.</para>
    ///
    /// <para><b>Where the data comes from, and why that is not a desktop order book.</b>
    /// <see cref="JustDropOrdersService"/> reads the SERVER's drawer live over the device-token
    /// door and keeps nothing. The desktop still cannot create, price, share or delete an order -
    /// every one of those lives in the shop, and every action on a card here is "open the web player
    /// at this code". That is the same doctrine <see cref="JustDropService"/> has always stated; the
    /// shelf renders the drawer, it does not own it.</para>
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
        /// How many receipts the shelf shows. The server keeps 30; a horizontal rack that runs to 30
        /// is a scrollbar nobody reaches the end of, and its tail is months old. The rest stay in
        /// the shop's own order panel, which is the surface built to page through them.
        /// </summary>
        private const int TakeawayShelfCap = 12;

        /// <summary>Guards against two in-flight loads racing to repaint the same rack - a tab show
        /// and a window close can land within a frame of each other.</summary>
        private bool _takeawayLoading;

        /// <summary>
        /// Rebuild the shelf. Kicks off an async read of the order drawer and repaints when it
        /// lands; the rack keeps showing whatever it last had in the meantime rather than blanking,
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

            int shown = 0;
            foreach (var order in orders)
            {
                if (shown >= TakeawayShelfCap) break;
                var card = BuildTakeawayDropCard(order);
                if (card == null) continue;
                shelf.Children.Add(card);
                shown++;
            }

            // The door comes last, so the shelf reads newest-first and ends on the invitation.
            // Hidden entirely when the feature is withheld or the server has it off - a card that
            // opens a door the user cannot see is a tease for something that does not exist.
            bool doorOpen = JustDropService.DoorAvailable;
            if (doorOpen)
            {
                var door = BuildTakeawayDoorCard();
                if (door != null) shelf.Children.Add(door);
            }

            if (tab.TxtTakeawayCount != null)
                tab.TxtTakeawayCount.Text = orders.Count > 0
                    ? Loc.GetF("sd_takeaway_kept", orders.Count)
                    : "";

            // The dashed door doubles as the empty state, so the sentence only shows when there is
            // neither a receipt nor a door: a fresh install, or someone signed out.
            bool empty = shown == 0 && !doorOpen;
            if (tab.TxtTakeawayEmpty != null)
                tab.TxtTakeawayEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (tab.TakeawayScroller != null)
                tab.TakeawayScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

            // The zone itself goes when there is nothing to say AND no door to offer, so a user who
            // has never touched Just Drop does not carry an empty rack forever.
            if (tab.TakeawayZone != null)
                tab.TakeawayZone.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// One delivered order. Geometry and colour come from SdTakeawayCard in PresetsTabView.xaml
        /// through <see cref="TryFindTabStyle"/>, with the same literal fallback the other
        /// code-built cards carry so a missing style degrades to a plain card, never an invisible one.
        /// </summary>
        private Border? BuildTakeawayDropCard(JustDropOrdersService.Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Code)) return null;

            var card = new Border { Tag = order };
            var style = TryFindTabStyle("SdTakeawayCard");
            if (style != null) card.Style = style;
            else
            {
                card.Width = 158; card.Height = 74;
                card.CornerRadius = new CornerRadius(12);
                card.Padding = new Thickness(11, 9, 11, 9);
                card.Margin = new Thickness(0, 1, 8, 1);
                card.Background = Application.Current.Resources["SurfaceBgBrush"] as Brush;
                card.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as Brush;
                card.BorderThickness = new Thickness(1);
                card.Cursor = System.Windows.Input.Cursors.Hand;
            }

            card.MouseLeftButtonUp += TakeawayCard_Click;
            card.ToolTip = Loc.Get("tooltip_takeaway_replay");
            PreparePresetCardFx(card);   // 158x74 clears the shared 1.02 hover lift

            var stack = new StackPanel();

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Helpers.EmojiTextBlock
            {
                Text = "🎚",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            });

            // The kind stamp follows the mod accent, unlike the RUN/PARTIAL stamps that preceded it:
            // "DROP" names what the card IS, not how it went, so there is no outcome for a colour to
            // encode and no reason for it to sit out a re-tint.
            var kind = new TextBlock
            {
                Text = Loc.Get("sd_takeaway_drop"),
                Foreground = Application.Current.Resources["PinkBrush"] as Brush
                             ?? new SolidColorBrush(Color.FromRgb(255, 105, 180)),
            };
            var kindStyle = TryFindTabStyle("SdTakeawayKind");
            if (kindStyle != null) kind.Style = kindStyle;
            else
            {
                kind.FontFamily = new FontFamily("Consolas");
                kind.FontSize = 9;
                kind.FontWeight = FontWeights.Bold;
                kind.VerticalAlignment = VerticalAlignment.Center;
            }
            head.Children.Add(kind);
            stack.Children.Add(head);

            var title = new Helpers.EmojiTextBlock { Text = SafeOrderName(order) };
            var titleStyle = TryFindTabStyle("SdTakeawayTitle");
            if (titleStyle != null) title.Style = titleStyle;
            else
            {
                title.Foreground = Application.Current.Resources["TextLightBrush"] as Brush
                                   ?? new SolidColorBrush(Colors.White);
                title.FontWeight = FontWeights.SemiBold;
                title.FontSize = 12.5;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                title.Margin = new Thickness(0, 6, 0, 0);
            }
            stack.Children.Add(title);

            var meta = new TextBlock { Text = FormatTakeawayMeta(order) };
            var metaStyle = TryFindTabStyle("SdTakeawayMeta");
            if (metaStyle != null) meta.Style = metaStyle;
            else
            {
                meta.Foreground = Application.Current.Resources["TextDimBrush"] as Brush;
                meta.FontFamily = new FontFamily("Consolas");
                meta.FontSize = 10;
                meta.Margin = new Thickness(0, 3, 0, 0);
                meta.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            stack.Children.Add(meta);

            card.Child = stack;
            return card;
        }

        /// <summary>The trailing dashed card: a door to the shop, nothing more.</summary>
        private Border? BuildTakeawayDoorCard()
        {
            var card = new Border();
            var style = TryFindTabStyle("SdTakeawayDoor");
            if (style != null) card.Style = style;
            else
            {
                card.Width = 158; card.Height = 74;
                card.CornerRadius = new CornerRadius(12);
                card.Padding = new Thickness(11, 9, 11, 9);
                card.Margin = new Thickness(0, 1, 8, 1);
                card.Background = Brushes.Transparent;
                card.BorderBrush = Application.Current.Resources["TransparentPink40Brush"] as Brush;
                card.BorderThickness = new Thickness(1);
                card.Cursor = System.Windows.Input.Cursors.Hand;
            }

            card.MouseLeftButtonUp += TakeawayDoor_Click;
            card.ToolTip = Loc.Get("tooltip_takeaway_order_drop");
            PreparePresetCardFx(card);

            var pink = Application.Current.Resources["PinkBrush"] as Brush
                       ?? new SolidColorBrush(Color.FromRgb(255, 105, 180));

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            stack.Children.Add(new TextBlock
            {
                Text = Loc.Get("sd_takeaway_order"),
                Foreground = pink,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            stack.Children.Add(new TextBlock
            {
                Text = Loc.Get("sd_takeaway_order_sub"),
                Foreground = Application.Current.Resources["TextDimBrush"] as Brush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });

            card.Child = stack;
            return card;
        }

        /// <summary>
        /// "18 MIN · AUG 12". Consolas and upper case because the line is structural, not prose - it
        /// is scanned down a rack rather than read. A size the app does not know yet yields 0
        /// minutes, and the date carries the card on its own rather than printing "0 MIN".
        /// </summary>
        private static string FormatTakeawayMeta(JustDropOrdersService.Order order)
        {
            try
            {
                string date = order.At.ToString("MMM d").ToUpperInvariant();
                int minutes = order.Minutes;
                return minutes > 0 ? Loc.GetF("sd_takeaway_meta", minutes, date) : date;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FormatTakeawayMeta failed: {E}", ex.Message);
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
