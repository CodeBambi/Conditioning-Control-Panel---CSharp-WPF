// PORTED from the two banner Hyperlinks in ConditioningControlPanel/MainWindow/MainWindow.xaml
// (TxtBannerPrimary -> https://linktr.ee/CodeBambi via HandleHyperlinkClick, TxtBannerWeb ->
// https://app.cclabs.app via BannerWebLink_Click in MainWindow.Marquee.cs).
//
// Avalonia has no inline Hyperlink, so the link stays the LAST Run of each TextBlock and this
// partial hit-tests the pointer against the TextBlock's own TextLayout: a press that lands on the
// last run's characters opens the link (ExternalOpener, so a sandbox never opens a real site), and
// the cursor turns into a hand while it hovers there. Only the beat on screen is hit-testable
// (Crossfade flips IsHitTestVisible, as WPF did), so a hidden beat never takes the click.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal const string BannerSupportUrl = "https://linktr.ee/CodeBambi";
        internal const string BannerWebAppUrl = "https://app.cclabs.app";

        private void InitializeBannerLinks()
        {
            WireBannerLink(BannerPrimary, BannerSupportUrl, retireWebBeat: false);
            WireBannerLink(BannerWeb, BannerWebAppUrl, retireWebBeat: true);
        }

        private void WireBannerLink(TextBlock? tb, string url, bool retireWebBeat)
        {
            if (tb == null) return;
            tb.PointerMoved += (_, e) =>
                tb.Cursor = IsOnBannerLink(tb, e.GetPosition(tb)) ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            tb.PointerExited += (_, _) => tb.Cursor = Cursor.Default;
            tb.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(tb).Properties.IsLeftButtonPressed) return;
                if (!IsOnBannerLink(tb, e.GetPosition(tb))) return;
                e.Handled = true;
                OpenBannerLink(url, retireWebBeat);
            };
        }

        /// <summary>WPF HandleHyperlinkClick / BannerWebLink_Click: open, and the web beat retires itself.</summary>
        internal void OpenBannerLink(string url, bool retireWebBeat)
        {
            try
            {
                if (!ExternalOpener.Open(url))
                    Log.Warning("Banner link did not open ({Url})", url);
            }
            catch (Exception ex) { Log.Error("Failed to open banner hyperlink - {Error}", ex.Message); }
            if (retireWebBeat) RetireWebBannerBeat();
        }

        /// <summary>True when <paramref name="p"/> (TextBlock coordinates) sits on the characters of the
        /// TextBlock's last Run, which is where both banners keep their link.</summary>
        internal static bool IsOnBannerLink(TextBlock tb, Point p)
        {
            try
            {
                var inlines = tb.Inlines;
                if (inlines == null || inlines.Count == 0) return false;
                if (inlines[inlines.Count - 1] is not Run link || string.IsNullOrEmpty(link.Text)) return false;
                var total = inlines.OfType<Run>().Sum(r => r.Text?.Length ?? 0);
                var linkStart = total - link.Text.Length;
                var local = new Point(p.X - tb.Padding.Left, p.Y - tb.Padding.Top);
                // The link run's own rectangles (alignment and trimming included), so a press beside
                // the text or on the lead-in copy never counts.
                foreach (var r in tb.TextLayout.HitTestTextRange(linkStart, link.Text.Length))
                    if (r.Contains(local)) return true;
                return false;
            }
            catch { return false; }
        }
    }
}
