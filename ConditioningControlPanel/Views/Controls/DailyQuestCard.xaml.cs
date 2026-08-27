using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>
    /// Everything one seat of the daily board needs to paint itself. Built by
    /// MainWindow.RefreshQuestUI, which owns the quest/localization/mod lookups; the card owns
    /// only how it looks.
    /// </summary>
    internal sealed class DailyQuestCardModel
    {
        public int Slot { get; init; }                  // zero-based
        public string Icon { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public int Current { get; init; }
        public int Target { get; init; }
        public bool IsCompleted { get; init; }
        public string XpText { get; init; } = "";
        public string? BonusText { get; init; }         // streak / reroll multipliers, or null
        public ImageSource? Art { get; init; }
        public bool CanReroll { get; init; }
        public string RerollText { get; init; } = "";
        public string? RerollTooltip { get; init; }
    }

    /// <summary>
    /// One card of the three-up daily board. See the header comment in DailyQuestCard.xaml for why
    /// the card is vertical, why the art frame is near-square, why the name and the ask are printed
    /// ON the art, and why it paints from code rather than from bindings.
    ///
    /// <para><b>The bar belongs to the card.</b> Before the board existed there was one daily bar
    /// and MainWindow tweened it through SetQuestProgress, because RefreshQuestUI runs before the
    /// tab has ever been measured and a bar can't be filled against a zero-width track. Three bars
    /// would have meant three of those fractions parked on the window. Instead each card remembers
    /// its own fraction and re-applies it on its track's SizeChanged, so the same "painted before
    /// layout" case is handled locally and MainWindow keeps exactly one of these (the weekly).</para>
    ///
    /// <para><b>Motion.</b> Hover lifts the card (MotionFx.HoverLift, which honours the app's
    /// reduced-motion gate) and blooms a gold shadow; the art does its own HoverPop from XAML. The
    /// shadow is attached on enter and detached on leave rather than living at rest - see the
    /// comment on Root in the XAML.</para>
    /// </summary>
    public partial class DailyQuestCard : UserControl
    {
        /// <summary>Raised when this seat's reroll button is pressed. MainWindow spends the
        /// reroll; the card never touches quest state.</summary>
        internal event EventHandler? RerollRequested;

        /// <summary>Zero-based seat index, echoed back on <see cref="RerollRequested"/> by way of
        /// the sender. Set by <see cref="Apply"/>.</summary>
        internal int Slot { get; private set; }

        /// <summary>Exposed so MainWindow.EventFx can burst at the cap of the bar that just
        /// filled - the same anchor the single daily bar used to give it.</summary>
        internal FrameworkElement ProgressTrack => Track;
        internal FrameworkElement ProgressFill => Fill;

        private double _fraction;
        private bool _hasFraction;
        private DropShadowEffect? _hoverGlow;

        private static readonly Brush GoldFill = Freeze(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly Brush DoneFill = Freeze(Color.FromRgb(0x00, 0xE6, 0x76));
        private static readonly Brush RestBorder = Freeze(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly Brush LiveBorder = Freeze(Color.FromArgb(0x99, 0xFF, 0xD7, 0x00));
        private static readonly Brush DoneBorder = Freeze(Color.FromArgb(0x99, 0x00, 0xE6, 0x76));

        private static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public DailyQuestCard()
        {
            InitializeComponent();

            Track.SizeChanged += (_, _) => ApplyFraction(animate: false);
            Root.MouseEnter += OnCardMouseEnter;
            Root.MouseLeave += OnCardMouseLeave;
        }

        // ---- PAINT ---------------------------------------------------------------

        /// <summary>Paint this seat from live quest state.</summary>
        internal void Apply(DailyQuestCardModel m)
        {
            Slot = m.Slot;
            Visibility = Visibility.Visible;
            Opacity = 1.0;

            TxtSlot.Text = (m.Slot + 1).ToString();
            TxtIcon.Text = m.Icon;
            TxtName.Text = m.Name;
            TxtDesc.Text = m.Description;
            TxtDesc.Visibility = string.IsNullOrWhiteSpace(m.Description) ? Visibility.Collapsed : Visibility.Visible;

            TxtXp.Text = m.XpText;
            if (string.IsNullOrWhiteSpace(m.BonusText))
            {
                TxtXpBonus.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtXpBonus.Text = m.BonusText;
                TxtXpBonus.Visibility = Visibility.Visible;
            }

            if (m.Art != null) Art.Source = m.Art;

            // A stamped seat reads 100% whatever its stored counter says: a quest completed by a
            // tracker that overshot its target (minutes arrive in whole-minute lumps) would
            // otherwise paint a bar that is not quite full under a green check.
            double fraction = m.IsCompleted ? 1.0
                            : m.Target > 0 ? Math.Max(0, Math.Min(1.0, (double)m.Current / m.Target))
                            : 0;

            int current = m.IsCompleted ? Math.Max(m.Current, m.Target) : m.Current;
            TxtProgress.Text = $"{current} / {m.Target}";

            if (m.IsCompleted)
            {
                CompletedOverlay.Visibility = Visibility.Visible;
                Root.BorderBrush = DoneBorder;
                Fill.Background = DoneFill;
                TxtProgress.Foreground = DoneFill;
                // The separator lives in this string rather than in a third TextBlock, so an empty
                // remainder leaves no orphaned dot on the line.
                TxtRemaining.Text = "\u00b7 done";
            }
            else
            {
                CompletedOverlay.Visibility = Visibility.Collapsed;
                Root.BorderBrush = LiveBorder;
                Fill.Background = GoldFill;
                TxtProgress.Foreground = GoldFill;
                int left = Math.Max(0, m.Target - m.Current);
                TxtRemaining.Text = left > 0 ? $"\u00b7 {left} to go" : "";
            }

            BtnReroll.Content = m.RerollText;
            BtnReroll.IsEnabled = m.CanReroll;
            BtnReroll.Visibility = m.IsCompleted ? Visibility.Collapsed : Visibility.Visible;
            BtnReroll.ToolTip = m.RerollTooltip;

            SetFraction(fraction);
        }

        /// <summary>
        /// A seat the pool could not fill. Rare (it needs every legal quest to be excluded or
        /// locked), but it must render as an obviously empty seat rather than as a broken card.
        /// </summary>
        internal void ShowEmpty(int slot, string title, string subtitle)
        {
            Slot = slot;
            Visibility = Visibility.Visible;
            Opacity = 0.55;

            TxtSlot.Text = (slot + 1).ToString();
            TxtIcon.Text = "";
            TxtName.Text = title;
            TxtDesc.Text = subtitle;
            TxtDesc.Visibility = Visibility.Visible;
            TxtXp.Text = "";
            TxtXpBonus.Visibility = Visibility.Collapsed;
            Art.Source = null;
            CompletedOverlay.Visibility = Visibility.Collapsed;
            Root.BorderBrush = RestBorder;
            TxtProgress.Text = "";
            TxtRemaining.Text = "";
            BtnReroll.Visibility = Visibility.Collapsed;

            SetFraction(0);
        }

        // ---- BAR -----------------------------------------------------------------

        private void SetFraction(double fraction)
        {
            if (double.IsNaN(fraction)) fraction = 0;
            _fraction = Math.Max(0, Math.Min(1, fraction));
            _hasFraction = true;
            ApplyFraction(animate: true);
        }

        /// <summary>
        /// Seat the fill against the track's CURRENT width. Called both from Apply (which can run
        /// before the tab has ever been measured, in which case the track is 0 wide and this is a
        /// no-op) and from the track's SizeChanged, which is what finally lands it. A resize is not
        /// progress, so that path never tweens.
        /// </summary>
        private void ApplyFraction(bool animate)
        {
            try
            {
                if (!_hasFraction) return;
                double available = Track.ActualWidth;
                if (available <= 0) return;

                double to = available * _fraction;
                double from = double.IsNaN(Fill.Width) ? 0 : Fill.Width;

                if (!animate)
                {
                    Fill.BeginAnimation(FrameworkElement.WidthProperty, null);
                    Fill.Width = to;
                    return;
                }

                if (Math.Abs(to - from) < 0.5) return;
                MotionFx.BarFill(Fill, from, to);
            }
            catch (Exception ex) { App.Logger?.Debug("DailyQuestCard bar: {E}", ex.Message); }
        }

        // ---- INTERACTION ---------------------------------------------------------

        private void BtnReroll_Click(object sender, RoutedEventArgs e)
        {
            RerollRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnCardMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                MotionFx.HoverLift(Root, true);

                if (!MotionFx.AllowTransitions) return;
                _hoverGlow ??= new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0xD7, 0x00),
                    ShadowDepth = 0,
                    BlurRadius = 0,
                    Opacity = 0,
                };
                Root.Effect = _hoverGlow;
                _hoverGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                    new DoubleAnimation(18, TimeSpan.FromMilliseconds(180)));
                _hoverGlow.BeginAnimation(DropShadowEffect.OpacityProperty,
                    new DoubleAnimation(0.5, TimeSpan.FromMilliseconds(180)));
            }
            catch (Exception ex) { App.Logger?.Debug("DailyQuestCard hover in: {E}", ex.Message); }
        }

        private void OnCardMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                MotionFx.HoverLift(Root, false);

                if (_hoverGlow == null) { Root.Effect = null; return; }

                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
                // Detach the effect only once it has actually faded, or the glow would vanish in
                // one frame instead of settling.
                fade.Completed += (_, _) =>
                {
                    try { if (!Root.IsMouseOver) Root.Effect = null; } catch { }
                };
                _hoverGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
                _hoverGlow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
            }
            catch (Exception ex) { App.Logger?.Debug("DailyQuestCard hover out: {E}", ex.Message); }
        }
    }
}
