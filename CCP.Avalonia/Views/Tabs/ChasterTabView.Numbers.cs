// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: RefreshNumbers (:205),
// RefreshDay (:771), LayoutCap (:791), RefreshRun (:809), StatRun_Click + BuildBill (:816-826).
// The tab's own receipt: the third number opens and closes the bill on paper under the numbers.
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

        private void NumbersInit()
        {
            StatRun.PointerReleased += (_, _) => ToggleBill();
            CapTrack.SizeChanged += (_, _) => LayoutCap();
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
            LayoutCap();

            var net = chaster.Bill().NetSeconds;
            TxtRun.Text = net == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(net);
            TxtRun.Foreground = FigureBrush(net);
            if (_billOpen) Receipt.Show(chaster.Bill());
        }

        private void LayoutCap() => CapFill.Width = System.Math.Max(0, CapTrack.Bounds.Width * _capFraction);

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
