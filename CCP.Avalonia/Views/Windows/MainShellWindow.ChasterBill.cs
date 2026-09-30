// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ChasterBill.cs: Circe's bill at close,
// the run's receipt held over the window for ExitBillSeconds (or one click) on the real exit path.
// An overlay inside RootGrid, never a window (a window at shutdown would hold the process).
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        public const int ExitBillSeconds = 4;

        private bool _exitBillShown;

        /// <summary>One second of the live bill's countdown; the tests drive it instead of waiting.</summary>
        internal Action? ExitBillTick { get; private set; }

        /// <summary>The exits WPF takes straight to Shutdown (double panic, the declined 18+ gate, an
        /// ungated first run): no bill, whatever is on the tab.</summary>
        internal void ExitWithoutBill()
        {
            _exitBillShown = true;
            RequestExit();
        }

        /// <summary>WPF TryShowExitBill: show the bill and hold the exit, or return false when there
        /// is no bill to show. <paramref name="continueExit"/> runs once, when it is dismissed or times out.</summary>
        internal bool TryShowExitBill(Action continueExit)
        {
            if (_exitBillShown) return false;
            try
            {
                var chaster = ChasterHead.Service;
                if (chaster == null || !chaster.IsLinked || CoreSettings.Current?.ChasterTabEnabled != true) return false;
                if (!IsVisible || WindowState == WindowState.Minimized) return false;
                var root = Named<Grid>("RootGrid");
                var bill = chaster.Bill();
                if (root == null || bill.IsEmpty) return false;
                _exitBillShown = true;

                var receipt = new ChasterReceiptView { Width = 320 };
                receipt.Show(bill);
                var countdown = new TextBlock
                {
                    FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
                };
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(receipt);
                stack.Children.Add(countdown);
                var overlay = new Grid
                {
                    Name = "ExitBillOverlay",
                    Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x1C)),
                    Cursor = new Cursor(StandardCursorType.Hand), ZIndex = 9999,
                };
                overlay.Children.Add(stack);
                Grid.SetRowSpan(overlay, 99);
                Grid.SetColumnSpan(overlay, 99);

                var left = ExitBillSeconds;
                countdown.Text = Loc.GetF("chaster_bill_closing", left);
                var finished = false;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                void Finish()
                {
                    if (finished) return;
                    finished = true;
                    timer.Stop();
                    try { root.Children.Remove(overlay); } catch (Exception ex) { Serilog.Log.Debug("[Chaster] bill remove: {E}", ex.Message); }
                    continueExit();   // always: the exit must never be lost behind the bill
                }
                overlay.PointerReleased += (_, _) => Finish();
                void Tick()
                {
                    left--;
                    if (left <= 0) Finish();
                    else countdown.Text = Loc.GetF("chaster_bill_closing", left);
                }
                timer.Tick += (_, _) => Tick();
                ExitBillTick = Tick;

                if (AmbientFxCanvas.Env.AllowTransitions)
                {
                    overlay.Opacity = 0;
                    overlay.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) } };
                    Dispatcher.UIThread.Post(() => overlay.Opacity = 1, DispatcherPriority.Render);
                }
                root.Children.Add(overlay);
                timer.Start();
                return true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("[Chaster] bill at close: {E}", ex.Message);
                return false;
            }
        }
    }
}
