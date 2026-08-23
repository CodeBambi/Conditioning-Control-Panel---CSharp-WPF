using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;

namespace CcpClient.Desktop.Views;

/// <summary>
/// <b>Recent Sessions</b> — upstream's <c>Windows/SessionLogHistoryWindow.xaml.cs</c>, the window
/// the door button opens (<c>MainWindow/MainWindow.Presets.cs:1440</c>). It lists the retained logs
/// newest first and reopens any one of them in the same <see cref="SessionRecapWindow"/> the
/// session's own end raised (<c>SessionLogHistoryWindow.xaml.cs:39-56</c>).
///
/// <para><b>The list is read when the window opens</b>, exactly as upstream reads it
/// (<c>:16</c>, <c>Loaded += ... LoadLogs()</c>), so a session that ended while this window was
/// shut is there the next time it is opened.</para>
/// </summary>
public partial class SessionHistoryWindow : Window
{
    private readonly ScriptedSessionLogStore _store;
    private readonly Action<ScriptedSessionLog, Window> _openRecap;

    /// <param name="store">The log store — the same one the running session writes into, never a
    /// second reader of the same folder.</param>
    /// <param name="openRecap">How a row opens its recap. Injected rather than constructed here so
    /// the ONE recap construction site stays in
    /// <see cref="Navigation.SessionRecapLaunch"/> — the <c>LoomLaunch</c> convention
    /// (<c>Navigation/LoomLaunch.cs</c>).</param>
    public SessionHistoryWindow(
        ScriptedSessionLogStore store, Action<ScriptedSessionLog, Window> openRecap)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(openRecap);
        InitializeComponent();
        _store = store;
        _openRecap = openRecap;
        Title = SessionRecapNotices.HistoryTitle;
        CloseButton.Click += (_, _) => Close();
        Opened += (_, _) => Reload();
    }

    /// <summary>The logs this window is currently listing, newest first. Public so a fact reads the
    /// rows the window really built.</summary>
    public IReadOnlyList<ScriptedSessionLog> Rows { get; private set; } = [];

    /// <summary>Escape closes, as it does on the recap and on every other window in this shell
    /// (<c>SessionCompleteWindow.xaml.cs:44-51</c>).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Upstream's <c>LoadLogs</c> (<c>:19-37</c>): read, build the rows, and put the
    /// empty sentence up instead when there are none. Public so the headed harness and a headless
    /// fact can re-read without closing and reopening the window.</summary>
    public void Reload()
    {
        Rows = _store.LoadRecent();
        HistoryList.Children.Clear();

        if (Rows.Count == 0)
        {
            EmptyLine.Text = SessionRecapNotices.NoHistory;
            EmptyLine.IsVisible = true;
            HistoryList.IsVisible = false;
            HistoryCount.Text = string.Empty;
            return;
        }

        EmptyLine.IsVisible = false;
        HistoryList.IsVisible = true;
        HistoryCount.Text = SessionRecapNotices.HistoryCount(Rows.Count);

        var ordinal = 0;
        foreach (var log in Rows)
        {
            HistoryList.Children.Add(BuildRow(log, ordinal));
            ordinal++;
        }
    }

    /// <summary>
    /// One history row — upstream's card (<c>SessionLogHistoryWindow.xaml:80-110</c>): icon and
    /// name, the status word in its own colour, and the started/duration/media line under it. The
    /// whole card is the click target, as upstream's is (<c>:39-56</c>).
    /// </summary>
    private Button BuildRow(ScriptedSessionLog log, int ordinal)
    {
        var suffix = ordinal.ToString(CultureInfo.InvariantCulture);
        var title = new TextBlock
        {
            Text = SessionRecapNotices.HistoryTitleFor(log),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Name = "SessionHistoryTitle" + suffix,
            [AutomationProperties.AutomationIdProperty] = "SessionHistoryTitle" + suffix,
        };
        var status = new TextBlock
        {
            Text = SessionRecapNotices.Status(log),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(SessionRecapNotices.StatusColour(log))),
            VerticalAlignment = VerticalAlignment.Center,
            Name = "SessionHistoryStatus" + suffix,
            [AutomationProperties.AutomationIdProperty] = "SessionHistoryStatus" + suffix,
        };
        var detail = new TextBlock
        {
            Text = SessionRecapNotices.HistoryRow(log),
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Avalonia.Thickness(0, 3, 0, 0),
            Name = "SessionHistoryDetail" + suffix,
            [AutomationProperties.AutomationIdProperty] = "SessionHistoryDetail" + suffix,
        };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(status, 1);
        head.Children.Add(title);
        head.Children.Add(status);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(detail);

        var row = new Button
        {
            Name = "SessionHistoryRow" + suffix,
            Content = body,
            [AutomationProperties.AutomationIdProperty] = "SessionHistoryRow" + suffix,
            [AutomationProperties.NameProperty] =
                $"{SessionRecapNotices.HistoryTitleFor(log)} — {SessionRecapNotices.Status(log)}",
        };
        row.Classes.Add("history-row");
        row.Click += (_, _) => _openRecap(log, this);
        return row;
    }
}
