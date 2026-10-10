// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: RefreshNumbers (:205),
// RefreshDay (:771), LayoutCap (:791), RefreshRun (:809), StatRun_Click + BuildBill (:816-826).
// The tab's own receipt: the third number opens and closes the bill on paper under the numbers.
// The cap meter fills to its new width in 0.6 s, quad out (WPF MotionFx.BarFill), at once under motion Off.
// ponytail: FxBill's print-down (height grow/fade/settle, Fx.cs:762) and the cap bloom are not ported;
// the receipt opens and closes at once, as WPF does under reduced motion.
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
            if (animate && System.Math.Abs(width - from) > 0.5 && IsVisible
                && global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level != Models.MotionLevel.Off)
                _capRun = Helpers.TransformTween.Run(CapFill, System.TimeSpan.FromSeconds(0.6),
                    new (double, global::Avalonia.AvaloniaProperty, double)[] { (0, WidthProperty, from), (1, WidthProperty, width) },
                    new global::Avalonia.Animation.Easings.QuadraticEaseOut());
            else CapFill.Width = width;
        }

        internal void ToggleBill()
        {
            _billOpen = !_billOpen;
            if (_billOpen) Receipt.Show(ChasterHead.Service?.Bill());
            ReceiptHost.IsVisible = _billOpen;
            TxtRunChevron.Text = _billOpen ? "\u25B4" : "\u25BE";
        }

        private IBrush FigureBrush(int seconds) =>
            seconds > 0 ? new SolidColorBrush(CostColour) : seconds < 0 ? new SolidColorBrush(EarnColour) : Brush("TextLightBrush");
    }
}
