using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// PHASE 5 (G3): the custom keyword-trigger + Screen OCR editors, rescued from the
    /// permanently-Collapsed <c>PatreonTabView</c> and mounted on the Awareness tab.
    /// <para>
    /// Pure re-hosting: every handler forwards to the same <see cref="MainWindow"/> methods the
    /// dead tab called, and the services behind them (<c>KeywordTriggerService</c>,
    /// <c>App.ScreenOcr</c>, <c>App.KeywordHighlight</c>) are untouched.
    /// </para>
    /// </summary>
    public partial class KeywordTriggersPanel : UserControl
    {
        public KeywordTriggersPanel()
        {
            InitializeComponent();
        }

        /// <summary>The drawer's top edge is parked this far below the viewport top when revealed.</summary>
        private const double RevealTopPadding = 24;

        /// <summary>
        /// Opens the drawer, scrolls it to the top of the Awareness tab's ScrollViewer, and
        /// pulses it. Used by the Awareness tab's "advanced editor" hyperlink when no preset is
        /// installed - before Phase 5 that branch dead-ended in the App Info popup because this
        /// editor had no home.
        /// <para>
        /// <b>Why the old version read as a dead click.</b> It set <c>IsExpanded</c> and then
        /// deferred <c>BringIntoView()</c> at <see cref="DispatcherPriority.Normal"/> "so the
        /// Expander has finished expanding first" - but Normal (9) is a HIGHER priority than
        /// Render (7) and Loaded (6), so the callback ran BEFORE the layout pass that gives the
        /// expanded drawer any height. The ScrollViewer therefore resolved the request against
        /// the collapsed geometry and a stale extent, decided the (still short) panel was
        /// already visible, and scrolled nothing. The drawer sits directly above the link, so
        /// with no scroll and no highlight there was nothing to notice - and when the drawer was
        /// already expanded, literally nothing happened at all.
        /// </para>
        /// <para>
        /// So: force the layout pass synchronously with <see cref="FrameworkElement.UpdateLayout"/>
        /// (priority-independent, and the memory of <c>Loaded</c> being starved on this app's
        /// dispatcher stops mattering), then drive the ancestor ScrollViewer explicitly instead of
        /// asking <c>BringIntoView</c> - which is a no-op for an element that is already on
        /// screen, and unhelpful for one taller than the viewport. Idempotent: re-clicking on an
        /// already-open drawer still re-scrolls and re-pulses.
        /// </para>
        /// </summary>
        internal void RevealTriggerEditor()
        {
            try
            {
                KeywordTriggersExpander.IsExpanded = true;

                // The Expander's content only becomes Visible through a template trigger, so it has
                // no measured height until layout runs. Everything below depends on that height.
                UpdateLayout();

                ScrollDrawerIntoView();
                PulseDrawer();
            }
            catch (Exception ex)
            {
                // Layout torn down mid-navigation - the drawer is still expanded, which is the
                // part that matters.
                App.Logger?.Debug("RevealTriggerEditor: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Scrolls the Awareness tab's outer ScrollViewer so the drawer's header sits just under
        /// the viewport top. Falls back to <c>BringIntoView</c> when no ScrollViewer ancestor
        /// exists (e.g. re-hosted in a dialog).
        /// </summary>
        private void ScrollDrawerIntoView()
        {
            var sv = FindAncestorScrollViewer(this);
            if (sv == null)
            {
                BringIntoView();
                return;
            }

            // TransformToAncestor gives us the drawer's top relative to the ScrollViewer's
            // viewport, so the absolute target is the current offset plus that delta.
            var topInViewport = TransformToAncestor(sv).Transform(new Point(0, 0)).Y;
            var target = sv.VerticalOffset + topInViewport - RevealTopPadding;
            sv.ScrollToVerticalOffset(Math.Max(0, Math.Min(target, sv.ScrollableHeight)));
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? from)
        {
            var node = from == null ? null : VisualTreeHelper.GetParent(from);
            while (node != null && node is not ScrollViewer)
                node = VisualTreeHelper.GetParent(node);
            return node as ScrollViewer;
        }

        /// <summary>
        /// A brief pink bloom around the drawer so a reveal lands even when the drawer was
        /// already open and the scroll barely moved. One-shot, <c>FillBehavior.Stop</c> plus an
        /// explicit clear so no clock (and no bitmap-effect layer) survives it, and gated on the
        /// house reduced-motion switch exactly like the other code-behind pings
        /// (StudioTabView.PingDot). No XAML storyboard - names don't resolve across the tab
        /// UserControls' namescopes.
        /// </summary>
        private void PulseDrawer()
        {
            if (!MotionFx.AllowTransitions) return;

            var glow = new DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0,
            };
            KeywordTriggersExpander.Effect = glow;

            var bloom = new DoubleAnimation(0.0, 0.9, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop,
            };
            bloom.Completed += (_, __) =>
            {
                try
                {
                    glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    if (ReferenceEquals(KeywordTriggersExpander.Effect, glow))
                        KeywordTriggersExpander.Effect = null;
                }
                catch { /* window closed mid-pulse */ }
            };
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, bloom);
        }

        private void BtnAddKeywordTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAddKeywordTrigger_Click(sender, e);
        }

        private void BtnImportFromCustomTriggers_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnImportFromCustomTriggers_Click(sender, e);
        }

        private void CmbOcrConfirmation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbOcrConfirmation_SelectionChanged(sender, e);
        }

        private void CmbOcrHighlightMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbOcrHighlightMode_SelectionChanged(sender, e);
        }

        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.InnerScrollViewer_PreviewMouseWheel(sender, e);
        }

        private void SliderKeywordBufferTimeout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordBufferTimeout_ValueChanged(sender, e);
        }

        private void SliderKeywordHighlightDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordHighlightDuration_ValueChanged(sender, e);
        }

        private void SliderKeywordSessionMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordSessionMultiplier_ValueChanged(sender, e);
        }

        private void SliderScreenOcrInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderScreenOcrInterval_ValueChanged(sender, e);
        }
    }
}
