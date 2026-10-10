// PORTED from WPF 7.1.5 MainWindow/MainWindow.RemoteControl.cs:680-738 (UpdateDirectoryListingStatus):
// the title bar pill that says whether a running remote session is listed in the directory.
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>What the pill shows. Text and tooltips are WPF's literals (7.1.5 has no keys for them).</summary>
        internal readonly record struct DirectoryPill(bool Visible, string Text, string DotHex, string Tip);

        /// <summary>
        /// WPF's four states: no session = hidden; active and not opted in = "Private only" (grey);
        /// opted in and nobody yet = "Listed" (directory purple); opted in and claimed = "Claimed" (green).
        /// </summary>
        internal static DirectoryPill DirectoryPillFor(bool active, bool optedIn, bool claimed)
        {
            if (!active) return new DirectoryPill(false, "", "#8A8AA0", "");
            if (!optedIn)
                return new DirectoryPill(true, "Private only", "#8A8AA0",
                    "Your session is private - you are not listed in the Available Subjects Directory.");
            return claimed
                ? new DirectoryPill(true, "Claimed", "#00FF88", "A controller has claimed your directory listing.")
                : new DirectoryPill(true, "Listed", "#B47BFF",
                    "You're listed in the Available Subjects Directory and waiting to be claimed.");
        }

        /// <summary>The Remote tab calls this whenever its session, its listing or its controller changes.</summary>
        internal void UpdateDirectoryListingStatus(bool active, bool optedIn, bool claimed)
        {
            if (Named<Border>("DirectoryStatusPill") is not { } pill) return;
            var state = DirectoryPillFor(active, optedIn, claimed);
            pill.IsVisible = state.Visible;
            if (!state.Visible) return;
            if (Named<TextBlock>("TxtDirectoryStatus") is { } text) text.Text = state.Text;
            if (Named<Ellipse>("DirectoryStatusDot") is { } dot) dot.Fill = new SolidColorBrush(Color.Parse(state.DotHex));
            ToolTip.SetTip(pill, state.Tip);
        }
    }
}
