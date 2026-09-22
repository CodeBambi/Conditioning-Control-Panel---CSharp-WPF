using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Circe's bill at close: the run's receipt, shown for a few seconds on the way out.
    ///
    /// <para>The owner asked for "a bill when CCP closes". It rides the two real exit paths
    /// (<see cref="RequestExit"/> and the Settings page's Exit button) and never any other: the
    /// title-bar X goes to the tray, and a receipt over a window that is about to hide is a
    /// receipt nobody reads. It is an overlay INSIDE <c>RootGrid</c>, never a new window (an
    /// unowned window at shutdown is the trap OnLastWindowClose documents).</para>
    ///
    /// <para>Shown at most once per run, only with an account linked, the tab on and something on
    /// the bill. It holds the exit for <see cref="ExitBillSeconds"/> or one click, then calls the
    /// exit path again; the guard makes that second call skip straight past it. The engine is
    /// already stopped when it shows, so nothing flashes over the paper.</para>
    /// </summary>
    public partial class MainWindow
    {
        public const int ExitBillSeconds = 4;

        private bool _exitBillShown;

        /// <summary>Show the bill and hold the exit, or return false when there is no bill to show.
        /// <paramref name="continueExit"/> is the exit path that called; it runs once, on the UI
        /// thread, when the bill is dismissed or times out.</summary>
        private bool TryShowExitBill(Action continueExit)
        {
            if (_exitBillShown) return false;
            try
            {
                var chaster = App.Chaster;
                if (chaster == null || !chaster.IsLinked || App.Settings?.Current?.ChasterTabEnabled != true) return false;
                if (!IsVisible || WindowState == WindowState.Minimized) return false;
                var bill = chaster.Bill();
                if (bill.IsEmpty) return false;
                _exitBillShown = true;

                var receipt = new ChasterReceiptView { Width = 320 };
                receipt.Show(bill);

                var countdown = new TextBlock
                {
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(receipt);
                stack.Children.Add(countdown);

                var overlay = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x1C)),
                    Cursor = Cursors.Hand,
                };
                overlay.Children.Add(stack);
                Panel.SetZIndex(overlay, 9999);
                Grid.SetRowSpan(overlay, 99);
                Grid.SetColumnSpan(overlay, 99);
                RootGrid.Children.Add(overlay);

                var left = ExitBillSeconds;
                countdown.Text = Loc.GetF("chaster_bill_closing", left);
                var finished = false;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

                void Finish()
                {
                    if (finished) return;
                    finished = true;
                    timer.Stop();
                    try { RootGrid.Children.Remove(overlay); } catch (Exception ex) { Diag.Swallowed(ex); }
                    continueExit();
                }

                overlay.MouseLeftButtonUp += (_, _) => Finish();
                timer.Tick += (_, _) =>
                {
                    left--;
                    if (left <= 0) Finish();
                    else countdown.Text = Loc.GetF("chaster_bill_closing", left);
                };
                timer.Start();

                if (MotionFx.AllowTransitions)
                {
                    overlay.Opacity = 0;
                    overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Chaster] bill at close: {E}", ex.Message);
                return false;
            }
        }
    }
}
