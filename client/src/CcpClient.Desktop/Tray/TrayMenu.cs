namespace CcpClient.Desktop.Tray;

/// <summary>
/// One entry in the tray's context menu: a command, or a separator.
///
/// <para>WPF's menu is built once at tray-icon construction and never rebuilt
/// (<c>Services/Notifications/TrayIconService.cs:95-110</c>): a <c>ContextMenuStrip</c> with
/// <c>ToolStripMenuItem</c>s whose <c>Click</c> handlers call straight into the window. This is
/// the same shape — a label and the thing it does — with the platform detail (an
/// <c>HMENU</c>, an <c>NSMenu</c>, a DBus menu layout) left to the backend.</para>
///
/// <para><b>Why <see cref="IsRestore"/> exists.</b> A tray menu whose items cannot bring the
/// window back is the hazard this whole packet is about: the port refused to tuck precisely
/// because the icon had no menu, and a menu with no way back would be that same refusal wearing
/// a costume. So exactly one item must be marked as the restore, <see cref="TrayMenu"/> refuses
/// to construct without it, and the mistake is impossible rather than merely discouraged.</para>
/// </summary>
public sealed class TrayMenuItem
{
    private readonly Action? _command;

    private TrayMenuItem(string id, string label, Action? command, bool isSeparator, bool isRestore)
    {
        Id = id;
        Label = label;
        _command = command;
        IsSeparator = isSeparator;
        IsRestore = isRestore;
    }

    /// <summary>Stable identity for diagnostics and for the id-to-item map a backend builds.
    /// Never shown to the user; the empty string for a separator.</summary>
    public string Id { get; }

    /// <summary>The text the user reads. Empty for a separator.</summary>
    public string Label { get; }

    /// <summary>A horizontal rule, not a command (WPF <c>TrayIconService.cs:107</c>).</summary>
    public bool IsSeparator { get; }

    /// <summary>The one item that brings the window back (WPF's "Show Dashboard",
    /// <c>TrayIconService.cs:98-100</c>).</summary>
    public bool IsRestore { get; }

    /// <summary>WPF's "Show Dashboard" (<c>TrayIconService.cs:98-100</c>).</summary>
    public static TrayMenuItem Restore(string id, string label, Action command) =>
        Build(id, label, command, isRestore: true);

    /// <summary>Any other command entry (WPF's wake item at <c>:102-105</c>, Exit at <c>:109-111</c>).</summary>
    public static TrayMenuItem Command(string id, string label, Action command) =>
        Build(id, label, command, isRestore: false);

    /// <summary>WPF <c>TrayIconService.cs:107</c>'s <c>ToolStripSeparator</c>.</summary>
    public static TrayMenuItem Separator() =>
        new(string.Empty, string.Empty, null, isSeparator: true, isRestore: false);

    /// <summary>Runs the entry's action. A separator has none, and invoking one is a no-op rather
    /// than a throw: a backend that hands back a separator's position instead of a command id is a
    /// defect to diagnose, not a reason to throw out of a native menu callback on the UI thread.</summary>
    public void Invoke() => _command?.Invoke();

    private static TrayMenuItem Build(string id, string label, Action command, bool isRestore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(command);
        return new TrayMenuItem(id, label, command, isSeparator: false, isRestore: isRestore);
    }
}

/// <summary>
/// The menu a right-click on the tray icon opens.
///
/// <para><b>The constructor is the safety.</b> It refuses a menu with no restore item, with more
/// than one, with duplicate ids, or with no commands at all. Those are not style rules: the menu
/// is reachable at a moment when it may be the user's shortest way back to a window that has left
/// the screen, and every one of those shapes is a menu that cannot do that job. Refusing at
/// construction puts the failure in one loud place instead of leaving a dead entry in the
/// notification area that nobody can see is dead.</para>
/// </summary>
public sealed class TrayMenu
{
    public TrayMenu(IEnumerable<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToArray();
        if (Array.Exists(list, i => i is null))
        {
            throw new ArgumentException("a tray menu entry was null", nameof(items));
        }

        var commands = list.Where(i => !i.IsSeparator).ToArray();
        if (commands.Length == 0)
        {
            throw new ArgumentException(
                "a tray menu with no commands is decoration: it gives the user a right-click that does nothing",
                nameof(items));
        }

        var restores = commands.Count(i => i.IsRestore);
        if (restores != 1)
        {
            throw new ArgumentException(
                $"a tray menu must carry exactly one restore item and this one carries {restores}. The icon exists to "
                + "be a way back to a window; a menu that cannot restore it rebuilds the hazard the port refused to ship "
                + "(wpf-surface-reachability.md §10 D20 @ 7527243e7)",
                nameof(items));
        }

        var duplicate = commands.GroupBy(i => i.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"tray menu id '{duplicate.Key}' is used by {duplicate.Count()} entries; an id identifies an entry in "
                + "diagnostics and in the backend's command map, so it must be unique",
                nameof(items));
        }

        Items = list;
    }

    /// <summary>Every entry, in the order the user sees them.</summary>
    public IReadOnlyList<TrayMenuItem> Items { get; }

    /// <summary>The one item that brings the window back.</summary>
    public TrayMenuItem RestoreItem => Items.First(i => i.IsRestore);
}
