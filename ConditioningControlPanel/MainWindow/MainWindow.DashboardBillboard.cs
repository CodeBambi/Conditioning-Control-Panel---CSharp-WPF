using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The billboard that takes the row the folded browser card gives back (owner ask,
    /// 2026-09-12). One card at a time, dots for the roster, the next card on a 12s clock.
    ///
    /// <para>The roster and the walk are pure and live in
    /// <see cref="Services.DashboardBillboard"/>; this file is the paint, the clock and the click.
    /// Manners are the remix doors': nothing opens by itself, the pointer on the card stops the
    /// clock, a dot jumps, and the dashboard works exactly the same with the strip ignored.</para>
    ///
    /// <para>The clock is an ambient loop by MotionFx's own definition (8-60s), so it only runs at
    /// <see cref="MotionFx.AllowAmbientLoops"/>. Below that the strip is a still card the dots
    /// still walk, which is the house fallback: static art, never a slower loop.</para>
    /// </summary>
    public partial class MainWindow
    {
        private const int BillboardFadeMs = 200;

        private DispatcherTimer? _billboardTimer;
        private int _billboardIndex;
        private bool _billboardPointerOver;
        private bool _billboardWired;

        /// <summary>The fold's one hook: the strip exists only while the browser is shut.</summary>
        partial void OnBrowserFoldChanged(bool collapsed)
        {
            try { ApplyBillboard(collapsed); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: fold hook failed"); }
        }

        private void ApplyBillboard(bool show)
        {
            var host = SettingsTab?.DashBillboard;
            if (host == null) return;

            host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
            {
                StopBillboardClock();
                return;
            }

            EnsureBillboardWired();
            PaintBillboardDots();
            PaintBillboardCard(fade: false);
            RestartBillboardClock();
        }

        /// <summary>
        /// One-time wiring. The pointer pauses the clock, and the host's own visibility starts and
        /// stops it, so switching away from Home costs nothing and coming back does not need the
        /// tab machinery to know the strip exists.
        /// </summary>
        private void EnsureBillboardWired()
        {
            if (_billboardWired) return;
            var dash = SettingsTab;
            var host = dash?.DashBillboard;
            var card = dash?.BillboardCard;
            if (host == null || card == null) return;

            card.MouseEnter += (_, _) => { _billboardPointerOver = true; StopBillboardClock(); };
            card.MouseLeave += (_, _) => { _billboardPointerOver = false; RestartBillboardClock(); };
            host.IsVisibleChanged += (_, _) =>
            {
                if (host.IsVisible) RestartBillboardClock();
                else StopBillboardClock();
            };
            _billboardWired = true;
        }

        private void RestartBillboardClock()
        {
            StopBillboardClock();

            var host = SettingsTab?.DashBillboard;
            if (host == null || !host.IsVisible) return;
            if (!MotionFx.AllowAmbientLoops) return;
            if (!Services.DashboardBillboard.ShouldAdvance(_billboardPointerOver, onScreen: true)) return;

            _billboardTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(Services.DashboardBillboard.RotateSeconds),
            };
            _billboardTimer.Tick += (_, _) =>
            {
                var strip = SettingsTab?.DashBillboard;
                if (!Services.DashboardBillboard.ShouldAdvance(_billboardPointerOver, strip?.IsVisible == true)) return;
                _billboardIndex = Services.DashboardBillboard.NextIndex(_billboardIndex, Services.DashboardBillboard.Roster.Count);
                PaintBillboardCard(fade: MotionFx.AllowTransitions);
                PaintBillboardDots();
            };
            _billboardTimer.Start();
        }

        private void StopBillboardClock()
        {
            try { _billboardTimer?.Stop(); } catch { }
            _billboardTimer = null;
        }

        private void PaintBillboardCard(bool fade)
        {
            var dash = SettingsTab;
            if (dash?.BillboardCard == null) return;

            var card = Services.DashboardBillboard.CardAt(_billboardIndex);

            if (dash.BillboardEyebrow != null) dash.BillboardEyebrow.Text = Loc.Get(card.EyebrowKey);
            if (dash.BillboardTitle != null) dash.BillboardTitle.Text = Loc.Get(card.TitleKey);
            if (dash.BillboardLine != null) dash.BillboardLine.Text = Loc.Get(card.LineKey);
            dash.BillboardCard.ToolTip = Loc.Get(card.TitleKey) + "  ·  " + Loc.Get(card.LineKey);

            if (dash.BillboardPoster != null)
            {
                try
                {
                    // A missing plate must leave the words readable over the shade, not throw
                    // through InitializeComponent's caller.
                    var art = new BitmapImage();
                    art.BeginInit();
                    art.UriSource = new Uri(Services.DashboardBillboard.PosterUri(card), UriKind.Absolute);
                    art.CacheOption = BitmapCacheOption.OnLoad;
                    art.EndInit();
                    art.Freeze();
                    dash.BillboardPoster.Source = art;
                }
                catch (Exception ex)
                {
                    dash.BillboardPoster.Source = null;
                    App.Logger?.Debug("Billboard poster {Poster} did not load: {E}", card.Poster, ex.Message);
                }
            }

            var face = dash.BillboardFace;
            if (face == null) return;
            face.BeginAnimation(UIElement.OpacityProperty, null);
            if (!fade) { face.Opacity = 1; return; }
            face.Opacity = 0;
            face.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(BillboardFadeMs))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }

        /// <summary>One dot per card, the one on screen filled. A dot click jumps to its card.</summary>
        private void PaintBillboardDots()
        {
            var dots = SettingsTab?.BillboardDots;
            if (dots == null) return;

            var roster = Services.DashboardBillboard.Roster;
            if (dots.Children.Count != roster.Count)
            {
                dots.Children.Clear();
                for (int i = 0; i < roster.Count; i++)
                {
                    int slot = i;
                    var dot = new Ellipse
                    {
                        Width = 5,
                        Height = 5,
                        Margin = new Thickness(3, 0, 3, 0),
                        Cursor = Cursors.Hand,
                        ToolTip = Loc.Get(roster[slot].TitleKey),
                    };
                    dot.MouseLeftButtonDown += (_, _) => JumpBillboardTo(slot);
                    dots.Children.Add(dot);
                }
            }

            var on = TryFindResource("PinkBrush") as Brush;
            var off = TryFindResource("GlassBorderBrush") as Brush ?? Brushes.Gray;
            for (int i = 0; i < dots.Children.Count; i++)
            {
                if (dots.Children[i] is Ellipse dot)
                    dot.Fill = i == _billboardIndex ? (on ?? Brushes.HotPink) : off;
            }
        }

        private void JumpBillboardTo(int slot)
        {
            try
            {
                int next = Services.DashboardBillboard.JumpTo(slot, Services.DashboardBillboard.Roster.Count);
                if (next == _billboardIndex) return;
                _billboardIndex = next;
                PaintBillboardCard(fade: MotionFx.AllowTransitions);
                PaintBillboardDots();
                RestartBillboardClock();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: dot jump failed"); }
        }

        /// <summary>
        /// The only thing on the strip that ever leaves the page. A Link goes out through
        /// BrowserLauncher - the four-strategy opener with the clipboard fallback, the same one the
        /// Web App door uses - and a Tab is a plain in-app navigation.
        /// </summary>
        internal void BillboardCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var card = Services.DashboardBillboard.CardAt(_billboardIndex);
                if (card.Kind == BillboardTargetKind.Link)
                    Helpers.BrowserLauncher.OpenUrlOrPrompt(card.Target, Loc.Get(card.TitleKey));
                else
                    ShowTab(card.Target);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: card click failed"); }
        }
    }
}
