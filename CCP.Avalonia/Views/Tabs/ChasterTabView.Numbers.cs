// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: RefreshNumbers (:205),
// RefreshDay (:771), LayoutCap (:791), RefreshRun (:809), StatRun_Click + BuildBill (:816-826).
// The tab's own receipt: the third number opens and closes the bill on paper under the numbers.
// The cap meter fills to its new width in 0.6 s, quad out (WPF MotionFx.BarFill), at once under motion Off.
// The bloom at the meter's tip lights as the fill arrives (0 until 80 % of the 0.6 s, full at the end, gone
// 0.45 s later). The third number pops, the tag takes a tug and the bill prints down (FxBill, Fx.cs).
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private bool _billOpen;
        private double _capFraction;
        private global::Avalonia.Threading.DispatcherTimer? _capRun;
        private Helpers.FxTrack.Run? _bloomRun;
        internal const double CapFillSeconds = 0.6, CapBloomTailSeconds = 0.45;

        private void NumbersInit()
        {
            StatRun.PointerReleased += (_, _) => ToggleBill();
            CapTrack.SizeChanged += (_, _) => LayoutCap(animate: false);
        }

        internal void RefreshNumbers()
        {
            var chaster = ChasterHead.Service;
            if (chaster == null) return;
            var balance = chaster.BalanceSeconds;
            TxtBalance.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtBalance.Foreground = FigureBrush(balance);
            TxtBalanceCaption.Text = Loc.Get(balance < 0 ? "chaster_credit_caption" : "chaster_balance_caption");
            RefreshTag(balance);

            var today = chaster.TodayAddedSeconds;
            var capSeconds = chaster.Caps.DailySeconds;
            var cap = CircesTab.Format(capSeconds, signed: false);
            TxtToday.Text = CircesTab.Format(today, signed: false);
            TxtTodayCap.Text = "/ " + cap;
            TxtTodaySub.Text = Loc.GetF("chaster_stat_today_sub", cap);
            _capFraction = TabPageText.CapFraction(today, capSeconds);
            LayoutCap(animate: true);

            var net = chaster.Bill().NetSeconds;
            TxtRun.Text = net == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(net);
            TxtRun.Foreground = FigureBrush(net);
            if (_billOpen) Receipt.Show(chaster.Bill());
        }

        /// <summary>The meter is a fill the code widens against the track's measured width; the
        /// track's SizeChanged keeps it right when the card resizes.</summary>
        private void LayoutCap(bool animate)
        {
            var width = System.Math.Max(0, CapTrack.Bounds.Width * _capFraction);
            var from = double.IsNaN(CapFill.Width) ? 0 : CapFill.Width;
            _capRun?.Stop();
            _capRun = null;
            _bloomRun?.Finish();
            _bloomRun = null;
            CapBloom.Width = width;
            if (animate && System.Math.Abs(width - from) > 0.5 && IsVisible
                && global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level != Models.MotionLevel.Off)
            {
                _capRun = Helpers.TransformTween.Run(CapFill, System.TimeSpan.FromSeconds(CapFillSeconds),
                    new (double, global::Avalonia.AvaloniaProperty, double)[] { (0, WidthProperty, from), (1, WidthProperty, width) },
                    new global::Avalonia.Animation.Easings.QuadraticEaseOut());
                // WPF MotionFx.BarFill's bloom: linear keys at 0, 80 % of the fill, the fill's end, and 0.45 s after.
                double fill = CapFillSeconds * 1000, total = fill + (CapBloomTailSeconds * 1000);
                _bloomRun = Helpers.FxTrack.Play(total, ms => CapBloom.Opacity = Helpers.FxTrack.Clamp01(
                        Helpers.FxTrack.Keys(ms, (0, 0, null), (fill * 0.8, 0, null), (fill, 1, null), (total, 0, null))),
                    () => CapBloom.Opacity = 0, _fxRuns);
            }
            else { CapFill.Width = width; CapBloom.Opacity = 0; }
        }

        internal void ToggleBill()
        {
            _billOpen = !_billOpen;
            if (_billOpen) Receipt.Show(ChasterHead.Service?.Bill());
            // WPF StatRun_Click: the number pops, the tag takes a tug, the bill prints down (or rolls back up)
            // and the arrow turns with it.
            FxPop(StatRun, 1.04);
            FxTagTug();
            FxBill(_billOpen);
        }

        private IBrush FigureBrush(int seconds) =>
            seconds > 0 ? new SolidColorBrush(CostColour) : seconds < 0 ? new SolidColorBrush(EarnColour) : Brush("TextLightBrush");
    }
}
