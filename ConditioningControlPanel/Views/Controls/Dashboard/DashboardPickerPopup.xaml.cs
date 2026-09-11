using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Controls.Dashboard
{
    /// <summary>
    /// The flat picker: 27 features in four ring groups, over the wall, for one slot at a time.
    ///
    /// <para>It knows nothing about layouts. A click raises <see cref="Picked"/> with a key and
    /// stops; MainWindow decides whether that is a place, a move, a replacement or a split, and
    /// hands back an <see cref="Ask"/> when it needs the user to choose. That split is what lets
    /// Phase F drop a three.js ring in front of the same decisions without reimplementing them.</para>
    ///
    /// <para>Locked features are placeable, which is the owner's call: every slot is a tile the
    /// user chose, and one they cannot open yet is an advertisement they chose to look at. The
    /// tile shows the bar and the wall shows it again in livery; the click lands on the
    /// destination's own gate, as every card in this app does.</para>
    /// </summary>
    public partial class DashboardPickerPopup : UserControl
    {
        /// <summary>Decode width for the shelf art. A tile is ~104 wide on the design canvas and
        /// the Viewbox can scale it up on a large monitor, so 320 leaves headroom without paying
        /// the wall's 768.</summary>
        private const int ShelfDecodeWidth = 320;

        private const double TileWidth = 104;

        /// <summary>16:9, which is the shape every piece of feature art is drawn at.</summary>
        private const double TileHeight = TileWidth * 9 / 16;

        private Action<bool>? _askAnswer;

        /// <summary>A feature was chosen. The key only: what it means for the slot is not the
        /// picker's business.</summary>
        internal event Action<string>? Picked;

        /// <summary>"Reset to default", already confirmed.</summary>
        internal event Action? ResetRequested;

        /// <summary>Done, Escape, or a click that finished the job.</summary>
        internal event Action? CloseRequested;

        /// <summary>The slot this picker was opened for. Read by the host when a pick comes back.</summary>
        internal int Slot { get; private set; }

        public DashboardPickerPopup()
        {
            InitializeComponent();
            ApplyCopy();
        }

        /// <summary>
        /// Point the picker at a slot and build the shelf. The shelf is rebuilt on every open
        /// rather than cached: entitlement, mod art and mod names can all have moved since the
        /// last one, and 27 decoded-to-320 tiles is a cheaper thing to rebuild than to invalidate
        /// correctly.
        /// </summary>
        internal void ShowFor(int slot)
        {
            Slot = slot;
            HideAsk();
            BuildShelf();
            Focus();
        }

        // ---- copy ----------------------------------------------------------------------

        private void ApplyCopy()
        {
            TxtHeader.Text = Loc.Get("dash_picker_title");
            BtnReset.Content = Loc.Get("dash_picker_reset");
            BtnDone.Content = Loc.Get("dash_picker_done");
            TxtAsk.Text = Loc.Get("dash_picker_ask");
            BtnAskReplace.Content = Loc.Get("dash_picker_replace");
            BtnAskSplit.Content = Loc.Get("dash_picker_split");
            BtnAskCancel.Content = Loc.Get("dash_picker_cancel");
        }

        // ---- the shelf -----------------------------------------------------------------

        private void BuildShelf()
        {
            Shelf.Children.Clear();

            for (int ring = 1; ring <= 4; ring++)
            {
                var rows = FeatureCatalog.All.Where(f => f.Ring == ring).ToList();
                if (rows.Count == 0) continue;

                Shelf.Children.Add(new TextBlock
                {
                    Text = Loc.Get("dash_ring_" + ring),
                    Foreground = TryFindResource("SecondaryBrush") as Brush ?? Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(2, ring == 1 ? 0 : 8, 0, 4),
                });

                var wrap = new WrapPanel();
                foreach (var f in rows) wrap.Children.Add(BuildTile(f));
                Shelf.Children.Add(wrap);
            }
        }

        /// <summary>
        /// One 16:9 art tile. Same resolver, same mod-override contract and same tier wording as
        /// the wall, so what the shelf promises is what the tile becomes.
        /// </summary>
        private UIElement BuildTile(DashboardFeature f)
        {
            var entitled = MainWindow.IsDashboardFeatureEntitled(f);
            var title = MainWindow.DashboardTitle(f);

            var face = new Grid();

            // Art. Deliberately not frozen: the brush is rebuilt on every open and a frozen one
            // would outlive the mod switch that invalidated its bitmap.
            var art = ModResourceResolver.ResolveImageDecoded(f.ArtPath, ShelfDecodeWidth)
                      ?? ModResourceResolver.ResolveImage(f.ArtPath);
            var tile = new Border
            {
                Width = TileWidth,
                Height = TileHeight,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(entitled ? 1 : 2),
                BorderBrush = entitled
                    ? TryFindResource("GlassBorderBrush") as Brush ?? Brushes.Gray
                    : TierLivery.BorderBrush(f.Tier),
                Background = art != null
                    ? new ImageBrush(art) { Stretch = Stretch.UniformToFill }
                    : TryFindResource("ElevatedSurfaceBrush") as Brush ?? Brushes.Black,
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                ToolTip = Loc.Get(f.BlurbLocKey),
                Child = face,
            };

            // Title over a bottom gradient, the wall's own treatment at shelf size.
            face.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(0xA8, 0, 0, 0), 90),
                Child = new TextBlock
                {
                    Text = title,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 8, 4, 3),
                },
            });

            if (!entitled)
            {
                // The badge says the price; the band says the bar. Same pair, same wording, same
                // colours as the wall and the rail.
                face.Children.Add(new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4, 4, 0, 0),
                    Padding = new Thickness(4, 1, 4, 2),
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x2E)),
                    BorderBrush = TierLivery.BorderBrush(f.Tier),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = Loc.Get(f.Tier >= 2 ? "hm3_rail_lock_t2" : "hm3_rail_lock_t1"),
                        Foreground = TierLivery.BorderBrush(f.Tier),
                        FontSize = 7,
                        FontWeight = FontWeights.Bold,
                    },
                });

                face.Children.Add(new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Height = 3,
                    Background = new SolidColorBrush(f.Tier >= 2
                        ? Color.FromRgb(0xB4, 0x7B, 0xFF)
                        : Color.FromRgb(0xF0, 0xC2, 0x4B)),
                });
            }

            tile.MouseLeftButtonUp += (_, _) => Picked?.Invoke(f.Key);
            return tile;
        }

        // ---- the ask -------------------------------------------------------------------

        /// <summary>
        /// Raises the three-way choice. <paramref name="answer"/> is called with true for Split
        /// and false for Replace; Cancel calls nothing and puts the strip away, because "never
        /// mind" is not an edit.
        /// </summary>
        internal void Ask(bool offerSplit, Action<bool> answer)
        {
            _askAnswer = answer;
            BtnAskSplit.Visibility = offerSplit ? Visibility.Visible : Visibility.Collapsed;
            AskStrip.Visibility = Visibility.Visible;
        }

        private void HideAsk()
        {
            _askAnswer = null;
            AskStrip.Visibility = Visibility.Collapsed;
        }

        private void Answer(bool split)
        {
            var answer = _askAnswer;
            HideAsk();
            answer?.Invoke(split);
        }

        private void OnAskReplaceClick(object sender, RoutedEventArgs e) => Answer(false);

        private void OnAskSplitClick(object sender, RoutedEventArgs e) => Answer(true);

        private void OnAskCancelClick(object sender, RoutedEventArgs e) => HideAsk();

        // ---- footer --------------------------------------------------------------------

        private void OnDoneClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            // The one edit that cannot be seen coming, so it is the one that asks first.
            var answer = MessageBox.Show(
                Loc.Get("dash_picker_reset_confirm"),
                Loc.Get("dash_picker_reset_title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            ResetRequested?.Invoke();
        }

        /// <summary>Escape backs out of the ask first, then out of the picker. One key, one step
        /// at a time, so it never throws away a half-made choice and the panel together.</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;

            if (AskStrip.Visibility == Visibility.Visible) { HideAsk(); return; }
            CloseRequested?.Invoke();
        }
    }
}
