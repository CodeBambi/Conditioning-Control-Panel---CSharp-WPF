using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;

namespace CcpClient.Desktop.Views;

/// <summary>
/// <b>Session Complete</b> — upstream's <c>Windows/SessionCompleteWindow.xaml.cs</c>, the window a
/// user sees when a scripted session ends, <b>however it ended</b>
/// (<c>MainWindow/MainWindow.Presets.cs:1681</c>; the log's <c>LogReady</c> fires for completion
/// and abort alike, <c>Services/Session/SessionLogService.cs:101</c>, and upstream says so at
/// <c>MainWindow/MainWindow.xaml.cs:373-375</c>).
///
/// <para><b>It is built from the LOG, never from the run</b>, which is what lets the same window
/// reopen a session from a week ago out of <see cref="SessionHistoryWindow"/> — upstream's own
/// second caller (<c>Windows/SessionLogHistoryWindow.xaml.cs:46-50</c>).</para>
///
/// <para><b>Three of upstream's parts are refused rather than faked</b>, each named on screen:
/// the <c>+N XP</c> column (nothing in this build awards XP), the random completion card and the
/// completion sound (neither <c>Cards/*.png</c> nor <c>lvup.mp3</c> is in this build's assets), and
/// the media row's file name with its reveal-in-Explorer click (the paths never leave the module
/// that drew them — <c>Effects/FlashImagesEffect.cs:151-155</c>). See
/// <see cref="SessionRecapNotices.NamesNotRecorded"/> and
/// <see cref="SessionRecapNotices.AwardsNotComputed"/>.</para>
/// </summary>
public partial class SessionRecapWindow : Window
{
    /// <summary>No parameterless constructor, deliberately, as with every other window in this
    /// shell: a recap with no log has nothing to recap.</summary>
    public SessionRecapWindow(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        InitializeComponent();
        Log = log;
        Title = SessionRecapNotices.RecapTitle;
        CloseButton.Click += (_, _) => Close();
        Apply(log);
    }

    /// <summary>The log this window is showing. Public so a fact reads what the window was really
    /// built from rather than re-deriving it.</summary>
    public ScriptedSessionLog Log { get; }

    /// <summary>Upstream's keyboard escape hatch (<c>SessionCompleteWindow.xaml.cs:44-51</c>), kept
    /// for its own stated reason: the close button must never be the only way out of a window that
    /// opens by itself.</summary>
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

    private void Apply(ScriptedSessionLog log)
    {
        // Upstream's header pair (:78-88): which headline, and the icon/name line under it that
        // only says "Completed" when it did.
        Headline.Text = SessionRecapNotices.Headline(log);
        Subtitle.Text = SessionRecapNotices.Subtitle(log);

        // Upstream's two surviving stats (:94-95).
        SessionName.Text = log.SessionName;
        Duration.Text = SessionRackNotices.Clock(log.Duration);

        NamesNotice.Text = SessionRecapNotices.NamesNotRecorded;
        AwardsNotice.Text = SessionRecapNotices.AwardsNotComputed;

        // Upstream's empty case is a sentence where the list would be, never a blank panel
        // (:111-116, SessionCompleteWindow.xaml:125-130) — and the count cell goes with it.
        if (log.Media.Count == 0)
        {
            NoMedia.Text = SessionRecapNotices.NoMedia;
            NoMedia.IsVisible = true;
            MediaCount.Text = string.Empty;
            return;
        }

        MediaCount.Text = SessionRecapNotices.MediaCount(log);

        // "newest entries last (chronological order matches the session timeline)" — upstream's own
        // comment and ordering (:106-109). The list is already in that order; nothing sorts it.
        var ordinal = 0;
        foreach (var entry in log.Media)
        {
            MediaList.Children.Add(BuildRow(entry, ordinal));
            ordinal++;
        }
    }

    /// <summary>
    /// One media row. Upstream's is a BUTTON that reveals the file
    /// (<c>SessionCompleteWindow.xaml:134-137</c>, <c>xaml.cs:173-194</c>); this is a plate,
    /// because the row has no file to reveal and a button that cannot act is worse than no button
    /// (the same call the rack row's edit and delete actions already got,
    /// <c>Views/Pages/StudioPage.axaml.cs:636-644</c>).
    ///
    /// <para>Upstream's colour coding survives — pink for a video, light blue for an image
    /// (<c>xaml.cs:246</c>, <c>:251</c>) — because it is the part of the row that reads at a
    /// glance.</para>
    /// </summary>
    private static Border BuildRow(ScriptedMediaEntry entry, int ordinal)
    {
        var time = new TextBlock
        {
            Text = SessionRackNotices.Clock(entry.SessionTime),
            FontFamily = new FontFamily("Consolas, monospace"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#FFA0A0B0")),
            Width = 56,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var kind = new TextBlock
        {
            Text = SessionRecapNotices.Kind(entry.Kind),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse(
                entry.Kind == ScriptedMediaKind.Video ? "#FFFF69B4" : "#FF87CEFA")),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(time, 0);
        Grid.SetColumn(kind, 1);
        grid.Children.Add(time);
        grid.Children.Add(kind);

        return new Border
        {
            Name = "SessionRecapMediaRow" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Background = new SolidColorBrush(Color.Parse("#FF2A2130")),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 6),
            Child = grid,
            [AutomationProperties.AutomationIdProperty] =
                "SessionRecapMediaRow" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [AutomationProperties.NameProperty] = SessionRecapNotices.MediaRow(entry),
        };
    }
}
